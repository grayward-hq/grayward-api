package com.vulnwatch.worker.gate;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.events.AiAvailabilityEvent;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;
import redis.clients.jedis.JedisPooled;

import java.time.Instant;

/**
 * Listens for AiAvailabilityEvent and writes the current AI status to a dedicated Redis key.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class AiAvailabilityGate {

    private final JedisPooled jedis;
    private final ObjectMapper mapper;

    @Value("${worker.ai.status.key:ai:status}")
    private String aiStatusKey;

    @Value("${worker.ai.status.ttl-seconds:300}")
    private long ttlSeconds;

    /**
     * Payload structure matched exactly to the C# gateway consumer contract.
     */
    private record AiStatusPayload(String availability, String reason, String updatedAt) {}

    @EventListener
    public void onAiAvailabilityChange(AiAvailabilityEvent event) {
        String availabilityName = event.availability().name();
        log.info("AI availability changed → {} | reason: {}", availabilityName, event.reason());

        try {
            AiStatusPayload payload = new AiStatusPayload(availabilityName, event.reason(), Instant.now().toString());
            String json = mapper.writeValueAsString(payload);

            // SETEX overwrites previous state atomically while maintaining a defensive TTL
            jedis.setex(aiStatusKey, ttlSeconds, json);
            log.info("AI status written to Redis [key={} availability={}]", aiStatusKey, availabilityName);

        } catch (JsonProcessingException e) {
            log.error("Failed to serialize AI availability payload for key '{}': {}", aiStatusKey, e.getMessage(), e);
        } catch (Exception e) {
            log.error("Failed to write AI status payload to Redis [key={}]: {}", aiStatusKey, e.getMessage(), e);
        }
    }
}