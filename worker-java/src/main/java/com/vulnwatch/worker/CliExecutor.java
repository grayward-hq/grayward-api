package com.vulnwatch.worker;


import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

import java.io.IOException;
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
                           boolean allowNonZeroExit) {
        log.debug("Executing: {}", String.join(" ", command));

        ProcessBuilder pb = new ProcessBuilder(command);
        pb.environment().put("PATH", "/usr/local/bin:/usr/bin:/bin");
        pb.redirectErrorStream(true);       // merge stderr into stdout

        Process process;
        try {
            process = pb.start();
        } catch (IOException e) {
            throw new CliExecutionException(
                    "Failed to start process: " + command.get(0), e);
        }

        Future<String> stdOutStream = executor.submit(()-> new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8));

        try{
            boolean finished = process.waitFor(timeoutSeconds, TimeUnit.SECONDS);

            if(!finished) {
                process.destroyForcibly();
                stdOutStream.cancel(true);
                throw new CliTimeoutException(
                        "Tool timed out after %ds: %s".formatted(timeoutSeconds, command.getFirst())
                );
            }

            String std = stdOutStream.get();

            int exit = process.exitValue();
            if(exit != 0){
                String msg = "Tool exited with code %d: %s".formatted(exit, command.getFirst());
                if(allowNonZeroExit){
                    log.warn("{} (non-zero exit tolorated - checking output file)", msg);
                } else {
                    throw new CliExecutionException(msg, null);
                }
            }
        } catch (InterruptedException |ExecutionException e) {
            Thread.currentThread().interrupt();
            process.destroyForcibly();
            throw new CliExecutionException("Interrupted while waiting for: " + command.getFirst(), e);
        }

    }

    /**
     * Reads the contents of {@code path} as a UTF-8 string, then deletes the
     * file unconditionally (even if parsing later fails).
     *
     * @throws CliExecutionException if the file does not exist or cannot be read
     */
    public String readAndDelete(Path path) {
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

    public static class CliTimeoutException extends RuntimeException {
        public CliTimeoutException(String message) { super(message); }
    }

    public static class CliExecutionException extends RuntimeException {
        public CliExecutionException(String message, Throwable cause) { super(message, cause); }
    }
}

