package com.vulnwatch.worker.owasp.enums;

import lombok.RequiredArgsConstructor;

@RequiredArgsConstructor
public enum OWASPComplianceStatus {
    COMPLIANT,        // no findings in this category

    PARTIAL,          // only LOW severity findings

    NON_COMPLIANT     // any MEDIUM / HIGH / CRITICAL finding

}
