package com.vulnwatch.worker.listener;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.config.QueueNames;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.processor.JobProcessor;
import io.github.resilience4j.bulkhead.Bulkhead;
import io.github.resilience4j.bulkhead.BulkheadRegistry;
import jakarta.annotation.PostConstruct;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import redis.clients.jedis.JedisPooled;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.TimeUnit;

/**
 * Blocks on a Redis queue and dispatches incoming scan jobs to the
 * appropriate {@link JobProcessor} on a virtual-thread executor,
 * stopped gracefully via {@link #stop()} on shutdown.
 *
 * <h2>Rate limiting</h2>
 * Jobs are gated by a per-job-type Resilience4j {@link Bulkhead}
 * ({@code resilience4j.bulkhead.instances.<type>-jobs.*}, e.g.
 * "domain-jobs" / "repository-jobs") <b>before</b> being handed to the
 * executor. This is the primary backpressure valve: a popped job that has
 * no free concurrency slot simply parks the poll loop instead of piling up
 * as another concurrently-running virtual thread, each of which fans out
 * into several CPU-heavy CLI tool invocations. Under sustained load this
 * naturally slows how fast the queue is drained, leaving the excess safely
 * on Redis rather than turning into unbounded concurrent scanner processes.
 */
@Slf4j
@RequiredArgsConstructor
@Component
public class QueueListener implements Runnable {

    private static final int SHUTDOWN_TIMEOUT_SECONDS = 10;

    @Value("${worker.blpop.timeout:5}")
    private int blpopTimeout;

    @Value("${worker.concurrency.permit-poll-interval-ms:100}")
    private long permitPollIntervalMs;

    private final QueueNames queueNames;
    private String queueName;

    private final JedisPooled jedis;
    private final Map<String, JobProcessor> processors;
    private final ObjectMapper mapper;
    private final CheckpointManager checkpointManager;
    private final BulkheadRegistry bulkheadRegistry;
    private final ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();

    private volatile boolean running = true;

    @PostConstruct
    void init() {
        this.queueName = queueNames.scanJobs();
    }


    @Override
    public void run() {
        log.info("QueueListener started — blocking on queue '{}'", queueName);

        while (running) {
            try {
                List<String> result = jedis.blpop(blpopTimeout, queueName);
                if (result == null)
                    continue;           // normal timeout, keep polling

                String payload = result.get(1);
                ScanJob job = deserialize(payload);
                if (job == null)
                    continue;           // malformed payload, already logged — drop it

                Bulkhead bulkhead = jobBulkhead(job.scanType());

                if (!awaitPermit(bulkhead)) {
                    // running flipped to false (shutdown) while we were waiting
                    // for capacity — the job is simply not dispatched; it was
                    // already popped off the queue, so on restart it's picked
                    // back up via CheckpointManager's crash-recovery path.
                    break;
                }

                try {
                    executor.submit(() -> handle(job, payload, bulkhead));
                } catch (RejectedExecutionException ree) {
                    // Executor was shut down in the narrow window between
                    // awaitPermit() succeeding and submit() — release the
                    // permit we just acquired so it isn't leaked, and stop.
                    log.warn("Executor rejected job [scanId={}] during shutdown, releasing permit", job.scanId());
                    bulkhead.onComplete();
                    break;
                }

            } catch (Exception e) {
                log.error("Error reading from queue '{}', retrying in 1s: {}", queueName, e.getMessage());
                backoff();
            }
        }

        log.info("QueueListener stopped.");
    }

    /**
     * Resolves the job-level bulkhead for a scan type, e.g. "Domain" →
     * "domain-jobs", "Repository" → "repository-jobs". Unrecognised or
     * missing scan types fall back to a "default-jobs" bulkhead rather
     * than bypassing rate limiting entirely.
     */
    private Bulkhead jobBulkhead(String scanType) {
        String name = "%s-jobs".formatted(scanType == null ? "default" : scanType.toLowerCase());
        return bulkheadRegistry.bulkhead(name);
    }

    /**
     * Blocks the poll loop until a concurrency slot is free for this job
     * type, or until shutdown is requested. Uses {@code tryAcquirePermission}
     * in a short poll loop (rather than the bulkhead's own blocking
     * {@code acquirePermission}) specifically so the wait is interruptible
     * by {@link #stop()} — an in-progress wait must not delay shutdown.
     *
     * @return true if a permit was acquired, false if shutdown intervened first
     */
    private boolean awaitPermit(Bulkhead bulkhead) {
        while (running) {
            if (bulkhead.tryAcquirePermission()) {
                return true;
            }
            try {
                Thread.sleep(permitPollIntervalMs);
            } catch (InterruptedException ie) {
                Thread.currentThread().interrupt();
                return false;
            }
        }
        return false;
    }

    private void handle(ScanJob job, String raw, Bulkhead bulkhead) {
        try {
            log.info("Received job [scanId={} domainId={} type={}]",
                    job.scanId(), job.domainId(), job.scanType());

            // mark checkpoint before handing to processor
            // If the worker crashes anywhere after this point, WorkerRunner
            // will re-queue this job on the next startup.
            checkpointManager.mark(job.scanId(), raw);

            JobProcessor processor = processors.get(job.scanType());
            if (processor == null) {
                log.warn("No processor registered for type '{}'. Known types: {}",
                        job.scanType(), processors.keySet());
                return;
            }

            try {
                processor.process(job);
            } catch (Exception e) {
                log.error("Processor failed [scanId={} type={}]",
                        job.scanId(), job.scanType(), e);
            }
        } finally {
            // Always release, even if checkpointing/lookup failed above —
            // otherwise a malformed processor map entry would permanently
            // leak a concurrency slot.
            bulkhead.onComplete();
        }
    }

    private ScanJob deserialize(String raw) {
        try {
            return mapper.readValue(raw, ScanJob.class);
        } catch (Exception e) {
            log.error("Failed to deserialize job payload, dropping message. Payload: {}", raw, e);
            return null;
        }
    }



    public void stop() {
        log.info("Shutting down QueueListener...");
        running = false;
        executor.shutdown();
        try {
            if (!executor.awaitTermination(SHUTDOWN_TIMEOUT_SECONDS, TimeUnit.SECONDS)) {
                log.warn("Executor did not terminate cleanly within {}s, forcing shutdown",
                        SHUTDOWN_TIMEOUT_SECONDS);
                executor.shutdownNow();
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            executor.shutdownNow();
        }
    }

    private static void backoff() {
        try {
            Thread.sleep(1000);
        } catch (InterruptedException ie) {
            Thread.currentThread().interrupt();
        }
    }
}