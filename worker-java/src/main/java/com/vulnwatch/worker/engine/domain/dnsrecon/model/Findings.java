package com.vulnwatch.worker.engine.domain.dnsrecon.model;

/**
 * Data object that represents security insights from scan
 *
 * @param severity
 * @param title
 * @param description
 */
public record Findings(Severity severity, String title, String description) {
  public static Findings info(String title, String description) {
    return new Findings(Severity.INFO, title, description);
  }

  public static Findings low(String title, String description) {
    return new Findings(Severity.LOW, title, description);
  }

  public static Findings medium(String title, String description) {
    return new Findings(Severity.MEDIUM, title, description);
  }

  public static Findings high(String title, String description) {
    return new Findings(Severity.HIGH, title, description);
  }
}
