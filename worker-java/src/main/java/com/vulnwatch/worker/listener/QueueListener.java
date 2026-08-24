package com.vulnwatch.worker.listener;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.config.QueueNames;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.processor.JobProcessor;
import jakarta.annotation.PostConstruct;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import redis.clients.jedis.JedisPooled;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.time.Duration;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Semaphore;
import java.util.concurrent.TimeUnit;

/**
 * Blocks on a Redis queue and dispatches incoming scan jobs to the
 * appropriate {@link JobProcessor} on a virtual-thread executor.
 * stopped gracefully via {@link #stop()} on shutdown.
 *
 * <p>Three safeguards exist because their absence took production down for six days: a restart
 * found 1,902 stale jobs, dequeued the lot at once, and ran roughly 20 CPU-bound threads on a
 * 2-core box at a load average of 6.34.</p>
 *
 * <ul>
 *   <li><b>Bounded concurrency.</b> A permit is taken <em>before</em> the blocking pop, not before
 *       the submit. Virtual threads are unbounded by design, which is excellent for I/O and wrong
 *       for scanning, which is CPU-bound. Gating only the submit would still drain the whole queue
 *       into memory as fast as Redis could serve it; gating the pop leaves an over-full queue in
 *       Redis, where it stays visible and recoverable.</li>
 *   <li><b>Staleness.</b> A job older than the cutoff is discarded rather than run. Scanning a
 *       domain someone asked about six weeks ago has no value, and the API's ScanReaper has already
 *       marked those rows Failed. The two were drifting apart because nothing removed the Redis
 *       copy.</li>
 *   <li><b>Backlog warning at startup.</b> A large queue on boot is the shape of the incident, so
 *       it is surfaced immediately rather than inferred later from CPU graphs.</li>
 * </ul>
 */
@Slf4j
@RequiredArgsConstructor
@Component
public class QueueListener implements Runnable {

    private static final int SHUTDOWN_TIMEOUT_SECONDS = 10;

    @Value("${worker.blpop.timeout:5}")
    private int blpopTimeout;

    /** Cap on jobs in flight. Scanning is CPU-bound, so this should track cores, not connections. */
    @Value("${worker.scan.max-concurrent:4}")
    private int maxConcurrent;

    /** Jobs older than this are discarded on dequeue. Zero or negative disables the check. */
    @Value("${worker.scan.max-age-hours:24}")
    private long maxAgeHours;

    /** Queue depth at startup that is worth shouting about. */
    @Value("${worker.scan.backlog-warn-threshold:50}")
    private long backlogWarnThreshold;

    private final QueueNames queueNames;
    private String queueName;

    private final JedisPooled jedis;
    private final Map<String, JobProcessor> processors;
    private final ObjectMapper mapper;
    private final CheckpointManager checkpointManager;
    private final ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();

    private Semaphore inFlight;

    private volatile boolean running = true;

    @PostConstruct
    void init() {
        this.queueName = queueNames.scanJobs();
        this.inFlight = new Semaphore(Math.max(1, maxConcurrent));
        warnOnStartupBacklog();
    }

    /**
     * Reports queue depth on boot. Deliberately only logs: silently flushing a backlog would destroy
     * work nobody has agreed to discard, and the staleness check already stops old jobs being run.
     * This exists so the condition is noticed within seconds instead of days.
     */
    private void warnOnStartupBacklog() {
        try {
            long depth = jedis.llen(queueName);
            if (depth > backlogWarnThreshold) {
                log.warn("Queue '{}' holds {} job(s) at startup, above the warning threshold of {}. "
                                + "Jobs older than {}h are discarded on dequeue; at most {} run at once.",
                        queueName, depth, backlogWarnThreshold, maxAgeHours, maxConcurrent);
            } else {
                log.info("Queue '{}' holds {} job(s) at startup.", queueName, depth);
            }
        } catch (Exception e) {
            log.warn("Could not read depth of queue '{}' at startup: {}", queueName, e.getMessage());
        }
    }

    @Override
    public void run() {
        log.info("QueueListener started — blocking on queue '{}' (max {} concurrent, discarding jobs older than {}h)",
                queueName, maxConcurrent, maxAgeHours);

        while (running) {
            boolean acquired = false;
            try {
                // Take the permit before popping. Holding back the pop is what keeps a backlog in
                // Redis rather than in this process's memory.
                inFlight.acquire();
                acquired = true;

                List<String> result = jedis.blpop(blpopTimeout, queueName);
                if (result == null) {
                    inFlight.release();     // normal timeout, keep polling
                    acquired = false;
                    continue;
                }

                String payload = result.get(1);
                executor.submit(() -> {
                    try {
                        handle(payload);
                    } finally {
                        inFlight.release();
                    }
                });
                acquired = false;           // ownership passed to the task

            } catch (InterruptedException ie) {
                Thread.currentThread().interrupt();
                running = false;
            } catch (Exception e) {
                log.error("Error reading from queue '{}', retrying in 1s: {}", queueName, e.getMessage());
                backoff();
            } finally {
                // Only true if the pop or the submit threw while the permit was still held.
                if (acquired) inFlight.release();
            }
        }

        log.info("QueueListener stopped.");
    }

    private void handle(String raw) {
        ScanJob job = deserialize(raw);
        if (job == null)
            return;

        if (isStale(job)) {
            // Not checkpointed on purpose: a checkpoint would have WorkerRunner re-queue this on the
            // next boot, which is the loop this exists to break. The API's ScanReaper has already
            // failed the corresponding row.
            log.warn("Discarding stale job [scanId={} domainId={} enqueuedAt={}] — older than {}h",
                    job.scanId(), job.domainId(), job.enqueuedAt(), maxAgeHours);
            return;
        }

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
    }

    /**
     * Fails open: an absent or unparseable timestamp is treated as fresh. Dropping a legitimate job
     * over a format quirk is worse than running one job that is older than it looks, and the
     * concurrency cap bounds the cost either way.
     */
    private boolean isStale(ScanJob job) {
        if (maxAgeHours <= 0) return false;

        String enqueuedAt = job.enqueuedAt();
        if (enqueuedAt == null || enqueuedAt.isBlank()) return false;

        Instant queuedAt;
        try {
            queuedAt = Instant.parse(enqueuedAt);
        } catch (Exception primary) {
            try {
                // .NET's "O" format carries an offset rather than a trailing Z when the DateTime is
                // not UTC, which Instant.parse rejects.
                queuedAt = OffsetDateTime.parse(enqueuedAt).toInstant();
            } catch (Exception fallback) {
                log.warn("Unparseable enqueuedAt '{}' [scanId={}] — treating the job as fresh",
                        enqueuedAt, job.scanId());
                return false;
            }
        }

        return Duration.between(queuedAt, Instant.now()).toHours() >= maxAgeHours;
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
