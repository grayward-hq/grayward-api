package com.vulnwatch.worker.state;

import com.vulnwatch.worker.enums.AiAvailability;
import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.state.SurfaceStateSnapshot;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;
import redis.clients.jedis.JedisPooled;

import java.time.Instant;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Redis-backed implementation of SurfaceStateManager.
 * Key structure (Redis hashes):
 *   surface-state:{scanId}:{surfaceType.name()}
 * Hash fields per surface:
 *   state         → SurfaceStatus name
 *   retryCount    → integer string
 *   failureReason → FailureReason name or empty string
 *   aiStatus      → AiAvailability name
 *   updatedAt     → ISO-8601 timestamp
 * TTL: controlled by worker.state.ttl-seconds (default 86400 = 24h).
 * All keys expire automatically — no manual cleanup needed in the happy path.
 * clear() is called explicitly on job completion to free memory immediately.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class RedisSurfaceStateManager implements SurfaceStateManager {

    private static final String KEY_PREFIX  = "surface-state:";
    private static final String F_STATE = "state";
    private static final String F_RETRY = "retryCount";
    private static final String F_REASON = "failureReason";
    private static final String F_AI_STATUS = "aiStatus";
    private static final String F_UPDATED = "updatedAt";

    private final JedisPooled jedis;

    @Value("${worker.state.ttl-seconds:86400}")
    private long ttlSeconds;


    @Override
    public void initSurfaces(String scanId, List<SurfaceType> surfaces) {
        for (SurfaceType surface : surfaces) {
            String key = key(scanId, surface);
            Map<String, String> fields = new HashMap<>();
            fields.put(F_STATE, SurfaceStatus.PENDING.name());
            fields.put(F_RETRY, "0");
            fields.put(F_REASON, "");
            fields.put(F_AI_STATUS, AiAvailability.AVAILABLE.name());
            fields.put(F_UPDATED, Instant.now().toString());

            jedis.hset(key, fields);
            jedis.expire(key, ttlSeconds);

            log.debug("Initialised surface state [scanId={} surface={}]", scanId, surface.name());
        }
    }



    @Override
    public void transition(String scanId, SurfaceType surface, SurfaceStatus newState) {
        String key = key(scanId, surface);
        Map<String, String> fields = new HashMap<>();
        fields.put(F_STATE,  newState.name());
        fields.put(F_UPDATED, Instant.now().toString());

        jedis.hset(key, fields);
        jedis.expire(key, ttlSeconds);  // reset TTL on every write

        log.debug("Surface transition [scanId={} surface={} → {}]",
                scanId, surface.name(), newState.name());
    }

    @Override
    public void transitionFailed(String scanId, SurfaceType surface,
                                 SurfaceStatus newState, FailureReason reason) {
        String key = key(scanId, surface);
        Map<String, String> fields = new HashMap<>();
        fields.put(F_STATE, newState.name());
        fields.put(F_REASON, reason.name());
        fields.put(F_UPDATED, Instant.now().toString());

        jedis.hset(key, fields);
        jedis.expire(key, ttlSeconds);

        log.warn("Surface failed [scanId={} surface={} state={} reason={}]",
                scanId, surface.name(), newState.name(), reason.name());
    }


    @Override
    public int incrementRetryCount(String scanId, SurfaceType surface) {
        String key = key(scanId, surface);
        // HINCRBY is atomic — safe under concurrent access
        long newCount = jedis.hincrBy(key, F_RETRY, 1);
        jedis.hset(key, F_UPDATED, Instant.now().toString());
        jedis.expire(key, ttlSeconds);

        log.debug("Retry count incremented [scanId={} surface={} retryCount={}]",
                scanId, surface.name(), newCount);

        return (int) newCount;
    }


    @Override
    public SurfaceStateSnapshot getSnapshot(String scanId, SurfaceType surface) {
        String key = key(scanId, surface);
        Map<String, String> fields = jedis.hgetAll(key);

        if (fields == null || fields.isEmpty()) {
            log.warn("No state found in Redis [scanId={} surface={}] — returning PENDING",
                    scanId, surface.name());
            return new SurfaceStateSnapshot(
                    surface, SurfaceStatus.PENDING, 0, null,
                    AiAvailability.AVAILABLE, Instant.now().toString());
        }

        return toSnapshot(surface, fields);
    }

    @Override
    public Map<SurfaceType, SurfaceStateSnapshot> getAllSnapshots(String scanId) {
        Map<SurfaceType, SurfaceStateSnapshot> result = new HashMap<>();

        for (SurfaceType surface : SurfaceType.values()) {
            String key = key(scanId, surface);
            Map<String, String> fields = jedis.hgetAll(key);
            if (fields != null && !fields.isEmpty()) {
                result.put(surface, toSnapshot(surface, fields));
            }
        }

        return result;
    }

    @Override
    public boolean allTerminal(String scanId) {
        for (SurfaceType surface : SurfaceType.values()) {
            String key = key(scanId, surface);
            Map<String, String> fields = jedis.hgetAll(key);

            // Surface was registered but not terminal — still in progress
            if (fields != null && !fields.isEmpty()) {
                SurfaceStatus status = parseStatus(fields.get(F_STATE));
                if (!status.isTerminal()) {
                    return false;
                }
            }
        }
        return true;
    }


    @Override
    public void clear(String scanId) {
        for (SurfaceType surface : SurfaceType.values()) {
            String key = key(scanId, surface);
            jedis.del(key);
        }
        log.debug("Cleared surface state keys [scanId={}]", scanId);
    }


    private String key(String scanId, SurfaceType surface) {
        return "%s%s:%s".formatted(KEY_PREFIX, scanId, surface.name());
    }

    private SurfaceStateSnapshot toSnapshot(SurfaceType surface, Map<String, String> fields) {
        SurfaceStatus status  = parseStatus(fields.get(F_STATE));
        int retryCount = parseIntSafe(fields.get(F_RETRY));
        FailureReason reason  = parseReason(fields.get(F_REASON));
        AiAvailability aiStat = parseAiStatus(fields.get(F_AI_STATUS));
        String updatedAt = fields.getOrDefault(F_UPDATED, Instant.now().toString());

        return new SurfaceStateSnapshot(surface, status, retryCount, reason, aiStat, updatedAt);
    }

    private SurfaceStatus parseStatus(String value) {
        if (value == null || value.isBlank())
            return SurfaceStatus.PENDING;
        try {
            return SurfaceStatus.valueOf(value);
        } catch (IllegalArgumentException e) {
            log.warn("Unknown SurfaceStatus in Redis: '{}' — defaulting to PENDING", value);
            return SurfaceStatus.PENDING;
        }
    }

    private FailureReason parseReason(String value) {
        if (value == null || value.isBlank())
            return null;
        try {
            return FailureReason.valueOf(value);
        } catch (IllegalArgumentException e) {
            return FailureReason.UNKNOWN;
        }
    }

    private AiAvailability parseAiStatus(String value) {
        if (value == null || value.isBlank())
            return AiAvailability.AVAILABLE;
        try {
            return AiAvailability.valueOf(value);
        } catch (IllegalArgumentException e) {
            return AiAvailability.AVAILABLE;
        }
    }

    private int parseIntSafe(String value) {
        if (value == null || value.isBlank())
            return 0;
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException e) {
            return 0;
        }
    }
}
