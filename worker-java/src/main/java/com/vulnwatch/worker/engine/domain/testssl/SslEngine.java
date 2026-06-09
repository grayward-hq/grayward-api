package com.vulnwatch.worker.engine.domain.testssl;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.dnsrecon.utility.RuleEngine;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.engine.parsers.TestsslParser;
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
    private final TestsslParser testsslParser;

    @Value("${tools.testssl.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.testssl.binary:testssl}")
    private String binary;

    @Value("${tools.temp:/Users/mitchelntuen/temp}")
    private String tempLocation;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.SSL;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "%s/testssl-%s.jsonl".formatted(tempLocation,job.scanId());

        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-U", "--jsonfile",
                outputFileName, domain
        );

        try{
            cliExecutor.run(command, timeoutSeconds, false);
            List<SslFindings> findings = testsslParser.parse(outFile.toFile());
            return EngineResult.success(SurfaceType.SSL, Map.of("findings", findings));
        }catch (Exception e){
            log.error("Error performing %s scan for scan_id:%s".formatted(job.scanType(), job.scanId()));
            throw new RuntimeException(e);
        }finally {
            cliExecutor.deleteSilently(outFile);
        }


    }


}