package com.vulnwatch.worker.config;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Configuration;

@Configuration
public class QueueNames {

    @Value("${app.env:local}")
    private String env;

    @Value("${worker.scanjob.queue:scan-jobs}")
    private String scanJobsBase;

    @Value("${worker.domain.result.queue:domain-intel}")
    private String domainIntelBase;

    @Value("${worker.repository.result.queue:repository-intel}")
    private String repositoryIntelBase;

    @Value("${worker.dlq.key:dead-letter}")
    private String dlqBase;

    @Value("${worker.ai.status.key:ai:status}")
    private String aiStatusBase;

    @Value("${worker.state.channel:scan-state-events}")
    private String scanStateChannelBase;

    // Notification-only channel: fired when a Domain-scoped scan's SUBDOMAINS surface
    // discovers/refreshes hosts. Separate from domainIntel() on purpose — the worker has
    // already persisted the Subdomains rows by the time this publishes (see
    // SubdomainPersistence#upsertDiscovered), so this exists purely so the API can react
    // (e.g. push a "N new subdomains" notice) without polling.
    @Value("${worker.subdomain.discovery.queue:subdomain-discovery}")
    private String subdomainDiscoveryBase;

    public String scanJobs() { return prefixed(scanJobsBase); }
    public String domainIntel() { return prefixed(domainIntelBase); }
    public String repositoryIntel() { return prefixed(repositoryIntelBase); }
    public String dlq() { return prefixed(dlqBase); }
    public String aiStatus() { return prefixed(aiStatusBase); }
    public String scanStateChannel() { return prefixed(scanStateChannelBase); }
    public String subdomainDiscovery() { return prefixed(subdomainDiscoveryBase); }

    private String prefixed(String base) {
        return env.equals("local") ? base : env + ":" + base;
    }
}