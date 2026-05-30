package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.nmap.NmapEngine;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.engine.parsers.NmapParser;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.Mockito;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.test.util.ReflectionTestUtils;

import java.io.File;
import java.nio.file.Path;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeoutException;

import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

@ExtendWith(MockitoExtension.class)
class NmapTest {

    @Mock
    private CliExecutor cliExecutor;

    @Mock
    private NmapParser nmapParser;

    @InjectMocks
    private NmapEngine nmapEngine;

    @Mock(strictness = Mock.Strictness.LENIENT)
    private ScanJob scanJob;

    private static final String TEMP_LOCATION = "/tmp/test";
    private static final String BINARY = "./nmap";
    private static final int TIMEOUT_SECONDS = 150;
    private static final String SCAN_ID = "scan-123";
    private static final String DOMAIN = "hng.tech";

    @BeforeEach
    void setUp() throws Exception {
        ReflectionTestUtils.setField(nmapEngine, "tempLocation", TEMP_LOCATION);
        ReflectionTestUtils.setField(nmapEngine, "binary", BINARY);
        ReflectionTestUtils.setField(nmapEngine, "timeoutSeconds", TIMEOUT_SECONDS);

        when(scanJob.scanId()).thenReturn(SCAN_ID);
        when(scanJob.domainName()).thenReturn(DOMAIN);

        ; // adjust constructor to match yours
    }

    // --- Happy path ---

    @Test
    void scan_success_returnsEngineResultWithFindings() throws Exception {
        String expectedOutputFile = "%s/nmap-%s.json".formatted(TEMP_LOCATION, SCAN_ID);
        Path expectedPath = Path.of(expectedOutputFile);

        List<NmapFindings> findings = List.of(mock(NmapFindings.class));

        when(nmapParser.parse(expectedPath.toFile())).thenReturn(findings);

        EngineResult result = nmapEngine.scan(scanJob);

        assertNotNull(result);
        assertEquals(SurfaceType.PORTS, result.surfaceType());
        assertTrue(result.success());

        @SuppressWarnings("unchecked")
        Map<String, Object> data = (Map<String, Object>) result.rawResult();
        assertEquals(findings, data.get("findings"));
    }

    @Test
    void scan_invokesCliExecutorWithCorrectCommand() throws Exception {
        String expectedOutputFile = "%s/nmap-%s.json".formatted(TEMP_LOCATION, SCAN_ID);
        List<String> expectedCommand = List.of(BINARY, "-oX", expectedOutputFile, DOMAIN);

        when(nmapParser.parse(any(File.class))).thenReturn(List.of());

        nmapEngine.scan(scanJob);

        verify(cliExecutor).run(expectedCommand, TIMEOUT_SECONDS, false);
    }

    @Test
    void scan_invokesReadAndDeleteWithCorrectPath() throws Exception {
        String expectedOutputFile = "%s/nmap-%s.json".formatted(TEMP_LOCATION, SCAN_ID);
        Path expectedPath = Path.of(expectedOutputFile);

        when(nmapParser.parse(any(File.class))).thenReturn(List.of());

        nmapEngine.scan(scanJob);

        verify(cliExecutor).readAndDelete(expectedPath);
    }

    @Test
    void scan_withEmptyFindings_returnsSuccessWithEmptyList() throws Exception {
        when(nmapParser.parse(any(File.class))).thenReturn(Collections.emptyList());

        EngineResult result = nmapEngine.scan(scanJob);

        assertTrue(result.success());
        @SuppressWarnings("unchecked")
        Map<String, Object> data = (Map<String, Object>) result.rawResult();
        assertTrue(((List<?>) data.get("findings")).isEmpty());
    }

    // --- Error / exception paths ---

    @Test
    void scan_whenCliExecutorThrows_propagatesException() throws Exception {
        doThrow(new RuntimeException("CLI failed"))
                .when(cliExecutor).run(anyList(), anyInt(), anyBoolean());

        assertThrows(RuntimeException.class, () -> nmapEngine.scan(scanJob));
    }

    @Test
    void scan_whenParserThrows_propagatesException() throws Exception {
        when(nmapParser.parse(any(File.class)))
                .thenThrow(new RuntimeException("Parse error"));

        assertThrows(RuntimeException.class, () -> nmapEngine.scan(scanJob));
    }

    @Test
    void scan_whenCliExecutorThrows_doesNotCallParser() throws Exception {
        doThrow(new RuntimeException("CLI failed"))
                .when(cliExecutor).run(anyList(), anyInt(), anyBoolean());

        assertThrows(RuntimeException.class, () -> nmapEngine.scan(scanJob));

        verifyNoInteractions(nmapParser);
    }

    @Test
    void scan_whenTimeoutOccurs_propagatesException() throws Exception {
        doThrow(new TimeoutException("Timed out"))
                .when(cliExecutor).run(anyList(), anyInt(), anyBoolean());

        assertThrows(TimeoutException.class, () -> nmapEngine.scan(scanJob));
    }

    // --- surfaceType ---

    @Test
    void surfaceType_returnsNull() {
        assertNull(nmapEngine.surfaceType());
    }

    // --- Output file naming ---

    @Test
    void scan_outputFileNameIncludesScanId() throws Exception {
        when(nmapParser.parse(any(File.class))).thenReturn(List.of());

        nmapEngine.scan(scanJob);

        ArgumentCaptor<List<String>> commandCaptor = ArgumentCaptor.forClass(List.class);
        verify(cliExecutor).run(commandCaptor.capture(), anyInt(), anyBoolean());

        String outputArg = commandCaptor.getValue().get(2); // index after "-oX"
        assertTrue(outputArg.contains(SCAN_ID));
    }

    @Test
    void scan_outputFileNameIncludesTempLocation() throws Exception {
        when(nmapParser.parse(any(File.class))).thenReturn(List.of());

        nmapEngine.scan(scanJob);

        ArgumentCaptor<List<String>> commandCaptor = ArgumentCaptor.forClass(List.class);
        verify(cliExecutor).run(commandCaptor.capture(), anyInt(), anyBoolean());

        String outputArg = commandCaptor.getValue().get(2);
        assertTrue(outputArg.startsWith(TEMP_LOCATION));
    }
}
