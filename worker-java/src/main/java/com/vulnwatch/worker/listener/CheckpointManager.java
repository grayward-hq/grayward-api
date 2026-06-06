package com.vulnwatch.worker.listener;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;
import redis.clients.jedis.JedisPooled;
import redis.clients.jedis.params.ScanParams;
import redis.clients.jedis.resps.ScanResult;

import java.net.InetAddress;
import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Crash-resume safety net for scan jobs.
 *
 * Redis key structure (hash):
 *   checkpoint:{scanId}
 *     payload → raw JSON string of the ScanJob
 *     startedAt → ISO-8601 timestamp
 *     workerId  → hostname:pid of the worker instance that picked it up
 *
 * Lifecycle:
 *   1. QueueListener pops a job from scan-jobs (BLPOP)
 *   2. CheckpointManager.mark() writes the hash BEFORE processing begins
 *   3. Processing runs (scan → enrich → persist → publish)
 *   4. CheckpointManager.clear() removes the hash AFTER successful publish
 *
 * On worker startup (WorkerRunner), recoverInProgress() scans for any
 * checkpoint keys still present — these are jobs that were in-flight
 * when the worker crashed. It re-queues them to the FRONT of scan-jobs
 * (LPUSH) so they are processed before any new jobs.
 *
 * TTL: worker.checkpoint.ttl-seconds (default 7200 = 2h).
 * Prevents orphaned keys if clear() is never reached (e.g. Redis itself crashes).
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class CheckpointManager {

    private static final String KEY_PREFIX = "checkpoint:";
    private static final String F_PAYLOAD = "payload";
    private static final String F_STARTED = "startedAt";
    private static final String F_WORKER = "workerId";

    private final JedisPooled jedis;
    private final ObjectMapper mapper;

    @Value("${worker.checkpoint.ttl-seconds:7200}")
    private long ttlSeconds;

    @Value("${worker.scanjob.queue:scan-jobs}")
    private String queueName;

    /**
     * Writes a checkpoint for the given job BEFORE processing begins.
     * Called by QueueListener immediately after deserialization succeeds.
     *
     * @param scanId scan ID, used as the Redis key suffix
     * @param rawPayload the raw JSON string popped from the queue
     */
    public void mark(String scanId, String rawPayload) {
        String key = key(scanId);

        Map<String, String> fields = Map.of(
                F_PAYLOAD, rawPayload,
                F_STARTED, Instant.now().toString(),
                F_WORKER,  workerId()
        );

        jedis.hset(key, fields);
        jedis.expire(key, ttlSeconds);

        log.debug("Checkpoint marked [scanId={} worker={}]", scanId, workerId());
    }


    /**
     * Removes the checkpoint after the job has been fully processed
     * and the result published to C#.
     * Called by DomainJobProcessor and RepositoryJobProcessor on success or
     * handled failure (DLQ + publish both completed).
     *
     * @param scanId scan ID of the completed job
     */
    public void clear(String scanId) {
        long deleted = jedis.del(key(scanId));
        if (deleted > 0) {
            log.debug("Checkpoint cleared [scanId={}]", scanId);
        } else {
            log.warn("Checkpoint clear called but key not found [scanId={}] — already cleared?", scanId);
        }
    }

    /**
     * Scans Redis for any checkpoint keys left over from a previous worker crash
     * and re-queues their payloads to the FRONT of the scan-jobs queue.
     *
     * Must be called by WorkerRunner BEFORE QueueListener.run() starts.
     * LPUSH is used so recovered jobs are processed before new incoming jobs.
     *
     * @return number of jobs recovered and re-queued
     */
    public int recoverInProgress() {
        List<String> orphanedKeys = scanCheckpointKeys();

        if (orphanedKeys.isEmpty()) {
            log.info("Checkpoint recovery: no orphaned jobs found");
            return 0;
        }

        log.warn("Checkpoint recovery: found {} orphaned job(s) — re-queuing", orphanedKeys.size());

        // Stream through keys, safely attempt to re-queue, and sum the successful counts
        int recovered = orphanedKeys.stream()
                .mapToInt(key -> {
                    try {
                        return requeue(key);
                    } catch (Exception e) {
                        log.error("Failed to recover checkpoint key '{}': {}", key, e.getMessage(), e);
                        return 0; // Return 0 so it doesn't break the sum calculation
                    }
                })
                .sum();

        log.info("Checkpoint recovery complete: {}/{} jobs re-queued", recovered, orphanedKeys.size());
        return recovered;
    }


    /**
     * Returns true if a checkpoint exists for the given scanId.
     * Used by tests and monitoring.
     */
    public boolean exists(String scanId) {
        return jedis.exists(key(scanId));
    }

    /**
     * Returns the raw payload stored in the checkpoint, or null if not found.
     */
    public String getPayload(String scanId) {
        return jedis.hget(key(scanId), F_PAYLOAD);
    }


    private int requeue(String key) {
        Map<String, String> fields = jedis.hgetAll(key);
        if (fields == null || fields.isEmpty()) {
            log.warn("Checkpoint key '{}' found but has no fields — skipping", key);
            return 0;
        }

        String rawPayload = fields.get(F_PAYLOAD);
        if (rawPayload == null || rawPayload.isBlank()) {
            log.warn("Checkpoint key '{}' has no payload field — skipping", key);
            jedis.del(key);  // clean up corrupt key
            return 0;
        }

        ScanJob job;
        try {
            job = mapper.readValue(rawPayload, ScanJob.class);
        } catch (JsonProcessingException e) {
            log.error("Checkpoint '{}' payload cannot be deserialized — dropping: {}", key, e.getMessage());
            jedis.del(key);  // unrecoverable, delete it
            return 0;
        }

        jedis.lpush(queueName, rawPayload);
        jedis.del(key); // Remove checkpoint — QueueListener will re-mark it on pickup

        log.info("Recovered job re-queued [scanId={} startedAt={} originalWorker={}]",
                job.scanId(),
                fields.getOrDefault(F_STARTED, "unknown"),
                fields.getOrDefault(F_WORKER, "unknown"));

        return 1;
    }

    /**
     * Uses Jedis SCAN (non-blocking, cursor-based) to find all checkpoint keys.
     * Never uses KEYS * — safe for production Redis.
    /**
     * Uses Jedis SCAN (non-blocking, cursor-based) to find all checkpoint keys.
     * Never uses KEYS * — safe for production Redis.
     */
    private List<String> scanCheckpointKeys() {
        List<String> found = new ArrayList<>();
        String cursor = ScanParams.SCAN_POINTER_START; // Use Jedis constant "0" for readability
        ScanParams params = new ScanParams()
                .match("%s*".formatted(KEY_PREFIX))
                .count(100);

        while (true) {
            ScanResult<String> result = jedis.scan(cursor, params);
            found.addAll(result.getResult());
            cursor = result.getCursor();

            if (cursor.equals(ScanParams.SCAN_POINTER_START)) {
                break;
            }
        }

        return found;
    }

    private String key(String scanId) {
        return KEY_PREFIX + scanId;
    }

    /**
     * Unique identifier for this worker instance.
     * hostname:PID — helps operators identify which instance was processing a job.
     */
    private String workerId() {
        try {
            String host = InetAddress.getLocalHost().getHostName();
            long pid = ProcessHandle.current().pid();
            return "%s:%d".formatted(host, pid);
        } catch (Exception e) {
            return "unknown";
        }
    }
}