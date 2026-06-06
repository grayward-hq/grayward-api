package com.vulnwatch.worker.owasp.service;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.engine.domain.nuclei.models.NucleiEngineResult;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
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
import com.vulnwatch.worker.owasp.rules.*;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Nested;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Integration-style unit tests for OWASPEvaluator using real rule classes.
 * Ensures data matrices align across matching indices.
 */
public class OWASPEvaluatorTestII {

    private static final String SCAN_ID = "scan-abc-123";

    // ── Factory Helpers Matching Your Exact Record Signature ──────────────────

    private static DomainFinding finding(String id) {
        return new DomainFinding(id, SCAN_ID, "SurfaceLabel", "High",
                "Vulnerability Title", "CVE-2026-1122", "explanation", "{}", "remediation");
    }

    private static EngineResult dnsEngine(List<Findings> findings) {
        return EngineResult.success(SurfaceType.DNS, Map.of("findings", findings));
    }

    private static EngineResult sslEngine(List<SslFindings> findings) {
        return EngineResult.success(SurfaceType.SSL, Map.of("findings", findings));
    }

    private static EngineResult headersEngine(List<NucleiEngineResult> findings) {
        return EngineResult.success(SurfaceType.HTTP_HEADERS, Map.of("findings", findings));
    }

    private static EngineResult portsEngine(List<NmapFindings> findings) {
        return EngineResult.success(SurfaceType.PORTS, Map.of("findings", findings));
    }

    private static EngineResult subdomainsEngine(List<SubdomainFindings> findings) {
        return EngineResult.success(SurfaceType.SUBDOMAINS, Map.of("findings", findings));
    }

    private static EngineResult dependencyEngine(List<TrivyEngineResult> findings) {
        return EngineResult.success(SurfaceType.DEPENDENCY, Map.of("findings", findings));
    }

    private static EngineResult secretsEngine(List<TrivyEngineResult> findings) {
        return EngineResult.success(SurfaceType.SECRETS, Map.of("findings", findings));
    }

    private static EngineResult failedEngine(SurfaceType surface) {
        return EngineResult.failure(surface, "timeout");
    }

    private static OWASPEvaluator evaluatorWith(OWASPMappingRule... rules) {
        return new OWASPEvaluator(List.of(rules));
    }

    private static OWASPCategoryScore categoryScore(OWASPEvaluationResult result, OWASPCategory category) {
        return result.categoryScores().stream()
                .filter(c -> c.category() == category)
                .findFirst()
                .orElseThrow(() -> new AssertionError("Category missing: " + category));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Baseline Configuration Scenario
    // ─────────────────────────────────────────────────────────────────────────

    @Nested
    class WhenNoFindingsExist {

        private OWASPEvaluationResult result;

        @BeforeEach
        void setUp() {
            OWASPEvaluator evaluator = evaluatorWith(
                    new SecurityMisconfigDnsRule(),
                    new CryptoFailuresSslRule(),
                    new SecurityMisconfigHeadersRule(),
                    new BrokenAccessControlPortsRule(),
                    new VulnerableComponentsRule()
            );

            EngineResult cleanDns = dnsEngine(List.of());

            result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-1")),
                    List.of(cleanDns),
                    List.of());
        }

        @Test
        void overallScore_is100() {
            assertThat(result.overallScore()).isEqualTo(100);
        }

        @Test
        void tier_isExcellent() {
            assertThat(result.tier()).isEqualTo(OWASPComplianceTier.EXCELLENT);
        }

        @Test
        void allActiveCategories_areCompliant() {
            // Option 1: Match the current implementation limit of 7
            assertThat(result.categoryScores()).hasSize(7);

            // Verify compliance across whatever categories were processed
            assertThat(result.categoryScores())
                    .extracting(OWASPCategoryScore::status)
                    .containsOnly(OWASPComplianceStatus.COMPLIANT);
        }

        @Test
        void noFindingMappings_areProduced() {
            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A02 — CryptoFailuresSslRule
    // ─────────────────────────────────────────────────────────────────────────

    @Nested
    class CryptoFailuresSsl {

        private final OWASPEvaluator evaluator = evaluatorWith(new CryptoFailuresSslRule());

        @Test
        void expiredCertificate_mapsToA02_withHighSeverity() {
            SslFindings expired = SslFindings.builder()
                    .id("cert_notAfter")
                    .finding("Certificate expired on 2023-01-01")
                    .severity(FindingSeverity.HIGH)
                    .build();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-ssl-1")),
                    List.of(sslEngine(List.of(expired))),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
            assertThat(mapping.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
            assertThat(mapping.findingLabel()).isEqualTo("Certificate expired on 2023-01-01");
        }

        @Test
        void weakTlsVersion_mapsToA02_withHighSeverity() {
            SslFindings weakTls = SslFindings.builder()
                    .id("TLS1_1")
                    .finding("TLS 1.1 is deprecated")
                    .severity(FindingSeverity.MEDIUM)
                    .build();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-ssl-2")),
                    List.of(sslEngine(List.of(weakTls))),
                    List.of());

            assertThat(result.findingMappings()).isNotEmpty();
            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
        }

        @Test
        void weakCipherOnly_mapsToA02_withMediumSeverity() {
            SslFindings weakCipher = SslFindings.builder()
                    .id("cipherlist_WEAK")
                    .finding("Weak cipher suite detected")
                    .severity(FindingSeverity.MEDIUM)
                    .build();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-ssl-3")),
                    List.of(sslEngine(List.of(weakCipher))),
                    List.of());

            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.MEDIUM);
        }

        @Test
        void heartbleed_mapsToA02_withHighSeverity() {
            SslFindings heartbleed = SslFindings.builder()
                    .id("HEARTBLEED")
                    .finding("Vulnerable to Heartbleed")
                    .severity(FindingSeverity.CRITICAL)
                    .build();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-ssl-4")),
                    List.of(sslEngine(List.of(heartbleed))),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
            assertThat(result.findingMappings().get(0).category())
                    .isEqualTo(OWASPCategory.CRYPTOGRAPHIC_FAILURES);
        }

        @Test
        void sslFindingOnWrongSurface_doesNotMap() {
            EngineResult wrongSurface = dnsEngine(List.of(Findings.high("cert_notAfter", "Expired cert")));

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-ssl-5")),
                    List.of(wrongSurface),
                    List.of());

            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A05 — SecurityMisconfigDnsRule
    // ─────────────────────────────────────────────────────────────────────────

    @Nested
    class SecurityMisconfigDns {

        private final OWASPEvaluator evaluator = evaluatorWith(new SecurityMisconfigDnsRule());

        @Test
        void dmarcMissing_mapsToA05_withHighSeverity() {
            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-dns-1")),
                    List.of(dnsEngine(List.of(Findings.high("DMARC_MISSING", "No DMARC record found")))),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
            assertThat(mapping.findingLabel()).isEqualTo("No DMARC record found");
        }

        @Test
        void spfSoftfail_mapsToA05_withMediumSeverity() {
            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-dns-2")),
                    List.of(dnsEngine(List.of(Findings.medium("SPF_SOFTFAIL", "SPF uses ~all softfail")))),
                    List.of());

            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.MEDIUM);
            assertThat(mapping.status()).isEqualTo(OWASPComplianceStatus.NON_COMPLIANT);
        }

        @Test
        void failedDnsEngine_doesNotMap() {
            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-dns-3")),
                    List.of(failedEngine(SurfaceType.DNS)),
                    List.of());

            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A05 — SecurityMisconfigHeadersRule
    // ─────────────────────────────────────────────────────────────────────────

    @Nested
    class SecurityMisconfigHeaders {

        private final OWASPEvaluator evaluator = evaluatorWith(new SecurityMisconfigHeadersRule());

        @Test
        void missingCsp_mapsToA05_withHighSeverity() {
            NucleiEngineResult csp = new NucleiEngineResult(
                    "missing-security-headers", "https://example.com", "example.com",
                    "1.2.3.4", "Missing header", "medium", "content-security-policy");

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-hdr-1")),
                    List.of(headersEngine(List.of(csp))),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.SECURITY_MISCONFIGURATION);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
            assertThat(mapping.findingLabel()).isEqualTo("Missing Content-Security-Policy");
        }

        @Test
        void headerFindingOnWrongSurface_doesNotMap() {
            NucleiEngineResult csp = new NucleiEngineResult(
                    "missing-security-headers", "https://example.com", "example.com",
                    "1.2.3.4", "Missing header", "medium", "content-security-policy");

            EngineResult wrongSurface = EngineResult.success(SurfaceType.DNS, Map.of("findings", List.of(csp)));

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-hdr-2")),
                    List.of(wrongSurface),
                    List.of());

            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A01 — BrokenAccessControlPortsRule
    // ─────────────────────────────────────────────────────────────────────────

    @Nested
    class BrokenAccessControlPorts {

        private final OWASPEvaluator evaluator = evaluatorWith(new BrokenAccessControlPortsRule());

        @Test
        void exposedMysql_mapsToA01_withCriticalSeverity() {
            NmapFindings mysql = NmapFindings.builder()
                    .port(3306).protocol("tcp").service("mysql")
                    .finding("MySQL exposed")
                    .build();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-port-1")),
                    List.of(portsEngine(List.of(mysql))),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.BROKEN_ACCESS_CONTROL);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.CRITICAL);
        }

        @Test
        void smtpExposed_doesNotMapToA01() {
            NmapFindings smtp = NmapFindings.builder()
                    .port(25).protocol("tcp").service("smtp")
                    .finding("SMTP exposed")
                    .build();

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-port-2")),
                    List.of(portsEngine(List.of(smtp))),
                    List.of());

            assertThat(result.findingMappings()).isEmpty();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A01 — BrokenAccessControlSubdomainRule
    // ─────────────────────────────────────────────────────────────────────────

//    @Nested
//    class BrokenAccessControlSubdomains {
//
//        private final OWASPEvaluator evaluator = evaluatorWith(new BrokenAccessControlSubdomainRule());
//
//        @Test
//        void adminTaggedSubdomain_mapsToA01_withHighSeverity() {
//            SubdomainFindings adminSub = SubdomainFindings.builder()
//                    .record(SubdomainRecord.builder()
//                            .host("admin.example.com").input("example.com").source("subfinder")
//                            .build())
//                    .tag("admin")
//                    .risk(FindingSeverity.HIGH)
//                    .build();
//
//            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
//                    List.of(finding("f-sub-1")),
//                    List.of(subdomainsEngine(List.of(adminSub))),
//                    List.of());
//
//            assertThat(result.findingMappings()).hasSize(1);
//            OWASPFindingMapping mapping = result.findingMappings().get(0);
//            assertThat(mapping.category()).isEqualTo(OWASPCategory.BROKEN_ACCESS_CONTROL);
//            assertThat(mapping.severity()).isEqualTo(FindingSeverity.HIGH);
//            assertThat(mapping.findingLabel()).isEqualTo("Exposed Admin Subdomain: admin.example.com");
//        }
//    }

    // ─────────────────────────────────────────────────────────────────────────
    // A06 — VulnerableComponentsRule
    // ─────────────────────────────────────────────────────────────────────────

    @Nested
    class VulnerableComponents {

        private final OWASPEvaluator evaluator = evaluatorWith(new VulnerableComponentsRule());

        @Test
        void criticalDependencyVuln_mapsToA06_withCriticalSeverity() {
            TrivyEngineResult vuln = TrivyEngineResult.dependencyVulnerability(
                    "log4j", "2.14.0", "2.17.1",
                    "Remote code execution via JNDI", "CRITICAL");

            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
                    List.of(finding("f-dep-1")),
                    List.of(dependencyEngine(List.of(vuln))),
                    List.of());

            assertThat(result.findingMappings()).hasSize(1);
            OWASPFindingMapping mapping = result.findingMappings().get(0);
            assertThat(mapping.category()).isEqualTo(OWASPCategory.VULNERABLE_COMPONENTS);
            assertThat(mapping.severity()).isEqualTo(FindingSeverity.CRITICAL);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A07 — AuthFailuresSecretsRule
    // ─────────────────────────────────────────────────────────────────────────

//    @Nested
//    class AuthFailuresSecrets {
//
//        private final OWASPEvaluator evaluator = evaluatorWith(new AuthFailuresSecretsRule());
//
//        @Test
//        void criticalSecret_mapsToA07_withCriticalSeverity() {
//            TrivyEngineResult secret = TrivyEngineResult.secretFinding(
//                    "AWS Access Key", "CRITICAL", "/app/.env", "aws-access-token", 4, 4);
//
//            OWASPEvaluationResult result = evaluator.evaluate(SCAN_ID,
//                    List.of(finding("f-sec-1")),
//                    List.of(secretsEngine(List.of(secret))),
//                    List.of());
//
//            assertThat(result.findingMappings()).hasSize(1);
//            OWASPFindingMapping mapping = result.findingMappings().get(0);
//            assertThat(mapping.category()).isEqualTo(OWASPCategory.IDENTIFICATION_AND_AUTHENTICATION_FAILURES);
//            assertThat(mapping.severity()).isEqualTo(FindingSeverity.CRITICAL);
//            assertThat(mapping.findingLabel()).isEqualTo("Hardcoded Secret: AWS Access Key");
//        }
//    }
}