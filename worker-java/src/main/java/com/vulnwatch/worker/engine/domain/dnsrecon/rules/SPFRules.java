package com.vulnwatch.worker.engine.domain.dnsrecon.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;

/** Rule for extracting insights from SPF Records */
@Component
public class SPFRules implements Rule {
  @Override
  public List<Findings> evaluate(ScanContext context) {
    List<String> spfRecords =
        context.txtRecordList().stream()
            .filter(v -> v.contains("v=spf1"))
            .toList();

    List<Findings> findings = new ArrayList<>();

    if (spfRecords.isEmpty()) {
      findings.add(Findings.high("SPF_MISSING", "No SPF record found"));
      return findings;
    }

    if (spfRecords.size() > 1) {
      findings.add(Findings.high("SPF_MULTIPLE_RECORDS", "Multiple SPF records detected"));

      return findings;

    } else {

      String spf = spfRecords.getFirst();

      if (spf.contains("+all")) {
        findings.add(Findings.high("SPF_OVERLY_PERMISSIVE", "SPF uses +all (allows any sender)"));
      }

      if (spf.contains("~all")) {
        findings.add(Findings.medium("SPF_SOFTFAIL", "SPF uses softfail (~all)"));
      }

      long includes =
          spf.chars().filter(c -> c == 'i').count(); // rough proxy; you can improve later

      if (includes > 10) {
        findings.add(Findings.medium("SPF_TOO_MANY_LOOKUPS", "SPF may exceed RFC lookup limits"));
      }
    }

    return findings;
  }
}
