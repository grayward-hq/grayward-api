package com.vulnwatch.worker.engine.domain.testssl;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.dnsrecon.utility.RuleEngine;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.io.File;
import java.io.IOException;
import java.nio.file.Path;
import java.util.*;

@Slf4j
@Component
@RequiredArgsConstructor
public class SslEngine implements Scanner {

    private final CliExecutor cliExecutor;

    @Value("${tools.testssl.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.testssl.binary:./testssl}")
    private String binary;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.SSL;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "/temp/testssl-%s.json".formatted(job.scanId());

        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-U", "--jsonfile",
                outputFileName, domain
        );

        try{
            cliExecutor.run(command, timeoutSeconds, false);
            String json = cliExecutor.readAndDelete(outFile);
            List<SslFindings> findings = extractFindings(outFile.toFile());
            return EngineResult.success(SurfaceType.SSL, Map.of("findings", findings));
        }catch (Exception e){
            log.error("Error performing %s scan for scan_id:%s".formatted(job.scanType(), job.scanId()));
            return EngineResult.failure(SurfaceType.SSL, e.getMessage());
        }


    }

    private List<SslFindings> extractFindings(File file) {
        try {
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
        } catch (IOException e){
            throw new RuntimeException("Unable to read file:"+file.getName(), e);
        }
    }
}