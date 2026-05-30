package com.vulnwatch.worker.engine.parsers;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.engine.domain.testssl.SslEngine;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import org.springframework.stereotype.Component;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

@Component
public class TestsslParser implements Parser<List<SslFindings>>{
    @Override
    public List<SslFindings> parse(File file) throws IOException {

            ObjectMapper mapper = new ObjectMapper();
            JsonNode root = mapper.readTree(file);
            List<SslFindings> findings = new ArrayList<>();

            for (JsonNode node : root) {
                String severity = node.path("severity").asText();
                String id = node.path("id").asText();
                String ip = node.path("ip").asText();
                String port = node.path("port").asText();
                String finding = node.path("finding").asText();

                if (!Objects.equals(severity, "INFO") && !Objects.equals(severity, "OK")) {
                    SslFindings sslFindings = SslFindings.builder()
                            .id(id)
                            .ip(ip)
                            .port(port)
                            .finding(finding)
                            .severity(severity)
                            .build();
                    findings.add(sslFindings);
                }
            }
            return findings;
    }
}
