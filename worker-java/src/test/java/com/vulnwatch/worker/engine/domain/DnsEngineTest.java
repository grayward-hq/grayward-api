package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.dnsrecon.DnsEngine;
import com.vulnwatch.worker.engine.domain.dnsrecon.utility.RuleEngine;
import com.vulnwatch.worker.engine.parsers.DnsParser;
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
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.context.bean.override.mockito.MockitoSpyBean;

import java.io.File;
import java.io.IOException;

import static org.assertj.core.api.AssertionsForClassTypes.assertThat;
import static org.assertj.core.api.AssertionsForClassTypes.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.doThrow;
import static reactor.core.publisher.Mono.when;

@SpringBootTest
@ExtendWith(MockitoExtension.class)
public class DnsEngineTest {

    @Autowired
    private DnsEngine dnsEngine;

    @MockitoSpyBean
    private DnsParser parser;

    @MockitoSpyBean
    private RuleEngine ruleEngine;

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
    }

    @Test
    void surfaceType_returnsDns(){
        assertThat(dnsEngine.surfaceType()).isEqualTo(SurfaceType.DNS);
    }

    @Test
    void scan_returnsEngineResult_success() throws Throwable{
        EngineResult result = dnsEngine.scan(scanJob);

        assertThat(result).isNotNull();
        assertThat(result.success()).isEqualTo(true);
        assertThat(result.surfaceType()).isEqualTo(SurfaceType.DNS);
        assertThat(result.errorMessage()).isEqualTo(null);
        assertThat(result.rawResult()).isNotNull();

        System.out.println(result.success());
        System.out.println(result.errorMessage());
    }

    @Test
    void scan_returnsEngineResult_failure() throws Throwable {
        doThrow(new IOException("forced failure"))
                .when(parser)
                .parse(any(File.class));

        EngineResult result = dnsEngine.scan(scanJob);

        assertThat(result).isNotNull();
        assertThat(result.success()).isEqualTo(false);
        assertThat(result.surfaceType()).isEqualTo(SurfaceType.DNS);
        assertThat(result.errorMessage()).isNotNull();
        assertThat(result.rawResult()).isNull();

    }

    @Test
    void scan_returnsEngineResult_failure_CliExecutionException() throws Throwable {
        doThrow(new CliExecutor.CliExecutionException("CLI execution error, type shift", null))
                .when(cliExecutor)
                .run(anyList(), anyInt(), anyBoolean());

        assertThatThrownBy(()->dnsEngine.scan(scanJob))
                .isInstanceOf(Exception.class);
    }

}
