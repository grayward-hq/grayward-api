package com.vulnwatch.worker.publisher;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.enums.ScanStatus;
import com.vulnwatch.worker.model.DomainIntel;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.stereotype.Component;

import java.time.Instant;

/**
 * Publishes domain intelligence results and processing status events to Redis queues.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainIntelPublisher {

    private final RedisTemplate<String, Object> redisTemplate;
    private final ObjectMapper mapper;

    @Value("${worker.domain.result.queue:scan-results}")
    private String resultQueue;

    /**
     * Strongly typed payload matching the consumer contract on the C# service.
     */
    private record ScanResultPayload(
            String scanId,
            String domainId,
            String domainName,
            String requestedBy,
            String status,
            int securityScore,
            String aiAvailability,
            String completedAt,
            String error
    ) {}

    public void publishSuccess(ScanJob job, DomainIntel result) {
        ScanResultPayload payload = buildPayload(
                job,
                ScanStatus.COMPLETED.getDisplayName(),
                result.securityScore(),
                result.aiAvailability().name(),
                ""
        );
        publish(payload);
    }

    public void publishFailure(ScanJob job, String errorMessage) {
        String safeError = errorMessage != null ? errorMessage : "Unknown error";
        ScanResultPayload payload = buildPayload(
                job,
                ScanStatus.FAILED.getDisplayName(),
                0,
                "UNKNOWN",
                safeError
        );
        publish(payload);
    }

    private ScanResultPayload buildPayload(ScanJob job, String status, int score, String aiAvailability, String error) {
        return new ScanResultPayload(
                job.scanId(),
                job.domainId(),
                job.domainName(),
                job.requestedBy(),
                status,
                score,
                aiAvailability,
                Instant.now().toString(),
                error
        );
    }

    private void publish(ScanResultPayload payload) {
        try {
            String json = mapper.writeValueAsString(payload);
            redisTemplate
                    .opsForList()
                    .rightPush(resultQueue, json);
            log.debug("Successfully published result event [queue={} scanId={}]", resultQueue, payload.scanId());
        } catch (JsonProcessingException e) {
            log.error("Failed to serialize scan result payload for scanId '{}': {}", payload.scanId(), e.getMessage(), e);
        } catch (Exception e) {
            log.error("Failed to push scan result to Redis queue [{}]: {}", resultQueue, e.getMessage(), e);
        }
    }
}