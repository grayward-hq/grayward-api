package com.vulnwatch.worker.persistence;

import com.fasterxml.jackson.databind.MapperFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.json.JsonMapper;
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
                "SecurityScore" = ?,
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

    // Fallback constants
    private static final String DEFAULT_SEVERITY = "Low";
    private static final String FALLBACK_EXPLANATION = "Engine ran but enrichment failed.";
    private static final String FALLBACK_REMEDIATION = "Review engine output manually.";
    private static final String EMPTY_JSON = "{}";

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
            List<AiResult> enrichments,
            int securityScore) {

        List<DomainFinding> findings = assembleFindings(scanId, engineResults, enrichments);

        try {
            insertFindings(findings);
            updateScanComplete(scanId, securityScore);
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


    private List<DomainFinding> assembleFindings(
            String scanId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        List<DomainFinding> findings = new ArrayList<>(engineResults.size());

        for (int i = 0; i < engineResults.size(); i++) {
            EngineResult engine = engineResults.get(i);
            AiResult enrichment = i < enrichments.size() ? enrichments.get(i) : null;

            findings.add(new DomainFinding(
                    UUID.randomUUID().toString(),
                    scanId,
                    surfaceLabel(engine.surfaceType()),
                    normaliseSeverity(enrichment),
                    buildTitle(engine, enrichment),
                    cveId(enrichment),
                    explanation(enrichment),
                    formatPayload(engine),
                    remediation(enrichment)
            ));
        }
        return findings;
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

    private void updateScanComplete(String scanId, int securityScore) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_COMPLETE, securityScore, now, now, uuid(scanId));

        if (updated == 0) {
            throw new IllegalStateException("No Scan row updated [scanId=%s]".formatted(scanId));
        }
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
            int updated = jdbc.update(
                    UPDATE_DOMAIN_SSL_EXPIRY,
                    expiry,
                    now(),
                    uuid(domainId)
            );

            if (updated == 0) {
                log.warn("No Domain row updated [domainId={}]", domainId);
            } else {
                log.debug("Updated SslCertExpiry [domainId={} expiry={}]", domainId, certExpiry);
            }
        } catch (Exception e) {
            log.warn("Failed to update SslCertExpiry [domainId={}]: {}", domainId, e.getMessage());
        }
    }


    private String surfaceLabel(SurfaceType surfaceType) {
        return surfaceType.getLabel();
    }

    private String normaliseSeverity(AiResult enrichment) {
        if (enrichment == null || enrichment.severity() == null) {
            return DEFAULT_SEVERITY;
        }

        String s = enrichment.severity().trim().toLowerCase();
        return switch (s) {
            case "critical" -> "Critical";
            case "high" -> "High";
            case "medium" -> "Medium";
            case "low" -> "Low";
            default -> DEFAULT_SEVERITY;
        };
    }

    private String explanation(AiResult enrichment) {
        return enrichment != null && enrichment.explanation() != null
                ? enrichment.explanation()
                : FALLBACK_EXPLANATION;
    }

    private String cveId(AiResult enrichment) {
        return enrichment != null ? enrichment.cveId() : null;
    }

    private String remediation(AiResult enrichment) {
        if (enrichment == null
                || enrichment.remediationSteps() == null
                || enrichment.remediationSteps().isEmpty()) {
            return FALLBACK_REMEDIATION;
        }
        return String.join("\n", enrichment.remediationSteps());
    }

    private String buildTitle(EngineResult engine, AiResult enrichment) {
        if (!engine.success()) {
            return "%s probe failed".formatted(engine.surfaceType().getLabel());
        }
        if (enrichment != null && enrichment.title() != null) {
            return enrichment.title();
        }
        return "%s scan completed".formatted(engine.surfaceType().getLabel());
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