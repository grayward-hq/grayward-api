package com.vulnwatch.worker.ai.model;

import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;

@Slf4j
@Component
public class PromptBuilder {


    public String domainSystemPrompt() {
        return """
                You are a cybersecurity analyst. Analyse raw scan data from DNS, SSL,
                or HTTP header probes and return a structured assessment.
                Be concise, technical, and actionable.
                """;
    }

    public String domainDescribePrompt(ScanJob job) {
        return """
                Generate a 2-3 sentence plain-English message telling a user that their
                security scan for domain "%s" (type: %s, scan ID: %s) has started.
                Mention what kinds of checks will run. Be professional and concise.
                Do not use bullet points.
                """.formatted(job.domainName(), job.scanType(), job.scanId());
    }

    public String domainEnrichPrompt(ScanJob job, EngineResult result) {
        return """
                Domain: %s
                Scan ID: %s
                Surface: %s
                Engine success: %s
                Technical findings:
                %s

                Analyse these findings and return your assessment.
                """.formatted(
                job.domainName(),
                job.scanId(),
                result.surfaceType().getLabel(),
                result.success(),
                result.success() ? result.rawResult() : "Engine failed: %s".formatted(result.errorMessage()));
    }



    public String repositorySystemPrompt() {
        return """
                You are a security analyst specialising in dependency vulnerability analysis.
                Analyse each dependency and return accurate, factual vulnerability data.
                Return one entry per dependency in the same order as the input.
                """;
    }

    public String repositoryEnrichPrompt(List<String> dependencies) {
        return """
                Analyse the following dependencies for known vulnerabilities.

                Dependencies:
                %s
                """.formatted(String.join("\n", dependencies));
    }
}