package com.vulnwatch.worker.retry;

import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.events.ScannerExhaustedEvent;
import com.vulnwatch.worker.exception.ScannerExecutionException;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.state.SurfaceStateManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.retry.annotation.Backoff;
import org.springframework.retry.annotation.Recover;
import org.springframework.retry.annotation.Retryable;
import org.springframework.stereotype.Component;

/**
 * Wraps each Scanner.scan() call with Spring Retry @Retryable.
 * Retry behaviour:
 *   - Max 3 attempts per surface
 *   - Exponential backoff: 2s → 4s between attempts
 *   - Each failed attempt transitions the surface to RETRYING in Redis
 *   - On exhaustion: @Recover fires ScannerExhaustedEvent + transitions to PERMANENTLY_FAILED
 * Isolation: DNS retrying does NOT block SSL or HTTP.
 * ScanOrchestrator calls this once per scanner on its own virtual thread,
 * so all three surfaces retry independently in parallel.
 * IMPORTANT: @Retryable requires this bean to be called through the Spring
 * proxy — ScanOrchestrator must inject ScannerRetryPolicy (not call it directly
 * as a plain object) for the annotation to take effect.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class ScannerRetryPolicy {

    private final SurfaceStateManager surfaceStateManager;
    private final ApplicationEventPublisher eventPublisher;

    /**
     * Executes the scanner with up to 3 attempts and exponential backoff.
     * On each failure:
     *   - Increments retry count in Redis
     *   - Transitions surface to RETRYING
     *   - Spring Retry sleeps backoff delay then calls again
     * On success returns the EngineResult immediately.
     * On exhaustion @Recover is called — never returns a result.
     *
     * @throws ScannerExecutionException always — signals to @Recover that all retries failed
     */
    @Retryable(
            retryFor  = Exception.class,
            maxAttempts = 3,
            backoff   = @Backoff(delay = 2000, multiplier = 2)
    )
    public EngineResult execute(Scanner scanner, ScanJob job) {
        SurfaceType surface = scanner.surfaceType();

        log.debug("Scanner executing [scanId={} surface={}]", job.scanId(), surface.name());

        EngineResult result;
        try {
            result = scanner.scan(job);
        } catch (Exception rawException) {
            // Protects against raw unhandled connection/socket timeout exceptions
            log.warn("Scanner threw raw unexpected exception [scanId={} surface={} message={}]",
                    job.scanId(), surface.name(), rawException.getMessage());

            int retryCount = surfaceStateManager.incrementRetryCount(job.scanId(), surface);
            surfaceStateManager.transition(job.scanId(), surface, SurfaceStatus.RETRYING);

            throw new ScannerExecutionException(
                    surface,
                    rawException.getMessage(),
                    retryCount,
                    FailureReason.SCANNER_ERROR
            );
        }

        if (!result.success()) {
            log.warn("Scanner returned failure [scanId={} surface={} reason={}]",
                    job.scanId(), surface.name(), result.errorMessage());

            int retryCount = surfaceStateManager.incrementRetryCount(job.scanId(), surface);
            surfaceStateManager.transition(job.scanId(), surface, SurfaceStatus.RETRYING);

            throw new ScannerExecutionException(
                    surface,
                    result.errorMessage(),
                    retryCount,
                    FailureReason.SCANNER_ERROR
            );
        }

        return result;
    }

    /**
     * Recovery method now accepts a generic Exception parameter to cleanly map
     * both handled business faults and raw nested execution bubble-ups.
     */
    @Recover
    public EngineResult recover(Exception e, Scanner scanner, ScanJob job) {
        SurfaceType surface = scanner.surfaceType();

        int totalRetries = 3;
        FailureReason reason = FailureReason.SCANNER_ERROR;
        String message = e.getMessage();

        // Extract detailed stats if it's our specialized wrapper exception
        if (e instanceof ScannerExecutionException see) {
            totalRetries = see.getRetryCount();
            reason = see.getFailureReason();
        }

        log.error("Scanner exhausted all retries permanently [scanId={} surface={} attempts={} reason={}]",
                job.scanId(), surface.name(), totalRetries, message);

        surfaceStateManager.transitionFailed(
                job.scanId(),
                surface,
                SurfaceStatus.PERMANENTLY_FAILED,
                reason
        );

        eventPublisher.publishEvent(new ScannerExhaustedEvent(
                job,
                surface,
                totalRetries,
                reason,
                message
        ));

        return null;
    }



}