package com.vulnwatch.worker.service.github;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import lombok.RequiredArgsConstructor;
import lombok.Synchronized;
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

    private record CachedToken(String token, Instant expiresAt) {
        boolean isStillValid() {
            return Instant.now().isBefore(expiresAt.minusSeconds(120));
        }
    }

    public String getInstallationToken(String installationId) {
        CachedToken cached = cache.get(installationId);
        if (cached != null && cached.isStillValid()) {
            return cached.token();
        }
        return mintAndCache(installationId);
    }

    @Synchronized
    private String mintAndCache(String installationId) {
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
    }
}