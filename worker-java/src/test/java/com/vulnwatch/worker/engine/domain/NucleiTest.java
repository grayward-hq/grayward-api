package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.nuclei.NucleiEngine;
import com.vulnwatch.worker.engine.parsers.NucleiParser;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.Mockito;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.bean.override.mockito.MockitoSpyBean;

import static org.assertj.core.api.AssertionsForClassTypes.assertThat;
import static org.assertj.core.api.AssertionsForClassTypes.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.ArgumentMatchers.anyBoolean;
import static org.mockito.Mockito.doThrow;

@SpringBootTest
@ExtendWith(MockitoExtension.class)
public class NucleiTest {

    @Autowired
    private NucleiEngine nucleiEngine;

    @MockitoSpyBean
    private NucleiParser parser;

    @MockitoSpyBean
    private CliExecutor cliExecutor;

    @Mock(strictness = Mock.Strictness.LENIENT)
    private ScanJob scanJob;


    @BeforeEach
    void beforeEach(){
        String scanId = "scanId";
        String domainName = "hng.tech";
        Mockito.when(scanJob.scanId()).thenReturn(scanId);
        Mockito.when(scanJob.domainName()).thenReturn(domainName);
        Mockito.when(scanJob.scanType()).thenReturn("DEPENDENCY/SECRETS");
    }

    @Test
    void surfaceType_returnsDns(){
        assertThat(nucleiEngine.surfaceType()).isEqualTo(SurfaceType.HTTP_HEADERS);
    }

    @Test
    void scan_returnsEngineResult_success() throws Throwable{
        EngineResult result = nucleiEngine.scan(scanJob);

        assertThat(result).isNotNull();
        assertThat(result.success()).isEqualTo(true);
        assertThat(result.surfaceType()).isEqualTo(SurfaceType.HTTP_HEADERS);
        assertThat(result.errorMessage()).isEqualTo(null);
        assertThat(result.rawResult()).isNotNull();

        System.out.println(result.success());
        System.out.println(result.errorMessage());
    }


    @Test
    void scan_returnsEngineResult_failure_CliExecutionException() throws Throwable {
        doThrow(new CliExecutor.CliExecutionException("CLI execution error, type shift", null))
                .when(cliExecutor)
                .run(anyList(), anyInt(), anyBoolean());

        assertThatThrownBy(()->nucleiEngine.scan(scanJob))
                .isInstanceOf(Exception.class);
    }

}
