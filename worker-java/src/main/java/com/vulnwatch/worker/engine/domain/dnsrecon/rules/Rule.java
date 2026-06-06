package com.vulnwatch.worker.engine.domain.dnsrecon.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;

import java.util.List;

/** General Rule interface for scan context processing */
public interface Rule {
    List<Findings> evaluate(ScanContext context);
}
