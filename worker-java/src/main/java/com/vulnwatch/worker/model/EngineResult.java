package com.vulnwatch.worker.model;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.payload.SurfacePayload;
import lombok.Builder;

import java.util.Map;

/**
 * Raw output from a single scan engine before AI enrichment.
 * technicalData is a typed payload of whatever the
 * engine discovered.
 */

public record EngineResult(
        SurfaceType surfaceType,          // Dns | Ssl | HttpHeaders
        boolean success,
        String errorMessage,     // null when success=true
        Map<String, Object> rawResult
) {
    public static EngineResult failure(SurfaceType surfaceType, String reason) {
        return new EngineResult(surfaceType, false, reason, null);
    }

    public static EngineResult success(SurfaceType surfaceType, Map<String, Object> rawResult){
        return new EngineResult(surfaceType, true, null, rawResult);
    }
}
