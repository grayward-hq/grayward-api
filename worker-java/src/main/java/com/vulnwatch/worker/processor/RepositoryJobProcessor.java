package com.vulnwatch.worker.processor;

import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.orchestrator.RepositoryScanOrchestrator;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

/**
 * Handles jobs where ScanJob.scanType() == "Repository".
 *
 * Delegates entirely to RepositoryScanOrchestrator, which resolves the
 * repo's real GitHub identity, mints auth if private, runs Trivy with
 * retry, enriches findings, and persists — all self-contained in the
 * worker.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class RepositoryJobProcessor implements JobProcessor {

    private final RepositoryScanOrchestrator orchestrator;

    @Override
    public void process(ScanJob job) {
        log.info("[{}] Starting repository scan for repoId={}", job.scanId(), job.repoId());
        orchestrator.scan(job);
    }
}