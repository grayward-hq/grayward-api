package com.vulnwatch.worker.config;

import org.springframework.ai.chat.client.ChatClient;
import org.springframework.ai.openai.OpenAiChatModel;
import org.springframework.ai.openai.OpenAiChatOptions;
import org.springframework.ai.openai.api.OpenAiApi;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnMissingBean;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * AI provider configuration.
 *
 * Only ONE provider dependency should be active in pom.xml at a time.
 * Switching providers requires two steps:
 *   1. Swap the commented dependency in pom.xml  (openai / anthropic / google-genai)
 *   2. Update AI_PROVIDER in .env (openai / groq / anthropic / gemini)

 */
@Configuration
public class AiConfig {

    @Value("${groq.api-key:}")
    private String groqApiKey;

    @Value("${groq.base-url:https://api.groq.com/openai}")
    private String groqBaseUrl;

    @Value("${groq.model:llama-3.3-70b-versatile}")
    private String groqModel;

    /**
     * Groq — manually wired because it reuses the OpenAI-compatible API
     * pointed at a different base URL with a different key.
     *
     * Requires: spring-ai-starter-model-openai in pom.xml
     * Activate: AI_PROVIDER=groq in .env
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "groq")
    public ChatClient groqChatClient() {
        OpenAiApi groqApi = OpenAiApi.builder()
                .baseUrl(groqBaseUrl)
                .apiKey(groqApiKey)
                .build();

        OpenAiChatModel model = OpenAiChatModel.builder()
                .openAiApi(groqApi)
                .defaultOptions(OpenAiChatOptions.builder()
                        .model(this.groqModel)
                        .build())
                .build();

        return ChatClient.builder(model).build();
    }

    /**
     * Default — covers OpenAI, Anthropic, and Gemini.
     *
     * Spring AI's starter auto-configures a ChatClient.Builder for whichever
     * provider is on the classpath. This bean just calls builder.build().
     * No provider-specific imports are needed — switching providers only requires
     * changing the pom.xml dependency and AI_PROVIDER in .env.
     */
    @Bean
    @ConditionalOnMissingBean(ChatClient.class)
    public ChatClient defaultChatClient(ChatClient.Builder builder) {
        return builder.build();
    }
}