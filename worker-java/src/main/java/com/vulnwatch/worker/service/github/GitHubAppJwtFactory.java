package com.vulnwatch.worker.service.github;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyFactory;
import java.security.PrivateKey;
import java.security.Signature;
import java.security.spec.PKCS8EncodedKeySpec;
import java.time.Instant;
import java.util.Base64;

/**
 * Mints short-lived GitHub App JWTs (RS256), used to exchange for a scoped
 * per-installation access token. Mirrors the approach the .NET API already
 * uses, implemented independently here so the
 * worker never depends on a token riding through the Redis job payload.
 *
 * Requires the private key in PKCS8 PEM format ("BEGIN PRIVATE KEY").
 * GitHub issues PKCS1 keys by default — convert once with:
 *   openssl pkcs8 -topk8 -inform PEM -outform PEM -nocrypt -in original.pem -out pkcs8.pem
 */
@Component
public class GitHubAppJwtFactory {

    private static final Base64.Encoder URL_ENCODER = Base64
            .getUrlEncoder()
            .withoutPadding();

    private final String appId;
    private final Path privateKeyPath;

    public GitHubAppJwtFactory(
            @Value("${github.app.id:}") String appId,
            @Value("${github.app.private-key-path:}") String privateKeyPath) {
        this.appId = appId;
        this.privateKeyPath = (privateKeyPath == null || privateKeyPath.isBlank())
                ? null
                : Path.of(privateKeyPath);
    }

    public boolean isConfigured() {
        return appId != null && !appId.isBlank() && privateKeyPath != null && Files.exists(privateKeyPath);
    }

    /** Creates a JWT valid for 9 minutes (GitHub's hard max is 10). */
    public String createJwt() {
        if (!isConfigured()) {
            throw new IllegalStateException(
                    "GitHub App not configured — set github.app.id and github.app.private-key-path");
        }

        try {
            long now = Instant
                    .now()
                    .getEpochSecond();
            String header = """
                    {"alg":"RS256","typ":"JWT"}""";
            String payload = """
                    {"iat":%d,"exp":%d,"iss":"%s"}""".formatted(now - 60, now + 540, appId);

            String signingInput = "%s.%s".formatted(URL_ENCODER.encodeToString(header.getBytes(StandardCharsets.UTF_8)),
                    URL_ENCODER.encodeToString(payload.getBytes(StandardCharsets.UTF_8)));

            byte[] signature = sign(signingInput);
            return "%s.%s".formatted(signingInput, URL_ENCODER.encodeToString(signature));

        } catch (Exception e) {
            throw new IllegalStateException("Failed to mint GitHub App JWT", e);
        }
    }

    private byte[] sign(String signingInput) throws Exception {
        PrivateKey privateKey = loadPrivateKey();
        Signature signer = Signature.getInstance("SHA256withRSA");
        signer.initSign(privateKey);
        signer.update(signingInput.getBytes(StandardCharsets.US_ASCII));
        return signer.sign();
    }

    private PrivateKey loadPrivateKey() throws Exception {
        String pem = Files.readString(privateKeyPath)
                .replace("-----BEGIN PRIVATE KEY-----", "")
                .replace("-----END PRIVATE KEY-----", "")
                .replaceAll("\\s", "");

        byte[] keyBytes = Base64.getDecoder().decode(pem);
        PKCS8EncodedKeySpec spec = new PKCS8EncodedKeySpec(keyBytes);
        return KeyFactory.getInstance("RSA").generatePrivate(spec);
    }
}