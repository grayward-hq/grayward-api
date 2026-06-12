package com.vulnwatch.worker.engine.domain.dnsrecon.rules;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.Findings;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;

import org.springframework.stereotype.Component;

import java.net.InetAddress;
import java.net.UnknownHostException;
import java.util.ArrayList;
import java.util.List;

/** Rules for extracting insights on */
@Component
public class IpRules implements Rule {

  @Override
  public List<Findings> evaluate(ScanContext context) {

    List<Findings> findings = new ArrayList<>();

    if (context.aRecordList().isEmpty()) {
      findings.add(Findings.medium("MISSING_IPV4", "DNS lookup returned no IPv4 addresses"));
    }

    if (context.aaaaRecordList().isEmpty()) {
      findings.add(Findings.medium("MISSING_IPV6", "DNS lookup returned no IPv6 addresses"));
    }

    context
        .aRecordList()
        .forEach(
            aRecord -> {

                if (isInternal(aRecord)) {
                findings.add(
                    Findings.high(
                        "PRIVATE_IPV4_LEAK", "A record resolves to internal IPv4 address: " + aRecord));
              }
            });

    context
        .aaaaRecordList()
        .forEach(
            aRecord -> {

                if (isInternal(aRecord)) {
                findings.add(
                    Findings.high(
                        "PRIVATE_IPV6_LEAK", "A record resolves to internal IPv6 address: " + aRecord));
              }
            });

    return findings;
  }

  private boolean isInternal(String ip) {
    try {
      InetAddress address = InetAddress.getByName(ip);

      return address.isAnyLocalAddress()
          || address.isLinkLocalAddress()
          || address.isSiteLocalAddress()
          || isPrivateIPv6(address);
    } catch (UnknownHostException e) {
      throw new IllegalArgumentException("IP address is not valid: " + ip);
    }
  }

  private boolean isPrivateIPv6(InetAddress addr) {

    String ip = addr.getHostAddress().toLowerCase();

    return ip.equals("::1") || ip.startsWith("fe80:") || ip.startsWith("fc") || ip.startsWith("fd");
  }
}
