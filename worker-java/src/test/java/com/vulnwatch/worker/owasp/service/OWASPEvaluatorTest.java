package com.vulnwatch.worker.owasp.service;

import com.vulnwatch.worker.enums.AiAvailability;
import com.vulnwatch.worker.enums.FailureReason;
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
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Nested;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Set;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

/**
 * Unit tests for OWASPEvaluator.
 *
 * Organisation:
 *   evaluate()  — result structure, rule matching, scoring, tier derivation, failed surfaces
 *   categoriesStillFailed() — DLQ-replay Redis-state derivation
 *   mapRecoveredSurface()   — single-surface mapping after DLQ replay
 *   recomputeCategoryScores() — full score recomputation from DB severities post-recovery
 *   overallScoreOf()  — averaging helper
 *
 * Penalty table (matches OWASPEvaluator constants):
 *   CRITICAL → 40   HIGH → 25   MEDIUM → 15   LOW → 5   NONE → 0
 *
 * Category count: 7 (OWASPCategory.values().length)
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("OWASPEvaluator")
class OWASPEvaluatorTest {

    // ── shared constants ──────────────────────────────────────────────────────

    private static final String SCAN_ID      = "scan-abc-123";
    private static final String FINDING_ID   = "finding-uuid-001";
    private static final String CVE_ID       = "CVE-2024-0001";
    private static final String LABEL_A      = "Missing DMARC Record";
    private static final String LABEL_B      = "Weak TLS Protocol";
    private static final int    CATEGORY_COUNT = OWASPCategory.values().length;

    @Mock private OWASPMappingRule ruleA;
    @Mock private OWASPMappingRule ruleB;

    // ── builder helpers ───────────────────────────────────────────────────────

    private OWASPEvaluator evaluatorWith(OWASPMappingRule... rules) {
        return new OWASPEvaluator(List.of(rules));
    }

    private DomainFinding finding(String id) {
        return new DomainFinding(id, SCAN_ID, "Dns", "High", "Some title",
                CVE_ID, "explanation", "{}", "fix it");
    }

    private EngineResult successEngine(SurfaceType surface) {
        return EngineResult.success(surface, Map.of("findings", List.of()));
    }

    private EngineResult failedEngine(SurfaceType surface) {
        return EngineResult.failure(surface, "timeout");
    }

    private AiResult aiResult() {
        return new AiResult("explanation", List.of("step 1"), null);
    }

    private OWASPCategoryScore categoryScore(OWASPEvaluationResult result, OWASPCategory category) {
        return result.categoryScores().stream()
                .filter(c -> c.category() == category)
                .findFirst()
                .orElseThrow(() -> new AssertionError("Category not found: " + category));
    }

    private SurfaceStateSnapshot snapshot(SurfaceType surface, SurfaceStatus status) {
        return new SurfaceStateSnapshot(surface, status, 0, null, AiAvailability.AVAILABLE, "2026-07-05T00:00:00Z");
    }

    private SurfaceStateSnapshot failedSnapshot(SurfaceType surface) {
        return new SurfaceStateSnapshot(surface, SurfaceStatus.PERMANENTLY_FAILED,
                3, FailureReason.TIMEOUT, AiAvailability.AVAILABLE, "2026-07-05T00:00:00Z");
    }

    // default: both mocked rules reject everything — each test enables what it needs
    @BeforeEach
    void setUpDefaults() {
        lenient().when(ruleA.matches(any(), any())).thenReturn(false);
        lenient().when(ruleB.matches(any(), any())).thenReturn(false);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // evaluate() — result structure
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — result structure")
    class EvaluateResultStructure {

        @Test
        @DisplayName("returns the same scanId that was passed in")
        void returnsCorrectScanId() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.scanId()).isEqualTo(SCAN_ID);
        }

        @Test
        @DisplayName("always returns exactly one score per OWASP category regardless of findings")
        void alwaysReturnsOneCategoryScorePerCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.categoryScores()).hasSize(CATEGORY_COUNT);
        }

        @Test
        @DisplayName("returns empty mappings when engineResults list is empty")
        void emptyMappings_whenNoEngineResults() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.findingMappings()).isEmpty();
        }

        @Test
        @DisplayName("returns empty mappings when engineResults is null (defensive path)")
        void emptyMappings_whenEngineResultsIsNull() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), null, List.of());

            assertThat(result.findingMappings()).isEmpty();
        }

        @Test
        @DisplayName("returns empty mappings when rule list is empty")
        void emptyMappings_whenNoRulesRegistered() {
            OWASPEvaluator evaluator = new OWASPEvaluator(List.of());

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // evaluate() — rule matching
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — rule matching")
    class EvaluateRuleMatching {

        @Test
        @DisplayName("creates one mapping when one rule matches one engine+finding pair")
        void oneMappingForOneRuleMatch() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings()).hasSize(1);
        }

        @Test
        @DisplayName("mapping carries correct scanId, findingId, category, severity, label, status")
        void mappingCarriesCorrectFields() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            OWASPFindingMapping mapping = result.findingMappings().getFirst();
            assertThat(mapping.scanId()).isEqualTo(SCAN_ID);
            assertThat(mapping.findingId()).isEqualTo(FINDING_ID);
            assertThat(mapping.cveId()).isEqualTo(CVE_ID);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
            assertThat(mapping.findingLabel()).isEqualTo(LABEL_A);
            assertThat(mapping.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("creates two mappings when two rules both match the same engine+finding pair")
        void twoMappings_whenTwoRulesMatchSameEngine() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            when(ruleB.matches(any(), any())).thenReturn(true);
            when(ruleB.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleB.findingLabel(any(), any())).thenReturn(LABEL_B);
            when(ruleB.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA, ruleB);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings()).hasSize(2);
        }

        @Test
        @DisplayName("does not map anything when the engine failed — rules never consulted")
        void noMapping_whenEngineFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(failedEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings()).isEmpty();
            verify(ruleA, never()).matches(any(), any());
        }

        @Test
        @DisplayName("does not map when finding is absent (findings list shorter than engines)")
        void noMapping_whenFindingIsAbsent() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    Collections.emptyList(),        // no findings
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings()).isEmpty();
            verify(ruleA, never()).matches(any(), any());
        }

        @Test
        @DisplayName("processes engine even when ai list is empty — passes null ai to rule")
        void handlesNullAi_whenAiListIsEmpty() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // aiResults is empty — evaluator should pass null ai to the rule
            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings()).hasSize(1);
            // Verify rule was called with null ai (second arg)
            verify(ruleA).matches(any(EngineResult.class), isNull());
        }

        @Test
        @DisplayName("passes null ai for second engine when aiResults list is shorter than engineResults")
        void passesNullAi_toSecondEngine_whenAiListShorter() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2")),
                    List.of(successEngine(SurfaceType.DNS), successEngine(SurfaceType.SSL)),
                    List.of(aiResult())     // only covers engine[0]; engine[1] gets null ai
            );

            assertThat(result.findingMappings()).hasSize(2);
            // rule called twice: once with real ai, once with null
            verify(ruleA, times(1)).matches(any(EngineResult.class), any(AiResult.class));
            verify(ruleA, times(1)).matches(any(EngineResult.class), isNull());
        }

        @Test
        @DisplayName("creates one mapping per engine when one rule matches all three engines")
        void oneMappingPerEngine_whenRuleMatchesAll() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.SSL),
                            successEngine(SurfaceType.HTTP_HEADERS)
                    ),
                    List.of()
            );

            assertThat(result.findingMappings()).hasSize(3);
        }

        @Test
        @DisplayName("skips failed engines and continues mapping successful ones")
        void skipsFailedEngines_continuesWithSuccessful() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // engine[1] (SSL) is failed — must be skipped; engine[0] and [2] succeed
            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(
                            successEngine(SurfaceType.DNS),
                            failedEngine(SurfaceType.SSL),
                            successEngine(SurfaceType.HTTP_HEADERS)
                    ),
                    List.of()
            );

            assertThat(result.findingMappings()).hasSize(2);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // evaluate() — compliance status derivation
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — compliance status")
    class EvaluateComplianceStatus {

        @Test
        @DisplayName("COMPLIANT when no findings map to a category")
        void compliant_whenNoFindingsMapped() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA); // ruleA always returns false

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::status)
                    .containsOnly(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("NON_COMPLIANT for a category when it has a MEDIUM finding")
        void nonCompliant_whenMediumFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

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
        @DisplayName("NON_COMPLIANT for a category when it has a HIGH finding")
        void nonCompliant_whenHighFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.SSL)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES).status())
                    .isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("NON_COMPLIANT for a category when it has a CRITICAL finding")
        void nonCompliant_whenCriticalFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.VULNERABLE_COMPONENTS);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DEPENDENCY)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.VULNERABLE_COMPONENTS).status())
                    .isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("PARTIAL for a category when it has only LOW findings")
        void partial_whenOnlyLowFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION).status())
                    .isEqualTo(OWASPComplianceStatus.PARTIAL);
        }

        @Test
        @DisplayName("finding-level status is NON_COMPLIANT for MEDIUM severity")
        void findingStatus_nonCompliant_forMedium() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings().getFirst().status())
                    .isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("finding-level status is PARTIAL for LOW severity")
        void findingStatus_partial_forLow() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings().getFirst().status())
                    .isEqualTo(OWASPComplianceStatus.PARTIAL);
        }

        @Test
        @DisplayName("finding-level status is COMPLIANT for NONE severity")
        void findingStatus_compliant_forNone() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.NONE);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.findingMappings().getFirst().status())
                    .isEqualTo(OWASPComplianceStatus.COMPLIANT);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // evaluate() — scoring
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — category scoring")
    class EvaluateScoring {

        @Test
        @DisplayName("category score is 100 when no rules match")
        void categoryScore100_whenNoRulesMatch() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION).score()).isEqualTo(100);
        }

        @Test
        @DisplayName("CRITICAL finding deducts 40 → category score 60")
        void criticalPenalty40_categoryScore60() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION).score()).isEqualTo(60);
        }

        @Test
        @DisplayName("HIGH finding deducts 25 → category score 75")
        void highPenalty25_categoryScore75() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.SSL)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES).score()).isEqualTo(75);
        }

        @Test
        @DisplayName("MEDIUM finding deducts 15 → category score 85")
        void mediumPenalty15_categoryScore85() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.PORTS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(85);
        }

        @Test
        @DisplayName("LOW finding deducts 5 → category score 95")
        void lowPenalty5_categoryScore95() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.PORTS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(95);
        }

        @Test
        @DisplayName("category score never goes below 0 when accumulated penalties exceed 100")
        void categoryScoreClampsToZero_whenPenaltiesExceedBase() {
            // 3 × CRITICAL (3 × 40 = 120) > 100 → must clamp to 0
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS)
                    ),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION).score())
                    .isEqualTo(0)
                    .isGreaterThanOrEqualTo(0);
        }

        @Test
        @DisplayName("two findings in the same category accumulate their penalties")
        void twoPenalties_accumulateForSameCategory() {
            // CRITICAL (40) + HIGH (25) = 65 penalty → score = max(0, 100-65) = 35
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            // first engine → CRITICAL
            when(ruleA.severity(any(), any()))
                    .thenReturn(FindingSeverity.CRITICAL)
                    .thenReturn(FindingSeverity.HIGH);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2")),
                    List.of(successEngine(SurfaceType.DNS), successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION).score()).isEqualTo(35);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // evaluate() — overall score and tier
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — overall score and tier")
    class EvaluateOverallScoreAndTier {

        @Test
        @DisplayName("overall score is 100 when no rules match")
        void overallScore100_whenNoRulesMatch() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.overallScore()).isEqualTo(100);
        }

        @Test
        @DisplayName("tier is EXCELLENT when overall score is 100")
        void tierExcellent_whenPerfectScore() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.EXCELLENT);
        }

        @Test
        @DisplayName("overall score is average of all category scores — CRITICAL on one category")
        void overallScore_isAverageOfCategories_oneCritical() {
            // CRITICAL on A05 → A05 = 60, others = 100
            // 7 categories: (60 + 6 × 100) / 7 = 660/7 = 94 (int truncation)
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            int expectedOverall = (60 + (CATEGORY_COUNT - 1) * 100) / CATEGORY_COUNT;
            assertThat(result.overallScore()).isEqualTo(expectedOverall);
        }

        @Test
        @DisplayName("tier is GOOD when overall score falls in 75–89 range")
        void tierGood_whenOverallScoreInGoodRange() {
            // Need a score in [75, 89].
            // Use 3 CRITICAL findings on 3 different categories:
            // 3 × 60 + 4 × 100 = 580 / 7 = 82 → GOOD ✓
            OWASPMappingRule ruleC = mock(OWASPMappingRule.class);
            OWASPMappingRule ruleD = mock(OWASPMappingRule.class);

            for (OWASPMappingRule r : new OWASPMappingRule[]{ruleA, ruleC, ruleD}) {
                when(r.matches(any(), any())).thenReturn(true);
                when(r.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
                when(r.findingLabel(any(), any())).thenReturn(LABEL_A);
            }
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);
            when(ruleC.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            when(ruleD.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluator evaluator = new OWASPEvaluator(List.of(ruleA, ruleC, ruleD));

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            assertThat(result.overallScore()).isBetween(75, 89);
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.GOOD);
        }

        @Test
        @DisplayName("tier is HIGH_RISK when overall score falls below 50")
        void tierHighRisk_whenOverallScoreBelow50() {
            // Need overall < 50. Force 6 out of 7 categories to 0:
            // 6 categories with CRITICAL → score 0 per category (using 3 CRITICAL findings each,
            // but we have one finding per engine so we'll use a rule that fires on each engine
            // and just need enough engines to push 6 categories to 0 — but since we only have one
            // finding per engine, the simplest approach: 6 engines, one per failing category,
            // 3 findings for each? Actually: each engine × rule fires once, penalty=40 per hit.
            // 6 separate single-category rules firing once = 6 categories at 60 each:
            // (60*6 + 100*1) / 7 = 460/7 = 65 → NEEDS_ATTENTION, not HIGH_RISK.
            //
            // To get HIGH_RISK we need all 7 categories scoring low:
            // Use 7 rules across all 7 categories with CRITICAL → (60*7)/7 = 60 — still too high.
            // Need additional penalties: add a second CRITICAL on each category.
            // score per category = max(0, 100 - 80) = 20; overall = (20*7)/7 = 20 → HIGH_RISK ✓
            // Two findings per engine: use 7 pairs of (engine, finding), same rule fires twice per category.

            List<OWASPCategory> allCats = List.of(OWASPCategory.values());
            List<OWASPMappingRule> rules = allCats.stream()
                    .map(cat -> {
                        OWASPMappingRule r = mock(OWASPMappingRule.class);
                        when(r.matches(any(), any())).thenReturn(true);
                        when(r.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
                        when(r.findingLabel(any(), any())).thenReturn(LABEL_A);
                        when(r.category()).thenReturn(cat);
                        return r;
                    })
                    .toList();

            OWASPEvaluator evaluator = new OWASPEvaluator(rules);

            // Two findings per category → 2 × 40 = 80 penalty → score = 20 per category
            List<DomainFinding> findings = List.of(
                    finding("f-1"), finding("f-2"), finding("f-3"), finding("f-4"),
                    finding("f-5"), finding("f-6"), finding("f-7"), finding("f-8"),
                    finding("f-9"), finding("f-10"), finding("f-11"), finding("f-12"),
                    finding("f-13"), finding("f-14")
            );
            List<EngineResult> engines = findings.stream()
                    .map(f -> successEngine(SurfaceType.DNS))
                    .toList();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, findings, engines, List.of());

            assertThat(result.overallScore()).isLessThan(50);
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.HIGH_RISK);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // evaluate() — failed surface zero-score override
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — failed surface zero-score override")
    class EvaluateFailedSurfaceOverride {

        @Test
        @DisplayName("SSL failure forces CRYPTOGRAPHIC_FAILURES category to 0 / NON_COMPLIANT")
        void sslFailure_zerosCryptographicFailuresCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.SSL)),
                    List.of()
            );

            OWASPCategoryScore a02 = categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(a02.score()).isEqualTo(0);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("PORTS failure forces BROKEN_ACCESS_CONTROL category to 0 / NON_COMPLIANT")
        void portsFailure_zerosBrokenAccessControlCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.PORTS)),
                    List.of()
            );

            OWASPCategoryScore a01 = categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL);
            assertThat(a01.score()).isEqualTo(0);
            assertThat(a01.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("DEPENDENCY failure forces VULNERABLE_COMPONENTS category to 0 / NON_COMPLIANT")
        void dependencyFailure_zerosVulnerableComponentsCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.DEPENDENCY)),
                    List.of()
            );

            OWASPCategoryScore a06 = categoryScore(result, OWASPCategory.VULNERABLE_COMPONENTS);
            assertThat(a06.score()).isEqualTo(0);
            assertThat(a06.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("DNS failure forces SECURITY_MISCONFIGURATION to 0 / NON_COMPLIANT")
        void dnsFailure_zerosSecurityMisconfigurationCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.DNS)),
                    List.of()
            );

            OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
            assertThat(a05.score()).isEqualTo(0);
            assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("HTTP_HEADERS failure forces SECURITY_MISCONFIGURATION to 0 / NON_COMPLIANT")
        void httpHeadersFailure_zerosSecurityMisconfigurationCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.HTTP_HEADERS)),
                    List.of()
            );

            OWASPCategoryScore a05 = categoryScore(result, OWASPCategory.SECURITY_MISCONFIGURATION);
            assertThat(a05.score()).isEqualTo(0);
            assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("surfaces with no SURFACE_CATEGORY_MAP entry (SECRETS, SUBDOMAINS) do not zero any category")
        void unmappedSurfaceFailure_doesNotZeroAnyCategory() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.SECRETS), failedEngine(SurfaceType.SUBDOMAINS)),
                    List.of()
            );

            // All categories should still be COMPLIANT (score 100) since no mapped surface failed
            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::score)
                    .containsOnly(100);
        }

        @Test
        @DisplayName("failed surface override applies even if a rule matched and produced a score for that category")
        void failedOverride_winsOverExistingCategoryScore() {
            // ruleA matches and would give SSL category a score of 75 (HIGH penalty),
            // but the SSL engine itself failed, so the override must still force it to 0.
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // Both engines present — one success (for rule matching), one failure (for override)
            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(
                            successEngine(SurfaceType.DNS),  // rule fires here, maps to CRYPTO
                            failedEngine(SurfaceType.SSL)    // this triggers the override
                    ),
                    List.of()
            );

            OWASPCategoryScore a02 = categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(a02.score()).isEqualTo(0);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("two simultaneous surface failures zero two different categories independently")
        void twoSurfaceFailures_zeroTwoCategories_independently() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.SSL), failedEngine(SurfaceType.PORTS)),
                    List.of()
            );

            assertThat(categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES).score()).isEqualTo(0);
            assertThat(categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(0);
        }

        @Test
        @DisplayName("failed surface lowers the overall score — not treated as 100")
        void failedSurface_lowersOverallScore() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(failedEngine(SurfaceType.SSL)),
                    List.of()
            );

            // A02 = 0; other 6 = 100 → overall = (0 + 6 × 100) / 7 = 85
            int expected = (0 + (CATEGORY_COUNT - 1) * 100) / CATEGORY_COUNT;
            assertThat(result.overallScore()).isEqualTo(expected);
            assertThat(result.overallScore()).isLessThan(100);
        }

        @Test
        @DisplayName("null engineResults list produces no failed-surface overrides")
        void nullEngineResults_producesNoOverrides() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID, List.of(), null, List.of());

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::score)
                    .containsOnly(100);
        }

        @Test
        @DisplayName("successful surface does not trigger override — category keeps its real score")
        void successfulSurface_doesNotTriggerOverride() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA); // ruleA returns false → no findings

            OWASPEvaluationResult result = evaluator.evaluate(
                    SCAN_ID,
                    List.of(),
                    List.of(successEngine(SurfaceType.SSL)),
                    List.of()
            );

            // SSL succeeded, no rule matched → A02 should be 100/COMPLIANT
            OWASPCategoryScore a02 = categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(a02.score()).isEqualTo(100);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.COMPLIANT);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // categoriesStillFailed()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("categoriesStillFailed()")
    class CategoriesStillFailed {

        @Test
        @DisplayName("returns empty set when all surfaces are SUCCESS")
        void emptySet_whenAllSurfacesSucceeded() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.SSL,  snapshot(SurfaceType.SSL,  SurfaceStatus.SUCCESS),
                    SurfaceType.DNS,  snapshot(SurfaceType.DNS,  SurfaceStatus.SUCCESS),
                    SurfaceType.PORTS,snapshot(SurfaceType.PORTS, SurfaceStatus.SUCCESS)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns CRYPTOGRAPHIC_FAILURES when SSL is PERMANENTLY_FAILED")
        void returnsCryptographicFailures_whenSslPermanentlyFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.SSL, failedSnapshot(SurfaceType.SSL),
                    SurfaceType.DNS, snapshot(SurfaceType.DNS, SurfaceStatus.SUCCESS)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).containsExactly(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
        }

        @Test
        @DisplayName("returns BROKEN_ACCESS_CONTROL when PORTS is PERMANENTLY_FAILED")
        void returnsBrokenAccessControl_whenPortsPermanentlyFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.PORTS, failedSnapshot(SurfaceType.PORTS)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).containsExactly(OWASPCategory.BROKEN_ACCESS_CONTROL);
        }

        @Test
        @DisplayName("returns VULNERABLE_COMPONENTS when DEPENDENCY is PERMANENTLY_FAILED")
        void returnsVulnerableComponents_whenDependencyPermanentlyFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.DEPENDENCY, failedSnapshot(SurfaceType.DEPENDENCY)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).containsExactly(OWASPCategory.VULNERABLE_COMPONENTS);
        }

        @Test
        @DisplayName("returns SECURITY_MISCONFIGURATION when DNS is PERMANENTLY_FAILED")
        void returnsSecurityMisconfiguration_whenDnsPermanentlyFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.DNS, failedSnapshot(SurfaceType.DNS)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).containsExactly(OWASPCategory.SECURITY_MISCONFIGURATION);
        }

        @Test
        @DisplayName("returns two categories when two different mapped surfaces are both PERMANENTLY_FAILED")
        void returnsTwoCategories_whenTwoMappedSurfacesFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.SSL,   failedSnapshot(SurfaceType.SSL),
                    SurfaceType.PORTS, failedSnapshot(SurfaceType.PORTS),
                    SurfaceType.DNS,   snapshot(SurfaceType.DNS, SurfaceStatus.SUCCESS)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).containsExactlyInAnyOrder(
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES,
                    OWASPCategory.BROKEN_ACCESS_CONTROL
            );
        }

        @Test
        @DisplayName("ignores surfaces not in the SURFACE_CATEGORY_MAP (SECRETS, SUBDOMAINS)")
        void ignoresUnmappedSurfaces_evenIfPermanentlyFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.SECRETS,   failedSnapshot(SurfaceType.SECRETS),
                    SurfaceType.SUBDOMAINS,failedSnapshot(SurfaceType.SUBDOMAINS)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("non-terminal failure states (RETRYING, SCANNING) are not treated as PERMANENTLY_FAILED")
        void nonTerminalFailureStates_areNotTreatedAsPermanentlyFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<SurfaceType, SurfaceStateSnapshot> snapshots = Map.of(
                    SurfaceType.SSL,  snapshot(SurfaceType.SSL,  SurfaceStatus.RETRYING),
                    SurfaceType.DNS,  snapshot(SurfaceType.DNS,  SurfaceStatus.SCANNING),
                    SurfaceType.PORTS,snapshot(SurfaceType.PORTS, SurfaceStatus.PENDING)
            );

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(snapshots);

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("empty snapshot map returns empty set")
        void emptySnapshotMap_returnsEmptySet() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Set<OWASPCategory> result = evaluator.categoriesStillFailed(Map.of());

            assertThat(result).isEmpty();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // mapRecoveredSurface()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("mapRecoveredSurface()")
    class MapRecoveredSurface {

        @Test
        @DisplayName("returns empty list when engine is null")
        void returnsEmpty_whenEngineIsNull() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, null, aiResult(), finding(FINDING_ID));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns empty list when engine failed (success=false)")
        void returnsEmpty_whenEngineFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, failedEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).isEmpty();
            verify(ruleA, never()).matches(any(), any());
        }

        @Test
        @DisplayName("returns empty list when finding is null")
        void returnsEmpty_whenFindingIsNull() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, successEngine(SurfaceType.SSL), aiResult(), null);

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns empty list when no rule matches the recovered engine")
        void returnsEmpty_whenNoRuleMatches() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA); // ruleA → false

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, successEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns one mapping when one rule matches the recovered engine+finding")
        void returnsOneMapping_whenOneRuleMatches() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, successEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).hasSize(1);
            OWASPFindingMapping mapping = result.getFirst();
            assertThat(mapping.scanId()).isEqualTo(SCAN_ID);
            assertThat(mapping.findingId()).isEqualTo(FINDING_ID);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
        }

        @Test
        @DisplayName("returns two mappings when two rules both match the recovered surface")
        void returnsTwoMappings_whenTwoRulesMatch() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            when(ruleB.matches(any(), any())).thenReturn(true);
            when(ruleB.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleB.findingLabel(any(), any())).thenReturn(LABEL_B);
            when(ruleB.category()).thenReturn(OWASPCategory.AUTH_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA, ruleB);

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, successEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).hasSize(2);
        }

        @Test
        @DisplayName("works correctly with null ai — passes null to rule's matches() and severity()")
        void worksWithNullAi() {
            when(ruleA.matches(any(), isNull())).thenReturn(true);
            when(ruleA.severity(any(), isNull())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), isNull())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPFindingMapping> result = evaluator.mapRecoveredSurface(
                    SCAN_ID, successEngine(SurfaceType.SSL), null, finding(FINDING_ID));

            assertThat(result).hasSize(1);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // recomputeCategoryScores()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("recomputeCategoryScores()")
    class RecomputeCategoryScores {

        @Test
        @DisplayName("always returns one score per category regardless of DB data")
        void alwaysReturnsAllCategories() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(Map.of(), Set.of());

            assertThat(result).hasSize(CATEGORY_COUNT);
        }

        @Test
        @DisplayName("all categories score 100 / COMPLIANT when DB has no severities and nothing is still failed")
        void allCompliant100_whenNothingFailed_andNoDbSeverities() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(Map.of(), Set.of());

            assertThat(result).extracting(OWASPCategoryScore::score).containsOnly(100);
            assertThat(result).extracting(OWASPCategoryScore::status)
                    .containsOnly(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("category in stillFailedCategories is forced to 0 / NON_COMPLIANT regardless of DB data")
        void stillFailedCategory_forcedToZero_regardlessOfDbSeverities() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // Even if DB has a LOW severity for this category, it's still in the failed set → 0
            Map<OWASPCategory, List<FindingSeverity>> dbSeverities = Map.of(
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES, List.of(FindingSeverity.LOW)
            );

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(
                    dbSeverities, Set.of(OWASPCategory.CRYPTOGRAPHIC_FAILURES));

            OWASPCategoryScore a02 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.CRYPTOGRAPHIC_FAILURES)
                    .findFirst().orElseThrow();

            assertThat(a02.score()).isEqualTo(0);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("category with CRITICAL severity in DB gets score 60 (100 - 40)")
        void criticalSeverityInDb_givesScore60() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<OWASPCategory, List<FindingSeverity>> dbSeverities = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.CRITICAL)
            );

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(dbSeverities, Set.of());

            OWASPCategoryScore a05 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.SECURITY_MISCONFIGURATION)
                    .findFirst().orElseThrow();

            assertThat(a05.score()).isEqualTo(60);
            assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("category with only LOW severity in DB is PARTIAL (not NON_COMPLIANT)")
        void lowSeverityInDb_givesPartialStatus() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            Map<OWASPCategory, List<FindingSeverity>> dbSeverities = Map.of(
                    OWASPCategory.BROKEN_ACCESS_CONTROL, List.of(FindingSeverity.LOW)
            );

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(dbSeverities, Set.of());

            OWASPCategoryScore a01 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.BROKEN_ACCESS_CONTROL)
                    .findFirst().orElseThrow();

            assertThat(a01.score()).isEqualTo(95);
            assertThat(a01.status()).isEqualTo(OWASPComplianceStatus.PARTIAL);
        }

        @Test
        @DisplayName("category not in DB severities and not still-failed scores 100 / COMPLIANT")
        void missingCategoryInDb_scores100_whenNotFailed() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // Only A05 has data; all other categories not present → should score 100
            Map<OWASPCategory, List<FindingSeverity>> dbSeverities = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.HIGH)
            );

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(dbSeverities, Set.of());

            OWASPCategoryScore a02 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.CRYPTOGRAPHIC_FAILURES)
                    .findFirst().orElseThrow();

            assertThat(a02.score()).isEqualTo(100);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("two severities in same category accumulate penalties correctly")
        void multipleSeveritiesInSameCategory_accumulatePenalties() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // HIGH (25) + MEDIUM (15) = 40 → score = 60
            Map<OWASPCategory, List<FindingSeverity>> dbSeverities = Map.of(
                    OWASPCategory.VULNERABLE_COMPONENTS,
                    List.of(FindingSeverity.HIGH, FindingSeverity.MEDIUM)
            );

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(dbSeverities, Set.of());

            OWASPCategoryScore a06 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.VULNERABLE_COMPONENTS)
                    .findFirst().orElseThrow();

            assertThat(a06.score()).isEqualTo(60);
        }

        @Test
        @DisplayName("failed categories and DB-backed categories can coexist in the same recomputation")
        void failedAndSucceededCategories_coexistCorrectly() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // A02 (SSL) still failed → forced 0
            // A05 has a HIGH finding in DB → 75
            // Everything else → 100
            Map<OWASPCategory, List<FindingSeverity>> dbSeverities = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.HIGH)
            );

            List<OWASPCategoryScore> result = evaluator.recomputeCategoryScores(
                    dbSeverities, Set.of(OWASPCategory.CRYPTOGRAPHIC_FAILURES));

            OWASPCategoryScore a02 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.CRYPTOGRAPHIC_FAILURES)
                    .findFirst().orElseThrow();
            OWASPCategoryScore a05 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.SECURITY_MISCONFIGURATION)
                    .findFirst().orElseThrow();

            assertThat(a02.score()).isEqualTo(0);
            assertThat(a05.score()).isEqualTo(75);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // overallScoreOf()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("overallScoreOf()")
    class OverallScoreOf {

        @Test
        @DisplayName("returns 100 when all category scores are 100")
        void returns100_whenAllPerfect() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPCategoryScore> scores = List.of(OWASPCategory.values()).stream()
                    .map(c -> new OWASPCategoryScore(c, OWASPComplianceStatus.COMPLIANT, 100, List.of()))
                    .toList();

            assertThat(evaluator.overallScoreOf(scores)).isEqualTo(100);
        }

        @Test
        @DisplayName("returns 100 when list is empty (default perfect score)")
        void returns100_whenListIsEmpty() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            assertThat(evaluator.overallScoreOf(List.of())).isEqualTo(100);
        }

        @Test
        @DisplayName("returns integer average of all provided category scores")
        void returnsIntegerAverage() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            // 60 + 100 + 100 + 100 + 100 + 100 + 100 = 660 / 7 = 94
            List<OWASPCategoryScore> scores = List.of(OWASPCategory.values()).stream()
                    .map((c) -> new OWASPCategoryScore(c, OWASPComplianceStatus.COMPLIANT,
                            c == OWASPCategory.SECURITY_MISCONFIGURATION ? 60 : 100, List.of()))
                    .toList();

            int expected = (60 + (CATEGORY_COUNT - 1) * 100) / CATEGORY_COUNT;
            assertThat(evaluator.overallScoreOf(scores)).isEqualTo(expected);
        }

        @Test
        @DisplayName("returns 0 when all category scores are 0")
        void returnsZero_whenAllZero() {
            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            List<OWASPCategoryScore> scores = List.of(OWASPCategory.values()).stream()
                    .map(c -> new OWASPCategoryScore(c, OWASPComplianceStatus.NON_COMPLIANT, 0, List.of()))
                    .toList();

            assertThat(evaluator.overallScoreOf(scores)).isEqualTo(0);
        }

        @Test
        @DisplayName("result matches the same average rule used inside evaluate()")
        void matchesAverageInsideEvaluate() {
            // Run evaluate() to get the real overall, then confirm overallScoreOf() on the
            // category scores produces the identical number — they must use the same formula.
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);

            OWASPEvaluationResult evaluated = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of()
            );

            int recomputed = evaluator.overallScoreOf(evaluated.categoryScores());

            assertThat(recomputed).isEqualTo(evaluated.overallScore());
        }
    }
}