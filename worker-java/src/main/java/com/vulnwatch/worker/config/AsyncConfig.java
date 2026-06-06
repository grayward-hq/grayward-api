package com.vulnwatch.worker.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.util.concurrent.*;

@Configuration
public class AsyncConfig {
    @Bean(destroyMethod = "shutdown")
    public ExecutorService executorService() {

        return new ThreadPoolExecutor(
                8, // core pool size
                16, // max pool size
                60L, TimeUnit.SECONDS, // idle thread timeout

                new LinkedBlockingQueue<>(100), // queue capacity

                new ThreadFactory() {
                    private int count = 1;

                    @Override
                    public Thread newThread(Runnable r) {
                        Thread t = new Thread(r);
                        t.setName("app-exec-" + count++);
                        return t;
                    }
                },

                new ThreadPoolExecutor.CallerRunsPolicy() // backpressure strategy
        );
    }
}
