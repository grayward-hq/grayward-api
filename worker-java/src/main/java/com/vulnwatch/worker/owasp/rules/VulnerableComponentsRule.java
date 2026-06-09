package com.vulnwatch.worker.owasp.rules;


import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.List;

/**
 * Maps outdated third-party library or package vulnerabilities to A06 – Vulnerable and Outdated Components.
 *
 * Source: TrivyEngine → Map.of("findings", List<TrivyEngineResult>)
 * Trigger: TrivyEngineResult containing a packageName without a secretLocation (Dependency Scan).
 */
@Component
public class VulnerableComponentsRule implements OWASPMappingRule {

    @Override
    public OWASPCategory category() {
        return OWASPCategory.VULNERABLE_COMPONENTS;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine.surfaceType() != SurfaceType.DEPENDENCY || !engine.success())
            return false;
        List<TrivyEngineResult> findings = castFindings(engine);

        // dependency row logic: packageName must not be null, and it shouldn't be flagged as a secret exposure
        return findings.stream()
                .anyMatch(this::isDependencyFinding);
    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        List<TrivyEngineResult> findings = castFindings(engine);

        boolean hasCritical = findings.stream()
                .filter(this::isDependencyFinding)
                .anyMatch(f -> "CRITICAL".equalsIgnoreCase(f.severity()));

        boolean hasHigh = findings.stream()
                .filter(this::isDependencyFinding)
                .anyMatch(f -> "HIGH".equalsIgnoreCase(f.severity()));

        if (hasCritical)
            return FindingSeverity.CRITICAL;
        if (hasHigh)
            return FindingSeverity.HIGH;
        return FindingSeverity.MEDIUM;
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<TrivyEngineResult> findings = castFindings(engine);
        return findings.stream()
                .filter(this::isDependencyFinding)
                .map(f -> "Vulnerable Component: %s %s".formatted(f.packageName(), f.installedVersion()))
                .findFirst()
                .orElse("Vulnerable Dependency Detected");
    }


    /**
     * Core business condition filtering standard dependency findings from secret leaks.
     */
    private boolean isDependencyFinding(TrivyEngineResult f) {
        return f != null && f.packageName() != null && f.secretLocation() == null;
    }

    /**
     * Safely unpacks and casts the raw map results to avoid runtime casting exceptions.
     */
    @SuppressWarnings("unchecked")
    private List<TrivyEngineResult> castFindings(EngineResult engine) {
        if (engine.rawResult() == null)
            return List.of();
        Object val = engine.rawResult().get("findings");
        return val instanceof List<?> list ? (List<TrivyEngineResult>) list : List.of();
    }
}