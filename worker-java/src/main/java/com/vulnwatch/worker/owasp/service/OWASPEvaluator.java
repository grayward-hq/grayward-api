package com.vulnwatch.worker.owasp.service;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.state.SurfaceStateSnapshot;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.enums.OWASPComplianceStatus;
import com.vulnwatch.worker.owasp.enums.OWASPComplianceTier;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import com.vulnwatch.worker.owasp.model.OWASPCategoryScore;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import com.vulnwatch.worker.owasp.model.OWASPFindingMapping;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.stream.Collectors;
import java.util.stream.IntStream;

/**
 * Evaluates raw scanning engine findings and translates them into targeted OWASP compliance mappings,
 * metrics scoring matrices, and corporate compliance tiers.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class OWASPEvaluator {

    private static final int BASE_SCORE = 100;
    private static final double DEFAULT_PERFECT_SCORE = 100.0;

    private static final int PENALTY_CRITICAL = 40;
    private static final int PENALTY_HIGH = 25;
    private static final int PENALTY_MEDIUM = 15;
    private static final int PENALTY_LOW = 5;
    private static final int PENALTY_NONE = 0;

    /**
     * Which OWASP category each surface's rule evaluates against. Mirrors the
     * surfaceType() checks inside each OWASPMappingRule implementation (see
     * owasp/rules/*.java) — kept here explicitly because OWASPMappingRule
     * doesn't expose its surface affinity, only its category(). If a surface
     * fails outright, we still know which category(ies) it was responsible
     * for and can avoid silently scoring them as compliant.
     *
     * NOTE: update this map if a new OWASPMappingRule/surface pairing is added.
     */
    private static final Map<SurfaceType, OWASPCategory> SURFACE_CATEGORY_MAP = Map.of(
            SurfaceType.SSL, OWASPCategory.CRYPTOGRAPHIC_FAILURES,
            SurfaceType.PORTS, OWASPCategory.BROKEN_ACCESS_CONTROL,
            SurfaceType.DEPENDENCY, OWASPCategory.VULNERABLE_COMPONENTS,
            SurfaceType.DNS, OWASPCategory.SECURITY_MISCONFIGURATION,
            SurfaceType.HTTP_HEADERS, OWASPCategory.SECURITY_MISCONFIGURATION
    );

    private final List<OWASPMappingRule> rules;

    public OWASPEvaluationResult evaluate(String scanId,
                                          List<DomainFinding> findings,
                                          List<EngineResult> engineResults,
                                          List<AiResult> aiResults) {

        // 1. Map individual scan logs to specialized OWASP context violations
        List<OWASPFindingMapping> mappings = mapToOwaspFindings(scanId, findings, engineResults, aiResults);

        // 2. Aggregate assessments across the standard OWASP Category Registry
        Map<OWASPCategory, List<OWASPFindingMapping>> mappingsByCategory = mappings.stream()
                .collect(Collectors.groupingBy(OWASPFindingMapping::category));

        List<OWASPCategoryScore> categoryScores = Arrays.stream(OWASPCategory.values())
                .map(cat -> scoreCategory(cat, mappingsByCategory
                        .getOrDefault(cat, Collections.emptyList())))
                .toList();

        // 2b. A failed surface must not be scored as if it were compliant.
        // Force any category whose surface didn't complete down to 0 / NON_COMPLIANT,
        // even if it had no findings to report (because it never got the chance to).
        Set<OWASPCategory> categoriesFromFailedSurfaces = (engineResults == null ? List.<EngineResult>of() : engineResults)
                .stream()
                .filter(e -> !e.success())
                .map(e -> SURFACE_CATEGORY_MAP.get(e.surfaceType()))
                .filter(Objects::nonNull)
                .collect(Collectors.toSet());

        List<OWASPCategoryScore> adjustedCategoryScores = applyFailedSurfaceOverrides(categoryScores, categoriesFromFailedSurfaces);

        if (!categoriesFromFailedSurfaces.isEmpty()) {
            log.warn("Zeroed OWASP categories due to failed surface scan(s) [scanId={} categories={}]",
                    scanId, categoriesFromFailedSurfaces);
        }

        // 3. Extrapolate global metrics and risk tier
        int overallScore = (int) adjustedCategoryScores.stream()
                .mapToInt(OWASPCategoryScore::score)
                .average()
                .orElse(DEFAULT_PERFECT_SCORE);

        OWASPComplianceTier tier = OWASPComplianceTier.fromScore(overallScore);

        log.info("OWASP evaluation complete [scanId={} mappings={} score={} tier={}]",
                scanId, mappings.size(), overallScore, tier.getLabel());

        return new OWASPEvaluationResult(scanId, mappings, adjustedCategoryScores, overallScore, tier);
    }

    private List<OWASPCategoryScore> applyFailedSurfaceOverrides(
            List<OWASPCategoryScore> categoryScores, Set<OWASPCategory> zeroedCategories) {
        return categoryScores.stream()
                .map(cs -> zeroedCategories.contains(cs.category())
                        ? new OWASPCategoryScore(cs.category(), OWASPComplianceStatus.NON_COMPLIANT, 0, cs.findings())
                        : cs)
                .toList();
    }

    /**
     * Re-derives which OWASP categories must still be treated as failed/unknown,
     * based on live Redis surface state rather than the in-memory EngineResult list
     * from the original run. Used when recalculating a scan's score after one
     * surface has been manually replayed from the DLQ — other surfaces may still
     * be sitting in PERMANENTLY_FAILED and must keep their category zeroed out.
     */
    public Set<OWASPCategory> categoriesStillFailed(Map<SurfaceType, SurfaceStateSnapshot> surfaceSnapshots) {
        Set<OWASPCategory> failed = new HashSet<>();
        for (Map.Entry<SurfaceType, SurfaceStateSnapshot> entry : surfaceSnapshots.entrySet()) {
            if (entry.getValue().status() == SurfaceStatus.PERMANENTLY_FAILED) {
                OWASPCategory cat = SURFACE_CATEGORY_MAP.get(entry.getKey());
                if (cat != null)
                    failed.add(cat);
            }
        }
        return failed;
    }

    /**
     * Builds fresh OWASPFindingMapping rows for a single, just-recovered surface.
     * Only needs the one EngineResult that was replayed — does NOT require
     * re-fetching every other surface's raw engine output, since those are
     * unaffected and already persisted in "OwaspMappings".
     */
    public List<OWASPFindingMapping> mapRecoveredSurface(String scanId, EngineResult engine, AiResult ai, DomainFinding finding) {
        if (finding == null || engine == null || !engine.success()) {
            return Collections.emptyList();
        }
        ScopedTuple tuple = new ScopedTuple(engine, ai, finding);
        return rules.stream()
                .filter(rule -> rule.matches(tuple.engine(), tuple.ai()))
                .map(rule -> createMapping(scanId, tuple, rule))
                .toList();
    }

    /**
     * Recomputes all 10 category scores from ground truth after a surface recovery:
     * severities already persisted in the DB for categories with real data, and a
     * forced 0 for any category still tied to a surface sitting in the DLQ.
     */
    public List<OWASPCategoryScore> recomputeCategoryScores(
            Map<OWASPCategory, List<FindingSeverity>> dbSeveritiesByCategory,
            Set<OWASPCategory> stillFailedCategories) {

        return Arrays.stream(OWASPCategory.values())
                .map(cat -> {
                    if (stillFailedCategories.contains(cat)) {
                        return new OWASPCategoryScore(cat, OWASPComplianceStatus.NON_COMPLIANT, 0, Collections.emptyList());
                    }
                    List<FindingSeverity> severities = dbSeveritiesByCategory.getOrDefault(cat, Collections.emptyList());
                    int score = severities.stream()
                            .mapToInt(this::getPenalty)
                            .reduce(BASE_SCORE, (accumulator, penalty) -> Math.max(0, accumulator - penalty));
                    OWASPComplianceStatus status = severities.isEmpty()
                            ? OWASPComplianceStatus.COMPLIANT
                            : (severities.stream().anyMatch(s -> s.isAtLeast(FindingSeverity.MEDIUM))
                            ? OWASPComplianceStatus.NON_COMPLIANT : OWASPComplianceStatus.PARTIAL);
                    return new OWASPCategoryScore(cat, status, score, Collections.emptyList());
                })
                .toList();
    }

    /** Averages category scores into a single overall score (same rule as evaluate()). */
    public int overallScoreOf(List<OWASPCategoryScore> categoryScores) {
        return (int) categoryScores.stream()
                .mapToInt(OWASPCategoryScore::score)
                .average()
                .orElse(DEFAULT_PERFECT_SCORE);
    }

    private List<OWASPFindingMapping> mapToOwaspFindings(String scanId,
                                                         List<DomainFinding> findings,
                                                         List<EngineResult> engines,
                                                         List<AiResult> ais) {
        if (engines == null || engines.isEmpty()) {
            return Collections.emptyList();
        }

        return IntStream.range(0, engines.size())
                .mapToObj(i -> {
                    EngineResult engine = engines.get(i);
                    AiResult ai = (ais != null && i < ais.size()) ? ais.get(i) : null;
                    DomainFinding finding = (findings != null && i < findings.size()) ? findings.get(i) : null;
                    return new ScopedTuple(engine, ai, finding);
                })
                .filter(tuple -> tuple.finding() != null && tuple.engine().success())
                .flatMap(tuple -> rules.stream()
                        .filter(rule -> rule.matches(tuple.engine(), tuple.ai()))
                        .map(rule -> createMapping(scanId, tuple, rule)))
                .collect(Collectors.toList());
    }

    private OWASPFindingMapping createMapping(String scanId, ScopedTuple tuple, OWASPMappingRule rule) {
        FindingSeverity severity = rule.severity(tuple.engine(), tuple.ai());
        String label = rule.findingLabel(tuple.engine(), tuple.ai());

        return new OWASPFindingMapping(
                tuple.finding().id(),
                tuple.finding().cveId(),
                scanId,
                rule.category(),
                deriveStatus(severity),
                severity,
                label
        );
    }

    private OWASPCategoryScore scoreCategory(OWASPCategory category, List<OWASPFindingMapping> mappings) {
        int score = mappings.stream()
                .mapToInt(m -> getPenalty(m.severity()))
                .reduce(BASE_SCORE, (accumulator, penalty) -> Math.max(0, accumulator - penalty));

        OWASPComplianceStatus status = deriveCategoryStatus(mappings);

        return new OWASPCategoryScore(category, status, score, mappings);
    }

    private OWASPComplianceStatus deriveCategoryStatus(List<OWASPFindingMapping> mappings) {
        if (mappings.isEmpty()) {
            return OWASPComplianceStatus.COMPLIANT;
        }

        boolean hasSevereFlaws = mappings.stream()
                .anyMatch(m -> m.severity().isAtLeast(FindingSeverity.MEDIUM));

        return hasSevereFlaws ? OWASPComplianceStatus.NON_COMPLIANT : OWASPComplianceStatus.PARTIAL;
    }

    private int getPenalty(FindingSeverity severity) {
        if (severity == null) return PENALTY_NONE;
        return switch (severity) {
            case CRITICAL -> PENALTY_CRITICAL;
            case HIGH -> PENALTY_HIGH;
            case MEDIUM -> PENALTY_MEDIUM;
            case LOW -> PENALTY_LOW;
            default -> PENALTY_NONE;
        };
    }

    private OWASPComplianceStatus deriveStatus(FindingSeverity severity) {
        if (severity == null)
            return OWASPComplianceStatus.COMPLIANT;
        if (severity.isAtLeast(FindingSeverity.MEDIUM))
            return OWASPComplianceStatus.NON_COMPLIANT;
        if (severity == FindingSeverity.LOW)
            return OWASPComplianceStatus.PARTIAL;
        return OWASPComplianceStatus.COMPLIANT;
    }


    private record ScopedTuple(EngineResult engine, AiResult ai, DomainFinding finding) {}
}