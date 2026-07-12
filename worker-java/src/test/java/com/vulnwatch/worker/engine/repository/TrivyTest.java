//package com.vulnwatch.worker.engine.repository;
//
//import com.vulnwatch.worker.CliExecutor;
//import com.vulnwatch.worker.engine.parsers.TrivyParser;
//import com.vulnwatch.worker.engine.repository.trivy.TrivyEngine;
//import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
//import com.vulnwatch.worker.model.RepositoryMetadata;
//import org.junit.jupiter.api.BeforeEach;
//import org.junit.jupiter.api.Test;
//import org.junit.jupiter.api.extension.ExtendWith;
//import org.mockito.Mockito;
//import org.mockito.junit.jupiter.MockitoExtension;
//import org.springframework.beans.factory.annotation.Autowired;
//import org.springframework.boot.test.context.SpringBootTest;
//import org.springframework.test.context.bean.override.mockito.MockitoSpyBean;
//
//import java.util.List;
//
//import static org.assertj.core.api.AssertionsForClassTypes.assertThat;
//import static org.assertj.core.api.AssertionsForClassTypes.assertThatThrownBy;
//import static org.mockito.ArgumentMatchers.*;
//import static org.mockito.Mockito.doThrow;
//
//@SpringBootTest
//@ExtendWith(MockitoExtension.class)
//public class TrivyTest {
//
//    @Autowired
//    private TrivyEngine trivyEngine;
//
//    @MockitoSpyBean
//    private TrivyParser parser;
//
//    @MockitoSpyBean
//    private CliExecutor cliExecutor;
//
//    private RepositoryMetadata publicRepoMetadata;
//    private String scanId;
//
//    @BeforeEach
//    void beforeEach() {
//        scanId = "scanId";
//        publicRepoMetadata = new RepositoryMetadata(
//                "11111111-1111-1111-1111-111111111111",
//                "Mitchie2910/ProfileQuery",
//                "main",
//                "",
//                false,
//                "22222222-2222-2222-2222-222222222222"
//        );
//    }
//
//    @Test
//    void scan_returnsFindings_success() throws Throwable {
//        List<TrivyEngineResult> results = trivyEngine.scan(publicRepoMetadata, null, scanId);
//
//        assertThat(results).isNotNull();
//    }
//
//    @Test
//    void scan_throws_whenCliExecutionFails() throws Throwable {
//        doThrow(new CliExecutor.CliExecutionException("CLI execution error, type shift", null))
//                .when(cliExecutor)
//                .run(anyList(), anyInt(), anyBoolean());
//
//        assertThatThrownBy(() -> trivyEngine.scan(publicRepoMetadata, null, scanId))
//                .isInstanceOf(Exception.class);
//    }
//
//    @Test
//    void scan_usesCredentials_whenRepoIsPrivate() throws Throwable {
//        RepositoryMetadata privateRepo = new RepositoryMetadata(
//                "11111111-1111-1111-1111-111111111111",
//                "someorg/private-repo",
//                "main",
//                "78912345",
//                true,
//                "22222222-2222-2222-2222-222222222222"
//        );
//
//        assertThat(privateRepo.requiresAuth()).isTrue();
//        // Full credential-passing behavior is exercised via TrivyCommandBuilder's
//        // own unit tests — this just confirms the metadata correctly signals
//        // that auth is required for a private repo.
//    }
//}