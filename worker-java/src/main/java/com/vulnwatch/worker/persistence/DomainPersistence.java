package com.vulnwatch.worker.persistence;

import com.fasterxml.jackson.databind.MapperFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.json.JsonMapper;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.engine.domain.nuclei.models.NucleiEngineResult;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.EngineResult;
import lombok.extern.slf4j.Slf4j;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;
import org.springframework.transaction.annotation.Transactional;

import java.sql.Timestamp;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.UUID;

@Slf4j
@Repository
public class DomainPersistence {

    private final JdbcTemplate jdbc;
    private final ObjectMapper mapper;


    private static final String INSERT_FINDING = """
            INSERT INTO "Findings" (
                "Id", "ScanId", "Surface", "Severity", "Title",
                "CveId", "AiExplanation", "TechnicalPayload",
                "RemediationSteps", "Status", "CreatedAt"
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'Open', ?)
            """;

    private static final String UPDATE_SCAN_MARK_RUNNING = """
            UPDATE "Scans"
            SET "Status" = 'Running',
                "StartedAt" = ?,
                "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    private static final String UPDATE_SCAN_COMPLETE = """
            UPDATE "Scans"
            SET "Status" = 'Completed',
                "CompletedAt" = ?,
                "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    private static final String UPDATE_SCAN_FAIL = """
            UPDATE "Scans"
            SET "Status" = 'Failed',
                "CompletedAt" = ?,
                "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    private static final String UPDATE_DOMAIN_SSL_EXPIRY = """
            UPDATE "Domains"
            SET "SslCertExpiry" = ?,
                "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    private static final String DELETE_FINDINGS_FOR_SURFACE = """
            DELETE FROM "Findings" WHERE "ScanId" = ? AND "Surface" = ?
            """;


    /**
     * Used whenever AI enrichment was unavailable or failed.
     * Never stored as null — consumers should always get a readable string.
     */
    private static final String AI_ENRICHMENT_NOT_AVAILABLE = "AI enrichment not available";

    private static final String DEFAULT_SEVERITY  = "Low";
    private static final String EMPTY_JSON  = "{}";


    public DomainPersistence(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
        this.mapper = JsonMapper.builder()
                .configure(MapperFeature.ACCEPT_CASE_INSENSITIVE_PROPERTIES, true)
                .build();
    }


    public void markRunning(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_MARK_RUNNING, now, now, uuid(scanId));
        if (updated == 0) {
            log.warn("markRunning: no Scan row found [scanId={}]", scanId);
        }
    }

    public List<DomainFinding> saveFindings(
            String scanId,
            String domainId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        List<DomainFinding> findings = assembleFindings(scanId, engineResults, enrichments);

        try {
            insertFindings(findings);
            markComplete(scanId);
            extractSslCertExpiry(engineResults, enrichments)
                    .ifPresent(expiry -> updateDomainSslExpiry(domainId, expiry));

            log.info("Saved {} findings [scanId={}]", findings.size(), scanId);
        } catch (Exception e) {
            log.error("Failed to save findings [scanId={}]", scanId, e);
            throw new RuntimeException("Persistence failure for scan %s".formatted(scanId), e);
        }

        return findings;
    }

    public void markFailed(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_FAIL, now, now, uuid(scanId));
        if (updated == 0) {
            log.warn("markFailed: no Scan row found [scanId={}]", scanId);
        }
    }

    /**
     * Replaces whatever Finding row(s) exist for a single surface on a scan
     * with a fresh one built from a newly-succeeded EngineResult. Used when a
     * surface that previously exhausted retries and landed in the DLQ is later
     * replayed by hand and succeeds — the old "probe failed" row is stale and
     * must not linger next to the new, real result.
     */
    @Transactional
    public DomainFinding replaceFindingForSurface(
            String scanId,
            SurfaceType surface,
            EngineResult freshResult,
            AiResult enrichment) {

        DomainFinding finding = assembleFindings(
                scanId, List.of(freshResult), enrichment == null ? List.of() : List.of(enrichment)
        ).getFirst();

        try {
            jdbc.update(DELETE_FINDINGS_FOR_SURFACE, uuid(scanId), surfaceLabel(surface));
            insertFindings(List.of(finding));
            log.info("Replaced Finding row after surface recovery [scanId={} surface={}]", scanId, surface);
        } catch (Exception e) {
            log.error("Failed to replace Finding row after surface recovery [scanId={} surface={}]",
                    scanId, surface, e);
            throw new RuntimeException("Failed to persist recovered surface finding for scan %s".formatted(scanId), e);
        }

        return finding;
    }


    private List<DomainFinding> assembleFindings(
            String scanId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        List<DomainFinding> findings = new ArrayList<>(engineResults.size());

        for (int i = 0; i < engineResults.size(); i++) {
            EngineResult engine = engineResults.get(i);
            AiResult enrichment = i < enrichments.size() ? enrichments.get(i) : null;

            // Severity and title always come from the scanner — never from AI.
            String severity = extractSeverityFromEngine(engine);
            String title = buildTitle(engine);

            findings.add(new DomainFinding(
                    UUID.randomUUID().toString(),
                    scanId,
                    surfaceLabel(engine.surfaceType()),
                    severity,
                    title,
                    null,                                // cveId — scanners don't produce CVE IDs;
                    aiExplanation(enrichment),           // "AI enrichment not available" if absent
                    formatPayload(engine),
                    aiRemediationSteps(enrichment)       // "AI enrichment not available" if absent
            ));
        }
        return findings;
    }


    /**
     * Walks the engine's rawResult findings list and returns the highest
     * severity present. Each scanner surface stores its typed findings under
     * the "findings" key in the rawResult map.
     *
     * Falls back to DEFAULT_SEVERITY when the engine failed, the findings
     * list is empty, or the findings type is unrecognised.
     */
    private String extractSeverityFromEngine(EngineResult engine) {
        if (!engine.success() || engine.rawResult() == null) {
            return DEFAULT_SEVERITY;
        }

        Object raw = engine.rawResult().get("findings");
        if (!(raw instanceof List<?> list) || list.isEmpty()) {
            return DEFAULT_SEVERITY;
        }

        FindingSeverity highest = FindingSeverity.NONE;

        for (Object item : list) {
            FindingSeverity sev = severityOf(item, engine.surfaceType());
            if (sev.isAtLeast(highest)) {
                highest = sev;
            }
        }

        // If all findings resolved to NONE (e.g. only informational DNS records),
        // return Low so the DB field is never blank.
        return highest == FindingSeverity.NONE ? DEFAULT_SEVERITY : toDisplayName(highest);
    }

    /**
     * Extracts a FindingSeverity from a single findings object.
     * Each scanner produces a different typed object, all of which carry
     * a severity field — either as a FindingSeverity enum or a String.
     */
    private FindingSeverity severityOf(Object item, SurfaceType surface) {
        return switch (surface) {
            case DNS -> item instanceof Findings f ? f.severity() : FindingSeverity.NONE;
            case SSL -> item instanceof SslFindings f ? f.severity() : FindingSeverity.NONE;
            case PORTS -> item instanceof NmapFindings f ? f.severity() : FindingSeverity.NONE;
            case SUBDOMAINS -> item instanceof SubdomainFindings f ? f.getRisk() : FindingSeverity.NONE;
            case HTTP_HEADERS -> item instanceof NucleiEngineResult f
                    ? FindingSeverity.fromName(f.severity())
                    : FindingSeverity.NONE;
            default -> FindingSeverity.NONE;
        };
    }

    /** Converts a FindingSeverity enum to the display-name string stored in the DB. */
    private String toDisplayName(FindingSeverity severity) {
        return switch (severity) {
            case CRITICAL -> "Critical";
            case HIGH -> "High";
            case MEDIUM -> "Medium";
            case LOW -> "Low";
            default -> DEFAULT_SEVERITY;
        };
    }


    /**
     * Builds a descriptive title purely from scanner output.
     * For a failed engine, indicates the failure.
     * For a successful engine, summarises the most significant finding.
     */

    private String buildTitle(EngineResult engine) {
        if (!engine.success()) {
            return "%s probe failed".formatted(engine.surfaceType().getLabel());
        }

        if (engine.rawResult() == null) {
            return engine.surfaceType().getLabel().formatted("%s scan completed");
        }

        Object raw = engine.rawResult().get("findings");
        if (!(raw instanceof List<?> list) || list.isEmpty()) {
            return "%s scan completed — no issues found".formatted(engine.surfaceType().getLabel());
        }

        // Return the first finding's human-readable label as the title.
        Object first = list.getFirst();
        String label = findingTitle(first, engine.surfaceType());
        return label != null ? label : "%s scan completed".formatted(engine.surfaceType().getLabel());
    }

    private String findingTitle(Object item, SurfaceType surface) {
        return switch (surface) {
            case DNS -> item instanceof Findings f ? f.title() : null;
            case SSL -> item instanceof SslFindings f ? f.id() : null;
            case PORTS -> item instanceof NmapFindings f ? f.finding() : null;
            case SUBDOMAINS -> item instanceof SubdomainFindings f ? f.getRecord().host() : null;
            case HTTP_HEADERS -> item instanceof NucleiEngineResult f ? f.issue() : null;
            default -> null;
        };
    }


    /**
     * Returns the AI-generated explanation, or "AI enrichment not available"
     * if the AI was unreachable, timed out, or the circuit breaker was open.
     * Never returns null.
     */
    private String aiExplanation(AiResult enrichment) {
        if (enrichment == null || enrichment.explanation() == null
                || enrichment.explanation().isBlank()) {
            return AI_ENRICHMENT_NOT_AVAILABLE;
        }
        return enrichment.explanation();
    }

    /**
     * Returns the AI-generated remediation steps joined as newline-separated text,
     * or "AI enrichment not available" when AI was absent or returned nothing.
     * Never returns null.
     */
    private String aiRemediationSteps(AiResult enrichment) {
        if (enrichment == null
                || enrichment.remediationSteps() == null
                || enrichment.remediationSteps().isEmpty()) {
            return AI_ENRICHMENT_NOT_AVAILABLE;
        }
        return String.join("\n", enrichment.remediationSteps());
    }


    private Optional<String> extractSslCertExpiry(
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        for (int i = 0; i < engineResults.size(); i++) {
            EngineResult engine = engineResults.get(i);
            if (engine.surfaceType() == SurfaceType.SSL && i < enrichments.size()) {
                AiResult enrichment = enrichments.get(i);
                if (enrichment != null && enrichment.certExpiry() != null) {
                    return Optional.of(enrichment.certExpiry());
                }
            }
        }
        return Optional.empty();
    }

    private void updateDomainSslExpiry(String domainId, String certExpiry) {
        try {
            OffsetDateTime expiry = Instant.parse(certExpiry).atOffset(ZoneOffset.UTC);
            int updated = jdbc.update(UPDATE_DOMAIN_SSL_EXPIRY, expiry, now(), uuid(domainId));
            if (updated == 0) {
                log.warn("No Domain row updated [domainId={}]", domainId);
            } else {
                log.debug("Updated SslCertExpiry [domainId={} expiry={}]", domainId, certExpiry);
            }
        } catch (Exception e) {
            log.warn("Failed to update SslCertExpiry [domainId={}]: {}", domainId, e.getMessage());
        }
    }


    private void insertFindings(List<DomainFinding> findings) {
        jdbc.batchUpdate(INSERT_FINDING, findings, findings.size(), (ps, f) -> {
            ps.setObject(1, uuid(f.id()));
            ps.setObject(2, uuid(f.scanId()));
            ps.setString(3, f.surface());
            ps.setString(4, f.severity());
            ps.setString(5, f.title());
            ps.setString(6, f.cveId());
            ps.setString(7, f.aiExplanation());
            ps.setString(8, f.technicalPayload());
            ps.setString(9, f.remediationSteps());
            ps.setTimestamp(10, Timestamp.from(Instant.now()));
        });
    }

    private void markComplete(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_COMPLETE, now, now, uuid(scanId));
        if (updated == 0) {
            throw new IllegalStateException("No Scan row updated [scanId=%s]".formatted(scanId));
        }
    }

    private String surfaceLabel(SurfaceType surfaceType) {
        return surfaceType.getLabel();
    }

    private String formatPayload(EngineResult engine) {
        try {
            if (!engine.success()) {
                String errorMsg = Objects.requireNonNullElse(engine.errorMessage(), "Unknown error");
                return mapper.writeValueAsString(Map.of("error", errorMsg));
            }
            return mapper.writeValueAsString(engine.rawResult());
        } catch (Exception e) {
            log.warn("Failed to serialize payload [surface={}]", engine.surfaceType(), e);
            return EMPTY_JSON;
        }
    }

    private OffsetDateTime now() {
        return OffsetDateTime.now(ZoneOffset.UTC);
    }

    private UUID uuid(String id) {
        return UUID.fromString(id);
    }
}