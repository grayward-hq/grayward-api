package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;


/**
 * Pluggable scanner contract for domain surface scanning.
 *
 * To add a new scanner:
 *   1. Implement this interface
 *   2. Annotate @Component
 *   3. Done — processors picks it up via List<Scanner> injection.
 *      No other class needs to change.
 */
public interface Scanner {

    /** Which surface this scanner covers. Used for state tracking. */
    SurfaceType surfaceType();

    /**
     * Executes the scan. Must never throw — catch all exceptions internally
     * and return EngineResult.failure(...) so the retry policy handles them.
     */
    EngineResult scan(ScanJob job);
}

