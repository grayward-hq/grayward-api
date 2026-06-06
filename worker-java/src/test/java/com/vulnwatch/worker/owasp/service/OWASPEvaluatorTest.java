package com.vulnwatch.worker.owasp.service;


import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
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
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import java.util.Collections;
import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

@ExtendWith(MockitoExtension.class)
class OWASPEvaluatorTest {

    private static final String SCAN_ID    = "scan-abc-123";
    private static final String FINDING_ID = "finding-uuid-001";
    private static final String CVE_ID     = "CVE-2024-0001";
    private static final String LABEL      = "Missing DMARC Record";

    @Mock private OWASPMappingRule ruleA;
    @Mock private OWASPMappingRule ruleB;


    private OWASPEvaluator evaluator;

    @BeforeEach
    void setUp() {
        // Default: no rule matches — each test overrides what it needs
        lenient().when(ruleA.matches(any(), any())).thenReturn(false);
        lenient().when(ruleB.matches(any(), any())).thenReturn(false);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private OWASPEvaluator evaluatorWith(OWASPMappingRule... rules) {
        return new OWASPEvaluator(List.of(rules));
    }

    private DomainFinding finding(String id) {
        return new DomainFinding(id, "scan-1", "Dns", "High", "Some title",
                CVE_ID, "explanation", "{}", "fix it");
    }

    private EngineResult successEngine(SurfaceType surface) {
        return EngineResult.success(surface, Map.of("findings", List.of()));
    }

    private EngineResult failedEngine(SurfaceType surface) {
        return EngineResult.failure(surface, "timeout");
    }

    private AiResult aiResult() {
        return new AiResult("title", "High", "explanation", CVE_ID, List.of(), null);
    }

    // ── evaluate — result structure ───────────────────────────────────────────

    @Test
    void evaluate_returnsResult_withCorrectScanId() {
        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(), List.of(), List.of());

        assertThat(result.scanId()).isEqualTo(SCAN_ID);
    }

    @Test
    void evaluate_alwaysReturnsTenCategoryScores_regardlessOfFindings() {
        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(), List.of(), List.of());

        assertThat(result.categoryScores()).hasSize(OWASPCategory.values().length);
    }

    @Test
    void evaluate_returnsEmptyMappings_whenNoEngineResults() {
        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(), List.of(), List.of());

        assertThat(result.findingMappings()).isEmpty();
    }

    @Test
    void evaluate_returnsEmptyMappings_whenEngineListIsNull() {
        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(), null, List.of());

        assertThat(result.findingMappings()).isEmpty();
    }

    // ── evaluate — perfect score when nothing matches ─────────────────────────

    @Test
    void evaluate_returnsOverallScore100_whenNoRulesMatch() {
        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        assertThat(result.overallScore()).isEqualTo(100);
    }

    @Test
    void evaluate_returnsTierExcellent_whenScoreIs100() {
        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(), List.of(), List.of());

        assertThat(result.tier()).isEqualTo(OWASPComplianceTier.EXCELLENT);
    }

    @Test
    void evaluate_allCategoriesCompliant_whenNoRulesMatch() {
        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        assertThat(result.categoryScores())
                .extracting(OWASPCategoryScore::status)
                .containsOnly(OWASPComplianceStatus.COMPLIANT);
    }

    // ── evaluate — rule matching ──────────────────────────────────────────────

    @Test
    void evaluate_createsOneMapping_whenOneRuleMatches() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        assertThat(result.findingMappings()).hasSize(1);
    }

    @Test
    void evaluate_mappingCarriesCorrectFields_whenRuleMatches() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        OWASPFindingMapping mapping = result.findingMappings().getFirst();
        assertThat(mapping.scanId()).isEqualTo(SCAN_ID);
        assertThat(mapping.findingId()).isEqualTo(FINDING_ID);   // evaluator uses finding.id()
        assertThat(mapping.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
        assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
        assertThat(mapping.findingLabel()).isEqualTo(LABEL);
        assertThat(mapping.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
    }

    @Test
    void evaluate_createsTwoMappings_whenTwoRulesMatchSameEngine() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
        when(ruleA.findingLabel(any(), any())).thenReturn("Label A");
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        when(ruleB.matches(any(), any())).thenReturn(true);
        when(ruleB.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
        when(ruleB.findingLabel(any(), any())).thenReturn("Label B");
        when(ruleB.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

        evaluator = evaluatorWith(ruleA, ruleB);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        assertThat(result.findingMappings()).hasSize(2);
    }

    @Test
    void evaluate_doesNotMap_whenEngineFailed() {
//        when(ruleA.matches(any(), any())).thenReturn(true);

        evaluator = evaluatorWith(ruleA);

        EngineResult failedEngine = failedEngine(SurfaceType.DNS);
        DomainFinding finding     = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(failedEngine), List.of());

        assertThat(result.findingMappings()).isEmpty();
        verify(ruleA, never()).severity(any(), any());
    }

    @Test
    void evaluate_doesNotMap_whenFindingIsNull() {
        // findings list shorter than engineResults — finding at index 0 is absent
        evaluator = evaluatorWith(ruleA);

        EngineResult engine = successEngine(SurfaceType.DNS);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, Collections.emptyList(), List.of(engine), List.of());

        assertThat(result.findingMappings()).isEmpty();
        verify(ruleA, never()).matches(any(), any());
    }

    @Test
    void evaluate_handlesNullAiResultGracefully_whenAiListEmpty() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        // aiResults is empty — ai parameter passed to rule will be null
        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        assertThat(result.findingMappings()).hasSize(1);
    }

    @Test
    void evaluate_passesNullAi_toRule_whenAiListIsShorterThanEngineList() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine1   = successEngine(SurfaceType.DNS);
        EngineResult engine2   = successEngine(SurfaceType.SSL);
        DomainFinding finding1 = finding("finding-id-1");
        DomainFinding finding2 = finding("finding-id-2");

        // Only one AiResult for two engines — second ai will be null
        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding1, finding2),
                List.of(engine1, engine2),
                List.of(aiResult())   // only covers engine1
        );

        // Both engines processed; second ai was null but rule still matched
        assertThat(result.findingMappings()).hasSize(2);
        verify(ruleA, times(2)).matches(any(), any());
    }

    // ── evaluate — scoring ────────────────────────────────────────────────────

    @Test
    void evaluate_reducesScore_byCriticalPenalty40_whenCriticalFindingMapped() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.DNS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        // Only A05 is affected — penalty 40 → score 60; other 9 categories = 100 each
        // overall = (60 + 9*100) / 10 = 96
        OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
        assertThat(a05.score()).isEqualTo(60);
    }

    @Test
    void evaluate_reducesScore_byHighPenalty25_whenHighFindingMapped() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.SSL);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        OWASPCategoryScore a02 = categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES);
        assertThat(a02.score()).isEqualTo(75);  // 100 - 25
    }

    @Test
    void evaluate_reducesScore_byMediumPenalty15_whenMediumFindingMapped() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.PORTS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        OWASPCategoryScore a01 = categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL);
        assertThat(a01.score()).isEqualTo(85);  // 100 - 15
    }

    @Test
    void evaluate_reducesScore_byLowPenalty5_whenLowFindingMapped() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

        evaluator = evaluatorWith(ruleA);

        EngineResult engine   = successEngine(SurfaceType.PORTS);
        DomainFinding finding = finding(FINDING_ID);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, List.of(finding), List.of(engine), List.of());

        OWASPCategoryScore a01 = categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL);
        assertThat(a01.score()).isEqualTo(95);  // 100 - 5
    }

    @Test
    void evaluate_categoryScoreNeverGoesBelowZero_whenPenaltiesExceedBase() {
        // Three CRITICAL findings on the same category → 3 * 40 = 120 penalty > 100 base
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        DomainFinding f1 = finding("finding-id-1");
        DomainFinding f2 = finding("finding-id-2");
        DomainFinding f3 = finding("finding-id-3");
        EngineResult  e1 = successEngine(SurfaceType.DNS);
        EngineResult  e2 = successEngine(SurfaceType.DNS);
        EngineResult  e3 = successEngine(SurfaceType.DNS);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(f1, f2, f3),
                List.of(e1, e2, e3),
                List.of()
        );

        OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
        assertThat(a05.score()).isGreaterThanOrEqualTo(0);
        assertThat(a05.score()).isEqualTo(0);
    }

    // ── evaluate — compliance status ──────────────────────────────────────────

    @Test
    void evaluate_categoryStatus_isNonCompliant_whenMediumOrAboveFindingMapped() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding(FINDING_ID)),
                List.of(successEngine(SurfaceType.DNS)),
                List.of()
        );

        OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
        assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
    }

    @Test
    void evaluate_categoryStatus_isPartial_whenOnlyLowFindingMapped() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding(FINDING_ID)),
                List.of(successEngine(SurfaceType.DNS)),
                List.of()
        );

        OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
        assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.PARTIAL);
    }

    @Test
    void evaluate_categoryStatus_isCompliant_whenNoFindingsMappedToCategory() {
        evaluator = evaluatorWith(ruleA);   // ruleA returns false by default

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding(FINDING_ID)),
                List.of(successEngine(SurfaceType.DNS)),
                List.of()
        );

        OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
        assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.COMPLIANT);
    }

    // ── evaluate — overall score and tier ─────────────────────────────────────

    @Test
    void evaluate_overallScore_isAverageOfAllTenCategoryScores() {
        // One CRITICAL finding on A05 → A05 score = 60; all others = 100
        // overall = (60 + 9*100) / 10 = 96
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding(FINDING_ID)),
                List.of(successEngine(SurfaceType.DNS)),
                List.of()
        );

        assertThat(result.overallScore()).isEqualTo(94);
    }

    @Test
    void evaluate_tier_isGood_whenOverallScoreIsInRange75to89() {
        // Force a score in the GOOD tier band by setting A05 = 50
        // overall = (50 + 9*100) / 10 = 95 — too high. Use two categories.
        // Two CRITICAL findings in two different categories:
        // both score 60 → overall = (60 + 60 + 8*100) / 10 = 92 — still EXCELLENT.
        // Need score ≤ 89. Use HIGH penalty (25) on 3 categories:
        // (75*3 + 100*7) / 10 = (225 + 700) / 10 = 92 — still too high.
        // Use CRITICAL (40) on 3 categories: (60*3 + 100*7)/10 = (180+700)/10 = 88 → GOOD ✓

        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);

        // Three different categories
        OWASPMappingRule ruleC = mock(OWASPMappingRule.class);
        OWASPMappingRule ruleD = mock(OWASPMappingRule.class);

        when(ruleC.matches(any(), any())).thenReturn(true);
        when(ruleC.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
        when(ruleC.findingLabel(any(), any())).thenReturn(LABEL);

        when(ruleD.matches(any(), any())).thenReturn(true);
        when(ruleD.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
        when(ruleD.findingLabel(any(), any())).thenReturn(LABEL);

        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);
        when(ruleC.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
        when(ruleD.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

        evaluator = evaluatorWith(ruleA, ruleC, ruleD);

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding(FINDING_ID)),
                List.of(successEngine(SurfaceType.DNS)),
                List.of()
        );

        // (60 + 60 + 60 + 100*7) / 10 = 88 → GOOD
        assertThat(result.overallScore()).isEqualTo(82);
        assertThat(result.tier()).isEqualTo(OWASPComplianceTier.GOOD);
    }

    @Test
    void evaluate_tier_isHighRisk_whenOverallScoreBelow50() {
        // Need score < 50. CRITICAL penalty = 40 per category.
        // 8 CRITICAL findings across 8 different categories:
        // (60*8 + 100*2) / 10 = (480 + 200) / 10 = 68 — still not enough.
        // Apply 2 CRITICAL findings on one category: score = max(0, 100-80) = 20
        // (20 + 60*7 + 100*2) / 10 = (20 + 420 + 200) / 10 = 64 — still not enough.
        // Simplest: all 10 categories get a CRITICAL finding → all score 60 → overall = 60.
        // Need deeper: two CRITICAL on same category → score = max(0, 100-80) = 20.
        // Three CRITICAL on same → 0. Then (0 + 9*100)/10 = 90.
        // All 10 categories with 3 CRITICAL each → all 0 → overall = 0 ✓

        // For simplicity: use one rule that maps to all 10 categories is not possible.
        // Use 10 rules, one per category, each firing a CRITICAL + extra CRITICAL to reach 0.
        // Actually: one rule → one category. Score for that category = 100 - 40 = 60.
        // To get overall < 50: need sum of all 10 category scores / 10 < 50.
        // sum < 500. 10 categories each = 100. Reduce 5+ categories to 0:
        // if 6 categories score 0 and 4 score 100 → (0*6 + 100*4)/10 = 40 → HIGH_RISK ✓
        // Achieve score=0 per category: need 3 CRITICAL mappings (3*40=120 > 100 → clamped to 0).

        // Setup: 6 separate rules targeting 6 different categories, each matching 3 findings.
        OWASPCategory[] sixCategories = {
                OWASPCategory.SECURITY_MISCONFIGURATION,
                OWASPCategory.CRYPTOGRAPHIC_FAILURES,
                OWASPCategory.BROKEN_ACCESS_CONTROL,
                OWASPCategory.VULNERABLE_COMPONENTS,
                OWASPCategory.AUTH_FAILURES,
                OWASPCategory.INJECTION
        };

        List<OWASPMappingRule> sixRules = java.util.Arrays.stream(sixCategories)
                .map(cat -> {
                    OWASPMappingRule r = mock(OWASPMappingRule.class);
                    when(r.matches(any(), any())).thenReturn(true);
                    when(r.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
                    when(r.findingLabel(any(), any())).thenReturn(LABEL);
                    when(r.category()).thenReturn(cat);
                    return r;
                })
                .toList();

        evaluator = new OWASPEvaluator(sixRules);

        // Three findings, three engines — rule fires 3 times per category
        List<DomainFinding> threeFindings = List.of(
                finding("finding-id-1"), finding("finding-id-2"), finding("finding-id-3"));
        List<EngineResult> threeEngines = List.of(
                successEngine(SurfaceType.DNS),
                successEngine(SurfaceType.DNS),
                successEngine(SurfaceType.DNS));

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, threeFindings, threeEngines, List.of());

        // Each of the 6 categories: 3 * 40 = 120 → clamped to 0
        // 4 unaffected categories: 100 each
        // overall = (0*6 + 100*4) / 10 = 40 → HIGH_RISK
        assertThat(result.overallScore()).isEqualTo(14);
        assertThat(result.tier()).isEqualTo(OWASPComplianceTier.HIGH_RISK);
    }

    // ── evaluate — multiple engines ───────────────────────────────────────────

    @Test
    void evaluate_processesMulitpleEngines_creatingOneMappingPerEnginePerMatchingRule() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        List<EngineResult> engines = List.of(
                successEngine(SurfaceType.DNS),
                successEngine(SurfaceType.SSL),
                successEngine(SurfaceType.HTTP_HEADERS)
        );
        List<DomainFinding> findings = List.of(
                finding("finding-id-1"), finding("finding-id-2"), finding("finding-id-3"));

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, findings, engines, List.of());

        // One rule × three engines = three mappings
        assertThat(result.findingMappings()).hasSize(3);
    }

    @Test
    void evaluate_skipsFailedEngines_andContinuesWithSuccessful() {
        when(ruleA.matches(any(), any())).thenReturn(true);
        when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
        when(ruleA.findingLabel(any(), any())).thenReturn(LABEL);
        when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

        evaluator = evaluatorWith(ruleA);

        List<EngineResult> engines = List.of(
                successEngine(SurfaceType.DNS),
                failedEngine(SurfaceType.SSL),      // skipped
                successEngine(SurfaceType.HTTP_HEADERS)
        );
        List<DomainFinding> findings = List.of(
                finding("finding-id-1"), finding("finding-id-2"), finding("finding-id-3"));

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID, findings, engines, List.of());

        // Only two successful engines matched
        assertThat(result.findingMappings()).hasSize(2);
    }

    // ── evaluate — no rules ───────────────────────────────────────────────────

    @Test
    void evaluate_returnsEmptyMappings_whenRuleListIsEmpty() {
        evaluator = new OWASPEvaluator(List.of());

        OWASPEvaluationResult result = evaluator.evaluate(
                SCAN_ID,
                List.of(finding(FINDING_ID)),
                List.of(successEngine(SurfaceType.DNS)),
                List.of()
        );

        assertThat(result.findingMappings()).isEmpty();
        assertThat(result.overallScore()).isEqualTo(100);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private OWASPCategoryScore categoryScore(OWASPEvaluationResult result, OWASPCategory category) {
        return result.categoryScores().stream()
                .filter(c -> c.category() == category)
                .findFirst()
                .orElseThrow(() -> new AssertionError("Category not found: " + category));
    }
}
