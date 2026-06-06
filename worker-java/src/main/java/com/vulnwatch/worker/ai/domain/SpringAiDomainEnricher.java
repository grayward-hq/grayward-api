package com.vulnwatch.worker.ai.domain;

import com.vulnwatch.worker.ai.interfaces.AiEnricher;
import com.vulnwatch.worker.ai.model.PromptBuilder;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.ai.chat.client.ChatClient;
import org.springframework.stereotype.Component;

@Slf4j
@Component
@RequiredArgsConstructor
public class SpringAiDomainEnricher implements AiEnricher {

    private final ChatClient chatClient;
    private final PromptBuilder promptBuilder;

    @Override
    public AiResult enrich(ScanJob job, EngineResult engineResult) {
        try {
            return chatClient.prompt()
                    .system(promptBuilder.domainSystemPrompt())
                    .user(promptBuilder.domainEnrichPrompt(job, engineResult))
                    .call()
                    .entity(AiResult.class);
        } catch (Exception e) {
            log.warn("AI enrichment failed [scanId={} surface={}]: {}",
                    job.scanId(), engineResult.surface(), e.getMessage());
            return null;
        }
    }

    @Override
    public String describe(ScanJob job) {
        try {
            return chatClient.prompt()
                    .user(promptBuilder.domainDescribePrompt(job))
                    .call()
                    .content();
        } catch (Exception e) {
            log.warn("AI describe failed [scanId={}]: {}", job.scanId(), e.getMessage());
            return null;
        }
    }
}
