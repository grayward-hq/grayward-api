package com.vulnwatch.worker.engine.domain.dnsrecon.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;

/** Rule for processing DMARC records */
@Component
public class DmarcRules implements Rule {
  @Override
  public List<Findings> evaluate(ScanContext context) {
    if (context.getDmarcRecords().isEmpty()) {
      return List.of(Findings.high("DMARC_MISSING", "No DMARC record found"));
    } else if (context.txtRecordList().size() > 1) {
      return List.of(Findings.high("DMARC_MISCONFIGURED", "No DMARC record found"));
    } else {

      String txt = String.join("", context.getDmarcRecords().getFirst());
      List<Findings> findings = new ArrayList<>();

      if (txt.contains("p=none")) {
        findings.add(Findings.medium("DMARC_MONITORING_ONLY", "DMARC policy is set to p=none"));
      }

      if (!txt.contains("rua=")) {
        findings.add(Findings.low("DMARC_NO_REPORTING", "DMARC missing aggregate reports (rua)"));
      }

      if (txt.contains("pct=0") || txt.contains("pct=10")) {
        findings.add(
            Findings.medium("DMARC_WEAK_ENFORCEMENT", "DMARC enforcement is partial (low pct)"));
      }

      return findings;
    }
  }
}
