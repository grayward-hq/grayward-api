package com.vulnwatch.worker.engine.repository.trivy;

import lombok.Builder;
import lombok.Getter;
import org.jetbrains.annotations.NotNull;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

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
    private final String severity = "LOW,MEDIUM,HIGH,CRITICAL";

    @Builder.Default
    private final String scanners = "vuln,secret";

    public static class TrivyCommandBuilderBuilder {

        /**
         * Convenience method to supply both credentials at once.
         */
        public void credentials(String username, String password) {
            this.username = username;
            this.password = password;
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

            return getStrings(context);
        }

        /**
         * Environment variables required to authenticate a private-repository
         * scan. Trivy reads {@code GITHUB_TOKEN} for GitHub repo auth, so the
         * token is passed via the child process environment instead of a
         * plaintext CLI flag. Preserves the same nonblank credential checks
         * that previously guarded the --username/--password flags.
         */
        public Map<String, String> buildEnv() {
            if (this.username != null && !this.username.isBlank() &&
                    this.password != null && !this.password.isBlank()) {
                return Map.of("GITHUB_TOKEN", this.password);
            }
            return Map.of();
        }
    }

    @NotNull
    private static List<String> getStrings(TrivyCommandBuilder context) {
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
        return command;
    }

    /**
     * Command as a loggable string. Credentials are no longer passed as CLI
     * arguments (they go through {@link TrivyCommandBuilderBuilder#buildEnv()}
     * into the child process environment instead), so there is nothing left
     * in the arg list to redact.
     */
    public static String redactForLogging(List<String> command) {
        return String.join(" ", command);
    }
}