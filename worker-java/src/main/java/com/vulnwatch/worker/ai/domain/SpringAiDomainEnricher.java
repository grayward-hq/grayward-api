package com.vulnwatch.worker.ai.domain;

import com.vulnwatch.worker.ai.interfaces.AiEnricher;
import com.vulnwatch.worker.ai.model.PromptBuilder;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import io.github.resilience4j.bulkhead.Bulkhead;
import io.github.resilience4j.bulkhead.BulkheadFullException;
import io.github.resilience4j.bulkhead.BulkheadRegistry;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.ai.chat.client.ChatClient;
import org.springframework.stereotype.Component;

/**
 * Every outbound call here is gated by the shared "ai-enrichment" Bulkhead
 * (see application.properties) so a burst of concurrent domain jobs — each
 * fanning out to up to 5 surfaces — can't open unbounded simultaneous
 * requests to the AI provider. The bulkhead is deliberately configured to
 * wait a long time for a slot rather than reject quickly: enrichment
 * quality matters more than shaving a few seconds off a busy period, so
 * callers queue instead of silently losing AI output under load.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class SpringAiDomainEnricher implements AiEnricher {

    private static final String AI_BULKHEAD = "ai-enrichment";

    private final ChatClient chatClient;
    private final PromptBuilder promptBuilder;
    private final BulkheadRegistry bulkheadRegistry;

    @Override
    public AiResult enrich(ScanJob job, EngineResult engineResult) {
        Bulkhead bulkhead = bulkheadRegistry.bulkhead(AI_BULKHEAD);
        try {
            bulkhead.acquirePermission();
        } catch (BulkheadFullException e) {
            log.warn("AI enrichment bulkhead saturated too long, skipping [scanId={} surface={}]",
                    job.scanId(), engineResult.surfaceType().getLabel());
            return null;
        }

        try {
            return chatClient.prompt()
                    .system(promptBuilder.domainSystemPrompt())
                    .user(promptBuilder.domainEnrichPrompt(job, engineResult))
                    .call()
                    .entity(AiResult.class);
        } catch (Exception e) {
            log.warn("AI enrichment failed [scanId={} surface={}]: {}",
                    job.scanId(), engineResult.surfaceType().getLabel(), e.getMessage());
            return null;
        } finally {
            bulkhead.onComplete();
        }
    }

    @Override
    public String describe(ScanJob job) {
        Bulkhead bulkhead = bulkheadRegistry.bulkhead(AI_BULKHEAD);
        try {
            bulkhead.acquirePermission();
        } catch (BulkheadFullException e) {
            log.warn("AI enrichment bulkhead saturated too long, skipping describe [scanId={}]", job.scanId());
            return null;
        }

        try {
            return chatClient.prompt()
                    .user(promptBuilder.domainDescribePrompt(job))
                    .call()
                    .content();
        } catch (Exception e) {
            log.warn("AI describe failed [scanId={}]: {}", job.scanId(), e.getMessage());
            return null;
        } finally {
            bulkhead.onComplete();
        }
    }

    @Override
    public String posture(OWASPEvaluationResult owaspResult) {
        Bulkhead bulkhead = bulkheadRegistry.bulkhead(AI_BULKHEAD);
        try {
            bulkhead.acquirePermission();
        } catch (BulkheadFullException e) {
            log.warn("AI enrichment bulkhead saturated too long, skipping posture [scanId={}]", owaspResult.scanId());
            return null;
        }

        try {
            return chatClient.prompt()
                    .system(promptBuilder.owaspPostureSystemPrompt())
                    .user(promptBuilder.owaspPostureUserPrompt(owaspResult))
                    .call()
                    .content();
        } catch (Exception e) {
            log.warn("OWASP posture call failed [scanId={}]: {}",
                    owaspResult.scanId(), e.getMessage());
            return null;
        } finally {
            bulkhead.onComplete();
        }
    }
}