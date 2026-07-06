package com.vulnwatch.worker.persistence;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import lombok.extern.slf4j.Slf4j;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

import java.sql.Timestamp;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * Persists repository scan findings into the SAME "Findings"/"Scans" tables
 * the domain pipeline already uses
 *
 * This also removes the need for a separate .NET-side Redis consumer:
 * the worker marks the Scan row Completed/Failed directly, exactly like
 * DomainPersistence does for domain scans.
 */
@Slf4j
@Repository
public class RepositoryFindingsPersistence {

    private static final String INSERT_FINDING = """
            INSERT INTO "Findings" (
                "Id", "ScanId", "Surface", "Severity", "Title",
                "CveId", "AiExplanation", "TechnicalPayload",
                "RemediationSteps", "Status", "CreatedAt"
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'Open', ?)
            """;

    private static final String UPDATE_SCAN_MARK_RUNNING = """
            UPDATE "Scans" SET "Status" = 'Running', "StartedAt" = ?, "UpdatedAt" = ? WHERE "Id" = ?
            """;

    private static final String UPDATE_SCAN_COMPLETE = """
            UPDATE "Scans" SET "Status" = 'Completed', "CompletedAt" = ?, "UpdatedAt" = ? WHERE "Id" = ?
            """;

    private static final String UPDATE_SCAN_FAIL = """
            UPDATE "Scans" SET "Status" = 'Failed', "CompletedAt" = ?, "UpdatedAt" = ? WHERE "Id" = ?
            """;

    private final JdbcTemplate jdbc;
    private final ObjectMapper mapper = new ObjectMapper();

    public RepositoryFindingsPersistence(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    public void markRunning(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_MARK_RUNNING, now, now, uuid(scanId));
        if (updated == 0) {
            log.warn("markRunning: no Scan row found [scanId={}]", scanId);
        }
    }

    public void saveFindings(String scanId, List<TrivyEngineResult> findings, List<AiResult> aiResults) {
        try {
            for (int i = 0; i < findings.size(); i++) {
                TrivyEngineResult finding = findings.get(i);
                AiResult ai = i < aiResults.size() ? aiResults.get(i) : null;
                insertFinding(scanId, finding, ai);
            }
            markComplete(scanId);
            log.info("Saved {} repository findings [scanId={}]", findings.size(), scanId);
        } catch (Exception e) {
            log.error("Failed to save repository findings [scanId={}]", scanId, e);
            throw new RuntimeException("Persistence failure for repository scan %s".formatted(scanId), e);
        }
    }

    public void markFailed(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_FAIL, now, now, uuid(scanId));
        if (updated == 0) {
            log.warn("markFailed: no Scan row found [scanId={}]", scanId);
        }
    }

    private void insertFinding(String scanId, TrivyEngineResult finding, AiResult ai) throws Exception {
        boolean isDependency = finding.packageName() != null;
        String surface = isDependency ? SurfaceType.DEPENDENCY.getLabel() : SurfaceType.SECRETS.getLabel();
        String title = isDependency
                ? "%s: %s".formatted(finding.packageName(), safe(finding.title()))
                : "Hardcoded secret: %s".formatted(safe(finding.category()));
        String cveId = isDependency ? extractCveId(finding.title()) : null;
        String severity = normalizeSeverity(finding.severity());
        String explanation = ai != null ? ai.explanation() : "AI enrichment not available";
        String remediation = ai != null && !ai.remediationSteps().isEmpty()
                ? String.join("\n", ai.remediationSteps())
                : defaultRemediation(isDependency, finding);

        String payload = mapper.writeValueAsString(Map.of(
                "packageName", safe(finding.packageName()),
                "installedVersion", safe(finding.installedVersion()),
                "fixedVersion", safe(finding.fixedVersion()),
                "location", safe(finding.secretLocation()),
                "category", safe(finding.category()),
                "startLine", finding.startLine() == null ? 0 : finding.startLine(),
                "endLine", finding.endLine() == null ? 0 : finding.endLine()
        ));

        jdbc.update(INSERT_FINDING,
                UUID.randomUUID(), uuid(scanId), surface, severity, title,
                cveId, explanation, payload, remediation, Timestamp.from(Instant.now()));
    }

    private String defaultRemediation(boolean isDependency, TrivyEngineResult finding) {
        if (isDependency && finding.fixedVersion() != null) {
            return "Upgrade %s to %s or later.".formatted(finding.packageName(), finding.fixedVersion());
        }
        return isDependency
                ? "No fixed version available yet, monitor for an upstream patch."
                : "Rotate the exposed credential immediately and remove it from version control history.";
    }

    private String extractCveId(String title) {
        if (title == null) return null;
        var matcher = Pattern
                .compile("CVE-\\d{4}-\\d+")
                .matcher(title);
        return matcher.find() ? matcher.group() : null;
    }

    private String normalizeSeverity(String trivySeverity) {
        if (trivySeverity == null)
            return "Low";
        return switch (trivySeverity.toUpperCase()) {
            case "CRITICAL" -> "Critical";
            case "HIGH" -> "High";
            case "MEDIUM" -> "Medium";
            default -> "Low";
        };
    }

    private String safe(String value) {
        return value == null || value.isBlank() ? "unknown" : value;
    }

    private void markComplete(String scanId) {
        OffsetDateTime now = now();
        int updated = jdbc.update(UPDATE_SCAN_COMPLETE, now, now, uuid(scanId));
        if (updated == 0) {
            throw new IllegalStateException("No Scan row updated [scanId=%s]".formatted(scanId));
        }
    }

    private OffsetDateTime now() {
        return OffsetDateTime.now(ZoneOffset.UTC);
    }

    private UUID uuid(String id) {
        return UUID.fromString(id);
    }
}