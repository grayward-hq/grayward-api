package com.vulnwatch.worker.engine.repository.trivy;

import lombok.Builder;
import lombok.Getter;
import java.util.ArrayList;
import java.util.List;

/**
 * Builder pattern for constructing the Trivy CLI invocation.
 *
 * Keeping command construction separate from execution makes the auth
 * handling (credentials never logged, only ever embedded in the exact
 * arg list passed to the process) and the branch/output plumbing easy
 * to unit test independently of CliExecutor.
 */
@Getter
@Builder(builderMethodName = "create", buildMethodName = "buildArgs")
public final class TrivyCommandBuilder {

    @Builder.Default
    private final String binary = "trivy";

    private final String repoUrl;
    private final String branch;
    private final String username;
    private final String password;
    private final String outputFile;

    @Builder.Default
    private final String severity = "MEDIUM,HIGH,CRITICAL";

    @Builder.Default
    private final String scanners = "vuln,secret";

    public static class TrivyCommandBuilderBuilder {

        /** Convenience method to supply both credentials at once. */
        public TrivyCommandBuilderBuilder credentials(String username, String password) {
            this.username = username;
            this.password = password;
            return this;
        }

        /** Overriding the final build execution to validate inputs and output the raw arg List. */
        public List<String> build() {
            // Validate required inputs before constructing the target instance
            if (this.repoUrl == null || this.repoUrl.isBlank()) {
                throw new IllegalStateException("repoUrl is required");
            }
            if (this.outputFile == null || this.outputFile.isBlank()) {
                throw new IllegalStateException("outputFile is required");
            }

            // Call Lombok's generated instance builder safely
            TrivyCommandBuilder context = this.buildArgs();

            List<String> command = new ArrayList<>(List.of(
                    context.binary,
                    "repo", context.repoUrl,
                    "--format", "json",
                    "--output", context.outputFile,
                    "--scanners", context.scanners,
                    "--severity", context.severity,
                    "--quiet"
            ));

            if (context.branch != null && !context.branch.isBlank()) {
                command.add("--branch");
                command.add(context.branch);
            }

            if (context.username != null && !context.username.isBlank() &&
                    context.password != null && !context.password.isBlank()) {
                command.add("--username");
                command.add(context.username);
                command.add("--password");
                command.add(context.password);
            }

            return command;
        }
    }

    /** Same command with the credential values masked, safe to pass to a logger. */
    public static String redactForLogging(List<String> command) {
        List<String> copy = new ArrayList<>(command);
        int pwIndex = copy.indexOf("--password");
        if (pwIndex >= 0 && pwIndex + 1 < copy.size()) {
            copy.set(pwIndex + 1, "***REDACTED***");
        }
        return String.join(" ", copy);
    }
}