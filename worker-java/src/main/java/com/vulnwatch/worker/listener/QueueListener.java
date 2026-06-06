package com.vulnwatch.worker.listener;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.processor.JobProcessor;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import redis.clients.jedis.JedisPooled;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

/**
 * Blocks on a Redis queue and dispatches incoming scan jobs to the
 * appropriate {@link JobProcessor} on a virtual-thread executor.
 * stopped gracefully via {@link #stop()} on shutdown.
 */
@Slf4j
@RequiredArgsConstructor
@Component
public class QueueListener implements Runnable {

    private static final int SHUTDOWN_TIMEOUT_SECONDS = 10;

    @Value("${worker.blpop.timeout:5}")
    private int blpopTimeout;

    @Value("${worker.scanjob.queue:scan-jobs}")
    private String queueName;

    private final JedisPooled jedis;
    private final Map<String, JobProcessor> processors;
    private final ObjectMapper mapper;
    private final CheckpointManager checkpointManager;
    private final ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();

    private volatile boolean running = true;


    @Override
    public void run() {
        log.info("QueueListener started — blocking on queue '{}'", queueName);

        while (running) {
            try {
                List<String> result = jedis.blpop(blpopTimeout, queueName);
                if (result == null)
                    continue;           // normal timeout, keep polling

                String payload = result.get(1);
                executor.submit(() -> handle(payload));

            } catch (Exception e) {
                log.error("Error reading from queue '{}', retrying in 1s: {}", queueName, e.getMessage());
                backoff();
            }
        }

        log.info("QueueListener stopped.");
    }

    private void handle(String raw) {
        ScanJob job = deserialize(raw);
        if (job == null)
            return;

        log.info("Received job [scanId={} domainId={} type={}]",
                job.scanId(), job.domainId(), job.scanType());


        // mark checkpoint before handing to processor
        // If the worker crashes anywhere after this point, WorkerRunner
        // will re-queue this job on the next startup.
        checkpointManager.mark(job.scanId(), raw);

        JobProcessor processor = processors.get(job.scanType());
        if (processor == null) {
            log.warn("No processor registered for type '{}'. Known types: {}",
                    job.scanType(), processors.keySet());
            return;
        }

        try {
            processor.process(job);
        } catch (Exception e) {
            log.error("Processor failed [scanId={} type={}]",
                    job.scanId(), job.scanType(), e);
        }
    }

    private ScanJob deserialize(String raw) {
        try {
            return mapper.readValue(raw, ScanJob.class);
        } catch (Exception e) {
            log.error("Failed to deserialize job payload, dropping message. Payload: {}", raw, e);
            return null;
        }
    }



    public void stop() {
        log.info("Shutting down QueueListener...");
        running = false;
        executor.shutdown();
        try {
            if (!executor.awaitTermination(SHUTDOWN_TIMEOUT_SECONDS, TimeUnit.SECONDS)) {
                log.warn("Executor did not terminate cleanly within {}s, forcing shutdown",
                        SHUTDOWN_TIMEOUT_SECONDS);
                executor.shutdownNow();
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            executor.shutdownNow();
        }
    }

    private static void backoff() {
        try {
            Thread.sleep(1000);
        } catch (InterruptedException ie) {
            Thread.currentThread().interrupt();
        }
    }
}