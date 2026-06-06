package com.vulnwatch.worker.queue;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.events.ScannerExhaustedEvent;
import com.vulnwatch.worker.model.state.SurfaceStateSnapshot;
import com.vulnwatch.worker.state.SurfaceStateManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;
import redis.clients.jedis.JedisPooled;

import java.time.Instant;

/**
 * Listens for ScannerExhaustedEvent and pushes a rich payload to the surface-level DLQ.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DeadLetterQueueHandler {

    private final JedisPooled jedis;
    private final SurfaceStateManager surfaceStateManager;
    private final ObjectMapper mapper;

    @Value("${worker.surface.dlq.key:surface-dead-letter}")
    private String dlqKey;

    /**
     * Strongly typed payload matching the contract expected by C# or monitoring systems.
     */
    private record JobSummary(
            String scanId,
            String domainId,
            String domainName,
            String requestedBy,
            String scanType
    ) {}

    private record DlqPayload(
            String type,
            String scanId,
            String surfaceType,
            int retryCount,
            String failureReason,
            String errorMessage,
            String aiAvailableAtTime,
            String surfaceState,
            JobSummary job,
            String enqueuedAt
    ) {}

    /**
     * Receives ScannerExhaustedEvent and pushes a rich DLQ entry.
     */
    @EventListener
    public void onScannerExhausted(ScannerExhaustedEvent event) {
        String scanId = event.job().scanId();
        String surfaceName = event.surfaceType().name();

        log.warn("Surface exhausted — pushing to DLQ [scanId={} surface={} retries={} reason={}]",
                scanId, surfaceName, event.retryCount(), event.failureReason());

        try {
            // Read the full surface snapshot from Redis for enriched metadata
            SurfaceStateSnapshot snapshot = surfaceStateManager.getSnapshot(scanId, event.surfaceType());

            String payloadJson = buildPayload(event, snapshot);
            jedis.rpush(dlqKey, payloadJson);

            log.error("Surface successfully pushed to DLQ [scanId={} surface={} dlq={}]",
                    scanId, surfaceName, dlqKey);

        } catch (JsonProcessingException e) {
            log.error("CRITICAL: Failed to serialize surface DLQ payload [scanId={} surface={}]: {}",
                    scanId, surfaceName, e.getMessage(), e);
        } catch (Exception e) {
            // DLQ push failure must never propagate — it would kill the virtual thread context
            log.error("CRITICAL: Failed to push surface to DLQ infrastructure [scanId={} surface={}]: {}",
                    scanId, surfaceName, e.getMessage(), e);
        }
    }

    private String buildPayload(ScannerExhaustedEvent event, SurfaceStateSnapshot snapshot) throws JsonProcessingException {
        var job = event.job();

        JobSummary jobSummary = new JobSummary(
                job.scanId(),
                job.domainId(),
                job.domainName(),
                job.requestedBy(),
                job.scanType()
        );

        String aiAvailability = snapshot.aiAvailability() != null ? snapshot.aiAvailability().name() : "UNKNOWN";

        DlqPayload payload = new DlqPayload(
                SurfaceStatus.PERMANENTLY_FAILED.name(),
                job.scanId(),
                event.surfaceType().name(),
                snapshot.retryCount(),
                event.failureReason().name(),
                event.errorMessage(),
                aiAvailability,
                snapshot.status().name(),
                jobSummary,
                Instant.now().toString()
        );

        return mapper.writeValueAsString(payload);
    }
}