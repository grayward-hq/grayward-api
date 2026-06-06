package com.vulnwatch.worker.engine;

import com.vulnwatch.worker.engine.domain.DnsEngine;
import com.vulnwatch.worker.engine.domain.HttpEngine;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.SslEngine;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.concurrent.StructuredTaskScope;

/**
 * Runs DNS, SSL, and HTTP engines in parallel using Java 21 virtual threads.
 *
 * Uses {@link StructuredTaskScope.ShutdownOnFailure} — if any engine throws
 * an unhandled exception the scope is cancelled and the exception propagates.
 * Engine-level errors (timeouts, connection refused) are caught inside each
 * engine and returned as {@link EngineResult#failure} so the scan continues.
 */
@Slf4j
@Component
public class ParallelScanner {

    private final List<Scanner> engines = List.of(
            new DnsEngine(),
            new SslEngine(),
            new HttpEngine()
    );

    @SuppressWarnings("preview")
    public List<EngineResult> scan(ScanJob job) throws Exception {
        try (var scope = new StructuredTaskScope.ShutdownOnFailure()) {

            List<StructuredTaskScope.Subtask<EngineResult>> subtasks = engines.stream()
                    .map(engine -> scope.fork(() -> runEngine(engine, job)))
                    .toList();

            scope.join();
            scope.throwIfFailed();

            return subtasks.stream()
                    .map(StructuredTaskScope.Subtask::get)
                    .toList();
        }
    }

    private EngineResult runEngine(Scanner engine, ScanJob job) {
        log.debug("Engine starting [scanId={} engine={} domain={}]",
                job.scanId(), engine.surfaceType(), job.domainName());

        EngineResult result = engine.scan(job);

        log.debug("Engine finished [scanId={} engine={} success={}]",
                job.scanId(), engine.surfaceType(), result.success());

        return result;
    }
}