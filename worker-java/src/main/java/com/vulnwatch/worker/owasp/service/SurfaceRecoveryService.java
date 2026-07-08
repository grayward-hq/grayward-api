package com.vulnwatch.worker.owasp.service;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.enums.OWASPComplianceTier;
import com.vulnwatch.worker.owasp.model.OWASPCategoryScore;
import com.vulnwatch.worker.owasp.model.OWASPFindingMapping;
import com.vulnwatch.worker.persistence.DomainPersistence;
import com.vulnwatch.worker.persistence.OWASPPersistence;
import com.vulnwatch.worker.state.SurfaceStateManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Handles the "manually replayed a DLQ'd surface and it passed" path.
 *
 * A surface that exhausted its retries earlier gets its OWASP category forced
 * to a 0 score (see OWASPEvaluator) so the scan doesn't look falsely compliant.
 * If someone later pulls that surface off the surface-dead-letter queue,
 * re-runs it by hand, and it now succeeds, the old zeroed score is stale and
 * misleading — this service brings the scan's score back in line with reality
 * WITHOUT re-running every other surface again:
 *
 *   1. Persist a fresh Finding row for the recovered surface (replacing the
 *      old "probe failed" one).
 *   2. Generate + upsert OWASP mappings for just that surface's findings.
 *   3. Mark the surface SUCCESS in Redis.
 *   4. Recompute all 10 category scores from ground truth: DB-persisted
 *      severities for categories with real data, plus a forced 0 for any
 *      OTHER category still tied to a surface that's still sitting in the DLQ.
 *   5. Overwrite the scan's overall score/tier.
 *
 * Entry point for whatever replays the DLQ (ops script, admin endpoint, or a
 * future automated surface-DLQ consumer) — call recoverSurface() once the
 * replayed scan for that single surface comes back successful.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class SurfaceRecoveryService {

    private final OWASPEvaluator owaspEvaluator;
    private final OWASPPersistence owaspPersistence;
    private final DomainPersistence domainPersistence;
    private final SurfaceStateManager surfaceStateManager;

    public void recoverSurface(String scanId, EngineResult freshResult, AiResult enrichment) {
        if (freshResult == null || !freshResult.success()) {
            log.warn("recoverSurface called without a successful result — nothing to recover [scanId={}]", scanId);
            return;
        }

        SurfaceType surface = freshResult.surfaceType();

        // 1. Swap the stale failure row for the real result.
        DomainFinding finding = domainPersistence.replaceFindingForSurface(scanId, surface, freshResult, enrichment);

        // 2. Build + upsert fresh OWASP mappings for this surface only.
        List<OWASPFindingMapping> newMappings = owaspEvaluator.mapRecoveredSurface(scanId, freshResult, enrichment, finding);
        owaspPersistence.saveFindingMappings(newMappings);

        // 3. This surface is no longer failed.
        SurfaceStatus recoveredState = enrichment != null ? SurfaceStatus.SUCCESS : SurfaceStatus.SUCCESS_NO_AI;
        surfaceStateManager.transition(scanId, surface, recoveredState);

        // 4. Recompute every category, DB rows for surfaces
        // that have real data, zero for any surface still stuck in the DLQ.
        Map<OWASPCategory, List<FindingSeverity>> dbSeverities = owaspPersistence.fetchSeveritiesByCategory(scanId);
        Set<OWASPCategory> stillFailed = owaspEvaluator.categoriesStillFailed(surfaceStateManager.getAllSnapshots(scanId));

        List<OWASPCategoryScore> categoryScores = owaspEvaluator.recomputeCategoryScores(dbSeverities, stillFailed);
        int overallScore = owaspEvaluator.overallScoreOf(categoryScores);
        OWASPComplianceTier tier = OWASPComplianceTier.fromScore(overallScore);

        // 5. Persist the corrected score.
        owaspPersistence.updateScanScore(scanId, overallScore, tier);

        log.info("Recalculated OWASP score after surface recovery [scanId={} surface={} newMappings={} newScore={} tier={}]",
                scanId, surface, newMappings.size(), overallScore, tier.getLabel());
    }
}