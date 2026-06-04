package com.vulnwatch.worker.model;

import java.util.List;

/**
 * Structured JSON response from the Grok enrichment call.
 * The AI receives real engine outputs and returns analysis.
 */
public record AiResult(
        String title,
        String severity,
        String explanation,
        String cveId,
        List<String> remediationSteps,
        String certExpiry        // populated only for SSL, null otherwise
) {}
