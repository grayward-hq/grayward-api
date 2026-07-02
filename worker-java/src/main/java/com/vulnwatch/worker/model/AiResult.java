package com.vulnwatch.worker.model;

import java.util.List;

/**
 * Structured JSON response from the Ai enrichment call.
 * The AI receives real engine outputs and returns analysis.
 */
public record AiResult(
        String explanation,
        List<String> remediationSteps,
        String certExpiry        // populated only for SSL surfaces, null otherwise
) {}