package com.vulnwatch.worker.enums;

import io.swagger.v3.oas.annotations.media.Schema;
import lombok.Getter;

/** Severity levels for security findings. Critical/High require immediate attention. */
@Getter
@Schema(description = "Severity level of a security finding")
public enum FindingSeverity {

    @Schema(description = "Immediate threat: exposed secrets, actively exploited vulnerabilities")
    CRITICAL("Critical"),

    @Schema(description = "Significant risk: missing critical security headers, expiring certificates")
    HIGH("High"),

    @Schema(description = "Moderate risk: missing security best practices, informational")
    MEDIUM("Medium"),

    @Schema(description = "Low risk: minor improvements, recommendations")
    LOW("Low"),

    NONE("None");

    private final String name;

    FindingSeverity(String name) {
        this.name = name;
    }
}
