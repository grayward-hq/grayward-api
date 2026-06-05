package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Severity;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.List;

@Component
public class BrokenAccessControlPortsRule implements OWASPMappingRule {
    @Override
    public OWASPCategory category() {
        return OWASPCategory.BROKEN_ACCESS_CONTROL;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine.surfaceType() != SurfaceType.PORTS || !engine.success()) return false;

        List<NmapFindings> findings = findings(engine);

        return !findings.isEmpty();

    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        List<NmapFindings> findings = findings(engine);

        boolean hasCritical = findings.stream()
                .anyMatch(f -> {
                    FindingSeverity severity = f.severity();
                    return severity == FindingSeverity.CRITICAL;
                });
        return hasCritical ? FindingSeverity.CRITICAL : FindingSeverity.HIGH;

    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<NmapFindings> findings = findings(engine);

        return findings.stream()

                .map(NmapFindings::finding)

                .findFirst()

                .orElse("Exposed Service / Broken Access Control");

    }


    List<NmapFindings> findings(EngineResult result){
        Object value = result.rawResult().get("findings");

        return value instanceof List<?> list
                ? (List<NmapFindings>) list
                : List.of();
    }
}
