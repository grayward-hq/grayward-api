package com.vulnwatch.worker.engine.domain.nuclei;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.nuclei.models.NucleiEngineResult;
import com.vulnwatch.worker.engine.parsers.NucleiParser;
import com.vulnwatch.worker.engine.parsers.TrivyParser;
import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

@Slf4j
@Component
@RequiredArgsConstructor
public class NucleiEngine implements Scanner {

    private final CliExecutor cliExecutor;
    private final NucleiParser nucleiParser;

    @Value("${tools.nuclei.timeout-seconds:550}")
    private int timeoutSeconds;

    @Value("${tools.nuclei.binary:nuclei}")
    private String binary;

    @Value("${tools.temp:/Users/mitchelntuen/temp}")
    private String tempLocation;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.HTTP_HEADERS;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domainName = job.domainName();
        String outputFileName = "%s/%s-%s.jsonl".formatted(tempLocation,binary,job.scanId());
        Path outFile = Path.of(outputFileName);

        List<String> command = new ArrayList<>(List.of(
                binary,
                "-u", domainName,
                "-t", "http/misconfiguration/http-missing-security-headers.yaml",
                "-jsonl",
                "-silent",
                "-omit-raw",
                "-no-color",
                "-o", outputFileName
        ));

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            List<NucleiEngineResult> findings = nucleiParser.parse(outFile.toFile());
            return EngineResult.success(SurfaceType.HTTP_HEADERS, Map.of("findings", findings));
        }catch (Exception e){
            log.error("Error performing %s scan for scan_id:%s, %s".formatted(job.scanType(), job.scanId(), e.getMessage()));
            throw new RuntimeException(e);
        }finally {
            cliExecutor.deleteSilently(outFile);
        }
    }
}
