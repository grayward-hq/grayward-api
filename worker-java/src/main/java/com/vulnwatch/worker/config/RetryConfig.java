package com.vulnwatch.worker.config;

import org.springframework.context.annotation.Configuration;
import org.springframework.retry.annotation.EnableRetry;

/**
 * Activates Spring Retry's AOP proxy infrastructure.
 *
 * @EnableRetry must be present for @Retryable and @Recover to fire.
 * Without this, those annotations are silently ignored.
 *
 * spring-retry and spring-aspects are already on the classpath
 * via spring-ai-retry (pulled in transitively by the Spring AI starters).
 * No additional pom.xml dependency is needed.
 */
@EnableRetry
@Configuration
public class RetryConfig {}
