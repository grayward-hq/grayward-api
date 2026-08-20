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
        log.info("Initializing QueueListener for queue '{}' with BLPOP timeout={}s and permit poll interval={}ms",
                queueName, blpopTimeout, permitPollIntervalMs);
    }

    @Override
    public void run() {
        log.info("QueueListener started — listening on Redis queue '{}'", queueName);

        while (running) {
            try {
                log.trace("Polling queue '{}' via BLPOP with timeout={}s", queueName, blpopTimeout);
                List<String> result = jedis.blpop(blpopTimeout, queueName);
                if (result == null) {
                    log.trace("BLPOP timed out on queue '{}', continuing poll loop", queueName);
                    continue; // normal timeout, keep polling
                }

                String payload = result.get(1);
                log.debug("Popped payload from queue '{}': {}", queueName, payload);

                ScanJob job = deserialize(payload);
                if (job == null) {
                    log.warn("Dropped unparseable payload popped from queue '{}'", queueName);
                    continue; // malformed payload, already logged — drop it
                }

                Bulkhead bulkhead = jobBulkhead(job.scanType());
                log.debug("Awaiting bulkhead permit [scanId={} scanType={} bulkhead={}]",
                        job.scanId(), job.scanType(), bulkhead.getName());

                // mark checkpoint before handing to processor
                // If the worker crashes anywhere after this point, WorkerRunner
                // will re-queue this job on the next startup.
                log.debug("Setting checkpoint for job [scanId={}]", job.scanId());
                checkpointManager.mark(job.scanId(), payload);

                if (!awaitPermit(bulkhead)) {
                    log.info("Interrupted while awaiting permit for job [scanId={} scanType={}]. Worker shutting down.",
                            job.scanId(), job.scanType());
                    // running flipped to false (shutdown) while we were waiting
                    // for capacity — the job is simply not dispatched; it was
                    // already popped off the queue, so on restart it's picked
                    // back up via CheckpointManager's crash-recovery path.
                    break;
                }

                log.debug("Acquired bulkhead permit for job [scanId={} scanType={} bulkhead={}]",
                        job.scanId(), job.scanType(), bulkhead.getName());

                try {
                    executor.submit(() -> handle(job, payload, bulkhead));
                    log.debug("Submitted job [scanId={}] to virtual thread executor", job.scanId());
                } catch (RejectedExecutionException ree) {
                    // Executor was shut down in the narrow window between
                    // awaitPermit() succeeding and submit() — release the
                    // permit we just acquired so it isn't leaked, and stop.
                    log.warn("Executor rejected job [scanId={}] during shutdown, releasing permit for bulkhead '{}'",
                            job.scanId(), bulkhead.getName());
                    bulkhead.onComplete();
                    break;
                } catch (Throwable t) {
                    log.error("Dispatch failed for job [scanId={}], releasing permit for bulkhead '{}': {}",
                            job.scanId(), bulkhead.getName(), t.getMessage(), t);
                    bulkhead.onComplete();
                    throw t;
                }
            } catch (Exception e) {
                log.error("Error reading from queue '{}', retrying in 1s: {}", queueName, e.getMessage(), e);
                backoff();
            }
        }

        log.info("QueueListener main loop exited for queue '{}'", queueName);
    }

    /**
     * Resolves the job-level bulkhead for a scan type, e.g. "Domain" →
     * "domain-jobs", "Repository" → "repository-jobs". Unrecognised or
     * missing scan types fall back to a "default-jobs" bulkhead rather
     * than bypassing rate limiting entirely.
     */
    private Bulkhead jobBulkhead(String scanType) {
        String name = "%s-jobs".formatted(scanType == null ? "default" : scanType.toLowerCase());
        log.trace("Resolved bulkhead '{}' for scanType '{}'", name, scanType);
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
        long startTime = System.currentTimeMillis();
        int attempts = 0;

        while (running) {
            attempts++;
            if (bulkhead.tryAcquirePermission()) {
                if (attempts > 1) {
                    log.info("Acquired permit for bulkhead '{}' after {} attempts ({}ms delayed)",
                            bulkhead.getName(), attempts, System.currentTimeMillis() - startTime);
                }
                return true;
            }
            log.trace("Bulkhead '{}' full, waiting {}ms before retry (attempt {})",
                    bulkhead.getName(), permitPollIntervalMs, attempts);
            try {
                Thread.sleep(permitPollIntervalMs);
            } catch (InterruptedException ie) {
                log.warn("Thread interrupted while waiting for permit on bulkhead '{}'", bulkhead.getName());
                Thread.currentThread().interrupt();
                return false;
            }
        }

        log.info("Aborting permit acquisition for bulkhead '{}' due to shutdown signal", bulkhead.getName());
        return false;
    }

    private void handle(ScanJob job, String raw, Bulkhead bulkhead) {
        long startTime = System.currentTimeMillis();
        log.info("Processing job [scanId={} domainId={} type={}]",
                job.scanId(), job.domainId(), job.scanType());

        try {

            JobProcessor processor = processors.get(job.scanType());
            if (processor == null) {
                log.warn("No processor registered for type '{}'. Known types: {}",
                        job.scanType(), processors.keySet());
                return;
            }

            log.debug("Dispatching job [scanId={}] to processor '{}'", job.scanId(), processor.getClass().getSimpleName());
            try {
                processor.process(job);
                log.info("Successfully processed job [scanId={} type={}] in {}ms",
                        job.scanId(), job.scanType(), System.currentTimeMillis() - startTime);
            } catch (Exception e) {
                log.error("Processor failed for job [scanId={} type={}] after {}ms: {}",
                        job.scanId(), job.scanType(), System.currentTimeMillis() - startTime, e.getMessage(), e);
            }
        } finally {
            // Always release, even if checkpointing/lookup failed above —
            // otherwise a malformed processor map entry would permanently
            // leak a concurrency slot.
            bulkhead.onComplete();
            log.debug("Released permit for bulkhead '{}' [scanId={}]", bulkhead.getName(), job.scanId());
        }
    }

    private ScanJob deserialize(String raw) {
        try {
            log.trace("Deserializing raw job payload: {}", raw);
            return mapper.readValue(raw, ScanJob.class);
        } catch (Exception e) {
            log.error("Failed to deserialize job payload, dropping message [length={}]: {}",
                    raw == null ? 0 : raw.length(), e.getMessage(), e);
            log.trace("Undeserializable payload: {}", raw);
            return null;

        }

    }

    public void stop() {
        log.info("Initiating shutdown of QueueListener...");
        running = false;
        executor.shutdown();
        log.debug("Awaiting executor termination (timeout={}s)...", SHUTDOWN_TIMEOUT_SECONDS);
        try {
            if (!executor.awaitTermination(SHUTDOWN_TIMEOUT_SECONDS, TimeUnit.SECONDS)) {
                log.warn("Executor did not terminate cleanly within {}s, forcing shutdownNow()",
                        SHUTDOWN_TIMEOUT_SECONDS);
                executor.shutdownNow();
            } else {
                log.info("Executor terminated cleanly");
            }
        } catch (InterruptedException e) {
            log.warn("Interrupted while awaiting executor termination, forcing shutdownNow()");
            Thread.currentThread().interrupt();
            executor.shutdownNow();
        }
        log.info("QueueListener shutdown sequence completed.");
    }

    private static void backoff() {
        log.debug("Backing off poll loop for 1 second due to error...");
        try {
            Thread.sleep(1000);
        } catch (InterruptedException ie) {
            log.warn("Backoff sleep interrupted");
            Thread.currentThread().interrupt();
        }
    }
}