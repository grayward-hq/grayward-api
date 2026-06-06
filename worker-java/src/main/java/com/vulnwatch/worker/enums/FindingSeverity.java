package com.vulnwatch.worker.enums;

import io.swagger.v3.oas.annotations.media.Schema;
import lombok.Getter;

/**
 * Severity levels for security findings. Critical/High require immediate attention.
 */
@Getter
@Schema(description = "Severity level of a security finding")
public enum FindingSeverity {

    @Schema(description = "Immediate threat: exposed secrets, actively exploited vulnerabilities")
    CRITICAL("Critical", 30),

    @Schema(description = "Significant risk: missing critical security headers, expiring certificates")
    HIGH("High", 20),

    @Schema(description = "Moderate risk: missing security best practices, informational")
    MEDIUM("Medium", 10),

    @Schema(description = "Low risk: minor improvements, recommendations")
    LOW("Low", 5),

    @Schema(description = "No structural risk noted")
    NONE("None", 0);

    private final String name;
    private final int deduction;

    FindingSeverity(String name, int deduction) {
        this.name = name;
        this.deduction = deduction;
    }

    /**
     * Returns true if this severity is equal to or more severe than the given threshold.
     * e.g. HIGH.isAtLeast(MEDIUM) → true, LOW.isAtLeast(HIGH) → false
     */
    public boolean isAtLeast(FindingSeverity threshold) {
        return this.deduction >= threshold.deduction;
    }

    /**
     * Safely parses a string value (e.g. from an AI model response) into the matching enum constant.
     * Returns NONE as a defensive default if no match is found.
     */
    public static FindingSeverity fromName(String name) {
        if (name == null) return NONE;
        for (FindingSeverity severity : values()) {
            if (severity.name.equalsIgnoreCase(name.trim())) {
                return severity;
            }
        }
        return NONE;
    }
}