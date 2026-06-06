package com.vulnwatch.worker.model.state;

import com.vulnwatch.worker.enums.AiAvailability;
import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;

/**
 * Immutable point-in-time snapshot of a surface's state.
 * Read from Redis by ScanJobStateMachine, DomainIntelPublisher,
 * and DeadLetterQueueHandler to build the final C# payload.
 */
public record SurfaceStateSnapshot(
        SurfaceType surface,
        SurfaceStatus status,
        int retryCount,
        FailureReason failureReason,    // null if not failed
        AiAvailability aiAvailability,  // availability at time of enrichment attempt
        String updatedAt                // ISO-8601 timestamp of last transition
) {}
