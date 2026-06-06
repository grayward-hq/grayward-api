package com.vulnwatch.worker.owasp.model;

import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.enums.OWASPComplianceStatus;

public record OWASPFindingMapping(
        String findingId,       // FK → Findings.Id
        String cveId,

        String scanId,

        OWASPCategory category,

        OWASPComplianceStatus status,

        FindingSeverity severity,

        String findingLabel     // human-readable label from the rule

) {
}
