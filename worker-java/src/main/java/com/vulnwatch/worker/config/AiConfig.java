package com.vulnwatch.worker.config;

import org.springframework.ai.anthropic.AnthropicChatModel;
import org.springframework.ai.chat.client.ChatClient;
import org.springframework.ai.google.genai.GoogleGenAiChatModel;
import org.springframework.ai.openai.OpenAiChatModel;
import org.springframework.ai.openai.api.OpenAiApi;
import org.springframework.ai.openai.OpenAiChatOptions;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Primary;

@Configuration
public class AiConfig {

    // Groq configuration injection variables
    @Value("${groq.api-key:}")
    private String groqApiKey;

    @Value("${groq.base-url:https://api.groq.com/openai}")
    private String groqBaseUrl;

    @Value("${groq.model:llama-3.3-70b-versatile}")
    private String groqModel;


    @Bean
    @Primary
    @ConditionalOnProperty(
            name = "worker.ai.provider",
            havingValue = "groq",
            matchIfMissing = true
    )
    public ChatClient groqChatClient() {
        // Correct way to initialize OpenAiApi using the Builder pattern
        OpenAiApi groqApi = OpenAiApi.builder()
                .baseUrl(this.groqBaseUrl)
                .apiKey(this.groqApiKey)
                .build();

        // Define options using bean setter style setters
        OpenAiChatOptions options = OpenAiChatOptions.builder()
                .model(this.groqModel)
                .build();

        // Assemble the Chat Model utilising its designated builder
        OpenAiChatModel dedicatedGroqModel = OpenAiChatModel.builder()
                .openAiApi(groqApi)
                .defaultOptions(options)
                .build();

        return ChatClient.builder(dedicatedGroqModel).build();
    }

    /**
     * Native OpenAI Client — Active when explicitly requested
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "openai")
    public ChatClient openAiChatClient(OpenAiChatModel openAiChatModel) {
        return ChatClient.builder(openAiChatModel).build();
    }

    /**
     * Anthropic Claude Core Engines
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "anthropic")
    public ChatClient anthropicChatClient(AnthropicChatModel anthropicChatModel) {
        return ChatClient.builder(anthropicChatModel).build();
    }

    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "claude")
    public ChatClient claudeChatClient(AnthropicChatModel anthropicChatModel) {
        return ChatClient.builder(anthropicChatModel).build();
    }

    /**
     * Google Gemini Engines
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "google")
    public ChatClient googleChatClient(GoogleGenAiChatModel googleGenAiChatModel) {
        return ChatClient.builder(googleGenAiChatModel).build();
    }

    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "gemini")
    public ChatClient geminiChatClient(GoogleGenAiChatModel googleGenAiChatModel) {
        return ChatClient.builder(googleGenAiChatModel).build();
    }
}