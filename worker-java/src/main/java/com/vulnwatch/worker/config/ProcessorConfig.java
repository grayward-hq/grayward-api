package com.vulnwatch.worker.config;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.processor.DomainJobProcessor;
import com.vulnwatch.worker.processor.JobProcessor;
import com.vulnwatch.worker.processor.RepositoryJobProcessor;
import com.vulnwatch.worker.processor.RetryableProcessor;
import redis.clients.jedis.JedisPooled;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.util.Map;

/**
 * Wires up the processor and scanner maps.
 *
 * ── Adding a new ecosystem scanner ──────
 * 1. Implement DependencyScanner
 * 2. Annotate with @Component("manifest-filename") e.g. @Component("pom.xml")
 * Spring auto-collects all DependencyScanner beans into Map<String, DependencyScanner>
 * — no changes needed here.
 *
 * ── Adding a new job type ────
 * 1. Implement JobProcessor
 * 2. Register a @Bean below with the job type name as the key
 */
@Configuration
public class ProcessorConfig {

    @Bean("Repository")
    public JobProcessor repositoryOrchestrator(
            RepositoryJobProcessor processor,
            JedisPooled jedis,
            ObjectMapper mapper,
            @Value("${worker.dlq.key:dead-letter}") String dlqKey) {
        return new RetryableProcessor(processor, jedis, dlqKey, mapper);
    }

    @Bean("Domain")
    public JobProcessor domainOrchestrator(
            DomainJobProcessor processor,
            JedisPooled jedis,
            ObjectMapper mapper,
            @Value("${worker.dlq.key:dead-letter}") String dlqKey) {
        return new RetryableProcessor(processor, jedis, dlqKey, mapper);
    }

}