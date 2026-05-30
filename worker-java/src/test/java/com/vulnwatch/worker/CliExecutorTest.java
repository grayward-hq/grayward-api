package com.vulnwatch.worker;

import org.junit.jupiter.api.*;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import static org.assertj.core.api.Assertions.*;

/**
 * Unit tests for {@link CliExecutor}.
 *
 * Requirements: JUnit 5, AssertJ, Java 17+.
 * These tests use real OS processes (echo, sleep, cat) that are available on
 * Linux/macOS. On Windows you would swap them for cmd /c echo etc.
 */
class CliExecutorTest {

    private ExecutorService executorService;
    private CliExecutor cliExecutor;

    @TempDir
    Path tempDir;

    @BeforeEach
    void setUp() {
        executorService = Executors.newCachedThreadPool();
        cliExecutor = new CliExecutor(executorService);
    }

    @AfterEach
    void tearDown() {
        executorService.shutdownNow();
    }

    // ── run() – happy path ────────────────────────────────────────────────────

    @Test
    @DisplayName("run() succeeds when command exits zero")
    void run_successfulCommand_doesNotThrow() {
        assertThatNoException().isThrownBy(() ->
                cliExecutor.run(List.of("echo", "hello"), 5, false));
    }

    @Test
    @DisplayName("run() accepts multiple arguments")
    void run_commandWithMultipleArgs_doesNotThrow() {
        assertThatNoException().isThrownBy(() ->
                cliExecutor.run(List.of("echo", "arg1", "arg2", "arg3"), 5, false));
    }

    // ── run() – non-zero exit ─────────────────────────────────────────────────

    @Test
    @DisplayName("run() throws CliExecutionException on non-zero exit when allowNonZeroExit=false")
    void run_nonZeroExit_strictMode_throwsCliExecutionException() {
        // 'false' as a shell command exits with code 1 on Unix
        assertThatThrownBy(() ->
                cliExecutor.run(List.of("bash", "-c", "exit 1"), 5, false))
                .isInstanceOf(CliExecutor.CliExecutionException.class)
                .hasMessageContaining("Tool exited with code 1");
    }

    @Test
    @DisplayName("run() does NOT throw on non-zero exit when allowNonZeroExit=true")
    void run_nonZeroExit_toleratedMode_doesNotThrow() {
        assertThatNoException().isThrownBy(() ->
                cliExecutor.run(List.of("bash", "-c", "exit 1"), 5, true));
    }

    @Test
    @DisplayName("run() CliExecutionException message includes command name")
    void run_nonZeroExit_exceptionMessageContainsCommandName() {
        assertThatThrownBy(() ->
                cliExecutor.run(List.of("bash", "-c", "exit 42"), 5, false))
                .isInstanceOf(CliExecutor.CliExecutionException.class)
                .hasMessageContaining("bash");
    }

    // ── run() – timeout ───────────────────────────────────────────────────────

    @Test
    @DisplayName("run() throws CliTimeoutException when process exceeds timeout")
    void run_processExceedsTimeout_throwsCliTimeoutException() {
        assertThatThrownBy(() ->
                cliExecutor.run(List.of("sleep", "60"), 1, false))
                .isInstanceOf(CliExecutor.CliTimeoutException.class)
                .hasMessageContaining("1")          // timeout value in message
                .hasMessageContaining("sleep");     // command name in message
    }

    @Test
    @DisplayName("run() completes normally when process finishes before timeout")
    void run_processFinishesBeforeTimeout_doesNotThrow() {
        // sleep 0 finishes instantly; generous 5 s timeout
        assertThatNoException().isThrownBy(() ->
                cliExecutor.run(List.of("sleep", "0"), 5, false));
    }

    // ── run() – bad executable ────────────────────────────────────────────────

    @Test
    @DisplayName("run() throws CliExecutionException when executable does not exist")
    void run_nonExistentExecutable_throwsCliExecutionException() {
        assertThatThrownBy(() ->
                cliExecutor.run(List.of("/nonexistent/binary"), 5, false))
                .isInstanceOf(CliExecutor.CliExecutionException.class)
                .hasMessageContaining("Failed to start process");
    }

    @Test
    @DisplayName("run() CliExecutionException wraps underlying IOException for bad executable")
    void run_nonExistentExecutable_causeIsIOException() {
        assertThatThrownBy(() ->
                cliExecutor.run(List.of("/nonexistent/binary"), 5, false))
                .isInstanceOf(CliExecutor.CliExecutionException.class)
                .cause()
                .isInstanceOf(IOException.class);
    }

    // ── readAndDelete() – happy path ──────────────────────────────────────────

    @Test
    @DisplayName("readAndDelete() returns file contents as a string")
    void readAndDelete_existingFile_returnsContent() throws Exception {
        Path file = tempDir.resolve("output.json");
        Files.writeString(file, "{\"key\":\"value\"}");

        String result = cliExecutor.readAndDelete(file);

        assertThat(result).isEqualTo("{\"key\":\"value\"}");
    }

    @Test
    @DisplayName("readAndDelete() deletes the file after reading")
    void readAndDelete_existingFile_fileIsDeletedAfterRead() throws Exception {
        Path file = tempDir.resolve("output.txt");
        Files.writeString(file, "some content");

        cliExecutor.readAndDelete(file);

        assertThat(file).doesNotExist();
    }

    @Test
    @DisplayName("readAndDelete() handles empty file without throwing")
    void readAndDelete_emptyFile_returnsEmptyString() throws Exception {
        Path file = tempDir.resolve("empty.txt");
        Files.createFile(file);

        String result = cliExecutor.readAndDelete(file);

        assertThat(result).isEmpty();
        assertThat(file).doesNotExist();
    }

    @Test
    @DisplayName("readAndDelete() handles file with multi-line UTF-8 content")
    void readAndDelete_multiLineUtf8Content_returnsFullContent() throws Exception {
        String content = "line1\nline2\nüñíçödé\n";
        Path file = tempDir.resolve("unicode.txt");
        Files.writeString(file, content);

        assertThat(cliExecutor.readAndDelete(file)).isEqualTo(content);
    }

    // ── readAndDelete() – missing file ────────────────────────────────────────

    @Test
    @DisplayName("readAndDelete() throws CliExecutionException when file does not exist")
    void readAndDelete_missingFile_throwsCliExecutionException() {
        Path missing = tempDir.resolve("does-not-exist.txt");

        assertThatThrownBy(() -> cliExecutor.readAndDelete(missing))
                .isInstanceOf(CliExecutor.CliExecutionException.class)
                .hasMessageContaining("Expected output file not found");
    }

    @Test
    @DisplayName("readAndDelete() exception message includes the missing path")
    void readAndDelete_missingFile_exceptionMessageContainsPath() {
        Path missing = tempDir.resolve("missing-output.json");

        assertThatThrownBy(() -> cliExecutor.readAndDelete(missing))
                .isInstanceOf(CliExecutor.CliExecutionException.class)
                .hasMessageContaining(missing.toString());
    }

    // ── deleteSilently() ─────────────────────────────────────────────────────

    @Test
    @DisplayName("deleteSilently() deletes an existing file without throwing")
    void deleteSilently_existingFile_fileIsDeleted() throws Exception {
        Path file = tempDir.resolve("temp.txt");
        Files.createFile(file);

        cliExecutor.deleteSilently(file);

        assertThat(file).doesNotExist();
    }

    @Test
    @DisplayName("deleteSilently() does not throw when file does not exist")
    void deleteSilently_nonExistentFile_doesNotThrow() {
        Path missing = tempDir.resolve("ghost.txt");

        assertThatNoException().isThrownBy(() -> cliExecutor.deleteSilently(missing));
    }

    @Test
    @DisplayName("deleteSilently() called twice on same path does not throw")
    void deleteSilently_calledTwiceOnSamePath_doesNotThrow() throws Exception {
        Path file = tempDir.resolve("double-delete.txt");
        Files.createFile(file);

        cliExecutor.deleteSilently(file);

        assertThatNoException().isThrownBy(() -> cliExecutor.deleteSilently(file));
    }

    // ── Exception type hierarchy ──────────────────────────────────────────────

    @Test
    @DisplayName("CliTimeoutException is a RuntimeException")
    void cliTimeoutException_isRuntimeException() {
        assertThat(new CliExecutor.CliTimeoutException("msg"))
                .isInstanceOf(RuntimeException.class);
    }

    @Test
    @DisplayName("CliExecutionException is a RuntimeException")
    void cliExecutionException_isRuntimeException() {
        assertThat(new CliExecutor.CliExecutionException("msg", null))
                .isInstanceOf(RuntimeException.class);
    }

    @Test
    @DisplayName("CliExecutionException preserves cause")
    void cliExecutionException_preservesCause() {
        Throwable cause = new IOException("disk full");
        CliExecutor.CliExecutionException ex =
                new CliExecutor.CliExecutionException("error", cause);

        assertThat(ex.getCause()).isSameAs(cause);
    }

    @Test
    @DisplayName("CliExecutionException accepts null cause")
    void cliExecutionException_acceptsNullCause() {
        assertThatNoException().isThrownBy(() ->
                new CliExecutor.CliExecutionException("no cause", null));
    }
}