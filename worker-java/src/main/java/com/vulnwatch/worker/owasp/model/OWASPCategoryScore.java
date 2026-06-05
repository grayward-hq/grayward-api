package com.vulnwatch.worker.owasp.model;

import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.enums.OWASPComplianceStatus;

import java.util.List;

public record OWASPCategoryScore(
        OWASPCategory category,

        OWASPComplianceStatus status,

        int score,                          // 0–100

        List<OWASPFindingMapping> findings  // the individual mappings that contributed

) {
}
