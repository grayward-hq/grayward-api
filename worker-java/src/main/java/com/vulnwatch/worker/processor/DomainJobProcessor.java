package com.vulnwatch.worker.processor;

import com.vulnwatch.worker.ai.domain.SpringAiDomainEnricher;
import com.vulnwatch.worker.engine.ParallelScanner;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.DomainIntel;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.persistence.DomainPersistence;
import com.vulnwatch.worker.publisher.DomainIntelPublisher;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Objects;

/**
 * Orchestrates a full domain scan:
 *   1. AI description (best-effort)
 *   2. Parallel DNS / SSL / HTTP engine scan
 *   3. Per-result AI enrichment
 *   4. Security score computation
 *   5. Persistence to PostgreSQL
 *   6. Redis publish for downstream consumers
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainJobProcessor implements JobProcessor {

    private final ParallelScanner scanner;
    private final SpringAiDomainEnricher enricher;
    private final DomainPersistence persistence;
    private final DomainIntelPublisher publisher;

    @Override
    public void process(ScanJob job) {
        log.info("Starting domain scan [scanId={} domain={}]", job.scanId(), job.domainName());

        describeJob(job);
        List<EngineResult> engineResults = runEngines(job);
        List<AiResult> enrichments = enrichResults(job, engineResults);
        int score = computeScore(enrichments);

        log.info("Security score [scanId={} score={}]", job.scanId(), score);

        List<DomainFinding> findings = persistence.saveFindings(
                                                            job.scanId(),
                                                            job.domainId(),
                                                            engineResults,
                                                            enrichments,
                                                            score);

        if (findings.isEmpty()) {
            log.warn("No findings persisted [scanId={}]", job.scanId());
            return;
        }

        publisher.publishSuccess(job, DomainIntel.of(job, score));
        log.info("Domain scan complete [scanId={}]", job.scanId());
    }



    /** Best-effort AI description — failure is logged but never fatal. */
    private void describeJob(ScanJob job) {
        try {
            String description = enricher.describe(job);
            if (description != null) {
                log.info("Job description [scanId={}]: {}", job.scanId(), description);
            }
        } catch (Exception e) {
            log.warn("Could not generate description [scanId={}]: {}", job.scanId(), e.getMessage());
        }
    }

    /** Runs DNS, SSL, and HTTP engines in parallel. Throws to trigger a retry on failure. */
    private List<EngineResult> runEngines(ScanJob job) {
        try {
            return scanner.scan(job);
        } catch (Exception e) {
            log.error("Parallel scan failed [scanId={}]", job.scanId(), e);
            throw new RuntimeException("Parallel scan failed for scanId=%s".formatted(job.scanId()), e);
        }
    }

    /** Enriches each engine result via AI sequentially to stay within rate limits. */
    private List<AiResult> enrichResults(ScanJob job, List<EngineResult> engineResults) {
        return engineResults.stream()
                .map(result -> enricher.enrich(job, result))
                .toList(); // null entries handled gracefully by persistence layer
    }



    private int computeScore(List<AiResult> enrichments) {
        int deductions = enrichments.stream()
                .filter(Objects::nonNull)
                .mapToInt(e -> switch (e.severity()) {
                    case "Critical" -> 30;
                    case "High" -> 20;
                    case "Medium" -> 10;
                    case "Low" ->  5;
                    default  ->  0;
                })
                .sum();
        return Math.max(0, 100 - deductions);
    }
}