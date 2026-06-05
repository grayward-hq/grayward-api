package com.vulnwatch.worker.owasp.enums;

import lombok.RequiredArgsConstructor;

@RequiredArgsConstructor
public enum OWASPComplianceTier {
    EXCELLENT      (90, 100, "Excellent"),

    GOOD           (75,  89, "Good"),

    NEEDS_ATTENTION(50,  74, "Needs Attention"),

    HIGH_RISK      ( 0,  49, "High Risk");

    private final int minScore;

    private final int maxScore;

    private final String label;

    public static OWASPComplianceTier fromScore(int score) {

        for (OWASPComplianceTier t : values()) {

            if (score >= t.minScore && score <= t.maxScore) return t;

        }

        return HIGH_RISK;

    }

}
