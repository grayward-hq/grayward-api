package com.vulnwatch.worker.engine.domain.subfinder.models;

import lombok.Builder;

@Builder
public record SubdomainRecord(
        String host,
        String input,
        String source
) {
}
