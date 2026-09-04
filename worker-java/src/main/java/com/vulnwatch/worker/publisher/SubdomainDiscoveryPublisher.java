package com.vulnwatch.worker.publisher;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.config.QueueNames;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.stereotype.Component;

import java.time.Instant;
import java.util.List;

/**
 * Publishes a "subdomains discovered/refreshed" notification to a queue separate from
 * domainIntel. Purely informational — by the time this is called, SubdomainPersistence has
 * already upserted the rows into "Subdomains", so this exists only so the API can react
 * (e.g. a SignalR "12 new subdomains" toast) without needing to poll the table.
 *
 * Kept as its own record/queue rather than folding into DomainIntelPublisher's
 * ScanResultPayload, so neither contract has to grow fields the other doesn't need.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class SubdomainDiscoveryPublisher {

    private final RedisTemplate<String, Object> redisTemplate;
    private final ObjectMapper mapper;
    private final QueueNames queueNames;

    private record SubdomainSummary(
            String host,
            String riskSeverity
    ) {}

    private record SubdomainDiscoveryPayload(
            String scanId,
            String domainId,
            String domainName,
            String requestedBy,
            int count,
            List<SubdomainSummary> subdomains,
            String discoveredAt
    ) {}

    public void publish(ScanJob job, List<SubdomainFindings> discovered) {
        if (discovered == null || discovered.isEmpty()) {
            return;
        }

        List<SubdomainSummary> summaries = discovered.stream()
                .map(f -> new SubdomainSummary(
                        f.getRecord().host(),
                        (f.getRisk() != null ? f.getRisk() : FindingSeverity.NONE).name()))
                .toList();

        SubdomainDiscoveryPayload payload = new SubdomainDiscoveryPayload(
                job.scanId(),
                job.domainId(),
                job.domainName(),
                job.requestedBy(),
                summaries.size(),
                summaries,
                Instant.now().toString()
        );

        String queue = queueNames.subdomainDiscovery();
        try {
            String json = mapper.writeValueAsString(payload);
            redisTemplate.opsForList().rightPush(queue, json);
            log.debug("Published subdomain discovery event [queue={} scanId={} count={}]",
                    queue, payload.scanId(), summaries.size());
        } catch (JsonProcessingException e) {
            log.error("Failed to serialize subdomain discovery payload [scanId={}]: {}",
                    job.scanId(), e.getMessage(), e);
        } catch (Exception e) {
            // Never let a notification failure affect the parent domain scan's own result.
            log.error("Failed to push subdomain discovery event [queue={}]: {}", queue, e.getMessage(), e);
        }
    }
}