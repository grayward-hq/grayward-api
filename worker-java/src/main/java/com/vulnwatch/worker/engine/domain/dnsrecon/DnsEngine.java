package com.vulnwatch.worker.engine.domain.dnsrecon;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import com.vulnwatch.worker.engine.domain.dnsrecon.utility.RuleEngine;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import org.springframework.beans.factory.annotation.Value;

import java.io.File;
import java.io.IOException;
import java.nio.file.Path;
import java.util.*;

@RequiredArgsConstructor
public class DnsEngine implements Scanner {

    private final CliExecutor cliExecutor;
    private final RuleEngine ruleEngine;

    @Value("${tools.testssl.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.testssl.binary:dnsrecon}")
    private String binary;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.DNS;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "/temp/dnsresolver-%s.json".formatted(job.scanId());

        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-d", domain,
                "-t", "std",
                "-a","-j",
                outputFileName
        );

        try{
            cliExecutor.run(command, 3, false);
            String json = cliExecutor.readAndDelete(outFile);
            ScanContext context = extractContext(outFile.toFile());
            Map<String, Object> findings = ruleEngine.scanJob(context);
            return EngineResult.success(SurfaceType.DNS, findings);


        } catch (IOException e) {
            return EngineResult.failure(SurfaceType.DNS, e.getMessage());
        }
    }

    private ScanContext extractContext(File file) throws IOException {
        ObjectMapper mapper = new ObjectMapper();
        JsonNode root = mapper.readTree(file);

        List<String> a = new ArrayList<>();
        List<String> aaaa = new ArrayList<>();
        List<String> ns = new ArrayList<>();
        List<String> mx = new ArrayList<>();
        List<String> soa = new ArrayList<>();
        List<String> txt = new ArrayList<>();
        List<String> dnskey = new ArrayList<>();
        List<String> rrsig = new ArrayList<>();


        for (JsonNode node : root) {
            String type = node.path("type").asText();

            switch (type) {

                case "A" -> {
                    String ip = node.path("address").asText(null);
                    if (ip != null) a.add(ip);
                }

                case "AAAA" -> {
                    String ip = node.path("address").asText(null);
                    if (ip != null) aaaa.add(ip);
                }

                case "NS" -> {
                    String target = node.path("target").asText(null);
                    if (target != null) ns.add(target);
                }

                case "MX" -> {
                    String exchange = node.path("exchange").asText(null);
                    if (exchange != null) mx.add(exchange);
                }

                case "SOA" -> {
                    String mname = node.path("mname").asText(null);
                    if (mname != null) soa.add(mname);
                }

                case "DNSKEY" -> {
                    String name = node.path("name").asText(null);
                    if (name != null) dnskey.add(name);
                }

                case "RRSIG" -> {
                    String name = node.path("name").asText(null);
                    if (name != null) rrsig.add(name);
                }

                case "TXT" -> {
                    JsonNode strings = node.path("strings");

                    if (strings.isArray()) {
                        for (JsonNode s : strings) {
                            txt.add(s.asText());
                        }
                    } else if (!strings.isMissingNode()) {
                        txt.add(strings.asText());
                    }
                }

                default -> {
                }
            }
        }
        return new ScanContext(
                a,
                aaaa,
                ns,
                mx,
                soa,
                txt,
                dnskey,
                rrsig
        );
    }
}