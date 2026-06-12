package com.vulnwatch.worker.persistence;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import com.vulnwatch.worker.owasp.model.OWASPFindingMapping;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

import java.util.List;
import java.util.UUID;

/**
 * Persists OWASP evaluation results to tables matching C# EF Core migrations.
 *
 * TABLE "OwaspMappings" (Plural - Fixes Bug 1)
 * ON CONFLICT ("ScanId", "FindingId", "CategoryCode") (3-column key - Fixes Bug 2)
 *
 * Enums are stored as raw member strings via C# .HasConversion<string>()
 */
@Slf4j
@Repository
@RequiredArgsConstructor
public class OWASPPersistence {

    private final JdbcTemplate jdbc;

    private static final String INSERT_MAPPING = """
            INSERT INTO "OwaspMappings"
                ("Id", "ScanId", "FindingId", "CategoryCode", "CategoryName",
                 "Status", "Severity", "FindingLabel", "CreatedAt", "UpdatedAt")
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())
            ON CONFLICT ("ScanId", "FindingId", "CategoryCode") DO UPDATE
              SET "CategoryName"  = EXCLUDED."CategoryName",
                  "Status"        = EXCLUDED."Status",
                  "Severity"      = EXCLUDED."Severity",
                  "FindingLabel"  = EXCLUDED."FindingLabel",
                  "UpdatedAt"     = NOW()
            """;

    private static final String UPDATE_SCAN_OWASP = """
            UPDATE "Scans"
            SET "OWASPScore" = ?, "OWASPTier" = ?, "UpdatedAt" = NOW()
            WHERE "Id" = ?
            """;

    private static final String UPDATE_SCAN_NARRATIVE = """
            UPDATE "Scans"
            SET "OWASPPostureSummary" = ?, "UpdatedAt" = NOW()
            WHERE "Id" = ?
            """;

    /**
     * Saves full OWASP evaluation mapping details followed by the scan-level metrics.
     */
    public void saveMapping(OWASPEvaluationResult result) {
        List<OWASPFindingMapping> mappings = result.findingMappings();

        if (!mappings.isEmpty()) {
            jdbc.batchUpdate(INSERT_MAPPING, mappings, mappings.size(), (ps, m) -> {
                ps.setObject(1, UUID.randomUUID());
                ps.setObject(2, UUID.fromString(m.scanId()));
                ps.setObject(3, UUID.fromString(m.findingId()));
                ps.setString(4, m.category().getCode());
                ps.setString(5, m.category().getDisplayName());
                ps.setString(6, m.status().name());
                ps.setString(7, severityName(m.severity()));
                ps.setString(8, m.findingLabel());
            });
        }

        int updated = jdbc.update(
                UPDATE_SCAN_OWASP,
                result.overallScore(),
                result.tier().getLabel(),
                UUID.fromString(result.scanId())
        );

        if (updated == 0) {
            throw new IllegalStateException(
                    "No Scan row updated for OWASP score [scanId=%s]".formatted(result.scanId()));
        }

        log.info("OWASP mapping saved [scanId={} rows={} score={} tier={}]",
                result.scanId(), mappings.size(), result.overallScore(), result.tier().getLabel());
    }

    /**
     * Persists the AI narrative safely on a best-effort path.
     */
    public void saveNarrative(String scanId, String narrative) {
        try {
            jdbc.update(UPDATE_SCAN_NARRATIVE, narrative, UUID.fromString(scanId));
            log.info("OWASP posture narrative saved [scanId={}]", scanId);
        } catch (Exception e) {
            log.error("Failed to save OWASP posture narrative [scanId={}] — non-fatal", scanId, e);
        }
    }

    private String severityName(FindingSeverity severity) {
        return severity != null ? severity.name() : "Low";
    }
}