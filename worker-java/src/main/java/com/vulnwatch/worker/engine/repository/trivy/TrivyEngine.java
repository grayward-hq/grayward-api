package com.vulnwatch.worker.engine.repository.trivy;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.parsers.TrivyParser;
import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.model.RepositoryMetadata;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.nio.file.Path;
import java.util.List;

/**
 * Runs Trivy against a repository. No longer implements the domain
 * `Scanner` interface. Called directly by
 * RepositoryScanOrchestrator instead.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class TrivyEngine {

    private final CliExecutor cliExecutor;
    private final TrivyParser trivyParser;

    @Value("${tools.trivy.timeout-seconds:400}")
    private int timeoutSeconds;

    @Value("${tools.trivy.binary:trivy}")
    private String binary;

    @Value("${tools.temp:/tmp/vulnwatch}")
    private String tempLocation;

    /**
     * @param metadata resolved repository identity (fullName, branch)
     * @param installationToken GitHub App installation token, or null for public repos
     * @param scanId used for the output file name and logging
     */
    public List<TrivyEngineResult> scan(RepositoryMetadata metadata, String installationToken, String scanId) {
        String outputFileName = "%s/trivy-%s.json".formatted(tempLocation, scanId);
        Path outFile = Path.of(outputFileName);

        String repoUrl = "https://github.com/%s".formatted(metadata.fullName());

        TrivyCommandBuilder.TrivyCommandBuilderBuilder commandBuilder = TrivyCommandBuilder.create()
                .binary(binary)
                .repoUrl(repoUrl)
                .branch(metadata.defaultBranch())
                .outputFile(outputFileName);


        if (installationToken != null && !installationToken.isBlank()) {
            // GitHub accepts "x-access-token" as the basic auth username when using installation tokens
            commandBuilder.credentials("x-access-token", installationToken);
        }

        List<String> command = commandBuilder.build();

        log.info("Running Trivy [scanId={} command={}]", scanId, TrivyCommandBuilder.redactForLogging(command));

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            return trivyParser.parse(outFile.toFile());
        } catch (Exception e) {
            log.error("Trivy execution failed [scanId={} repo={}]: {}", scanId, metadata.fullName(), e.getMessage());
            throw new RuntimeException(e);
        } finally {
            cliExecutor.deleteSilently(outFile);
        }
    }
}