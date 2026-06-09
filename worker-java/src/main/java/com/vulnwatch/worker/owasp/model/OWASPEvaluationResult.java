package com.vulnwatch.worker.owasp.model;

import com.vulnwatch.worker.owasp.enums.OWASPComplianceTier;

import java.util.List;

public record OWASPEvaluationResult(
        String scanId,

        List<OWASPFindingMapping> findingMappings,  // one per matched finding

        List<OWASPCategoryScore> categoryScores,    // always all 10 categories

        int overallScore,

        OWASPComplianceTier tier

) {
}
