package com.vulnwatch.worker.engine.domain.nmap.models;

import lombok.Builder;

@Builder
public record NmapFindings(
        int port,
        String protocol,
        String service,
        String finding
) {
    public static NmapFindings x11Findings(int port, String protocol, String service){
        return NmapFindings.builder()
                .port(port)
                .protocol(protocol)
                .service("X11 service exposed")
                .build();
    }
}
