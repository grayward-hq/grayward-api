package com.vulnwatch.worker.engine.parsers;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import org.springframework.stereotype.Component;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

@Component
public class TrivyParser implements Parser<List<TrivyEngineResult>> {

    private final ObjectMapper objectMapper = new ObjectMapper();

    @Override
    public List<TrivyEngineResult> parse(File file) throws IOException {
        JsonNode root = objectMapper.readTree(file);

        List<TrivyEngineResult> findings = new ArrayList<>();

        for (JsonNode resultNode : root.path("Results")) {

            // Dependency vulnerabilities
            JsonNode vulnerabilities = resultNode.path("Vulnerabilities");
            if (vulnerabilities.isArray()) {
                for (JsonNode vuln : vulnerabilities) {
                    findings.add(
                            TrivyEngineResult.dependencyVulnerability(
                                    vuln.path("PkgName").asText(null),
                                    vuln.path("InstalledVersion").asText(null),
                                    vuln.path("FixedVersion").asText(null),
                                    vuln.path("Title").asText(null),
                                    vuln.path("Severity").asText(null)
                            )
                    );
                }
            }

            // Secrets
            JsonNode secrets = resultNode.path("Secrets");
            if (secrets.isArray()) {
                String target = resultNode.path("Target").asText(null);

                for (JsonNode secret : secrets) {
                    findings.add(
                            TrivyEngineResult.secretFinding(
                                    secret.path("Title").asText(null),
                                    secret.path("Severity").asText(null),
                                    target,
                                    secret.path("Category").asText(null),
                                    secret.path("StartLine").asInt(),
                                    secret.path("EndLine").asInt()
                            )
                    );
                }
            }
        }

        return findings;
    }
}