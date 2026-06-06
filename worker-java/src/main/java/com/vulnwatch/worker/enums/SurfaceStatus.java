package com.vulnwatch.worker.enums;

/**
 * Lifecycle state of a single scanner surface within a scan job.
 * Tracked independently per surface in Redis by RedisSurfaceStateManager.
 */
public enum SurfaceStatus {

    /** Surface registered, scanner not yet started. */
    PENDING,

    /** Scanner is actively running. */
    SCANNING,

    /** Scanner failed on this attempt — will be retried. */
    RETRYING,

    /** Scanner succeeded; AI enrichment is in progress. */
    ENRICHING,

    /** AI enrichment completed successfully. Terminal. */
    SUCCESS,

    /**
     * AI enrichment skipped — circuit breaker was open.
     * Scanner result still persisted. Terminal.
     */
    SUCCESS_NO_AI,

    /** Scanner exhausted all retries — sent to DLQ. Terminal. */
    PERMANENTLY_FAILED;

    public boolean isTerminal() {
        return this == SUCCESS || this == SUCCESS_NO_AI || this == PERMANENTLY_FAILED;
    }

    public boolean isPending() {
        return this == PENDING;
    }

    public boolean isRetrying() {
        return this == RETRYING;
    }

    public boolean isSuccess() {
        return this == SUCCESS || this == SUCCESS_NO_AI;
    }

    public boolean isFailed() {
        return this == PERMANENTLY_FAILED;
    }
}