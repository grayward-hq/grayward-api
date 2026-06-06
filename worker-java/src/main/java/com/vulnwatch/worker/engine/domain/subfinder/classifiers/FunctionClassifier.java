package com.vulnwatch.worker.engine.domain.subfinder.classifiers;

import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.model.Finding;

public class FunctionClassifier implements Classifier{
    @Override
    public FindingSeverity classify(SubdomainRecord record, SubdomainFindings.SubdomainFindingsBuilder builder) {
        String h = record.host().toLowerCase();
        if (h.contains("auth") || h.contains("identity")) {
            builder.tag("auth").risk(FindingSeverity.CRITICAL)
                    .note("Auth endpoint externally discoverable — credential harvest risk");
        }
        if (h.contains("kyc")) {
            builder.tag("kyc").risk(FindingSeverity.CRITICAL)
                    .note("KYC endpoint — PII exposure, may lack prod-level controls");
        }
        if (h.contains("payroll")) {
            builder.tag("finance").risk(FindingSeverity.CRITICAL)
                    .note("Payroll feed — financial data exfil target");
        }
        if (h.contains("ops")) {
            builder.tag("ops").risk(FindingSeverity.HIGH);
        }

        return builder.build().getRisk();

    }
}
