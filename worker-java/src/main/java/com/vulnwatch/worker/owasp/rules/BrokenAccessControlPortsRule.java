package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.Collections;
import java.util.List;
import java.util.Set;

@Component
public class BrokenAccessControlPortsRule implements OWASPMappingRule {

    // Services that indicate broken access control when publicly exposed
    private static final Set<String> ADMIN_FINDINGS = Set.of(
            "HTTP alternate/admin port",   // port 8080
            "MySQL exposed",               // port 3306
            "PostgreSQL exposed",          // port 5432
            "SMB exposed",                 // port 445
            "X11 service exposed"
    );

    private static final Set<String> CRITICAL_FINDINGS = Set.of(
            "MySQL exposed", "PostgreSQL exposed", "SMB exposed"
    );

    @Override
    public OWASPCategory category() {
        return OWASPCategory.BROKEN_ACCESS_CONTROL;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine == null || engine.surfaceType() != SurfaceType.PORTS || !engine.success()) {
            return false;
        }
        List<NmapFindings> findings = extractFindings(engine);
        return findings.stream().anyMatch(f -> ADMIN_FINDINGS.contains(f.finding()));
    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        List<NmapFindings> findings = extractFindings(engine);
        boolean hasCritical = findings.stream()
                .anyMatch(f -> CRITICAL_FINDINGS.contains(f.finding()));
        return hasCritical ? FindingSeverity.CRITICAL : FindingSeverity.HIGH;
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<NmapFindings> findings = extractFindings(engine);
        return findings.stream()
                .filter(f -> ADMIN_FINDINGS.contains(f.finding()))
                .map(NmapFindings::finding)
                .findFirst()
                .orElse("Exposed Service / Broken Access Control");
    }

    /**
     * Safely extracts and casts NmapFindings from the EngineResult rawResult map payload.
     * Guards against ClassCastExceptions during composite structural iteration cycles.
     */
    @SuppressWarnings("unchecked")
    private List<NmapFindings> extractFindings(EngineResult result) {
        if (result == null || result.rawResult() == null) {
            return Collections.emptyList();
        }

        Object value = result.rawResult().get("findings");
        if (value instanceof List<?>) {
            List<?> rawList = (List<?>) value;
            // Safe checking of runtime type of elements inside list
            if (!rawList.isEmpty() && !(rawList.get(0) instanceof NmapFindings)) {
                return Collections.emptyList();
            }
            return (List<NmapFindings>) rawList;
        }

        return Collections.emptyList();
    }
}