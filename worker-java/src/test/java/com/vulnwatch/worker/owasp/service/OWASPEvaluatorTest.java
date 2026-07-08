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
import static org.mockito.ArgumentMatchers.isNull;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/**
 * Comprehensive unit tests for {@link OWASPEvaluator}.
 *
 * <p>Structure mirrors the public surface of the class:
 * <ol>
 *   <li>{@code evaluate()} — result structure</li>
 *   <li>{@code evaluate()} — rule matching and mapping field correctness</li>
 *   <li>{@code evaluate()} — per-finding compliance status derivation</li>
 *   <li>{@code evaluate()} — per-category scoring (penalties and clamping)</li>
 *   <li>{@code evaluate()} — per-category compliance status</li>
 *   <li>{@code evaluate()} — overall score and tier</li>
 *   <li>{@code evaluate()} — failed-surface zero-score override</li>
 *   <li>{@code categoriesStillFailed()} — DLQ replay state derivation</li>
 *   <li>{@code mapRecoveredSurface()} — single-surface remapping after DLQ replay</li>
 *   <li>{@code recomputeCategoryScores()} — post-recovery score recomputation</li>
 *   <li>{@code overallScoreOf()} — averaging helper</li>
 * </ol>
 *
 * <p>Penalty constants (mirror OWASPEvaluator private constants):
 * <pre>
 *   CRITICAL → 40    HIGH → 25    MEDIUM → 15    LOW → 5    NONE → 0
 * </pre>
 *
 * <p>OWASPCategory has 7 values: BROKEN_ACCESS_CONTROL, CRYPTOGRAPHIC_FAILURES,
 * INJECTION, INSECURE_DESIGN, SECURITY_MISCONFIGURATION, AUTH_FAILURES, VULNERABLE_COMPONENTS.
 *
 * <p>OWASPComplianceTier bands:
 * <pre>
 *   EXCELLENT       90–100
 *   GOOD            75–89
 *   NEEDS_ATTENTION 50–74
 *   HIGH_RISK        0–49
 * </pre>
 *
 * <p>SURFACE_CATEGORY_MAP (from OWASPEvaluator):
 * <pre>
 *   SSL          → CRYPTOGRAPHIC_FAILURES
 *   PORTS        → BROKEN_ACCESS_CONTROL
 *   DEPENDENCY   → VULNERABLE_COMPONENTS
 *   DNS          → SECURITY_MISCONFIGURATION
 *   HTTP_HEADERS → SECURITY_MISCONFIGURATION
 *   SECRETS, SUBDOMAINS → (unmapped — no category affected)
 * </pre>
 */
@ExtendWith(MockitoExtension.class)
@DisplayName("OWASPEvaluator")
class OWASPEvaluatorTest {

    // ── constants ─────────────────────────────────────────────────────────────

    private static final String SCAN_ID = "scan-abc-123";
    private static final String FINDING_ID = "finding-uuid-001";
    private static final String CVE_ID = "CVE-2024-0001";
    private static final String LABEL_A = "Missing DMARC Record";
    private static final String LABEL_B = "Weak TLS Protocol";

    /**
     * Total number of OWASP categories the evaluator always emits — one score per category.
     * Derived from the enum so it stays correct if a category is added later.
     */
    private static final int CATEGORY_COUNT = OWASPCategory.values().length; // 7

    @Mock private OWASPMappingRule ruleA;
    @Mock private OWASPMappingRule ruleB;

    // ── factory helpers ───────────────────────────────────────────────────────

    /** Build an evaluator with exactly the given rules registered. */
    private OWASPEvaluator evaluatorWith(OWASPMappingRule... rules) {
        return new OWASPEvaluator(List.of(rules));
    }

    /** Minimal DomainFinding with the given id. */
    private DomainFinding finding(String id) {
        return new DomainFinding(id, SCAN_ID, "Dns", "High",
                "Some title", CVE_ID, "explanation", "{}", "fix it");
    }

    /** Successful EngineResult for a given surface. */
    private EngineResult successEngine(SurfaceType surface) {
        return EngineResult.success(surface, Map.of("findings", List.of()));
    }

    /** Failed EngineResult for a given surface. */
    private EngineResult failedEngine(SurfaceType surface) {
        return EngineResult.failure(surface, "timeout");
    }

    /** Minimal AiResult with no SSL-specific data. */
    private AiResult aiResult() {
        return new AiResult("explanation", List.of("step 1"), null);
    }

    /** Build a SUCCESS SurfaceStateSnapshot. */
    private SurfaceStateSnapshot successSnapshot(SurfaceType surface) {
        return new SurfaceStateSnapshot(
                surface, SurfaceStatus.SUCCESS, 0,
                null, AiAvailability.AVAILABLE, "2026-07-05T00:00:00Z");
    }

    /** Build a PERMANENTLY_FAILED SurfaceStateSnapshot. */
    private SurfaceStateSnapshot failedSnapshot(SurfaceType surface) {
        return new SurfaceStateSnapshot(
                surface, SurfaceStatus.PERMANENTLY_FAILED, 3,
                FailureReason.TIMEOUT, AiAvailability.AVAILABLE, "2026-07-05T00:00:00Z");
    }

    /** Build a non-terminal SurfaceStateSnapshot (e.g. RETRYING). */
    private SurfaceStateSnapshot nonTerminalSnapshot(SurfaceType surface, SurfaceStatus status) {
        return new SurfaceStateSnapshot(
                surface, status, 1,
                null, AiAvailability.AVAILABLE, "2026-07-05T00:00:00Z");
    }

    /** Pull one OWASPCategoryScore from a result by category, failing loudly if absent. */
    private OWASPCategoryScore categoryScore(OWASPEvaluationResult result, OWASPCategory category) {
        return result.categoryScores().stream()
                .filter(c -> c.category() == category)
                .findFirst()
                .orElseThrow(() -> new AssertionError("Category not found in result: " + category));
    }

    // Default: all mocked rules reject everything.
    // Each test stubs only what it actually exercises.
    @BeforeEach
    void defaultRulesRejectAll() {
        org.mockito.Mockito.lenient().when(ruleA.matches(any(), any())).thenReturn(false);
        org.mockito.Mockito.lenient().when(ruleB.matches(any(), any())).thenReturn(false);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1. evaluate() — result structure
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — result structure")
    class EvaluateResultStructure {

        @Test
        @DisplayName("echoes the scanId passed in")
        void returnsScanIdUnchanged() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.scanId()).isEqualTo(SCAN_ID);
        }

        @Test
        @DisplayName("always returns exactly one OWASPCategoryScore per category (7 total)")
        void alwaysEmitsOneCategoryScorePerCategory() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.categoryScores()).hasSize(CATEGORY_COUNT);
        }

        @Test
        @DisplayName("categoryScores contains every OWASPCategory exactly once")
        void categoryScoresContainsEveryCategory() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::category)
                    .containsExactlyInAnyOrder(OWASPCategory.values());
        }

        @Test
        @DisplayName("returns empty findingMappings when engineResults list is empty")
        void emptyMappings_whenEngineListEmpty() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.findingMappings()).isEmpty();
        }

        @Test
        @DisplayName("returns empty findingMappings when engineResults is null (null-safety guard)")
        void emptyMappings_whenEngineListNull() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), null, List.of());

            assertThat(result.findingMappings()).isEmpty();
        }

        @Test
        @DisplayName("returns empty findingMappings when no rules are registered")
        void emptyMappings_whenNoRulesRegistered() {
            OWASPEvaluationResult result = new OWASPEvaluator(List.of())
                    .evaluate(SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DNS)),
                            List.of());

            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. evaluate() — rule matching and mapping field correctness
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — rule matching and mapping fields")
    class EvaluateRuleMatching {

        @Test
        @DisplayName("creates one mapping when one rule matches one engine+finding pair")
        void oneMappingForOneMatch() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
        }

        @Test
        @DisplayName("mapping carries correct scanId, findingId, cveId, category, severity, label and status")
        void mappingFieldsAreCorrect() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            OWASPFindingMapping m = result.findingMappings().getFirst();
            assertThat(m.scanId()).isEqualTo(SCAN_ID);
            assertThat(m.findingId()).isEqualTo(FINDING_ID);
            assertThat(m.cveId()).isEqualTo(CVE_ID);
            assertThat(m.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
            assertThat(m.severity()).isEqualTo(FindingSeverity.HIGH);
            assertThat(m.findingLabel()).isEqualTo(LABEL_A);
            // HIGH is ≥ MEDIUM → NON_COMPLIANT
            assertThat(m.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
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

            OWASPEvaluationResult result = evaluatorWith(ruleA, ruleB).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            assertThat(result.findingMappings()).hasSize(2);
        }

        @Test
        @DisplayName("creates N mappings (one per engine) when one rule matches all N engines")
        void oneMappingPerEngine_whenRuleMatchesAll() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.SSL),
                            successEngine(SurfaceType.HTTP_HEADERS)),
                    List.of());

            // 1 rule × 3 engines = 3 mappings
            assertThat(result.findingMappings()).hasSize(3);
        }

        @Test
        @DisplayName("skips a failed engine entirely — rule is never consulted for it")
        void skipsFailedEngine_ruleNeverConsulted() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(failedEngine(SurfaceType.DNS)),
                    List.of());

            assertThat(result.findingMappings()).isEmpty();
            verify(ruleA, never()).matches(any(), any());
        }

        @Test
        @DisplayName("skips a failed engine mid-list and maps the surrounding successful ones")
        void skipsFailedEngine_mapsSuccessfulNeighbours() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(
                            successEngine(SurfaceType.DNS),
                            failedEngine(SurfaceType.SSL),       // skipped
                            successEngine(SurfaceType.HTTP_HEADERS)),
                    List.of());

            assertThat(result.findingMappings()).hasSize(2);
        }

        @Test
        @DisplayName("skips an index with no corresponding finding (findings list shorter than engines)")
        void skipsIndex_whenFindingAbsent() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    Collections.emptyList(),                    // no findings at all
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            assertThat(result.findingMappings()).isEmpty();
            // Rule must not be consulted when the finding is missing
            verify(ruleA, never()).matches(any(), any());
        }

        @Test
        @DisplayName("passes null ai to the rule when aiResults list is empty")
        void passesNullAi_whenAiListEmpty() {
            when(ruleA.matches(any(), isNull())).thenReturn(true);
            when(ruleA.severity(any(), isNull())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), isNull())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());     // aiResults empty → ai == null

            assertThat(result.findingMappings()).hasSize(1);
            verify(ruleA).matches(any(EngineResult.class), isNull());
        }

        @Test
        @DisplayName("passes null ai for engine[1] when aiResults covers only engine[0]")
        void passesNullAi_forSecondEngine_whenAiListShorter() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2")),
                    List.of(successEngine(SurfaceType.DNS), successEngine(SurfaceType.SSL)),
                    List.of(aiResult()));   // covers only engine[0]

            // Rule called twice: once with real AiResult, once with null
            verify(ruleA, times(1)).matches(any(EngineResult.class), any(AiResult.class));
            verify(ruleA, times(1)).matches(any(EngineResult.class), isNull());
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. evaluate() — per-finding compliance status derivation
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — per-finding compliance status (deriveStatus)")
    class FindingLevelStatus {

        private OWASPEvaluationResult evaluateWithSeverity(FindingSeverity severity) {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(severity);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            return evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());
        }

        @Test
        @DisplayName("CRITICAL severity → finding status NON_COMPLIANT")
        void criticalSeverity_findingStatusNonCompliant() {
            OWASPFindingMapping m = evaluateWithSeverity(FindingSeverity.CRITICAL)
                    .findingMappings().getFirst();
            assertThat(m.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("HIGH severity → finding status NON_COMPLIANT")
        void highSeverity_findingStatusNonCompliant() {
            OWASPFindingMapping m = evaluateWithSeverity(FindingSeverity.HIGH)
                    .findingMappings().getFirst();
            assertThat(m.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("MEDIUM severity → finding status NON_COMPLIANT")
        void mediumSeverity_findingStatusNonCompliant() {
            OWASPFindingMapping m = evaluateWithSeverity(FindingSeverity.MEDIUM)
                    .findingMappings().getFirst();
            assertThat(m.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("LOW severity → finding status PARTIAL")
        void lowSeverity_findingStatusPartial() {
            OWASPFindingMapping m = evaluateWithSeverity(FindingSeverity.LOW)
                    .findingMappings().getFirst();
            assertThat(m.status()).isEqualTo(OWASPComplianceStatus.PARTIAL);
        }

        @Test
        @DisplayName("NONE severity → finding status COMPLIANT")
        void noneSeverity_findingStatusCompliant() {
            OWASPFindingMapping m = evaluateWithSeverity(FindingSeverity.NONE)
                    .findingMappings().getFirst();
            assertThat(m.status()).isEqualTo(OWASPComplianceStatus.COMPLIANT);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4. evaluate() — per-category scoring (penalties and clamping)
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — per-category score (scoreCategory)")
    class CategoryScoring {

        private OWASPCategoryScore scoredCategory(FindingSeverity severity, OWASPCategory category) {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(severity);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(category);

            return categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DNS)),
                            List.of()),
                    category);
        }

        @Test
        @DisplayName("score is 100 when no findings map to a category")
        void score100_whenNoFindings() {
            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA)
                            .evaluate(SCAN_ID,
                                    List.of(finding(FINDING_ID)),
                                    List.of(successEngine(SurfaceType.DNS)),
                                    List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.score()).isEqualTo(100);
        }

        @Test
        @DisplayName("CRITICAL finding deducts 40 → category score 60")
        void criticalPenalty_40_score60() {
            assertThat(scoredCategory(FindingSeverity.CRITICAL,
                    OWASPCategory.SECURITY_MISCONFIGURATION).score()).isEqualTo(60);
        }

        @Test
        @DisplayName("HIGH finding deducts 25 → category score 75")
        void highPenalty_25_score75() {
            assertThat(scoredCategory(FindingSeverity.HIGH,
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES).score()).isEqualTo(75);
        }

        @Test
        @DisplayName("MEDIUM finding deducts 15 → category score 85")
        void mediumPenalty_15_score85() {
            assertThat(scoredCategory(FindingSeverity.MEDIUM,
                    OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(85);
        }

        @Test
        @DisplayName("LOW finding deducts 5 → category score 95")
        void lowPenalty_5_score95() {
            assertThat(scoredCategory(FindingSeverity.LOW,
                    OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(95);
        }

        @Test
        @DisplayName("NONE finding deducts 0 → category score stays 100")
        void nonePenalty_0_score100() {
            assertThat(scoredCategory(FindingSeverity.NONE,
                    OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(100);
        }

        @Test
        @DisplayName("category score is clamped to 0 when cumulative penalties exceed 100")
        void categoryScoreClampsToZero_whenPenaltiesExceed100() {
            // 3 × CRITICAL = 3 × 40 = 120 → max(0, 100 − 120) = 0
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                            List.of(successEngine(SurfaceType.DNS),
                                    successEngine(SurfaceType.DNS),
                                    successEngine(SurfaceType.DNS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.score()).isEqualTo(0);
        }

        @Test
        @DisplayName("two findings in the same category accumulate their penalties")
        void twoPenaltiesAccumulate_inSameCategory() {
            // CRITICAL (40) + HIGH (25) = 65 → score = max(0, 100 − 65) = 35
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);
            when(ruleA.severity(any(), any()))
                    .thenReturn(FindingSeverity.CRITICAL)
                    .thenReturn(FindingSeverity.HIGH);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding("f-1"), finding("f-2")),
                            List.of(successEngine(SurfaceType.DNS),
                                    successEngine(SurfaceType.DNS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.score()).isEqualTo(35);
        }

        @Test
        @DisplayName("only the affected category loses points — others remain at 100")
        void unaffectedCategoriesStayAt100() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            // Every category except A05 must still be 100
            result.categoryScores().stream()
                    .filter(cs -> cs.category() != OWASPCategory.SECURITY_MISCONFIGURATION)
                    .forEach(cs -> assertThat(cs.score())
                            .as("Expected 100 for %s", cs.category())
                            .isEqualTo(100));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5. evaluate() — per-category compliance status
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — per-category compliance status (deriveCategoryStatus)")
    class CategoryComplianceStatus {

        @Test
        @DisplayName("COMPLIANT when no findings map to a category")
        void compliant_whenNoFindings() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)      // ruleA always false
                    .evaluate(SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DNS)),
                            List.of());

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::status)
                    .containsOnly(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("NON_COMPLIANT for a category that has any MEDIUM finding")
        void nonCompliant_whenMediumFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DNS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("NON_COMPLIANT for a category that has any HIGH finding")
        void nonCompliant_whenHighFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.SSL)),
                            List.of()),
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("NON_COMPLIANT for a category that has any CRITICAL finding")
        void nonCompliant_whenCriticalFinding() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.VULNERABLE_COMPONENTS);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DEPENDENCY)),
                            List.of()),
                    OWASPCategory.VULNERABLE_COMPONENTS);

            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("PARTIAL when the category has only LOW findings")
        void partial_whenOnlyLowFindings() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DNS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.PARTIAL);
        }

        @Test
        @DisplayName("NON_COMPLIANT when category has one LOW and one MEDIUM finding — MEDIUM wins")
        void nonCompliant_whenMixedLowAndMedium() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);
            when(ruleA.severity(any(), any()))
                    .thenReturn(FindingSeverity.LOW)
                    .thenReturn(FindingSeverity.MEDIUM);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding("f-1"), finding("f-2")),
                            List.of(successEngine(SurfaceType.DNS),
                                    successEngine(SurfaceType.DNS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 6. evaluate() — overall score and tier
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — overall score and compliance tier")
    class OverallScoreAndTier {

        @Test
        @DisplayName("overall score is 100 when no rules match anything")
        void overallScore100_whenNoRulesMatch() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.overallScore()).isEqualTo(100);
        }

        @Test
        @DisplayName("tier is EXCELLENT when overall score is 100")
        void tierExcellent_whenPerfectScore() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), List.of(), List.of());

            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.EXCELLENT);
        }

        @Test
        @DisplayName("overall score is the integer average of all category scores")
        void overallScore_isIntegerAverageOfCategoryScores() {
            // One CRITICAL on A05 → A05 = 60; other 6 categories = 100
            // overall = (60 + 6 × 100) / 7 = 660 / 7 = 94 (int truncation)
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            int expected = (60 + (CATEGORY_COUNT - 1) * 100) / CATEGORY_COUNT;
            assertThat(result.overallScore()).isEqualTo(expected);
        }

        @Test
        @DisplayName("tier is EXCELLENT when overall score is exactly 90")
        void tierExcellent_atLowerBound90() {
            // Need overall = 90. With 7 categories: sum = 90 × 7 = 630.
            // One category must score: 630 − 6×100 = 30 → need penalty = 70.
            // CRITICAL(40) + HIGH(25) + LOW(5) = 70 → score = 30 ✓
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);
            when(ruleA.severity(any(), any()))
                    .thenReturn(FindingSeverity.CRITICAL)   // −40 → 60
                    .thenReturn(FindingSeverity.HIGH)       // −25 → 35
                    .thenReturn(FindingSeverity.LOW);       // −5  → 30

            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS)),
                    List.of());

            // A05=30, others=100 → (30 + 6×100)/7 = 630/7 = 90
            assertThat(result.overallScore()).isEqualTo(90);
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.EXCELLENT);
        }

        @Test
        @DisplayName("tier is GOOD when overall score is in the 75–89 range")
        void tierGood_whenOverallScoreInGoodRange() {
            // Three CRITICAL findings in three different categories:
            // 3 cats × 60 + 4 cats × 100 = 580 / 7 = 82 → GOOD ✓
            OWASPMappingRule ruleC = mock(OWASPMappingRule.class);
            OWASPMappingRule ruleD = mock(OWASPMappingRule.class);
            for (OWASPMappingRule r : List.of(ruleA, ruleC, ruleD)) {
                when(r.matches(any(), any())).thenReturn(true);
                when(r.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
                when(r.findingLabel(any(), any())).thenReturn(LABEL_A);
            }
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);
            when(ruleC.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            when(ruleD.category()).thenReturn(OWASPCategory.BROKEN_ACCESS_CONTROL);

            OWASPEvaluationResult result = new OWASPEvaluator(List.of(ruleA, ruleC, ruleD))
                    .evaluate(SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(successEngine(SurfaceType.DNS)),
                            List.of());

            assertThat(result.overallScore()).isBetween(75, 89);
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.GOOD);
        }

        @Test
        @DisplayName("tier is NEEDS_ATTENTION when overall score is in the 50–74 range")
        void tierNeedsAttention_whenOverallScoreInRange() {
            // 5 categories each = 0 (3×CRITICAL each), 2 remaining = 100
            // (0×5 + 100×2)/7 = 200/7 = 28 — too low; use 4 zeroed, 3 at 100:
            // (0×4 + 100×3)/7 = 300/7 = 42 — still HIGH_RISK.
            // Let's use 3 zeroed + 4 at 100: (0×3 + 100×4)/7 = 400/7 = 57 → NEEDS_ATTENTION ✓
            // Zero a category: score=0 requires ≥3 CRITICAL per category.
            // Since each rule fires on every engine, use 3 engines + 3 rules on 3 categories.
            List<OWASPCategory> threeCats = List.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION,
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES,
                    OWASPCategory.BROKEN_ACCESS_CONTROL);

            List<OWASPMappingRule> rules = threeCats.stream().map(cat -> {
                OWASPMappingRule r = mock(OWASPMappingRule.class);
                when(r.matches(any(), any())).thenReturn(true);
                when(r.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
                when(r.findingLabel(any(), any())).thenReturn(LABEL_A);
                when(r.category()).thenReturn(cat);
                return r;
            }).toList();

            OWASPEvaluationResult result = new OWASPEvaluator(rules).evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS)),
                    List.of());

            // 3 cats each score max(0, 100-3×40)=0; 4 cats=100 → 400/7=57
            assertThat(result.overallScore()).isBetween(50, 74);
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.NEEDS_ATTENTION);
        }

        @Test
        @DisplayName("tier is HIGH_RISK when overall score is below 50")
        void tierHighRisk_whenOverallScoreBelow50() {
            // Zero 6 categories (6 rules × 3 CRITICAL findings each → all = 0)
            // 1 remaining = 100 → (0×6 + 100)/7 = 100/7 = 14 → HIGH_RISK ✓
            List<OWASPCategory> sixCats = List.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION,
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES,
                    OWASPCategory.BROKEN_ACCESS_CONTROL,
                    OWASPCategory.VULNERABLE_COMPONENTS,
                    OWASPCategory.AUTH_FAILURES,
                    OWASPCategory.INJECTION);

            List<OWASPMappingRule> rules = sixCats.stream().map(cat -> {
                OWASPMappingRule r = mock(OWASPMappingRule.class);
                when(r.matches(any(), any())).thenReturn(true);
                when(r.severity(any(), any())).thenReturn(FindingSeverity.CRITICAL);
                when(r.findingLabel(any(), any())).thenReturn(LABEL_A);
                when(r.category()).thenReturn(cat);
                return r;
            }).toList();

            OWASPEvaluationResult result = new OWASPEvaluator(rules).evaluate(
                    SCAN_ID,
                    List.of(finding("f-1"), finding("f-2"), finding("f-3")),
                    List.of(successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS),
                            successEngine(SurfaceType.DNS)),
                    List.of());

            assertThat(result.overallScore()).isLessThan(50);
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.HIGH_RISK);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 7. evaluate() — failed-surface zero-score override
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("evaluate() — failed-surface zero-score override")
    class FailedSurfaceOverride {

        @Test
        @DisplayName("SSL failure → CRYPTOGRAPHIC_FAILURES category forced to 0 / NON_COMPLIANT")
        void sslFailure_zerosCryptographicFailures() {
            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID, List.of(),
                            List.of(failedEngine(SurfaceType.SSL)),
                            List.of()),
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            assertThat(cs.score()).isEqualTo(0);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("PORTS failure → BROKEN_ACCESS_CONTROL category forced to 0 / NON_COMPLIANT")
        void portsFailure_zerosBrokenAccessControl() {
            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID, List.of(),
                            List.of(failedEngine(SurfaceType.PORTS)),
                            List.of()),
                    OWASPCategory.BROKEN_ACCESS_CONTROL);

            assertThat(cs.score()).isEqualTo(0);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("DEPENDENCY failure → VULNERABLE_COMPONENTS category forced to 0 / NON_COMPLIANT")
        void dependencyFailure_zerosVulnerableComponents() {
            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID, List.of(),
                            List.of(failedEngine(SurfaceType.DEPENDENCY)),
                            List.of()),
                    OWASPCategory.VULNERABLE_COMPONENTS);

            assertThat(cs.score()).isEqualTo(0);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("DNS failure → SECURITY_MISCONFIGURATION category forced to 0 / NON_COMPLIANT")
        void dnsFailure_zerosSecurityMisconfiguration() {
            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID, List.of(),
                            List.of(failedEngine(SurfaceType.DNS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.score()).isEqualTo(0);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("HTTP_HEADERS failure → SECURITY_MISCONFIGURATION forced to 0 / NON_COMPLIANT")
        void httpHeadersFailure_zerosSecurityMisconfiguration() {
            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID, List.of(),
                            List.of(failedEngine(SurfaceType.HTTP_HEADERS)),
                            List.of()),
                    OWASPCategory.SECURITY_MISCONFIGURATION);

            assertThat(cs.score()).isEqualTo(0);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("SECRETS failure does not zero any category (unmapped surface)")
        void secretsFailure_doesNotZeroAnyCategory() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID, List.of(),
                    List.of(failedEngine(SurfaceType.SECRETS)),
                    List.of());

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::score)
                    .containsOnly(100);
        }

        @Test
        @DisplayName("SUBDOMAINS failure does not zero any category (unmapped surface)")
        void subdomainsFailure_doesNotZeroAnyCategory() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID, List.of(),
                    List.of(failedEngine(SurfaceType.SUBDOMAINS)),
                    List.of());

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::score)
                    .containsOnly(100);
        }

        @Test
        @DisplayName("override wins even when a rule has already produced a score for that category")
        void failedOverrideWins_evenWhenRuleAlreadyProducedAScore() {
            // ruleA maps a HIGH finding to CRYPTOGRAPHIC_FAILURES via a successful DNS engine,
            // but the SSL engine (which owns CRYPTOGRAPHIC_FAILURES in SURFACE_CATEGORY_MAP) fails.
            // The override must still force that category to 0.
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            OWASPCategoryScore cs = categoryScore(
                    evaluatorWith(ruleA).evaluate(
                            SCAN_ID,
                            List.of(finding(FINDING_ID)),
                            List.of(
                                    successEngine(SurfaceType.DNS),  // rule fires → score=75 normally
                                    failedEngine(SurfaceType.SSL)),  // triggers override
                            List.of()),
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            assertThat(cs.score()).isEqualTo(0);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("two simultaneous surface failures zero two different categories independently")
        void twoSimultaneousFailures_zeroTwoCategoriesIndependently() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID, List.of(),
                    List.of(failedEngine(SurfaceType.SSL), failedEngine(SurfaceType.PORTS)),
                    List.of());

            assertThat(categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES).score()).isEqualTo(0);
            assertThat(categoryScore(result, OWASPCategory.BROKEN_ACCESS_CONTROL).score()).isEqualTo(0);
        }

        @Test
        @DisplayName("failed surface lowers the overall score — not left at 100")
        void failedSurface_lowersOverallScore() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID, List.of(),
                    List.of(failedEngine(SurfaceType.SSL)),
                    List.of());

            // A02=0; other 6=100 → (0 + 6×100)/7 = 600/7 = 85 (int truncation)
            int expected = (0 + (CATEGORY_COUNT - 1) * 100) / CATEGORY_COUNT;
            assertThat(result.overallScore()).isEqualTo(expected);
            assertThat(result.overallScore()).isLessThan(100);
        }

        @Test
        @DisplayName("successful surface is not zeroed — category keeps its real score")
        void successfulSurface_isNotOverridden() {
            OWASPEvaluationResult result = evaluatorWith(ruleA).evaluate(
                    SCAN_ID, List.of(),
                    List.of(successEngine(SurfaceType.SSL)),
                    List.of());

            OWASPCategoryScore a02 = categoryScore(result, OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(a02.score()).isEqualTo(100);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("null engineResults list produces no overrides")
        void nullEngineResults_producesNoOverrides() {
            OWASPEvaluationResult result = evaluatorWith(ruleA)
                    .evaluate(SCAN_ID, List.of(), null, List.of());

            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::score)
                    .containsOnly(100);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 8. categoriesStillFailed()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("categoriesStillFailed()")
    class CategoriesStillFailed {

        @Test
        @DisplayName("returns empty set when snapshot map is empty")
        void emptySet_whenSnapshotMapEmpty() {
            Set<OWASPCategory> result = evaluatorWith(ruleA)
                    .categoriesStillFailed(Map.of());

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns empty set when all surfaces have SUCCESS status")
        void emptySet_whenAllSurfacesSucceeded() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.SSL,   successSnapshot(SurfaceType.SSL),
                    SurfaceType.DNS,   successSnapshot(SurfaceType.DNS),
                    SurfaceType.PORTS, successSnapshot(SurfaceType.PORTS)));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("SSL PERMANENTLY_FAILED → returns {CRYPTOGRAPHIC_FAILURES}")
        void sslPermanentlyFailed_returnsCryptographicFailures() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.SSL, failedSnapshot(SurfaceType.SSL),
                    SurfaceType.DNS, successSnapshot(SurfaceType.DNS)));

            assertThat(result).containsExactly(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
        }

        @Test
        @DisplayName("PORTS PERMANENTLY_FAILED → returns {BROKEN_ACCESS_CONTROL}")
        void portsPermanentlyFailed_returnsBrokenAccessControl() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.PORTS, failedSnapshot(SurfaceType.PORTS)));

            assertThat(result).containsExactly(OWASPCategory.BROKEN_ACCESS_CONTROL);
        }

        @Test
        @DisplayName("DEPENDENCY PERMANENTLY_FAILED → returns {VULNERABLE_COMPONENTS}")
        void dependencyPermanentlyFailed_returnsVulnerableComponents() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.DEPENDENCY, failedSnapshot(SurfaceType.DEPENDENCY)));

            assertThat(result).containsExactly(OWASPCategory.VULNERABLE_COMPONENTS);
        }

        @Test
        @DisplayName("DNS PERMANENTLY_FAILED → returns {SECURITY_MISCONFIGURATION}")
        void dnsPermanentlyFailed_returnsSecurityMisconfiguration() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.DNS, failedSnapshot(SurfaceType.DNS)));

            assertThat(result).containsExactly(OWASPCategory.SECURITY_MISCONFIGURATION);
        }

        @Test
        @DisplayName("HTTP_HEADERS PERMANENTLY_FAILED → returns {SECURITY_MISCONFIGURATION}")
        void httpHeadersPermanentlyFailed_returnsSecurityMisconfiguration() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.HTTP_HEADERS, failedSnapshot(SurfaceType.HTTP_HEADERS)));

            assertThat(result).containsExactly(OWASPCategory.SECURITY_MISCONFIGURATION);
        }

        @Test
        @DisplayName("SSL and PORTS both PERMANENTLY_FAILED → returns both mapped categories")
        void twoFailedSurfaces_returnsTwoCategories() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.SSL,   failedSnapshot(SurfaceType.SSL),
                    SurfaceType.PORTS, failedSnapshot(SurfaceType.PORTS),
                    SurfaceType.DNS,   successSnapshot(SurfaceType.DNS)));

            assertThat(result).containsExactlyInAnyOrder(
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES,
                    OWASPCategory.BROKEN_ACCESS_CONTROL);
        }

        @Test
        @DisplayName("SECRETS PERMANENTLY_FAILED → returns empty set (unmapped surface)")
        void secretsPermanentlyFailed_returnsEmpty() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.SECRETS, failedSnapshot(SurfaceType.SECRETS)));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("SUBDOMAINS PERMANENTLY_FAILED → returns empty set (unmapped surface)")
        void subdomainsPermanentlyFailed_returnsEmpty() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.SUBDOMAINS, failedSnapshot(SurfaceType.SUBDOMAINS)));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("RETRYING status is not treated as PERMANENTLY_FAILED")
        void retryingStatus_notTreatedAsFailed() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.SSL, nonTerminalSnapshot(SurfaceType.SSL, SurfaceStatus.RETRYING)));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("SCANNING status is not treated as PERMANENTLY_FAILED")
        void scanningStatus_notTreatedAsFailed() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.DNS, nonTerminalSnapshot(SurfaceType.DNS, SurfaceStatus.SCANNING)));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("PENDING status is not treated as PERMANENTLY_FAILED")
        void pendingStatus_notTreatedAsFailed() {
            Set<OWASPCategory> result = evaluatorWith(ruleA).categoriesStillFailed(Map.of(
                    SurfaceType.PORTS, nonTerminalSnapshot(SurfaceType.PORTS, SurfaceStatus.PENDING)));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("SUCCESS_NO_AI status is not treated as PERMANENTLY_FAILED")
        void successNoAiStatus_notTreatedAsFailed() {
            SurfaceStateSnapshot snap = new SurfaceStateSnapshot(
                    SurfaceType.SSL, SurfaceStatus.SUCCESS_NO_AI, 0,
                    null, AiAvailability.UNAVAILABLE, "2026-07-05T00:00:00Z");

            Set<OWASPCategory> result = evaluatorWith(ruleA)
                    .categoriesStillFailed(Map.of(SurfaceType.SSL, snap));

            assertThat(result).isEmpty();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 9. mapRecoveredSurface()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("mapRecoveredSurface()")
    class MapRecoveredSurface {

        @Test
        @DisplayName("returns empty list when engine is null")
        void returnsEmpty_whenEngineNull() {
            List<OWASPFindingMapping> result = evaluatorWith(ruleA)
                    .mapRecoveredSurface(SCAN_ID, null, aiResult(), finding(FINDING_ID));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns empty list when engine failed (success=false) — rule never consulted")
        void returnsEmpty_whenEngineFailed_ruleNeverCalled() {
            List<OWASPFindingMapping> result = evaluatorWith(ruleA)
                    .mapRecoveredSurface(SCAN_ID,
                            failedEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).isEmpty();
            verify(ruleA, never()).matches(any(), any());
        }

        @Test
        @DisplayName("returns empty list when finding is null")
        void returnsEmpty_whenFindingNull() {
            List<OWASPFindingMapping> result = evaluatorWith(ruleA)
                    .mapRecoveredSurface(SCAN_ID,
                            successEngine(SurfaceType.SSL), aiResult(), null);

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns empty list when no rule matches the recovered engine")
        void returnsEmpty_whenNoRuleMatches() {
            // ruleA returns false by default (set in @BeforeEach)
            List<OWASPFindingMapping> result = evaluatorWith(ruleA)
                    .mapRecoveredSurface(SCAN_ID,
                            successEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).isEmpty();
        }

        @Test
        @DisplayName("returns one mapping with correct fields when one rule matches")
        void returnsOneMapping_withCorrectFields() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            List<OWASPFindingMapping> result = evaluatorWith(ruleA)
                    .mapRecoveredSurface(SCAN_ID,
                            successEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).hasSize(1);
            OWASPFindingMapping m = result.getFirst();
            assertThat(m.scanId()).isEqualTo(SCAN_ID);
            assertThat(m.findingId()).isEqualTo(FINDING_ID);
            assertThat(m.category()).isEqualTo(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(m.severity()).isEqualTo(FindingSeverity.HIGH);
            assertThat(m.findingLabel()).isEqualTo(LABEL_A);
        }

        @Test
        @DisplayName("returns two mappings when two rules both match")
        void returnsTwoMappings_whenTwoRulesMatch() {
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            when(ruleB.matches(any(), any())).thenReturn(true);
            when(ruleB.severity(any(), any())).thenReturn(FindingSeverity.MEDIUM);
            when(ruleB.findingLabel(any(), any())).thenReturn(LABEL_B);
            when(ruleB.category()).thenReturn(OWASPCategory.AUTH_FAILURES);

            List<OWASPFindingMapping> result = evaluatorWith(ruleA, ruleB)
                    .mapRecoveredSurface(SCAN_ID,
                            successEngine(SurfaceType.SSL), aiResult(), finding(FINDING_ID));

            assertThat(result).hasSize(2);
        }

        @Test
        @DisplayName("works correctly with null ai — passes null through to the rule")
        void worksWithNullAi() {
            when(ruleA.matches(any(), isNull())).thenReturn(true);
            when(ruleA.severity(any(), isNull())).thenReturn(FindingSeverity.LOW);
            when(ruleA.findingLabel(any(), isNull())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.CRYPTOGRAPHIC_FAILURES);

            List<OWASPFindingMapping> result = evaluatorWith(ruleA)
                    .mapRecoveredSurface(SCAN_ID,
                            successEngine(SurfaceType.SSL), null, finding(FINDING_ID));

            assertThat(result).hasSize(1);
            verify(ruleA).matches(any(EngineResult.class), isNull());
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 10. recomputeCategoryScores()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("recomputeCategoryScores()")
    class RecomputeCategoryScores {

        @Test
        @DisplayName("returns exactly one score per category (7 total)")
        void returnsOneCategoryScorePerCategory() {
            List<OWASPCategoryScore> result = evaluatorWith(ruleA)
                    .recomputeCategoryScores(Map.of(), Set.of());

            assertThat(result).hasSize(CATEGORY_COUNT);
        }

        @Test
        @DisplayName("all categories score 100 / COMPLIANT when DB is empty and nothing is still failed")
        void allCompliant100_whenEmptyDbAndNoFailed() {
            List<OWASPCategoryScore> result = evaluatorWith(ruleA)
                    .recomputeCategoryScores(Map.of(), Set.of());

            assertThat(result).extracting(OWASPCategoryScore::score).containsOnly(100);
            assertThat(result).extracting(OWASPCategoryScore::status)
                    .containsOnly(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("still-failed category is forced to 0 / NON_COMPLIANT regardless of DB severities")
        void stillFailedCategory_forcedToZero_ignoresDbSeverities() {
            // Even if DB says this category has a LOW finding, the surface is still in DLQ → 0
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.CRYPTOGRAPHIC_FAILURES, List.of(FindingSeverity.LOW));

            List<OWASPCategoryScore> result = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of(OWASPCategory.CRYPTOGRAPHIC_FAILURES));

            OWASPCategoryScore a02 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.CRYPTOGRAPHIC_FAILURES)
                    .findFirst().orElseThrow();

            assertThat(a02.score()).isEqualTo(0);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("CRITICAL severity from DB → category score 60 / NON_COMPLIANT")
        void criticalInDb_score60_nonCompliant() {
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.CRITICAL));

            OWASPCategoryScore cs = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.SECURITY_MISCONFIGURATION)
                    .findFirst().orElseThrow();

            assertThat(cs.score()).isEqualTo(60);     // 100 − 40
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("HIGH severity from DB → category score 75 / NON_COMPLIANT")
        void highInDb_score75_nonCompliant() {
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.HIGH));

            OWASPCategoryScore cs = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.SECURITY_MISCONFIGURATION)
                    .findFirst().orElseThrow();

            assertThat(cs.score()).isEqualTo(75);     // 100 − 25
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("MEDIUM severity from DB → category score 85 / NON_COMPLIANT")
        void mediumInDb_score85_nonCompliant() {
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.BROKEN_ACCESS_CONTROL, List.of(FindingSeverity.MEDIUM));

            OWASPCategoryScore cs = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.BROKEN_ACCESS_CONTROL)
                    .findFirst().orElseThrow();

            assertThat(cs.score()).isEqualTo(85);     // 100 − 15
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("LOW severity from DB → category score 95 / PARTIAL")
        void lowInDb_score95_partial() {
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.BROKEN_ACCESS_CONTROL, List.of(FindingSeverity.LOW));

            OWASPCategoryScore cs = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.BROKEN_ACCESS_CONTROL)
                    .findFirst().orElseThrow();

            assertThat(cs.score()).isEqualTo(95);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.PARTIAL);
        }

        @Test
        @DisplayName("multiple severities in same DB category accumulate penalties correctly")
        void multipleSeveritiesAccumulate() {
            // HIGH (25) + MEDIUM (15) = 40 → score = 60
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.VULNERABLE_COMPONENTS,
                    List.of(FindingSeverity.HIGH, FindingSeverity.MEDIUM));

            OWASPCategoryScore cs = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.VULNERABLE_COMPONENTS)
                    .findFirst().orElseThrow();

            assertThat(cs.score()).isEqualTo(60);
            assertThat(cs.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("category absent from DB and not still-failed scores 100 / COMPLIANT")
        void absentFromDb_notFailed_score100() {
            // Only A05 has data; A02 (CRYPTOGRAPHIC_FAILURES) is absent from DB and not failed
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.HIGH));

            OWASPCategoryScore a02 = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.CRYPTOGRAPHIC_FAILURES)
                    .findFirst().orElseThrow();

            assertThat(a02.score()).isEqualTo(100);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        @DisplayName("failed and DB-backed categories co-exist correctly in one recomputation")
        void failedAndDbBackedCoexist() {
            // A02 (SSL) still failed → 0
            // A05 has a HIGH in DB → 75
            // everything else → 100
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION, List.of(FindingSeverity.HIGH));

            List<OWASPCategoryScore> result = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of(OWASPCategory.CRYPTOGRAPHIC_FAILURES));

            OWASPCategoryScore a02 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.CRYPTOGRAPHIC_FAILURES)
                    .findFirst().orElseThrow();
            OWASPCategoryScore a05 = result.stream()
                    .filter(s -> s.category() == OWASPCategory.SECURITY_MISCONFIGURATION)
                    .findFirst().orElseThrow();

            assertThat(a02.score()).isEqualTo(0);
            assertThat(a02.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
            assertThat(a05.score()).isEqualTo(75);
            assertThat(a05.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        @DisplayName("score is clamped to 0 when DB severities produce penalties exceeding 100")
        void scoreClampedToZero_whenDbPenaltiesExceed100() {
            // 3 × CRITICAL (3 × 40 = 120) → max(0, 100 − 120) = 0
            Map<OWASPCategory, List<FindingSeverity>> db = Map.of(
                    OWASPCategory.SECURITY_MISCONFIGURATION,
                    List.of(FindingSeverity.CRITICAL, FindingSeverity.CRITICAL, FindingSeverity.CRITICAL));

            OWASPCategoryScore cs = evaluatorWith(ruleA)
                    .recomputeCategoryScores(db, Set.of())
                    .stream()
                    .filter(s -> s.category() == OWASPCategory.SECURITY_MISCONFIGURATION)
                    .findFirst().orElseThrow();

            assertThat(cs.score()).isEqualTo(0);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 11. overallScoreOf()
    // ═════════════════════════════════════════════════════════════════════════

    @Nested
    @DisplayName("overallScoreOf()")
    class OverallScoreOf {

        @Test
        @DisplayName("returns 100 when all category scores are 100")
        void returns100_whenAllScoresPerfect() {
            List<OWASPCategoryScore> allPerfect = List.of(OWASPCategory.values()).stream()
                    .map(c -> new OWASPCategoryScore(c, OWASPComplianceStatus.COMPLIANT, 100, List.of()))
                    .toList();

            assertThat(evaluatorWith(ruleA).overallScoreOf(allPerfect)).isEqualTo(100);
        }

        @Test
        @DisplayName("returns 100 (default perfect score) when the list is empty")
        void returns100_whenListEmpty() {
            assertThat(evaluatorWith(ruleA).overallScoreOf(List.of())).isEqualTo(100);
        }

        @Test
        @DisplayName("returns 0 when all category scores are 0")
        void returns0_whenAllScoresZero() {
            List<OWASPCategoryScore> allZero = List.of(OWASPCategory.values()).stream()
                    .map(c -> new OWASPCategoryScore(c, OWASPComplianceStatus.NON_COMPLIANT, 0, List.of()))
                    .toList();

            assertThat(evaluatorWith(ruleA).overallScoreOf(allZero)).isEqualTo(0);
        }

        @Test
        @DisplayName("returns integer average (truncated) of the given category scores")
        void returnsIntegerAverage() {
            // One category at 60 (CRITICAL penalty=40), rest at 100 → (60 + 6 × 100) / 7 = 660/7 = 94
            List<OWASPCategoryScore> scores = List.of(OWASPCategory.values()).stream()
                    .map(c -> new OWASPCategoryScore(c, OWASPComplianceStatus.COMPLIANT,
                            c == OWASPCategory.SECURITY_MISCONFIGURATION ? 60 : 100, List.of()))
                    .toList();

            int expected = (60 + (CATEGORY_COUNT - 1) * 100) / CATEGORY_COUNT;
            assertThat(evaluatorWith(ruleA).overallScoreOf(scores)).isEqualTo(expected);
        }

        @Test
        @DisplayName("result matches the overall score produced by evaluate() for the same inputs")
        void matchesOverallFromEvaluate() {
            // Run a real evaluate(), then confirm overallScoreOf(categoryScores) gives the same number.
            when(ruleA.matches(any(), any())).thenReturn(true);
            when(ruleA.severity(any(), any())).thenReturn(FindingSeverity.HIGH);
            when(ruleA.findingLabel(any(), any())).thenReturn(LABEL_A);
            when(ruleA.category()).thenReturn(OWASPCategory.SECURITY_MISCONFIGURATION);

            OWASPEvaluator evaluator = evaluatorWith(ruleA);
            OWASPEvaluationResult evaluated = evaluator.evaluate(
                    SCAN_ID,
                    List.of(finding(FINDING_ID)),
                    List.of(successEngine(SurfaceType.DNS)),
                    List.of());

            int recomputed = evaluator.overallScoreOf(evaluated.categoryScores());
            assertThat(recomputed).isEqualTo(evaluated.overallScore());
        }
    }
}