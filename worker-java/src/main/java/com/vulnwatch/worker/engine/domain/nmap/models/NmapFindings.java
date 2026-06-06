package com.vulnwatch.worker.engine.domain.nmap.models;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Severity;
import com.vulnwatch.worker.enums.FindingSeverity;
import lombok.Builder;

@Builder
public record NmapFindings(
        int port,
        String protocol,
        String service,
        String finding,
        FindingSeverity severity
) {
    public static NmapFindings x11Findings(int port, String protocol, String service){
        return NmapFindings.builder()
                .port(port)
                .protocol(protocol)
                .service("X11 service exposed")
                .severity(FindingSeverity.CRITICAL)
                .build();
    }
}
