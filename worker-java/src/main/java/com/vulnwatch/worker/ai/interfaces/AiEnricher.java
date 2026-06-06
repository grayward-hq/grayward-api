package com.vulnwatch.worker.ai.interfaces;

import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;

public interface AiEnricher {
    /**
     * Enriches a single engine surface result.
     * Returns null when AI is unavailable — callers must handle gracefully.
     */
    AiResult enrich(ScanJob job, EngineResult engineResult);

    /**
     * Generates a plain-English scan-start description.
     * Returns null on failure — always best-effort.
     */
    String describe(ScanJob job);
}
