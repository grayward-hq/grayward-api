package com.vulnwatch.worker.config;

import com.vulnwatch.worker.enums.AiAvailability;
import com.vulnwatch.worker.events.AiAvailabilityEvent;
import io.github.resilience4j.circuitbreaker.CircuitBreaker;
import io.github.resilience4j.circuitbreaker.CircuitBreakerConfig;
import io.github.resilience4j.circuitbreaker.CircuitBreakerRegistry;
import io.github.resilience4j.circuitbreaker.event.CircuitBreakerOnStateTransitionEvent;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Duration;

/**
 * Registers a Resilience4j CircuitBreaker that wraps all AI enrichment calls.
 */
@Slf4j
@Configuration
public class AiCircuitBreakerConfig {

    public static final String AI_ENRICHER_CB = "aiEnricher";

    @Value("${worker.ai.cb.sliding-window-size:10}")
    private int slidingWindowSize;

    @Value("${worker.ai.cb.failure-rate-threshold:50.0}")
    private float failureRateThreshold;

    @Value("${worker.ai.cb.wait-duration-seconds:30}")
    private long waitDurationSeconds;

    @Value("${worker.ai.cb.half-open-calls:3}")
    private int halfOpenCalls;

    @Bean
    public CircuitBreakerRegistry circuitBreakerRegistry(ApplicationEventPublisher eventPublisher) {

        CircuitBreakerConfig config = CircuitBreakerConfig.custom()
                .slidingWindowType(CircuitBreakerConfig.SlidingWindowType.COUNT_BASED)
                .slidingWindowSize(slidingWindowSize)
                .failureRateThreshold(failureRateThreshold)
                .waitDurationInOpenState(Duration.ofSeconds(waitDurationSeconds))
                .permittedNumberOfCallsInHalfOpenState(halfOpenCalls)
                .recordExceptions(Exception.class)
                .build();

        CircuitBreakerRegistry registry = CircuitBreakerRegistry.of(config);

        // Bind lifecycle event publisher actions
        registry.circuitBreaker(AI_ENRICHER_CB)
                .getEventPublisher()
                .onStateTransition(event -> handleStateTransition(event, eventPublisher));

        return registry;
    }

    @Bean
    public CircuitBreaker aiCircuitBreaker(CircuitBreakerRegistry registry) {
        return registry.circuitBreaker(AI_ENRICHER_CB);
    }

    private void handleStateTransition(
            CircuitBreakerOnStateTransitionEvent event,
            ApplicationEventPublisher eventPublisher) {

        AiAvailability availability = switch (event.getStateTransition()) {
            case CLOSED_TO_OPEN, HALF_OPEN_TO_OPEN  -> AiAvailability.UNAVAILABLE;
            case HALF_OPEN_TO_CLOSED, FORCED_OPEN_TO_CLOSED -> AiAvailability.AVAILABLE;
            default  -> AiAvailability.DEGRADED;
        };

        String reason = "Circuit breaker transition: %s → %s"
                .formatted(event.getStateTransition().getFromState(), event.getStateTransition().getToState());

        log.warn("AI circuit breaker state change [{}]: {}", availability.name(), reason);
        eventPublisher.publishEvent(new AiAvailabilityEvent(availability, reason));
    }
}