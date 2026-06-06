package com.vulnwatch.worker.state;

import com.vulnwatch.worker.enums.ScanStatus;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.state.SurfaceStateSnapshot;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;
import redis.clients.jedis.JedisPooled;
import java.util.List;
import java.util.Map;

/**
 * Drives job-level state transitions for a scan.
 *
 * Job state key (Redis string):
 *   scan-state:{scanId}  →  ScanStatus name
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
 * This is Redis-only coordination state for the worker's own use.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class ScanJobStateMachine {

    private static final String KEY_PREFIX = "scan-state:";

    private final JedisPooled jedis;
    private final SurfaceStateManager surfaceStateManager;

    @Value("${worker.state.ttl-seconds:86400}")
    private long ttlSeconds;


    /**
     * Marks the job as SCANNING and initialises all domain surfaces.
     * Called by DomainJobProcessor immediately after checkpoint.mark().
     */
    public void start(String scanId, List<SurfaceType> surfaces) {
        write(scanId, ScanStatus.RUNNING);

        surfaceStateManager.initSurfaces(scanId, surfaces);

        log.info("Job started [scanId={} state=RUNNING, surfaces={}]", scanId, surfaces);
    }

    /**
     * Derives the terminal job state from all surface states and writes it.
     * Called by ScanOrchestrator once all surface virtual threads complete.
     *
     * Rules:
     *   - All surfaces SUCCESS/SUCCESS_NO_AI → COMPLETED
     *   - Mix of success and PERMANENTLY_FAILED → COMPLETED (partial results still published)
     *   - All surfaces PERMANENTLY_FAILED → FAILED
     */
    public ScanStatus advance(String scanId) {
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

        write(scanId, derived);

        log.info("Job advanced [scanId={} total={} succeeded={} failed={} → {}]",
                scanId, total, succeeded, failed, derived.name());

        return derived;
    }

    /**
     * Marks the job FAILED immediately — used when a fatal unrecoverable
     * error occurs before any surface processing begins (e.g. deserialization failure).
     */
    public void fail(String scanId) {
        write(scanId, ScanStatus.FAILED);
        log.error("Job marked FAILED [scanId={}]", scanId);
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


    private void write(String scanId, ScanStatus status) {
        String key = KEY_PREFIX + scanId;
        jedis.set(key, status.name());
        jedis.expire(key, ttlSeconds);
    }
}