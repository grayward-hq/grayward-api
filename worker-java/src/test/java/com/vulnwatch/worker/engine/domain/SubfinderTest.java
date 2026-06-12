package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.subfinder.SubdomainEngine;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import com.vulnwatch.worker.engine.domain.subfinder.utility.JsonlParser;
import com.vulnwatch.worker.engine.domain.subfinder.utility.SubdomainClassificationPipeline;
import com.vulnwatch.worker.engine.domain.testssl.SslEngine;
import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.engine.parsers.TestsslParser;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.Mock;
import org.mockito.Mockito;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.bean.override.mockito.MockitoBean;

import java.io.File;
import java.io.IOException;
import java.nio.file.Path;
import java.util.List;

import static org.assertj.core.api.AssertionsForClassTypes.assertThat;
import static org.assertj.core.api.AssertionsForClassTypes.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.ArgumentMatchers.anyBoolean;
import static org.mockito.Mockito.*;

@SpringBootTest
public class SubfinderTest {
    @Autowired
    private SubdomainEngine subdomainEngine;

    @MockitoBean
    private SubdomainClassificationPipeline pipeline;

    @MockitoBean
    private JsonlParser parser;

    @MockitoBean
    private CliExecutor cliExecutor;

    @Mock(strictness = Mock.Strictness.LENIENT)
    private ScanJob scanJob;

    @BeforeEach
    void beforeEach() throws Exception {
        String scanId = "scanId";
        String domainName = "hng.tech";
        Mockito.when(scanJob.scanId()).thenReturn(scanId);
        Mockito.when(scanJob.domainName()).thenReturn(domainName);
    }

    @Test
    void surfaceType_returnsSsl(){
        assertThat(subdomainEngine.surfaceType()).isEqualTo(SurfaceType.SUBDOMAINS);
    }

    @Test
    void scan_returnsEngineResult_success() throws Throwable{
        List<SubdomainRecord> mockRecords = List.of(
                new SubdomainRecord(
                        "api.example.com",
                        "example.com",
                        "crtsh"
                )
        );

        when(parser.parse(any(Path.class)))
                .thenReturn(mockRecords);

        List<SubdomainFindings> mockFindings = List.of(
                mock(SubdomainFindings.class)
        );

        when(pipeline.process(mockRecords))
                .thenReturn(mockFindings);

        EngineResult result = subdomainEngine.scan(scanJob);

        assertThat(result).isNotNull();
        assertThat(result.success()).isEqualTo(true);
        assertThat(result.surfaceType()).isEqualTo(SurfaceType.SUBDOMAINS);
        assertThat(result.errorMessage()).isEqualTo(null);
        assertThat(result.rawResult()).isNotNull();

        System.out.println(result.success());
        System.out.println(result.errorMessage());
    }

    @Test
    void scan_returnsEngineResult_failure_parser_IOException() throws Throwable {
        doThrow(new IOException("forced failure"))
                .when(parser)
                .parse(any(Path.class));

        assertThatThrownBy(()->subdomainEngine.scan(scanJob))
                .isInstanceOf(Exception.class);

    }

    @Test
    void scan_returnsEngineResult_failure_CliExecutionException() throws Throwable {
        doThrow(new CliExecutor.CliExecutionException("CLI execution error, type shift", null))
                .when(cliExecutor)
                .run(anyList(), anyInt(), anyBoolean());

        assertThatThrownBy(()->subdomainEngine.scan(scanJob))
                .isInstanceOf(Exception.class);
    }



}
