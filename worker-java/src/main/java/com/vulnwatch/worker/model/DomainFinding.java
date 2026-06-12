package com.vulnwatch.worker.model;

public record DomainFinding(
        String id,       //new
        String scanId,
        String surface,
        String severity,
        String title,
        String cveId,
        String aiExplanation,       // AI-generated plain-English explanation of the issue
        String technicalPayload,
        String remediationSteps
) {
}

