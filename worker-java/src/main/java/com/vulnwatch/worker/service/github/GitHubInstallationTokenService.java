package com.vulnwatch.worker.service.github;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.annotation.PostConstruct;
import jakarta.annotation.PreDestroy;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.time.Instant;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.locks.ReentrantLock;

/**
 * Exchanges a GitHub App JWT for a short-lived, per-installation access
 * token (scoped only to that installation's repos).
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class GitHubInstallationTokenService {

    private final GitHubAppJwtFactory jwtFactory;
    private final HttpClient githubHttpClient;
    private final ObjectMapper objectMapper;

    @Value("${github.api-url:https://api.github.com}")
    private String apiUrl;

    private final Map<String, CachedToken> cache = new ConcurrentHashMap<>();

    private final Map<String, LockEntry> installationLocks = new ConcurrentHashMap<>();

    private static final Duration LOCK_IDLE_TTL = Duration.ofHours(1);

    private final ScheduledExecutorService evictionScheduler =
            Executors.newSingleThreadScheduledExecutor(r -> {
                Thread t = new Thread(r, "github-token-lock-evictor");
                t.setDaemon(true);
                return t;
            });

    private static final class LockEntry {
        private final ReentrantLock lock = new ReentrantLock();
        private volatile Instant lastUsed = Instant.now();
    }

    private record CachedToken(String token, Instant expiresAt) {
        boolean isStillValid() {
            return Instant.now().isBefore(expiresAt.minusSeconds(120));
        }
    }

    @PostConstruct
    void startEvictionSweeper() {
        evictionScheduler.scheduleAtFixedRate(
                this::evictStaleEntries, 15, 15, TimeUnit.MINUTES);
    }

    @PreDestroy
    void stopEvictionSweeper() {
        evictionScheduler.shutdownNow();
    }

    /**
     * Removes lock entries that have been idle for longer than
     * {@link #LOCK_IDLE_TTL} (skipping any lock currently held, so an
     * in-flight mint is never disrupted) and prunes cache entries whose
     * token has already expired.
     */
    private void evictStaleEntries() {
        Instant cutoff = Instant.now().minus(LOCK_IDLE_TTL);

        installationLocks.entrySet().removeIf(entry -> {
            LockEntry lockEntry = entry.getValue();
            if (lockEntry.lastUsed.isAfter(cutoff)) {
                return false;
            }
            if (!lockEntry.lock.tryLock()) {
                return false; // currently in use — leave it alone
            }
            try {
                return lockEntry.lastUsed.isBefore(cutoff);
            } finally {
                lockEntry.lock.unlock();
            }
        });

        cache.entrySet().removeIf(entry -> entry.getValue().expiresAt().isBefore(Instant.now()));
    }

    public String getInstallationToken(String installationId) {
        CachedToken cached = cache.get(installationId);
        if (cached != null && cached.isStillValid()) {
            return cached.token();
        }
        return mintAndCache(installationId);
    }

    private String mintAndCache(String installationId) {
        LockEntry lockEntry = installationLocks.computeIfAbsent(installationId, id -> new LockEntry());

        lockEntry.lock.lock();
        try {
            lockEntry.lastUsed = Instant.now();

            CachedToken cached = cache.get(installationId);
            if (cached != null && cached.isStillValid()) {
                return cached.token();
            }

            try {
                String jwt = jwtFactory.createJwt();
                String url = "%s/app/installations/%s/access_tokens".formatted(apiUrl, installationId);

                HttpRequest request = HttpRequest.newBuilder()
                        .uri(URI.create(url))
                        .header("Authorization", "Bearer %s".formatted(jwt))
                        .header("Accept", "application/vnd.github+json")
                        .header("X-GitHub-Api-Version", "2022-11-28")
                        .POST(HttpRequest.BodyPublishers.noBody())
                        .timeout(Duration.ofSeconds(5))
                        .build();

                // Utilizing the decoupled client bean
                HttpResponse<String> response = githubHttpClient.send(request, HttpResponse.BodyHandlers.ofString());

                if (response.statusCode() != 201) {
                    throw new IllegalStateException(
                            "GitHub installation token exchange failed (%d): %s"
                                    .formatted(response.statusCode(), response.body()));
                }

                JsonNode body = objectMapper.readTree(response.body());
                String token = body.get("token").asText();
                Instant expiresAt = Instant.parse(body.get("expires_at").asText());

                cache.put(installationId, new CachedToken(token, expiresAt));
                log.info("Minted GitHub installation token [installationId={} expiresAt={}]",
                        installationId, expiresAt);

                return token;

            } catch (Exception e) {
                throw new IllegalStateException(
                        "Failed to obtain GitHub installation token [installationId=%s]"
                                .formatted(installationId), e);
            }
        } finally {
            lockEntry.lock.unlock();
        }
    }
}