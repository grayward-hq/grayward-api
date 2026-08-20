//package com.vulnwatch.worker.engine.domain;
//
//import com.vulnwatch.worker.CliExecutor;
//import com.vulnwatch.worker.engine.domain.testssl.SslEngine;
//import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
//import com.vulnwatch.worker.engine.parsers.TestsslParser;
//import com.vulnwatch.worker.enums.FindingSeverity;
//import com.vulnwatch.worker.enums.SurfaceType;
//import com.vulnwatch.worker.model.EngineResult;
//import com.vulnwatch.worker.model.ScanJob;
//import org.junit.jupiter.api.BeforeEach;
//import org.junit.jupiter.api.Test;
//import org.mockito.Mock;
//import org.mockito.Mockito;
//import org.springframework.beans.factory.annotation.Autowired;
//import org.springframework.boot.test.context.SpringBootTest;
//import org.springframework.test.context.bean.override.mockito.MockitoBean;
//
//import java.io.File;
//import java.io.IOException;
//import java.util.List;
//
//import static org.assertj.core.api.AssertionsForClassTypes.assertThat;
//import static org.assertj.core.api.AssertionsForClassTypes.assertThatThrownBy;
//import static org.mockito.ArgumentMatchers.*;
//import static org.mockito.ArgumentMatchers.anyBoolean;
//import static org.mockito.Mockito.*;
//
//@SpringBootTest
//public class TestsslTest {
//    @Autowired
//    private SslEngine sslEngine;
//
//    @MockitoBean
//    private TestsslParser parser;
//
//    @MockitoBean
//    private CliExecutor cliExecutor;
//
//    @Mock(strictness = Mock.Strictness.LENIENT)
//    private ScanJob scanJob;
//
//
//    @BeforeEach
//    void beforeEach() throws Exception {
//        String scanId = "scanId";
//        String domainName = "hng.tech";
//        Mockito.when(scanJob.scanId()).thenReturn(scanId);
//        Mockito.when(scanJob.domainName()).thenReturn(domainName);
//
//
//    }
//
//    @Test
//    void surfaceType_returnsSsl(){
//        assertThat(sslEngine.surfaceType()).isEqualTo(SurfaceType.SSL);
//
//
//    }
//
//    @Test
//    void scan_returnsEngineResult_success() throws Throwable{
//        List<SslFindings> mockedFindings = List.of(
//                new SslFindings("id", "ip", "port", "finding", FindingSeverity.NONE )
//        );
//        when(parser.parse(any(File.class)))
//                .thenReturn(mockedFindings);
//
//        EngineResult result = sslEngine.scan(scanJob);
//
//        assertThat(result).isNotNull();
//        assertThat(result.success()).isEqualTo(true);
//        assertThat(result.surfaceType()).isEqualTo(SurfaceType.SSL);
//        assertThat(result.errorMessage()).isEqualTo(null);
//        assertThat(result.rawResult()).isNotNull();
//
//        System.out.println(result.success());
//        System.out.println(result.errorMessage());
//    }
//
//    @Test
//    void scan_returnsEngineResult_failure_parser_IOException() throws Throwable {
//        doThrow(new IOException("forced failure"))
//                .when(parser)
//                .parse(any(File.class));
//
//        assertThatThrownBy(()->sslEngine.scan(scanJob))
//                .isInstanceOf(Exception.class);
//
//    }
//
//    @Test
//    void scan_returnsEngineResult_failure_CliExecutionException() throws Throwable {
//        doThrow(new CliExecutor.CliExecutionException("CLI execution error, type shift", null))
//                .when(cliExecutor)
//                .run(anyList(), anyInt(), anyBoolean());
//
//        assertThatThrownBy(()->sslEngine.scan(scanJob))
//                .isInstanceOf(Exception.class);
//    }
//
//
//}
