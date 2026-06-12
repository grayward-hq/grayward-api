package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
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

class SecurityMisconfigDnsRuleTest {

    private SecurityMisconfigDnsRule rule;
    private AiResult ai;

    @BeforeEach
    void setUp() {
        rule = new SecurityMisconfigDnsRule();
        ai = mock(AiResult.class);
    }

    // --- category() ---

    @Test
    void category_returnsSecurityMisconfiguration() {
        assertThat(rule.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
    }

    // --- matches() ---

    @Test
    void matches_returnsFalse_whenSurfaceTypeIsNotDns() {
        EngineResult engine = engineResult(SurfaceType.PORTS, true,
                List.of(Findings.high("DMARC_MISSING", "No DMARC record")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matches_returnsFalse_whenEngineNotSuccessful() {
        EngineResult engine = engineResult(SurfaceType.DNS, false,
                List.of(Findings.high("DMARC_MISSING", "No DMARC record")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matches_returnsFalse_whenFindingsEmpty() {
        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of());
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void matches_returnsFalse_whenNoFindingTitleMatchesMisconfigSet() {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.info("SOME_UNRELATED_TITLE", "desc")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "DMARC_MISSING", "DMARC_MISCONFIGURED", "DMARC_MONITORING_ONLY",
            "DMARC_WEAK_ENFORCEMENT",
            "SPF_MISSING", "SPF_MULTIPLE_RECORDS", "SPF_OVERLY_PERMISSIVE", "SPF_SOFTFAIL"
    })
    void matches_returnsTrue_forEachKnownMisconfigTitle(String title) {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.medium(title, "desc")));
        assertThat(rule.matches(engine, ai)).isTrue();
    }

    @Test
    void matches_returnsTrue_whenOnlyOneOfMultipleFindingsMatchesMisconfigSet() {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.info("UNRELATED", "desc"), Findings.high("SPF_MISSING", "desc")));
        assertThat(rule.matches(engine, ai)).isTrue();
    }

    @Test
    void matches_returnsFalse_whenFindingTitleIsNull() {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(new Findings(FindingSeverity.HIGH, null, "desc")));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    // --- severity() ---

    @ParameterizedTest
    @ValueSource(strings = {
            "DMARC_MISSING", "DMARC_MISCONFIGURED",
            "SPF_MISSING", "SPF_MULTIPLE_RECORDS", "SPF_OVERLY_PERMISSIVE"
    })
    void severity_returnsHigh_forHighImpactTitles(String title) {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.high(title, "desc")));
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.HIGH);
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "DMARC_MONITORING_ONLY", "DMARC_WEAK_ENFORCEMENT", "SPF_SOFTFAIL"
    })
    void severity_returnsMedium_forNonHighImpactTitles(String title) {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.medium(title, "desc")));
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.MEDIUM);
    }

    @Test
    void severity_returnsHigh_whenMixOfHighAndMediumTitlesPresent() {
        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of(
                Findings.medium("SPF_SOFTFAIL", "desc"),
                Findings.high("DMARC_MISSING", "desc")
        ));
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.HIGH);
    }

    @Test
    void severity_returnsMedium_whenFindingsEmpty() {
        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of());
        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.MEDIUM);
    }

    // --- findingLabel() ---

    @Test
    void findingLabel_returnsDescriptionOfFirstMatchingFinding() {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.high("DMARC_MISSING", "DMARC record not found")));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("DMARC record not found");
    }

    @Test
    void findingLabel_skipsNonMisconfigFindings_andReturnsFirstMatch() {
        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of(
                Findings.info("UNRELATED", "should be skipped"),
                Findings.medium("SPF_SOFTFAIL", "SPF soft fail configured")
        ));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("SPF soft fail configured");
    }

    @Test
    void findingLabel_returnsFallback_whenNoFindingMatchesMisconfigSet() {
        EngineResult engine = engineResult(SurfaceType.DNS, true,
                List.of(Findings.info("UNRELATED", "desc")));
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("DNS Security Misconfiguration");
    }

    @Test
    void findingLabel_returnsFallback_whenFindingsEmpty() {
        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of());
        assertThat(rule.findingLabel(engine, ai)).isEqualTo("DNS Security Misconfiguration");
    }

    // --- castFindings() edge cases ---

    @Test
    void castFindings_returnsEmpty_whenRawResultIsNull() {
        EngineResult engine = mock(EngineResult.class);
        when(engine.surfaceType()).thenReturn(SurfaceType.DNS);
        when(engine.success()).thenReturn(true);
        when(engine.rawResult()).thenReturn(null);
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void castFindings_returnsEmpty_whenFindingsKeyAbsent() {
        EngineResult engine = mock(EngineResult.class);
        when(engine.surfaceType()).thenReturn(SurfaceType.DNS);
        when(engine.success()).thenReturn(true);
        when(engine.rawResult()).thenReturn(Map.of());
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    @Test
    void castFindings_returnsEmpty_whenFindingsValueIsNotAList() {
        EngineResult engine = mock(EngineResult.class);
        when(engine.surfaceType()).thenReturn(SurfaceType.DNS);
        when(engine.success()).thenReturn(true);
        when(engine.rawResult()).thenReturn(Map.of("findings", "unexpected-string"));
        assertThat(rule.matches(engine, ai)).isFalse();
    }

    // --- helpers ---

    private EngineResult engineResult(SurfaceType surfaceType, boolean success,
                                      List<Findings> findings) {
        EngineResult engine = mock(EngineResult.class);
        when(engine.surfaceType()).thenReturn(surfaceType);
        when(engine.success()).thenReturn(success);
        when(engine.rawResult()).thenReturn(Map.of("findings", findings));
        return engine;
    }
}