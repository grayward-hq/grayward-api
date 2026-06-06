package com.vulnwatch.worker.events;

import com.vulnwatch.worker.enums.AiAvailability;

/**
 * Published by AiCircuitBreakerConfig when the Resilience4j circuit
 * breaker changes state (CLOSED → OPEN → HALF_OPEN → CLOSED).
 *
 * Consumed by:
 *   - AiAvailabilityGate → publishes AI status to C# queues (Phase 6)
 *   - ScanMetricsRecorder → records CB state change (Phase 11)
 *
 * reason is a human-readable string from Resilience4j
 * e.g. "failure rate exceeded threshold", "manual override".
 */
public record AiAvailabilityEvent(
        AiAvailability availability,
        String reason
) {}