package com.vulnwatch.worker.engine.domain.testssl.models;

import com.vulnwatch.worker.enums.FindingSeverity;
import lombok.Builder;

@Builder
public record SslFindings(
        String id,
        String ip,
        String port,
        String finding,
        FindingSeverity severity
) {
}
