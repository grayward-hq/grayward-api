package com.vulnwatch.worker.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Configures virtual threads to prevent I/O pool exhaustion during concurrent scans.
 */
@Configuration
public class AsyncConfig {

    /**
     * Backs CliExecutor process stream readers. Replaces the old 16-max fixed pool
     * with virtual threads to stop tasks from queuing up during heavy concurrent runs.
     * Since BufferedReader blocking allows carrier threads to unmount safely without pinning,
     * this guarantees immediate execution for every stream-draining task.
     */
    @Bean(destroyMethod = "shutdown")
    public ExecutorService executorService() {
        return Executors.newVirtualThreadPerTaskExecutor();
    }
}