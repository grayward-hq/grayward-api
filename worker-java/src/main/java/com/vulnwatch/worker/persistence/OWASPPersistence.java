package com.vulnwatch.worker.persistence;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.enums.OWASPComplianceTier;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import com.vulnwatch.worker.owasp.model.OWASPFindingMapping;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
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
            SET "OWASPScore" = ?, "OWASPTier" = ?, "SecurityScore" = ?, "UpdatedAt" = NOW()
            WHERE "Id" = ?
            """;

    private static final String UPDATE_SCAN_NARRATIVE = """
            UPDATE "Scans"
            SET "OWASPPostureSummary" = ?, "UpdatedAt" = NOW()
            WHERE "Id" = ?
            """;

    private static final String SELECT_SEVERITIES_BY_SCAN = """
            SELECT "CategoryCode", "Severity" FROM "OwaspMappings" WHERE "ScanId" = ?
            """;

    /**
     * Saves full OWASP evaluation mapping details followed by the scan-level metrics.
     */
    public void saveMapping(OWASPEvaluationResult result) {
        saveFindingMappings(result.findingMappings());
        updateScanScore(result.scanId(), result.overallScore(), result.tier());

        log.info("OWASP mapping saved [scanId={} rows={} score={} tier={}]",
                result.scanId(), result.findingMappings().size(), result.overallScore(), result.tier().getLabel());
    }

    /**
     * Upserts a batch of finding mappings only, without touching the scan-level score.
     * Used both by the normal evaluation flow and by surface-recovery recalculation,
     * where mappings for a single recovered surface are saved before the overall
     * score is recomputed from the full, current set of mappings in the DB.
     */
    public void saveFindingMappings(List<OWASPFindingMapping> mappings) {
        if (mappings == null || mappings.isEmpty()) {
            return;
        }
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

    /**
     * Updates just the scan-level score/tier columns. Split out from saveMapping()
     * so it can be called again on its own once a previously-failed surface is
     * replayed and the whole scan's score needs recalculating.
     */
    public void updateScanScore(String scanId, int overallScore, OWASPComplianceTier tier) {
        int updated = jdbc.update(
                UPDATE_SCAN_OWASP,
                overallScore,
                tier.getLabel(),
                overallScore,
                UUID.fromString(scanId)
        );

        if (updated == 0) {
            throw new IllegalStateException(
                    "No Scan row updated for OWASP score [scanId=%s]".formatted(scanId));
        }
    }

    /**
     * Fetches every currently-persisted finding severity for a scan, grouped by
     * OWASP category. This is "ground truth" for all surfaces that have already
     * succeeded — used to recompute the overall score after one surface is
     * recovered from the DLQ, without needing to re-run every other surface again.
     */
    public Map<OWASPCategory, List<FindingSeverity>> fetchSeveritiesByCategory(String scanId) {
        List<Object[]> rows = jdbc.query(SELECT_SEVERITIES_BY_SCAN,
                (rs, rowNum) -> new Object[]{rs.getString("CategoryCode"), rs.getString("Severity")},
                UUID.fromString(scanId));

        Map<OWASPCategory, List<FindingSeverity>> grouped = new HashMap<>();
        for (Object[] row : rows) {
            OWASPCategory category;
            try {
                category = OWASPCategory.fromCode((String) row[0]);
            } catch (IllegalArgumentException e) {
                log.warn("Unknown OWASP category code in DB [scanId={} code={}]", scanId, row[0]);
                continue;
            }
            FindingSeverity severity = FindingSeverity.fromName((String) row[1]);
            grouped.computeIfAbsent(category, k -> new ArrayList<>()).add(severity);
        }
        return grouped;
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
        return severity != null ? severity.name() : FindingSeverity.NONE.name();
    }
}