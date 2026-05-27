package com.vulnwatch.worker.engine.domain.dnsrecon.utility;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import com.vulnwatch.worker.engine.domain.dnsrecon.rules.Rule;

import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Service that applies rules to scan context */
@Service
@RequiredArgsConstructor
public class RuleEngine {

  private final List<Rule> rules;

  public Map<String, Object> scanJob(ScanContext context) {

    Map<String, Object> result = new LinkedHashMap<>();
    List<Findings> findings = new ArrayList<>();

    result.put("aRecords", context.aRecordList());
    result.put("aaaaRecords", context.aaaaRecordList());
    result.put("nsRecords", context.nsRecordList());
    result.put("mxRecords", context.mxRecordList());
    result.put("dnsKeyRecords", context.dnsKeyRecordList());
    result.put("txtRecords", context.txtRecordList());

    for (Rule rule : rules) {
      findings.addAll(rule.evaluate(context));
    }
    result.put("findings", findings);

    return result;
  }
}
