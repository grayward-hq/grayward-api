package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.nuclei.models.NucleiEngineResult;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

class SecurityMisconfigHeadersRuleTest {

    private SecurityMisconfigHeadersRule rule;
    private AiResult ai;

    @BeforeEach
    void setUp() {
        rule = new SecurityMisconfigHeadersRule();
        ai = mock(AiResult.class);
    }

    // --- category() ---

    @Test
    void category_returnsSecurityMisconfiguration() {
        assertThat(rule.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
    }

    // --- matches() ---

    @Test
    void matches_returnsFalse_whenSurfaceTypeIsNotHttpHeaders() {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(nucleiResult("content-security-policy")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matches_returnsFalse_whenEngineNotSuccessful() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, false,
                List.of(nucleiResult("content-security-policy")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matches_returnsFalse_whenNoFindingsMatchSecurityHeaders() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("some-irrelevant-header")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matches_returnsFalse_whenFindingsEmpty() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true, List.of());
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "content-security-policy",
            "strict-transport-security",
            "x-frame-options",
            "x-content-type-options",
            "permissions-policy",
            "referrer-policy",
            "cross-origin-embedder-policy",
            "cross-origin-opener-policy",
            "cross-origin-resource-policy"
    })
    void matches_returnsTrue_forEachKnownSecurityHeader(String header) {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult(header)));
        assertThat(rule.matches(engine, ai)).isTrue();
    }

    @Test
    void matches_returnsTrue_whenHeaderTypeIsMixedCase() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("Content-Security-Policy")));
        assertThat(rule.matches(engine, ai)).isTrue();
    }

    @Test
    void matches_returnsTrue_whenOnlyOneOfMultipleFindingsMatchesSecurityHeader() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("irrelevant-header"), nucleiResult("x-frame-options")));
        assertThat(rule.matches(engine, ai)).isTrue();
    }

    // --- severity() ---

    @ParameterizedTest
    @ValueSource(strings = {
            "content-security-policy",
            "strict-transport-security",
            "x-frame-options"
    })
    void severity_returnsHigh_forHighImpactHeaders(String header) {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult(header)));
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.HIGH);
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "x-content-type-options",
            "permissions-policy",
            "referrer-policy",
            "cross-origin-embedder-policy",
            "cross-origin-opener-policy",
            "cross-origin-resource-policy"
    })
    void severity_returnsMedium_forNonHighImpactHeaders(String header) {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult(header)));
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.MEDIUM);
    }

    @Test
    void severity_returnsHigh_whenMixOfHighAndMediumImpactPresent() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("permissions-policy"), nucleiResult("x-frame-options")));
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.HIGH);
    }

    @Test
    void severity_returnsMedium_whenFindingsEmpty() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true, List.of());
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.MEDIUM);
    }

    // --- findingLabel() ---

    @Test
    void findingLabel_returnsMissingHeaderLabel_forFirstMatchingSecurityHeader() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("x-frame-options")));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Missing X-Frame-Options");
    }

    @Test
    void findingLabel_skipsNonSecurityHeaders_andReturnsFirstMatch() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("irrelevant-header"), nucleiResult("referrer-policy")));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Missing Referrer-Policy");
    }

    @Test
    void findingLabel_returnsFormattedDisplayName_withCapitalizedTokens() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("content-security-policy")));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Missing Content-Security-Policy");
    }

    @Test
    void findingLabel_returnsFallback_whenNoSecurityHeaderMatches() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult("irrelevant-header")));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Missing Security Header");
    }

    @Test
    void findingLabel_returnsFallback_whenFindingsEmpty() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true, List.of());
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Missing Security Header");
    }

    // --- castFindings() edge cases ---

    @Test
    void castFindings_returnsEmpty_whenRawResultIsNull() {
        EngineResult engine = mock(EngineResult.class);
        when(engine.rawResult()).thenReturn(null);
        // matches() should handle null rawResult without throwing
        when(engine.surfaceType()).thenReturn(SurfaceType.HTTP_HEADERS);
        when(engine.success()).thenReturn(true);
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void castFindings_returnsEmpty_whenFindingsKeyAbsent() {
        EngineResult engine = mock(EngineResult.class);
        when(engine.rawResult()).thenReturn(Map.of());
        when(engine.surfaceType()).thenReturn(SurfaceType.HTTP_HEADERS);
        when(engine.success()).thenReturn(true);
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void castFindings_returnsEmpty_whenFindingsValueIsNotAList() {
        EngineResult engine = mock(EngineResult.class);
        when(engine.rawResult()).thenReturn(Map.of("findings", "unexpected-string"));
        when(engine.surfaceType()).thenReturn(SurfaceType.HTTP_HEADERS);
        when(engine.success()).thenReturn(true);
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matchesHeader_returnsFalse_whenHeaderTypeIsNull() {
        EngineResult engine = engineResult(SurfaceType.HTTP_HEADERS, true,
                List.of(nucleiResult(null)));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    // --- helpers ---

    private EngineResult engineResult(SurfaceType surfaceType, boolean success,
                                      List<NucleiEngineResult> findings) {
        EngineResult engine = mock(EngineResult.class);
        when(engine.surfaceType()).thenReturn(surfaceType);
        when(engine.success()).thenReturn(success);
        when(engine.rawResult()).thenReturn(Map.of("findings", findings));
        return engine;
    }

    private NucleiEngineResult nucleiResult(String headerType) {
        return new NucleiEngineResult(
                "missing-header", "https://example.com", "example.com",
                "93.184.216.34", "Missing security header", "info", headerType
        );
    }
}