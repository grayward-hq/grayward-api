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
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "groq")
    public ChatClient groqChatClient() {
        OpenAiApi groqApi = OpenAiApi.builder()
                .baseUrl(this.groqBaseUrl)
                .apiKey(this.groqApiKey)
                .build();

        OpenAiChatOptions options = OpenAiChatOptions.builder()
                .model(this.groqModel)
                .build();

        OpenAiChatModel dedicatedGroqModel = OpenAiChatModel.builder()
                .openAiApi(groqApi)
                .defaultOptions(options)
                .build();

        return ChatClient.builder(dedicatedGroqModel).build();
    }

    /**
     * Native OpenAI Client — Active when worker.ai.provider=openai
     */
    @Bean
    @Primary
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "openai")
    public ChatClient openAiChatClient(OpenAiChatModel openAiChatModel) {
        return ChatClient.builder(openAiChatModel).build();
    }

    /**
     * Anthropic Claude — Active when worker.ai.provider=anthropic
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "anthropic")
    public ChatClient anthropicChatClient(AnthropicChatModel anthropicChatModel) {
        return ChatClient.builder(anthropicChatModel).build();
    }

    /**
     * Anthropic Claude (alias) — Active when worker.ai.provider=claude
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "claude")
    public ChatClient claudeChatClient(AnthropicChatModel anthropicChatModel) {
        return ChatClient.builder(anthropicChatModel).build();
    }

    /**
     * Google Gemini — Active when worker.ai.provider=google
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "google")
    public ChatClient googleChatClient(GoogleGenAiChatModel googleGenAiChatModel) {
        return ChatClient.builder(googleGenAiChatModel).build();
    }

    /**
     * Google Gemini (alias) — Active when worker.ai.provider=gemini
     */
    @Bean
    @ConditionalOnProperty(name = "worker.ai.provider", havingValue = "gemini")
    public ChatClient geminiChatClient(GoogleGenAiChatModel googleGenAiChatModel) {
        return ChatClient.builder(googleGenAiChatModel).build();
    }
}