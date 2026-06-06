//package com.vulnwatch.worker.owasp.rules;
//
//import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
//import com.vulnwatch.worker.enums.FindingSeverity;
//import com.vulnwatch.worker.enums.SurfaceType;
//import com.vulnwatch.worker.model.AiResult;
//import com.vulnwatch.worker.model.EngineResult;
//import com.vulnwatch.worker.owasp.enums.OWASPCategory;
//import org.junit.jupiter.api.BeforeEach;
//import org.junit.jupiter.api.Test;
//
//import java.util.List;
//import java.util.Map;
//
//import static org.assertj.core.api.Assertions.assertThat;
//import static org.mockito.Mockito.mock;
//import static org.mockito.Mockito.when;
//
//public class CryptoFailureSslRuleTest {
//
//    private CryptoFailuresSslRule rule;
//    private AiResult ai;
//
//    @BeforeEach
//    void setUp() {
//        rule = new CryptoFailuresSslRule();
//        ai = mock(AiResult.class);
//    }
//
//    // --- category() ---
//
//    @Test
//    void category_returnsSecurityMisconfiguration() {
//        assertThat(rule.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
//    }
//
//    // --- matches() ---
//
//    @Test
//    void matches_returnsFalse_whenSurfaceTypeIsNotDns() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true, List.of(finding(FindingSeverity.HIGH)));
//        assertThat(rule.matches(engine, ai)).isFalse();
//    }
//
//    @Test
//    void matches_returnsFalse_whenEngineNotSuccessful() {
//        EngineResult engine = engineResult(SurfaceType.DNS, false, List.of(finding(FindingSeverity.HIGH)));
//        assertThat(rule.matches(engine, ai)).isFalse();
//    }
//
//    @Test
//    void matches_returnsFalse_whenFindingsEmpty() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of());
//        assertThat(rule.matches(engine, ai)).isFalse();
//    }
//
//    @Test
//    void matches_returnsTrue_whenDnsSurfaceSuccessfulWithFindings() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of(finding(FindingSeverity.MEDIUM)));
//        assertThat(rule.matches(engine, ai)).isTrue();
//    }
//
//    // --- severity() ---
//
//    @Test
//    void severity_returnsMaxSeverityAcrossFindings() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true,
//                List.of(finding(FindingSeverity.LOW), finding(FindingSeverity.CRITICAL)));
//        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.CRITICAL);
//    }
//
//    @Test
//    void severity_returnsSingleSeverity_whenOneFinding() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of(finding(FindingSeverity.HIGH)));
//        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.HIGH);
//    }
//
//    @Test
//    void severity_returnsNone_whenFindingsEmpty() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of());
//        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.NONE);
//    }
//
//    // --- findingLabel() ---
//
//    @Test
//    void findingLabel_returnsFirstFindingLabel() {
//        SslFindings first = findingWithLabel("Weak cipher suite detected");
//        SslFindings second = findingWithLabel("TLS 1.0 enabled");
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of(first, second));
//        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Weak cipher suite detected");
//    }
//
//    @Test
//    void findingLabel_returnsFallback_whenFindingsEmpty() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of());
//        assertThat(rule.findingLabel(engine, ai)).isEqualTo("DNS Security Misconfiguration");
//    }
//
//    // --- findings() raw result edge cases ---
//
//    @Test
//    void findings_returnsEmptyList_whenRawResultValueIsNotAList() {
//        EngineResult engine = mock(EngineResult.class);
//        when(engine.rawResult()).thenReturn(Map.of("findings", "not-a-list"));
//        assertThat(rule.findings(engine)).isEmpty();
//    }
//
//    @Test
//    void findings_returnsEmptyList_whenFindingsKeyAbsent() {
//        EngineResult engine = mock(EngineResult.class);
//        when(engine.rawResult()).thenReturn(Map.of());
//        assertThat(rule.findings(engine)).isEmpty();
//    }
//
//    // --- helpers ---
//
//    private EngineResult engineResult(SurfaceType surfaceType, boolean success,
//                                      List<SslFindings> findings) {
//        EngineResult engine = mock(EngineResult.class);
//        when(engine.surfaceType()).thenReturn(surfaceType);
//        when(engine.success()).thenReturn(success);
//        when(engine.rawResult()).thenReturn(Map.of("findings", findings));
//        return engine;
//    }
//
//    private SslFindings finding(FindingSeverity severity) {
//        return SslFindings.builder()
//                .id("CVE-0000").ip("192.168.1.1").port("443")
//                .finding("SSL misconfiguration").severity(severity)
//                .build();
//    }
//
//    private SslFindings findingWithLabel(String label) {
//        return SslFindings.builder()
//                .id("CVE-0001").ip("192.168.1.1").port("443")
//                .finding(label).severity(FindingSeverity.MEDIUM)
//                .build();
//    }
//}