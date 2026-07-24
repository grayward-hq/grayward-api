package com.vulnwatch.worker.ai.repository;

import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;

/**
 * Builds a description of a secret finding that's safe to send to a
 * third-party AI provider, never the actual secret value.
 *
 * TrivyEngineResult.secretFinding() only ever carries title/category/
 * location/line-range (no raw match text) as currently parsed, but this
 * exists as an explicit, defensive boundary: if TrivyParser is ever
 * extended to also capture Trivy's "Match" field (which contains a
 * partially-redacted excerpt of the secret), this is the single place
 * that decides what's safe to forward, rather than leaving it implicit.
 */
public final class SecretRedactor {

    private SecretRedactor() {}

    public static String describeForAi(TrivyEngineResult finding) {
        return """
                A potential hardcoded secret was detected.
                Category: %s
                Lines: %d-%d
                Severity: %s
                Do not attempt to guess or reconstruct the secret value — \
                only explain the risk and remediation steps for this category of exposure.
                """.formatted(
                nullToUnknown(finding.category()),
                finding.startLine() != null ? finding.startLine() : 0,
                finding.endLine() != null ? finding.endLine() : 0,
                nullToUnknown(finding.severity())
        );
    }

    private static String nullToUnknown(String value) {
        return value == null || value.isBlank() ? "unknown" : value;
    }
}