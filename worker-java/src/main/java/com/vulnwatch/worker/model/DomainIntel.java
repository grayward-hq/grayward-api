package com.vulnwatch.worker.model;

import com.vulnwatch.worker.enums.AiAvailability;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;

import java.time.Instant;

public record DomainIntel(
        String scanId,
        String domainId,
        String domainName,
        String requestedBy,
        int securityScore,
        int owaspScore,          // ← NEW
        String owaspTier,        // ← NEW  e.g. "Good"
        AiAvailability aiAvailability,
        Instant completedAt
) {
    public static DomainIntel of(
            ScanJob job,
            int securityScore,
            OWASPEvaluationResult owaspResult,
            AiAvailability aiAvailability) {
        return new DomainIntel(
                job.scanId(), job.domainId(), job.domainName(), job.requestedBy(),
                securityScore,
                owaspResult.overallScore(),
                owaspResult.tier().getLabel(),
                aiAvailability,
                Instant.now()
        );
    }
}