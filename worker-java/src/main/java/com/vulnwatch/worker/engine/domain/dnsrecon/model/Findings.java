package com.vulnwatch.worker.engine.domain.dnsrecon.model;

import com.vulnwatch.worker.enums.FindingSeverity;

/**
 * Data object that represents security insights from scan
 *
 * @param severity
 * @param title
 * @param description
 */
public record Findings(FindingSeverity severity, String title, String description) {
  public static Findings info(String title, String description) {
    return new Findings(FindingSeverity.NONE, title, description);
  }

  public static Findings low(String title, String description) {
    return new Findings(FindingSeverity.LOW, title, description);
  }

  public static Findings medium(String title, String description) {
    return new Findings(FindingSeverity.MEDIUM, title, description);
  }

  public static Findings high(String title, String description) {
    return new Findings(FindingSeverity.HIGH, title, description);
  }
}
