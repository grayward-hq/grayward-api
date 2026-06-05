package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.nuclei.models.NucleiEngineResult; // Assumed package for NucleiEngineResult
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.Arrays;
import java.util.List;
import java.util.Set;
import java.util.stream.Collectors;

/**
 * Maps missing or misconfigured HTTP security headers to A05 – Security Misconfiguration.
 *
 * Source: NucleiEngine → Map.of("findings", List<NucleiEngineResult>)
 * Trigger: NucleiEngineResult.headerType matches known security header tags.
 */
@Component
public class SecurityMisconfigHeadersRule implements OWASPMappingRule {

    // These headers from nuclei matcher-name are security misconfigurations
    private static final Set<String> SECURITY_HEADERS = Set.of(
            "content-security-policy",
            "strict-transport-security",
            "x-frame-options",
            "x-content-type-options",
            "permissions-policy",
            "referrer-policy",
            "cross-origin-embedder-policy",
            "cross-origin-opener-policy",
            "cross-origin-resource-policy"
    );

    // Higher-impact headers
    private static final Set<String> HIGH_IMPACT = Set.of(
            "content-security-policy",
            "strict-transport-security",
            "x-frame-options"
    );

    @Override
    public OWASPCategory category() {
        return OWASPCategory.SECURITY_MISCONFIGURATION;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine.surfaceType() != SurfaceType.HTTP_HEADERS || !engine.success())
            return false;
        List<NucleiEngineResult> findings = castFindings(engine);
        return findings.stream()
                .anyMatch(f -> matchesHeader(f.headerType(), SECURITY_HEADERS));
    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        List<NucleiEngineResult> findings = castFindings(engine);
        boolean hasHighImpact = findings.stream()
                .anyMatch(f -> matchesHeader(f.headerType(), HIGH_IMPACT));
        return hasHighImpact ? FindingSeverity.HIGH : FindingSeverity.MEDIUM;
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<NucleiEngineResult> findings = castFindings(engine);
        return findings.stream()
                .filter(f -> matchesHeader(f.headerType(), SECURITY_HEADERS))
                .map(f -> "Missing %s".formatted(toDisplayName(f.headerType())))
                .findFirst()
                .orElse("Missing Security Header");
    }


    /**
     * Capitalizes hyphenated header tokens for clean display names.
     */
    private String toDisplayName(String headerType) {
        if (headerType == null || headerType.isBlank())
            return "Security Header";
        return Arrays.stream(headerType.split("-"))
                .filter(w -> !w.isEmpty())
                .map(w -> Character.toUpperCase(w.charAt(0)) + w.substring(1))
                .collect(Collectors.joining("-"));
    }

    /**
     * Safely unpacks and casts the raw map results to avoid runtime casting exceptions.
     */
    @SuppressWarnings("unchecked")
    private List<NucleiEngineResult> castFindings(EngineResult engine) {
        if (engine.rawResult() == null) return List.of();
        Object val = engine.rawResult().get("findings");
        return val instanceof List<?> list ? (List<NucleiEngineResult>) list : List.of();
    }

    /**
     * Validates that the header type is non-null and exists within the designated filter collection.
     */
    private boolean matchesHeader(String headerType, Set<String> targetHeaders) {
        if (headerType == null) return false;
        return targetHeaders.contains(headerType.toLowerCase());
    }
}