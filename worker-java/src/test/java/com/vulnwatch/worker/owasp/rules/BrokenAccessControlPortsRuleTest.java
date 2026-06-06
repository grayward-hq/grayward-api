//package com.vulnwatch.worker.owasp.rules;
//
//import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
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
//class BrokenAccessControlPortsRuleTest {
//
//    private BrokenAccessControlPortsRule rule;
//    private AiResult ai;
//
//    @BeforeEach
//    void setUp() {
//        rule = new BrokenAccessControlPortsRule();
//        ai = mock(AiResult.class);
//    }
//
//    // --- category() ---
//
//    @Test
//    void category_returnsBrokenAccessControl() {
//        assertThat(rule.category()).isEqualTo(OWASPCategory.BROKEN_ACCESS_CONTROL);
//    }
//
//    // --- matches() ---
//
//    @Test
//    void matches_returnsFalse_whenSurfaceTypeIsNotPorts() {
//        EngineResult engine = engineResult(SurfaceType.DNS, true, List.of(highFinding()));
//        assertThat(rule.matches(engine, ai)).isFalse();
//    }
//
//    @Test
//    void matches_returnsFalse_whenEngineNotSuccessful() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, false, List.of(highFinding()));
//        assertThat(rule.matches(engine, ai)).isFalse();
//    }
//
//    @Test
//    void matches_returnsFalse_whenFindingsEmpty() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true, List.of());
//        assertThat(rule.matches(engine, ai)).isFalse();
//    }
//
//    @Test
//    void matches_returnsTrue_whenPortsSurfaceSuccessfulWithFindings() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true, List.of(highFinding()));
//        assertThat(rule.matches(engine, ai)).isTrue();
//    }
//
//    // --- severity() ---
//
//    @Test
//    void severity_returnsCritical_whenAnyFindingIsCritical() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true,
//                List.of(highFinding(), criticalFinding()));
//        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.CRITICAL);
//    }
//
//    @Test
//    void severity_returnsHigh_whenNoCriticalFindingsPresent() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true, List.of(highFinding()));
//        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.HIGH);
//    }
//
//    @Test
//    void severity_returnsCritical_whenAllFindingsAreCritical() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true,
//                List.of(criticalFinding(), criticalFinding()));
//        assertThat(rule.severity(engine, ai)).isEqualTo(FindingSeverity.CRITICAL);
//    }
//
//    // --- findingLabel() ---
//
//    @Test
//    void findingLabel_returnsFirstFindingLabel() {
//        NmapFindings first = findingWithLabel("SSH exposed");
//        NmapFindings second = findingWithLabel("HTTP exposed");
//        EngineResult engine = engineResult(SurfaceType.PORTS, true, List.of(first, second));
//        assertThat(rule.findingLabel(engine, ai)).isEqualTo("SSH exposed");
//    }
//
//    @Test
//    void findingLabel_returnsFallback_whenFindingsEmpty() {
//        EngineResult engine = engineResult(SurfaceType.PORTS, true, List.of());
//        assertThat(rule.findingLabel(engine, ai)).isEqualTo("Exposed Service / Broken Access Control");
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
//                                      List<NmapFindings> findings) {
//        EngineResult engine = mock(EngineResult.class);
//        when(engine.surfaceType()).thenReturn(surfaceType);
//        when(engine.success()).thenReturn(success);
//        when(engine.rawResult()).thenReturn(Map.of("findings", findings));
//        return engine;
//    }
//
//    private NmapFindings highFinding() {
//        return NmapFindings.builder()
//                .port(22).protocol("tcp").service("ssh")
//                .finding("SSH exposed").severity(FindingSeverity.HIGH)
//                .build();
//    }
//
//    private NmapFindings criticalFinding() {
//        return NmapFindings.builder()
//                .port(6000).protocol("tcp").service("x11")
//                .finding("X11 exposed").severity(FindingSeverity.CRITICAL)
//                .build();
//    }
//
//    private NmapFindings findingWithLabel(String label) {
//        return NmapFindings.builder()
//                .port(80).protocol("tcp").service("http")
//                .finding(label).severity(FindingSeverity.HIGH)
//                .build();
//    }
//}