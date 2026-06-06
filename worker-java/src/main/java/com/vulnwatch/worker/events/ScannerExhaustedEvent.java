package com.vulnwatch.worker.events;

import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.ScanJob;

/**
 * Published by ScannerRetryPolicy when a scanner exhausts all 3 retry
 * attempts on a surface.
 * Consumed by:
 *   - DeadLetterQueueHandler  → enriches with surface metadata, pushes to DLQ
 *   - ScanMetricsRecorder → increments DLQ counter (Phase 11)
 * Carries the original ScanJob so consumers have the full context
 * without needing another Redis read.
 */
public record ScannerExhaustedEvent(
        ScanJob job,
        SurfaceType surfaceType,
        int retryCount,
        FailureReason failureReason,
        String errorMessage
) {}