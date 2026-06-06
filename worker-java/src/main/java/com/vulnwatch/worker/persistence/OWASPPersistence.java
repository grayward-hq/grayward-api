package com.vulnwatch.worker.persistence;

import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import com.vulnwatch.worker.owasp.model.OWASPFindingMapping;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

import java.util.List;
import java.util.UUID;

@Slf4j
@Repository
@RequiredArgsConstructor
public class OWASPPersistence {

    private final JdbcTemplate jdbc;

    private static final String INSERT_MAPPING = """
        INSERT INTO "owasp_mapping"
            ("id", "scan_id", "finding_id", "category_code", "category_name",
             "status", "severity", "finding_label", "created_at", "updated_at")
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW())
        ON CONFLICT ("scan_id", "finding_id") DO UPDATE
          SET "category_code"  = EXCLUDED."category_code",
              "category_name"  = EXCLUDED."category_name",
              "status"         = EXCLUDED."status",
              "severity"       = EXCLUDED."severity",
              "finding_label"  = EXCLUDED."finding_label",
              "updated_at"     = NOW()
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
                ps.setString(7, m.severity().name());
                ps.setString(8, m.findingLabel());
            });
        }

        jdbc.update(UPDATE_SCAN_OWASP,
                result.overallScore(),
                result.tier().getLabel(),
                UUID.fromString(result.scanId())
        );

        log.info("OWASP mapping saved records [scanId={} rows={} score={} tier={}]",
                result.scanId(), mappings.size(), result.overallScore(), result.tier().getLabel());
    }

    /**
     * Best-effort execution block: swallows anomalies to prevent secondary
     * engine logging failures if contextual AI summaries run into transaction drops.
     */
    public void saveNarrative(String scanId, String narrative) {
        try {
            jdbc.update(UPDATE_SCAN_NARRATIVE, narrative, UUID.fromString(scanId));
            log.info("OWASP narrative summary successfully updated [scanId={}]", scanId);
        } catch (Exception e) {
            log.error("Failed to commit post-scan posture narrative [scanId={}]", scanId, e);
        }
    }
}