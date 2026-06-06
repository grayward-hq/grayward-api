package com.vulnwatch.worker;

import com.vulnwatch.worker.listener.CheckpointManager;
import com.vulnwatch.worker.listener.QueueListener;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.stereotype.Component;

@Slf4j
@Component
@RequiredArgsConstructor
public class WorkerRunner implements ApplicationRunner {

    private final QueueListener queueListener;
    private final CheckpointManager checkpointManager;

    @Override
    public void run(ApplicationArguments args) {
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            log.info("Shutdown hook triggered. Stopping queue listener loop...");
            queueListener.stop();
        }));

        log.info("Worker started. Executing pre-boot checkpoint recovery checks...");

        // recover any jobs that were in-flight when the worker last crashed.
        // must run before queueListener.run() opens the BLPOP loop.
        int recovered = checkpointManager.recoverInProgress();
        if (recovered < 1) {
            log.info("No active checkpoints or orphaned tasks found in cache. System clean.");
        } else {
            log.info("Successfully re-queued {} orphaned job(s) back into active processing.", recovered);
        }

        log.info("Entering queue poll routine. Awaiting incoming scan jobs...");
        queueListener.run(); // blocks — listens on queue
    }
}