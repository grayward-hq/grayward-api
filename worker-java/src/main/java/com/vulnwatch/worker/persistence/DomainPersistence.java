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

    // SQL Statements
    private static final String INSERT_FINDING = """
            INSERT INTO "Findings"
            (
                "Id", "ScanId", "Surface", "Severity", "Title",
                "CveId", "AiExplanation", "TechnicalPayload",
                "RemediationSteps", "Status", "CreatedAt"
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'Open', ?)
            """;

    private static final String UPDATE_SCAN = """
            UPDATE "Scans"
            SET "Status" = 'Completed', "SecurityScore" = ?,
                "CompletedAt" = ?, "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    private static final String UPDATE_DOMAIN_SSL_EXPIRY = """
            UPDATE "Domains"
            SET "SslCertExpiry" = ?, "UpdatedAt" = ?
            WHERE "Id" = ?
            """;

    // Fallbacks and Default String Constants
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

    /**
     * Orchestrates the persistence workflow for a completed scan evaluation.
     */
    public List<DomainFinding> saveFindings(
            String scanId,
            String domainId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments,
            int securityScore) {

        List<DomainFinding> findings = assembleFindings(scanId, engineResults, enrichments);

        try {
            insertFindings(findings);
            updateScan(scanId, securityScore);
            extractSslCertExpiry(engineResults, enrichments)
                    .ifPresent(expiry -> updateDomainSslExpiry(domainId, expiry));

            log.info("Saved {} findings [scanId={}]", findings.size(), scanId);
        } catch (Exception e) {
            log.error("Failed to save findings [scanId={}]", scanId, e);
            throw new RuntimeException("Persistence failure", e);
        }

        return findings;
    }


    private List<DomainFinding> assembleFindings(
            String scanId,
            List<EngineResult> engineResults,
            List<AiResult> enrichments) {

        List<DomainFinding> findings = new ArrayList<>();

        for (int i = 0; i < engineResults.size(); i++) {
            EngineResult engine = engineResults.get(i);
            AiResult enrichment = i < enrichments.size() ? enrichments.get(i) : null;

            findings.add(new DomainFinding(
                    UUID.randomUUID().toString(),
                    scanId,
                    engine.surfaceType().getLabel(),
                    severity(enrichment),
                    buildTitle(engine, enrichment), // passing enrichment context
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
            ps.setObject(1, UUID.fromString(f.id()));   // ← was UUID.randomUUID()
            ps.setObject(2, UUID.fromString(f.scanId()));
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

    private void updateScan(String scanId, int securityScore) {
        OffsetDateTime now = OffsetDateTime.now(ZoneOffset.UTC);
        int updated = jdbc.update(UPDATE_SCAN, securityScore, now, now, UUID.fromString(scanId));
        if (updated == 0) {
            throw new IllegalStateException("No scan row updated for scanId=%s".formatted(scanId));
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
                    OffsetDateTime.now(ZoneOffset.UTC),
                    UUID.fromString(domainId)
            );

            if (updated == 0) {
                log.warn("No domain row updated [domainId={}]", domainId);
            } else {
                log.debug("Updated SslCertExpiry [domainId={} expiry={}]", domainId, certExpiry);
            }
        } catch (Exception e) {
            log.warn("Failed to update SslCertExpiry [domainId={}]: {}", domainId, e.getMessage());
        }
    }


    private String severity(AiResult enrichment) {
        return enrichment != null ? enrichment.severity() : DEFAULT_SEVERITY;
    }

    private String explanation(AiResult enrichment) {
        return enrichment != null ? enrichment.explanation() : FALLBACK_EXPLANATION;
    }

    private String cveId(AiResult enrichment) {
        return enrichment != null ? enrichment.cveId() : null;
    }

    private String remediation(AiResult enrichment) {
        if (enrichment == null || enrichment.remediationSteps() == null || enrichment.remediationSteps().isEmpty()) {
            return FALLBACK_REMEDIATION;
        }
        return String.join("\n", enrichment.remediationSteps());
    }

    // New contextual title determination logic
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
}