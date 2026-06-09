package com.vulnwatch.worker.engine.repository;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.nuclei.NucleiEngine;
import com.vulnwatch.worker.engine.parsers.NucleiParser;
import com.vulnwatch.worker.engine.parsers.TrivyParser;
import com.vulnwatch.worker.engine.repository.trivy.TrivyEngine;
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
import static org.mockito.Mockito.doThrow;

@SpringBootTest
@ExtendWith(MockitoExtension.class)
public class TrivyTest {
    @Autowired
    private TrivyEngine trivyEngine;

    @MockitoSpyBean
    private TrivyParser parser;

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
        Mockito.when(scanJob.repoId()).thenReturn("https://github.com/Mitchie2910/ProfileQuery.git");
    }

    @Test
    void surfaceType_returnsDns(){
        assertThat(trivyEngine.surfaceType()).isEqualTo(SurfaceType.DEPENDENCY);
    }

    @Test
    void scan_returnsEngineResult_success() throws Throwable{
        EngineResult result = trivyEngine.scan(scanJob);

        assertThat(result).isNotNull();
        assertThat(result.success()).isEqualTo(true);
        assertThat(result.surfaceType()).isEqualTo(SurfaceType.DEPENDENCY);
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

        assertThatThrownBy(()->trivyEngine.scan(scanJob))
                .isInstanceOf(Exception.class);
    }
}
