package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.Finding;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.Comparator;
import java.util.List;

@Component
public class CryptoFailuresSslRule implements OWASPMappingRule {
    @Override
    public OWASPCategory category() {
        return OWASPCategory.SECURITY_MISCONFIGURATION;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine.surfaceType() != SurfaceType.DNS || !engine.success()) return false;

        List<SslFindings> findings = findings(engine);

        return !findings.isEmpty();

    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        List<SslFindings> findings = findings(engine);
        return findings.stream()
                .map(SslFindings::severity)
                .max(Comparator.reverseOrder())
                .orElse(FindingSeverity.NONE);
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<SslFindings> findings = findings(engine);

        return findings.stream()

                .map(SslFindings::finding)

                .findFirst()

                .orElse("DNS Security Misconfiguration");

    }

    List<SslFindings> findings(EngineResult result){
        Object value = result.rawResult().get("findings");

        return value instanceof List<?> list
                ? (List<SslFindings>) list
                : List.of();
    }
}
