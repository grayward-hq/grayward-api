package com.vulnwatch.worker.engine.domain.nuclei.models;

public record NucleiEngineResult(
        String templateId,
        String url,
        String host,
        String ip,
        String issue,
        String severity,
        String headerType
) {

}
