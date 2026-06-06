package com.vulnwatch.worker.enums;

/**
 * Resilience4j circuit breaker changes state.
 * C# uses this to gate new scan requests .
 */
public enum AiAvailability {

    /** Circuit breaker CLOSED — AI enrichment operating normally. */
    AVAILABLE,

    /**
     * Circuit breaker HALF_OPEN — AI is being probed after a failure period.
     * Scans proceed but enrichment may still fail.
     */
    DEGRADED,

    /** Circuit breaker OPEN — AI enrichment unavailable. */
    UNAVAILABLE
}