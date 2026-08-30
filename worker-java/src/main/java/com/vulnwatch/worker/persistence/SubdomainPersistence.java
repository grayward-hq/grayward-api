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

/**
 * Persistence for the subdomain-scanning feature. Deliberately NOT a modification of
 * {@link DomainPersistence} — kept as a parallel, self-contained class so the existing,
 * tested domain-scan persistence path (which this class does not call, and is not called
 * by) carries zero behavioral risk from this feature.
 *
 * Two independent responsibilities:
 *  1. {@link #upsertDiscovered} — called after an ordinary DOMAIN scan's SUBDOMAINS surface
 *     succeeds, to record/refresh rows in the new "Subdomains" table.
 *  2. {@link #saveFindings} / {@link #markFailed} — called by SubdomainJobProcessor when a
 *     user explicitly scans a single subdomain (DNS/SSL/PORTS/HTTP_HEADERS against it).
 *
 * Requires the migration described in the subdomain-scanning plan:
 *   - new table "Subdomains" (Id, DomainId FK, Host, Source, RiskSeverity, IsActive,
 *     SslCertExpiry, LastScanId, FirstSeenAt, LastSeenAt, CreatedAt, UpdatedAt),
 *     unique on (DomainId, Host)
 *   - "Scans"."SubdomainId" nullable FK column
 */
@Slf4j
@Repository
public class SubdomainPersistence {

    private final JdbcTemplate jdbc;
    private final ObjectMapper mapper;

    private static final String UPSERT_SUBDOMAIN = """
            INSERT INTO "Subdomains" (
                "Id", "DomainId", "Host", "Source", "RiskSeverity",
                "IsActive", "FirstSeenAt", "LastSeenAt", "CreatedAt"
            ) VALUES (?, ?, ?, ?, ?, true, ?, ?, ?)
            ON CONFLICT ("DomainId", "Host") DO UPDATE SET
                "Source"       = EXCLUDED."Source",
                "RiskSeverity" = EXCLUDED."RiskSeverity",
                "IsActive"     = EXCLUDED."IsActive",
                "LastSeenAt"   = EXCLUDED."LastSeenAt",
                "UpdatedAt"    = EXCLUDED."LastSeenAt"
            """;

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

    private static final String UPDATE_SUBDOMAIN_SSL_EXPIRY = """
            UPDATE "Subdomains"
            SET "SslCertExpiry" = ?,
                "LastScanId" = ?,
                "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    private static final String AI_ENRICHMENT_NOT_AVAILABLE = "AI enrichment not available";
    private static final String DEFAULT_SEVERITY = "Low";
    private static final String EMPTY_JSON = "{}";

    public SubdomainPersistence(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
        this.mapper = JsonMapper.builder()
                .configure(MapperFeature.ACCEPT_CASE_INSENSITIVE_PROPERTIES, true)
                .build();
    }

    // ── 1. Discovery (called from a Domain-scoped scan's DomainJobProcessor) ─────────

    /**
     * Upserts every subdomain discovered by the SUBDOMAINS surface for the given parent
     * domain. Insert-or-refresh: a host seen before just gets its risk classification and
     * LastSeenAt bumped, never duplicated (unique index on DomainId+Host).
     */
    public void upsertDiscovered(String domainId, List<SubdomainFindings> discovered) {
        if (discovered == null || discovered.isEmpty()) {
            return;
        }

        OffsetDateTime now = now();
        try {
            jdbc.batchUpdate(UPSERT_SUBDOMAIN, discovered, discovered.size(), (ps, f) -> {
                ps.setObject(1, UUID.randomUUID());
                ps.setObject(2, uuid(domainId));
                ps.setString(3, f.getRecord().host());
                ps.setString(4, Objects.requireNonNullElse(f.getRecord().source(), "subfinder"));
                ps.setString(5, f.getRisk() != null ? f.getRisk().name() : FindingSeverity.NONE.name());
                ps.setObject(6, now);
                ps.setObject(7, now);
                ps.setObject(8, now);
            });
            log.info("Upserted {} discovered subdomain(s) [domainId={}]", discovered.size(), domainId);
        } catch (Exception e) {
            // Discovery persistence must never fail the parent domain scan it rode in on.
            log.error("Failed to upsert discovered subdomains [domainId={}]: {}", domainId, e.getMessage(), e);
        }
    }

    // ── 2. Scanning a subdomain directly (called from SubdomainJobProcessor) ─────────

    public void markRunning(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_MARK_RUNNING, now, now, uuid(scanId));
        if (updated == 0) {
            log.warn("markRunning: no Scan row found [scanId={}]", scanId);
        }
    }

    /**
     * @param scanId      the Scan row created by the API for this subdomain scan
     * @param subdomainId the target subdomain's id — carried in ScanJob.domainId() for
     *                    Subdomain-type jobs (see SubdomainJobProcessor)
     */
    public List<DomainFinding> saveFindings(
            String scanId,
            String subdomainId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        List<DomainFinding> findings = assembleFindings(scanId, engineResults, enrichments);

        try {
            insertFindings(findings);
            markComplete(scanId);
            extractSslCertExpiry(engineResults, enrichments)
                    .ifPresent(expiry -> updateSubdomainSslExpiry(subdomainId, scanId, expiry));

            log.info("Saved {} findings [scanId={} subdomainId={}]", findings.size(), scanId, subdomainId);
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

    // ── shared assembly helpers (intentionally mirrors DomainPersistence) ────────────

    private List<DomainFinding> assembleFindings(
            String scanId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        List<DomainFinding> findings = new ArrayList<>(engineResults.size());

        for (int i = 0; i < engineResults.size(); i++) {
            EngineResult engine = engineResults.get(i);
            AiResult enrichment = i < enrichments.size() ? enrichments.get(i) : null;

            String severity = extractSeverityFromEngine(engine);
            String title = buildTitle(engine);

            findings.add(new DomainFinding(
                    UUID.randomUUID().toString(),
                    scanId,
                    surfaceLabel(engine.surfaceType()),
                    severity,
                    title,
                    null,
                    aiExplanation(enrichment),
                    formatPayload(engine),
                    aiRemediationSteps(enrichment)
            ));
        }
        return findings;
    }

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

        return highest == FindingSeverity.NONE ? DEFAULT_SEVERITY : toDisplayName(highest);
    }

    private FindingSeverity severityOf(Object item, SurfaceType surface) {
        return switch (surface) {
            case DNS -> item instanceof Findings f ? f.severity() : FindingSeverity.NONE;
            case SSL -> item instanceof SslFindings f ? f.severity() : FindingSeverity.NONE;
            case PORTS -> item instanceof NmapFindings f ? f.severity() : FindingSeverity.NONE;
            case HTTP_HEADERS -> item instanceof NucleiEngineResult f
                    ? FindingSeverity.fromName(f.severity())
                    : FindingSeverity.NONE;
            // SUBDOMAINS is deliberately absent: a Subdomain-targeted job never runs it
            // (SubdomainJobProcessor strips it before the orchestrator sees the job).
            default -> FindingSeverity.NONE;
        };
    }

    private String toDisplayName(FindingSeverity severity) {
        return switch (severity) {
            case CRITICAL -> "Critical";
            case HIGH -> "High";
            case MEDIUM -> "Medium";
            case LOW -> "Low";
            default -> DEFAULT_SEVERITY;
        };
    }

    private String buildTitle(EngineResult engine) {
        if (!engine.success()) {
            return "%s probe failed".formatted(engine.surfaceType().getLabel());
        }

        if (engine.rawResult() == null) {
            return "%s scan completed".formatted(engine.surfaceType().getLabel());
        }

        Object raw = engine.rawResult().get("findings");
        if (!(raw instanceof List<?> list) || list.isEmpty()) {
            return "%s scan completed — no issues found".formatted(engine.surfaceType().getLabel());
        }

        Object first = list.getFirst();
        String label = findingTitle(first, engine.surfaceType());
        return label != null ? label : "%s scan completed".formatted(engine.surfaceType().getLabel());
    }

    private String findingTitle(Object item, SurfaceType surface) {
        return switch (surface) {
            case DNS -> item instanceof Findings f ? f.title() : null;
            case SSL -> item instanceof SslFindings f ? f.id() : null;
            case PORTS -> item instanceof NmapFindings f ? f.finding() : null;
            case HTTP_HEADERS -> item instanceof NucleiEngineResult f ? f.issue() : null;
            default -> null;
        };
    }

    private String aiExplanation(AiResult enrichment) {
        if (enrichment == null || enrichment.explanation() == null || enrichment.explanation().isBlank()) {
            return AI_ENRICHMENT_NOT_AVAILABLE;
        }
        return enrichment.explanation();
    }

    private String aiRemediationSteps(AiResult enrichment) {
        if (enrichment == null || enrichment.remediationSteps() == null || enrichment.remediationSteps().isEmpty()) {
            return AI_ENRICHMENT_NOT_AVAILABLE;
        }
        return String.join("\n", enrichment.remediationSteps());
    }

    private Optional<String> extractSslCertExpiry(List<EngineResult> engineResults, List<AiResult> enrichments) {
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

    private void updateSubdomainSslExpiry(String subdomainId, String scanId, String certExpiry) {
        try {
            OffsetDateTime expiry = Instant.parse(certExpiry).atOffset(ZoneOffset.UTC);
            int updated = jdbc.update(
                    UPDATE_SUBDOMAIN_SSL_EXPIRY, expiry, uuid(scanId), now(), uuid(subdomainId));
            if (updated == 0) {
                log.warn("No Subdomain row updated [subdomainId={}]", subdomainId);
            } else {
                log.debug("Updated Subdomain SslCertExpiry [subdomainId={} expiry={}]", subdomainId, certExpiry);
            }
        } catch (Exception e) {
            log.warn("Failed to update Subdomain SslCertExpiry [subdomainId={}]: {}", subdomainId, e.getMessage());
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