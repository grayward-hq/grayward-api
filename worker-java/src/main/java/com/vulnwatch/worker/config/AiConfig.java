package com.vulnwatch.worker.config;

import org.springframework.ai.anthropic.AnthropicChatModel;
import org.springframework.ai.chat.client.ChatClient;
import org.springframework.ai.google.genai.GoogleGenAiChatModel; // Fixed import
import org.springframework.ai.openai.OpenAiChatModel;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class AiConfig {

    @Value("${worker.ai.provider:groq}")   // groq | anthropic | google (or gemini)
    private String aiProvider;

    /**
     * Single ChatClient bean — model is chosen by worker.ai.provider property.
     * No code change needed to swap models.
     */
    @Bean
    public ChatClient chatClient(
            OpenAiChatModel openAiChatModel,           // used for groq (OpenAI-compatible endpoint)
            AnthropicChatModel anthropicChatModel,
            GoogleGenAiChatModel googleGenAiChatModel) { // Fixed class type

        var model = switch (aiProvider.toLowerCase().trim()) {
            case "anthropic", "claude" -> anthropicChatModel;
            case "google", "gemini"  -> googleGenAiChatModel;
            default  -> openAiChatModel;       // groq / openai is default
        };

        return ChatClient
                .builder(model)
                .build();
    }
}