package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;

public class BrokenAccessControlPortsRule implements OWASPMappingRule {
    @Override
    public OWASPCategory category() {
        return null;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        return false;
    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        return null;
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        return "";
    }
}
