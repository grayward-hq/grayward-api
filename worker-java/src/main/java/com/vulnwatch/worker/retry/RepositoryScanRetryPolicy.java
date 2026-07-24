package com.vulnwatch.worker.retry;

import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.exception.RepositoryScanException;
import lombok.extern.slf4j.Slf4j;
import org.springframework.retry.annotation.Backoff;
import org.springframework.retry.annotation.Recover;
import org.springframework.retry.annotation.Retryable;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.concurrent.Callable;

/**
 * Wraps the Trivy execution+parse step with retry/backoff, giving the
 * repository pipeline the same resilience the domain scanners already get
 * from ScannerRetryPolicy
 */
@Slf4j
@Component
public class RepositoryScanRetryPolicy {

    @Retryable(
            retryFor = Exception.class,
            maxAttempts = 3,
            backoff = @Backoff(delay = 3000, multiplier = 2)
    )
    public List<TrivyEngineResult> execute(String scanId, Callable<List<TrivyEngineResult>> trivyInvocation) {
        try {
            return trivyInvocation.call();
        } catch (Exception e) {
            log.warn("Trivy execution attempt failed [scanId={}]: {}", scanId, e.getMessage());
            throw new RepositoryScanException("Trivy execution failed for scan %s".formatted(scanId), e);
        }
    }

    @Recover
    public List<TrivyEngineResult> recover(Exception e, String scanId, Callable<List<TrivyEngineResult>> trivyInvocation) {
        log.error("Trivy exhausted all retries permanently [scanId={}]: {}", scanId, e.getMessage(), e);
        throw new RepositoryScanException("Trivy permanently failed after retries for scan %s".formatted(scanId), e);
    }
}