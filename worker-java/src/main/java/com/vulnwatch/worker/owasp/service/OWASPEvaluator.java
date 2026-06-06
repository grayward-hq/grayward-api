package com.vulnwatch.worker.owasp.service;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.EngineResult;
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
import java.util.List;
import java.util.Map;
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

        // 3. Extrapolate global metrics and risk tier
        int overallScore = (int) categoryScores.stream()
                .mapToInt(OWASPCategoryScore::score)
                .average()
                .orElse(DEFAULT_PERFECT_SCORE);

        OWASPComplianceTier tier = OWASPComplianceTier.fromScore(overallScore);

        log.info("OWASP evaluation complete [scanId={} mappings={} score={} tier={}]",
                scanId, mappings.size(), overallScore, tier.getLabel());

        return new OWASPEvaluationResult(scanId, mappings, categoryScores, overallScore, tier);
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