package com.vulnwatch.worker.engine.repository.trivy;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
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
public class TrivyEngine implements Scanner {
    private final CliExecutor cliExecutor;
    private final TrivyParser trivyParser;

    @Value("${tools.testssl.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.testssl.binary:trivy}")
    private String binary;

    @Value("${tools.temp:/Users/mitchelntuen/temp}")
    private String tempLocation;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.DEPENDENCY;
    }

    @Override
    public EngineResult scan(ScanJob job){
        String repoId = job.repoId();
        String outputFileName = "%s/%s-%s.jsonl".formatted(tempLocation,binary,job.scanId());
        Path outFile = Path.of(outputFileName);

        String repoUrl = repoId.startsWith("https")
                ? repoId
                : "https://github.com/" + repoId;

        List<String> command = new ArrayList<>(List.of(
                binary,
                "repo", repoUrl,
                "--format", "json",
                "--output", outFile.toString(),
                "--scanners", "vuln,secret",
                "--severity", "MEDIUM,HIGH,CRITICAL",
                "--quiet"
        ));

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            List<TrivyEngineResult> findings = trivyParser.parse(outFile.toFile());
            return EngineResult.success(SurfaceType.DEPENDENCY, Map.of("findings", findings));
        }catch (Exception e){
            log.error("Error performing %s scan for scan_id:%s".formatted(job.scanType(), job.scanId()));
            throw new RuntimeException(e);
        }
    }
}
