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
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.io.IOException;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;

@Component
@RequiredArgsConstructor
public class SubdomainEngine implements Scanner {

    private final CliExecutor cliExecutor;
    private final JsonlParser jsonlParser;
    private final SubdomainClassificationPipeline classificationPipeline;

    @Value("${tools.subfinder.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.subfinder.binary:subfinder}")
    private String binary;

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "/temp/subfinder-%s.json".formatted(job.scanId());

        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-d", domain,
                "-o", outputFileName
        );

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            String json = cliExecutor.readAndDelete(outFile);
            List<SubdomainRecord> records = jsonlParser.parse(outFile);
            List<SubdomainFindings> findings = classificationPipeline.process(records);

            return EngineResult.success(SurfaceType.SUBDOMAINS, Map.of("findings", findings));


        } catch (IOException e) {
            return EngineResult.failure(SurfaceType.SUBDOMAINS, e.getMessage());
        }

    }

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.SUBDOMAINS;
    }
}
