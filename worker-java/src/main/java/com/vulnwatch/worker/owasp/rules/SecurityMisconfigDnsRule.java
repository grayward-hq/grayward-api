package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;

import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Set;

/**
 * Maps DNS security misconfigurations (such as SPF and DMARC issues) to A05 – Security Misconfiguration.
 *
 * Source: DnsEngine → Map.of("findings", List<Findings>)
 * Trigger: Findings.title matches known DNS misconfiguration entries.
 */
@Component
public class SecurityMisconfigDnsRule implements OWASPMappingRule {

    private static final Set<String> MISCONFIG_TITLES = Set.of(
            "DMARC_MISSING", "DMARC_MISCONFIGURED", "DMARC_MONITORING_ONLY",
            "DMARC_WEAK_ENFORCEMENT",
            "SPF_MISSING", "SPF_MULTIPLE_RECORDS", "SPF_OVERLY_PERMISSIVE", "SPF_SOFTFAIL"
    );

    private static final Set<String> HIGH_TITLES = Set.of(
            "DMARC_MISSING", "DMARC_MISCONFIGURED",
            "SPF_MISSING", "SPF_MULTIPLE_RECORDS", "SPF_OVERLY_PERMISSIVE"
    );

    @Override
    public OWASPCategory category() {
        return OWASPCategory.SECURITY_MISCONFIGURATION;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine.surfaceType() != SurfaceType.DNS || !engine.success())
            return false;
        List<Findings> findings = castFindings(engine);
        return findings.stream()
                .anyMatch(f -> matchesTitle(f.title(), MISCONFIG_TITLES));
    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        List<Findings> findings = castFindings(engine);
        boolean hasHigh = findings.stream()
                .anyMatch(f -> matchesTitle(f.title(), HIGH_TITLES));
        return hasHigh ? FindingSeverity.HIGH : FindingSeverity.MEDIUM;
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<Findings> findings = castFindings(engine);
        return findings.stream()
                .filter(f -> matchesTitle(f.title(), MISCONFIG_TITLES))
                .map(Findings::description)
                .findFirst()
                .orElse("DNS Security Misconfiguration");
    }


    /**
     * Safely unpacks and casts the raw map results to avoid runtime casting exceptions.
     */
    @SuppressWarnings("unchecked")
    private List<Findings> castFindings(EngineResult engine) {
        if (engine.rawResult() == null) return List.of();
        Object val = engine.rawResult().get("findings");
        return val instanceof List<?> list ? (List<Findings>) list : List.of();
    }

    /**
     * Validates that the finding title is non-null and exists in the designated filter collection.
     */
    private boolean matchesTitle(String title, Set<String> targetTitles) {
        if (title == null)
            return false;
        return targetTitles.contains(title);
    }
}