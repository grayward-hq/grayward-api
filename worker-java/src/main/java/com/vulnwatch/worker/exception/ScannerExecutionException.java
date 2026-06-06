package com.vulnwatch.worker.exception;

import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceType;
import lombok.Getter;

/**
 * Typed exception that carries failure context through Spring Retry's
 * retry/recover cycle without losing structured information in a message string.
 */
@Getter
public class ScannerExecutionException extends RuntimeException {

    private final SurfaceType surfaceType;
    private final int retryCount;
    private final FailureReason failureReason;

    public ScannerExecutionException(SurfaceType surfaceType, String message,
                                     int retryCount, FailureReason failureReason) {
        super("[%s] %s".formatted(surfaceType.name(), message));
        this.surfaceType = surfaceType;
        this.retryCount = retryCount;
        this.failureReason = failureReason;
    }

}