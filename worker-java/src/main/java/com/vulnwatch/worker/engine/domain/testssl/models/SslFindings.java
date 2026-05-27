package com.vulnwatch.worker.engine.domain.testssl.models;

import lombok.Builder;

@Builder
public record SslFindings(
        String id,
        String ip,
        String port,
        String finding,
        String severity
) {
}
