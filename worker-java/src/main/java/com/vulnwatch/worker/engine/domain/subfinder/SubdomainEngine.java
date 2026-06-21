package com.vulnwatch.worker.engine.domain.subfinder;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.engine.domain.subfinder.utility.JsonlParser;
import com.vulnwatch.worker.engine.domain.subfinder.utility.SubdomainClassificationPipeline;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.io.IOException;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;

@Component
@RequiredArgsConstructor
@Slf4j
public class SubdomainEngine implements Scanner {

    private final CliExecutor cliExecutor;
    private final JsonlParser jsonlParser;
    private final SubdomainClassificationPipeline classificationPipeline;

    @Value("${tools.subfinder.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.subfinder.binary:subfinder}")
    private String binary;

    @Value("${tools.temp:/Users/mitchelntuen/temp}")
    private String tempLocation;

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "%s/subfinder-%s.json".formatted(tempLocation,job.scanId());
        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-d", domain,
                "-json",
                "-silent",
                "-duc",
                "-max-time", "2",
                "-o", outputFileName
        );

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            List<SubdomainRecord> records = jsonlParser.parse(outFile);
            List<SubdomainFindings> findings = classificationPipeline.process(records);

            return EngineResult.success(SurfaceType.SUBDOMAINS, Map.of("findings", findings));


        } catch (Exception e) {
            log.error(e.getMessage());
            throw new RuntimeException(e);
        } finally {
            cliExecutor.deleteSilently(outFile);
        }

    }

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.SUBDOMAINS;
    }
}