package com.vulnwatch.worker.enums;

import io.swagger.v3.oas.annotations.media.Schema;

/**
 * Status of a scan throughout its lifecycle. Matches the 'status' column in the 'scans' table.
 *
 * <p>Lifecycle flow:
 *
 * <pre>
 * QUEUED → RUNNING → COMPLETED
 *                ↘ FAILED
 * </pre>
 */
@Schema(description = "Status of a scan throughout its lifecycle")
public enum ScanStatus {
    @Schema(
            description = "Scan has been created and queued, waiting for worker to pick it up",
            example = "QUEUED")
    QUEUED,
    @Schema(
            description = "Worker is actively processing the scan, scanners are running",
            example = "RUNNING")
    RUNNING,

    @Schema(
            description = "Scan completed successfully, findings saved, security score calculated",
            example = "COMPLETED")
    COMPLETED,

    @Schema(
            description = "Scan failed due to an error (Redis, database, or unexpected exception)",
            example = "FAILED")
    FAILED;

    /** Returns true if the scan is in a terminal state (cannot transition further). */
    public boolean isTerminal() {
        return this == COMPLETED || this == FAILED;
    }

    /** Returns true if the scan is currently active (not terminal). */
    public boolean isActive() {
        return this == QUEUED || this == RUNNING;
    }

    /** Returns the display name for UI/logging. */
    public String getDisplayName() {
        return switch (this) {
            case QUEUED -> "Queued";
            case RUNNING -> "Running";
            case COMPLETED -> "Completed";
            case FAILED -> "Failed";
            default -> name();
        };
    }
}
