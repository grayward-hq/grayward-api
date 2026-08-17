package com.vulnwatch.worker.state;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.config.QueueNames;
import com.vulnwatch.worker.enums.ScanStatus;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.events.StateTransitionEvent;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.model.state.SurfaceStateSnapshot;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;
import redis.clients.jedis.JedisPooled;

import java.time.Instant;
import java.util.List;
import java.util.Map;

/**
 * Drives job-level state transitions for a scan.
 *
 * Job state key (Redis string, worker-internal, used for coordination/polling):
 *   scan-state:{scanId}  →  ScanStatus name
 *
 * As of this change, every transition is ALSO published on a Redis Pub/Sub
 * channel (see QueueNames#scanStateChannel) so the API can push it straight
 * to the requesting user over SignalR instead of polling this key or
 * GET /scans/{id}/report. The key remains the source of truth for
 * getState()/CheckpointManager; the publish is a best-effort notification —
 * a failed publish never fails the scan.
 *
 * Lifecycle:
 *   QUEUED → SCANNING → COMPLETED
 *                     ↘ PARTIALLY_COMPLETED  (some surfaces DLQ'd)
 *                     ↘ FAILED               (all surfaces DLQ'd)
 *
 * The job state is derived from surface states — it never advances
 * independently. ScanOrchestrator calls advance() after all surfaces
 * reach a terminal state.
 *
 * Does NOT update the Postgres "Scans" table — that remains the
 * responsibility of DomainPersistence, which already does it correctly.
 * This is Redis-only coordination state for the worker's own use, plus
 * the live notification channel described above.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class ScanJobStateMachine {

    @Value("${worker.state.scan-key-prefix:scan-state:}")
    private String KEY_PREFIX;

    private final JedisPooled jedis;
    private final SurfaceStateManager surfaceStateManager;
    private final QueueNames queueNames;
    private final ObjectMapper mapper;

    @Value("${worker.state.ttl-seconds:86400}")
    private long ttlSeconds;

    /**
     * Marks the job as SCANNING and initialises all domain surfaces.
     * Called by DomainJobProcessor immediately after checkpoint.mark().
     */
    public void start(ScanJob job, List<SurfaceType> surfaces) {
        write(job, ScanStatus.RUNNING);

        surfaceStateManager.initSurfaces(job.scanId(), surfaces);

        log.info("Job started [scanId={} state=RUNNING, surfaces={}]", job.scanId(), surfaces);
    }

    /**
     * Derives the terminal job state from all surface states and writes it.
     * Called by ScanOrchestrator once all surface virtual threads complete.
     * <p>
     * Rules:
     * - All surfaces SUCCESS/SUCCESS_NO_AI → COMPLETED
     * - Mix of success and PERMANENTLY_FAILED → COMPLETED (partial results still published)
     * - All surfaces PERMANENTLY_FAILED → FAILED
     */
    public void advance(ScanJob job) {
        String scanId = job.scanId();
        Map<SurfaceType, SurfaceStateSnapshot> snapshots =
                surfaceStateManager.getAllSnapshots(scanId);

        long total = snapshots.size();
        long failed = snapshots.values().stream()
                .filter(s -> s.status() == SurfaceStatus.PERMANENTLY_FAILED)
                .count();
        long succeeded = snapshots.values().stream()
                .filter(s -> s.status().isSuccess())
                .count();

        ScanStatus derived;
        if (failed == 0) {
            derived = ScanStatus.COMPLETED;
        } else if (succeeded == 0) {
            derived = ScanStatus.FAILED;
        } else {
            // partial — some succeeded, some DLQ'd
            // still publish COMPLETED so C# gets partial findings
            derived = ScanStatus.COMPLETED;
        }

        write(job, derived);

        log.info("Job advanced [scanId={} total={} succeeded={} failed={} → {}]",
                scanId, total, succeeded, failed, derived.name());
    }

    /**
     * Marks the job FAILED immediately — used when a fatal unrecoverable
     * error occurs before any surface processing begins (e.g. deserialization failure).
     */
    public void fail(ScanJob job) {
        write(job, ScanStatus.FAILED);
        log.error("Job marked FAILED [scanId={}]", job.scanId());
    }

    /**
     * Returns the current job-level state.
     * Returns QUEUED if no state exists (safe default).
     */
    public ScanStatus getState(String scanId) {
        String raw = jedis.get(KEY_PREFIX + scanId);
        if (raw == null || raw.isBlank())
            return ScanStatus.QUEUED;
        try {
            return ScanStatus.valueOf(raw);
        } catch (IllegalArgumentException e) {
            log.warn("Unknown ScanStatus in Redis: '{}' — defaulting to QUEUED", raw);
            return ScanStatus.QUEUED;
        }
    }

    /**
     * Removes the job state key. Called by CheckpointManager after
     * the job is fully complete and the C# payload has been published.
     */
    public void clear(String scanId) {
        jedis.del(KEY_PREFIX + scanId);
        log.debug("Cleared job state key [scanId={}]", scanId);
    }

    private void write(ScanJob job, ScanStatus status) {
        String key = KEY_PREFIX + job.scanId();
        jedis.set(key, status.name());
        jedis.expire(key, ttlSeconds);
        publishTransition(job, status);
    }

    /**
     * Best-effort: a publish failure (Redis Pub/Sub hiccup, serialization
     * error) must never fail the scan itself — the key write above already
     * happened and remains the source of truth.
     */
    private void publishTransition(ScanJob job, ScanStatus status) {
        try {
            StateTransitionEvent event = new StateTransitionEvent(
                    job.scanId(),
                    job.domainId(),
                    job.domainName(),
                    job.requestedBy(),
                    status.name(),
                    Instant.now().toString()
            );
            String json = mapper.writeValueAsString(event);
            jedis.publish(queueNames.scanStateChannel(), json);
        } catch (Exception e) {
            log.warn("Failed to publish state transition [scanId={} status={}]: {}",
                    job.scanId(), status, e.getMessage());
        }
    }
}