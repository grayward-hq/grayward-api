package com.vulnwatch.worker.engine.domain.dnsrecon;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import com.vulnwatch.worker.engine.domain.dnsrecon.utility.RuleEngine;
import com.vulnwatch.worker.engine.parsers.DnsParser;
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

@RequiredArgsConstructor
@Component
@Slf4j
public class DnsEngine implements Scanner {

    private final CliExecutor cliExecutor;
    private final RuleEngine ruleEngine;
    private final DnsParser dnsParser;

    @Value("${tools.dnsrecon.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.dnsrecon.binary:/Users/mitchelntuen/venv/bin/dnsrecon}")
    private String binary;

    @Value("${tools.temp:/Users/mitchelntuen/temp}")
    private String tempLocation;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.DNS;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "%s/dnsrecon-%s.json".formatted(tempLocation,job.scanId());

        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-d", domain,
                "-t", "std",
                "-j", outputFileName
        );

        try{
            cliExecutor.run(command, timeoutSeconds, false);
            ScanContext context = dnsParser.parse(outFile.toFile());

            Map<String, Object> findings = ruleEngine.scanJob(context);
            return EngineResult.success(SurfaceType.DNS, findings);


        }catch (IOException e){
            log.error("Unable to parse file:%s".formatted(outFile));
            return EngineResult.failure(SurfaceType.DNS, "Error processing: %s".formatted(outputFileName));
        } catch (Exception e){
            log.error(e.getMessage());
            throw new RuntimeException(e);
        }finally {
            cliExecutor.deleteSilently(outFile);
        }
    }
}