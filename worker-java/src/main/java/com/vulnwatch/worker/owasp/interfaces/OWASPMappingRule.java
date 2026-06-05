package com.vulnwatch.worker.owasp.interfaces;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;

public interface OWASPMappingRule {

    /** The OWASP category this rule maps into. */
    OWASPCategory category();

    /**
     * Returns true if the rawResult map from this engine surface
     * contains a finding that belongs to this category.
     *
     * rawResult is the exact Map<String, Object> from EngineResult.
     * Cast the "findings" value to the correct typed list using the
     * surface type. ai may be null — implementations must null-check.
     */
    boolean matches(EngineResult engine, AiResult ai);

    /**
     * Severity of the triggered finding.
     * Only called when matches() == true.
     */
    FindingSeverity severity(EngineResult engine, AiResult ai);

    /**
     * Short human-readable label for this specific finding.
     * Used in the category drill-down UI.
     */
    String findingLabel(EngineResult engine, AiResult ai);
}
