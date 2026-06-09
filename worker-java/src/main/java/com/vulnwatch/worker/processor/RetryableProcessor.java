package com.vulnwatch.worker.processor;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.retry.annotation.Backoff;
import org.springframework.retry.annotation.Recover;
import org.springframework.retry.annotation.Retryable;
import redis.clients.jedis.JedisPooled;

import java.time.Instant;
import java.util.Map;

/**
 * Decorates a {@link JobProcessor} with declarative exponential backoff.
 * If execution completely fails after 3 attempts, the job metadata is routed into a Redis DLQ.
 */
@Slf4j
@RequiredArgsConstructor
public class RetryableProcessor implements JobProcessor {

    private final JobProcessor delegate;
    private final JedisPooled jedis;
    private final String dlqKey;
    private final ObjectMapper mapper;

    @Override
    @Retryable(
            retryFor = Exception.class,
            maxAttempts = 3,
            backoff = @Backoff(delay = 2000, multiplier = 2.0)
    )
    public void process(ScanJob job) {
        log.info("Processing job via execution pipeline [scanId={} type={}]", job.scanId(), job.scanType());
        delegate.process(job);
    }

    /**
     * Fallback method triggered automatically when all retry attempts are exhausted.
     * Note: The first argument MUST be the exception thrown, followed by the original method arguments.
     */
    @Recover
    public void recover(Exception cause, ScanJob job) {
        String reason = cause != null ? cause.getMessage() : "Unknown operational failure";
        log.error("Job processor completely exhausted all retry attempts [scanId={} reason={}]", job.scanId(), reason);

        try {
            // Enrich the dead letter payload so operators have immediate debugging triage context
            Map<String, Object> dlqWrapper = Map.of(
                    "failedAt", Instant.now().toString(),
                    "failureReason", reason,
                    "attemptsExhausted", 3,
                    "job", job
            );

            String payload = mapper.writeValueAsString(dlqWrapper);
            jedis.lpush(dlqKey, payload);

            log.info("Successfully dropped job tracking payload to global DLQ [scanId={} queue={}]", job.scanId(), dlqKey);

        } catch (JsonProcessingException e) {
            log.error("CRITICAL: Failed to serialize fallback job payload for DLQ [scanId={}]: {}", job.scanId(), e.getMessage(), e);
        } catch (Exception e) {
            log.error("CRITICAL: Failed to push message transport wrapper to Redis DLQ [scanId={} queue={}]: {}", job.scanId(), dlqKey, e.getMessage(), e);
        }
    }
}