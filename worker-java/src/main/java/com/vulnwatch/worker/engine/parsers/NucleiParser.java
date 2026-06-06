package com.vulnwatch.worker.engine.parsers;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.engine.domain.nuclei.models.NucleiEngineResult;
import org.springframework.stereotype.Component;

import java.io.BufferedReader;
import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

@Component
public class NucleiParser implements Parser<List<NucleiEngineResult>> {
    @Override
    public List<NucleiEngineResult> parse(File file) throws IOException {
        ObjectMapper mapper = new ObjectMapper();
        List<NucleiEngineResult> findings = new ArrayList<>();

        try (BufferedReader br = Files.newBufferedReader(file.toPath())) {
            String line;

            while ((line = br.readLine()) != null) {
                if (line.isBlank()) continue;

                JsonNode node = mapper.readTree(line);

                JsonNode info = node.path("info");

                String templateId = node.path("template-id").asText(null);
                String url = node.path("matched-at").asText(null);
                String host = node.path("host").asText(null);
                String ip = node.path("ip").asText(null);

                String severity = info.path("severity").asText(null);
                String issueName = info.path("name").asText(null);

                // THIS is the key field for header scans
                String headerType = node.path("matcher-name").asText(null);

                findings.add(new NucleiEngineResult(
                        templateId,
                        url,
                        host,
                        ip,
                        issueName,
                        severity,
                        headerType
                ));
            }
        }

        return findings;
    }
}
