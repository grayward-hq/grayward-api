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

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

@Slf4j
@Component
@RequiredArgsConstructor
public class OWASPEvaluator {
    private static final int BASE_SCORE = 100;

    private final List<OWASPMappingRule> rules; // Spring injects all @Component implementations

    public OWASPEvaluationResult evaluate(String scanId, List<DomainFinding> findings, List<EngineResult> engineResults, List<AiResult> aiResults) {
        List<OWASPFindingMapping> mappings = new ArrayList<>();

        // One pass: pair each (finding, engineResult, aiResult) and apply all rules

        for (int i = 0; i < engineResults.size(); i++) {

            EngineResult engine = engineResults.get(i);

            AiResult ai = i < aiResults.size() ? aiResults.get(i) : null;

            DomainFinding finding = i < findings.size() ? findings.get(i) : null;

            if (finding == null || !engine.success()) continue;

            for (OWASPMappingRule rule : rules) {

                if (rule.matches(engine, ai)) {

                    FindingSeverity sev = rule.severity(engine, ai);

                    mappings.add(new OWASPFindingMapping(

                            finding.cveId(),

                            scanId,

                            rule.category(),

                            deriveStatus(sev),

                            sev,

                            rule.findingLabel(engine, ai)

                    ));

                }
            }
        }

        // Group by category, score all 10

        Map<OWASPCategory, List<OWASPFindingMapping>> byCategory =

                mappings.stream().collect(Collectors.groupingBy(OWASPFindingMapping::category));

        List<OWASPCategoryScore> categoryScores = Arrays.stream(OWASPCategory.values())

                .map(cat -> scoreCategory(cat, byCategory.getOrDefault(cat, List.of())))

                .toList();

        int overallScore = (int) categoryScores.stream()

                .mapToInt(OWASPCategoryScore::score)

                .average()

                .orElse(100.0);

        OWASPComplianceTier tier = OWASPComplianceTier.fromScore(overallScore);

        log.info("OWASP evaluation complete [scanId={} mappings={} score={} tier={}]",

                scanId, mappings.size(), overallScore, tier.getLabel());

        return new OWASPEvaluationResult(scanId, mappings, categoryScores, overallScore, tier);

    }

    private OWASPCategoryScore scoreCategory(

            OWASPCategory category, List<OWASPFindingMapping> mappings) {

        int score = mappings.stream()

                .mapToInt(m -> penalty(m.severity()))

                .reduce(BASE_SCORE, (acc, p) -> Math.max(0, acc - p));

        OWASPComplianceStatus status = mappings.isEmpty() ? OWASPComplianceStatus.COMPLIANT

                : mappings.stream().anyMatch(m -> m.severity().isAtLeast(FindingSeverity.MEDIUM))

                  ? OWASPComplianceStatus.NON_COMPLIANT

                  : OWASPComplianceStatus.PARTIAL;

        return new OWASPCategoryScore(category, status, score, mappings);

    }

    // OWASP-specific penalties — independent of FindingSeverity.deduction

    private int penalty(FindingSeverity severity) {

        return switch (severity) {

            case CRITICAL -> 40;

            case HIGH     -> 25;

            case MEDIUM   -> 15;

            case LOW      ->  5;

            default       ->  0;

        };

    }

    private OWASPComplianceStatus deriveStatus(FindingSeverity severity) {

        if (severity.isAtLeast(FindingSeverity.MEDIUM)) return OWASPComplianceStatus.NON_COMPLIANT;

        if (severity == FindingSeverity.LOW)             return OWASPComplianceStatus.PARTIAL;

        return OWASPComplianceStatus.COMPLIANT;

    }

}
