package com.vulnwatch.worker.processor;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.model.ScanJob;
import lombok.extern.slf4j.Slf4j;
import redis.clients.jedis.JedisPooled;

/**
 * Decorates a {@link JobProcessor} with exponential-backoff retry logic.
 * After {@value MAX_ATTEMPTS} failed attempts the job is pushed to a
 * dead-letter queue (DLQ) in Redis for manual inspection.
 */
@Slf4j
public class RetryableProcessor implements JobProcessor {

    private static final int  MAX_ATTEMPTS  = 3;
    private static final long BASE_DELAY_MS = 2_000L;

    private final JobProcessor delegate;
    private final JedisPooled jedis;
    private final String dlqKey;
    private final ObjectMapper mapper;

    public RetryableProcessor(JobProcessor delegate, JedisPooled jedis, String dlqKey, ObjectMapper mapper) {
        this.delegate = delegate;
        this.jedis = jedis;
        this.dlqKey = dlqKey;
        this.mapper = mapper;
    }

    @Override
    public void process(ScanJob job) {
        Exception lastException = null;

        for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
            try {
                if (attempt > 1) backoff(job, attempt);
                delegate.process(job);
                return;

            } catch (InterruptedException ie) {
                Thread.currentThread().interrupt();
                log.warn("Retry interrupted [scanId={}], aborting", job.scanId());
                return;

            } catch (Exception e) {
                lastException = e;
                log.warn("Attempt {}/{} failed [scanId={}]: {}",
                        attempt, MAX_ATTEMPTS, job.scanId(), e.getMessage());
            }
        }

        pushToDlq(job, lastException);
    }

    private void backoff(ScanJob job, int attempt) throws InterruptedException {
        long delay = BASE_DELAY_MS * (long) Math.pow(2, attempt - 2);
        log.info("Retrying [scanId={} attempt={}/{} delayMs={}]",
                job.scanId(), attempt, MAX_ATTEMPTS, delay);
        Thread.sleep(delay);
    }

    private void pushToDlq(ScanJob job, Exception cause) {
        try {
            String payload = mapper.writeValueAsString(job);
            jedis.lpush(dlqKey, payload);
            log.error("Job sent to DLQ after {} failed attempts [scanId={} reason={}]",
                    MAX_ATTEMPTS, job.scanId(), cause != null ? cause.getMessage() : "unknown");
        } catch (Exception e) {
            log.error("Failed to push job to DLQ [scanId={} dlq={}]", job.scanId(), dlqKey, e);
        }
    }
}