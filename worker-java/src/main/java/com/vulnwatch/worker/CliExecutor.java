package com.vulnwatch.worker;


import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.concurrent.*;

/**
 *
 */
@Slf4j
@RequiredArgsConstructor
@Component
public final class CliExecutor {

    private final ExecutorService executor;

    /**
     * Runs a command and waits up to {@code timeoutSeconds} for it to finish.
     *
     * @param command         the command + arguments
     * @param timeoutSeconds  hard wall-clock limit
     * @param allowNonZeroExit if true, a non-zero exit code is logged as a
     *                         warning but does not throw; callers should still
     *                         check whether the output file was written
     * @throws CliTimeoutException  if the process does not finish in time
     * @throws CliExecutionException if the process exits non-zero and
     *                               {@code allowNonZeroExit} is false, or if
     *                               the OS fails to start the process
     */
    public void run(List<String> command,
                    int timeoutSeconds,
                    boolean allowNonZeroExit) throws Exception {

        log.debug("Executing: {}", String.join(" ", command));

        ProcessBuilder pb = new ProcessBuilder(command);

        // IMPORTANT: don't override PATH unless you must
        pb.environment().put("PATH", System.getenv("PATH"));

        pb.redirectErrorStream(true);

        Process process;
        try {
            process = pb.start();

        } catch (IOException e) {
            throw new CliExecutionException(
                    "Failed to start process: " + command.get(0), e);
        }

        StringBuilder output = new StringBuilder();

        Future<?> readerFuture = executor.submit(() -> {
            try (BufferedReader br = new BufferedReader(
                    new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8))) {

                String line;
                while ((line = br.readLine()) != null) {
                    output.append(line).append("\n");
                }

            } catch (IOException ignored) {}
        });

        boolean finished;
        try {
            finished = process.waitFor(timeoutSeconds, TimeUnit.SECONDS);

            if (!finished) {
                process.destroyForcibly();
                throw new CliTimeoutException(
                        "Tool timed out after %ds: %s"
                                .formatted(timeoutSeconds, command.getFirst())
                );
            }

            // ensure reader finishes too
            readerFuture.get();

        } catch (InterruptedException | ExecutionException e) {
            process.destroyForcibly();
            Thread.currentThread().interrupt();
            throw new CliExecutionException(
                    "Interrupted while waiting for: " + command.getFirst(), e);
        }

        String std = output.toString();

        int exit = process.exitValue();

        if (exit != 0) {
            String msg = "Tool exited with code %d: %s: %s"
                    .formatted(exit, command.getFirst(), std);

            if (allowNonZeroExit) {
                log.warn("{} (non-zero exit tolerated)", msg);
            } else {
                throw new CliExecutionException(msg, null);
            }
        }
    }

    /**
     * Reads the contents of {@code path} as a UTF-8 string, then deletes the
     * file unconditionally (even if parsing later fails).
     *
     * @throws CliExecutionException if the file does not exist or cannot be read
     */
    public String readAndDelete(Path path) throws Exception {
        try {
            if (!Files.exists(path)) {
                throw new CliExecutionException(
                        "Expected output file not found: " + path, null);
            }
            String content = Files.readString(path);
            Files.deleteIfExists(path);
            return content;
        } catch (IOException e) {
            try { Files.deleteIfExists(path); } catch (IOException ignored) {}
            throw new CliExecutionException("Failed to read tool output: " + path, e);
        }
    }

    /**
     * Deletes a temp file silently — intended for finally-blocks where the
     * file may or may not exist.
     */
    public void deleteSilently(Path path) {
        try {
            Files.deleteIfExists(path);
        } catch (IOException e) {
            log.warn("Could not delete temp file {}: {}", path, e.getMessage());
        }
    }

    // ── Exception types ───────────────────────────────────────────────────────

    public static class CliTimeoutException extends Exception {
        public CliTimeoutException(String message) { super(message); }
    }

    public static class CliExecutionException extends Exception {
        public CliExecutionException(String message, Throwable cause) { super(message, cause); }
    }
}

