package com.vulnwatch.worker.owasp.rules;

import com.vulnwatch.worker.engine.domain.testssl.models.SslFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.owasp.enums.OWASPCategory;
import com.vulnwatch.worker.owasp.interfaces.OWASPMappingRule;
import org.springframework.stereotype.Component;

import java.util.Collections;
import java.util.List;
import java.util.Set;

@Component
public class CryptoFailuresSslRule implements OWASPMappingRule {

    // testssl id prefixes that indicate cryptographic failures
    private static final Set<String> CRYPTO_IDS = Set.of(
            "cert_notAfter", "cert_expired", "cert_trust",
            "cipherlist_LOW", "cipherlist_WEAK", "cipherlist_3DES",
            "SSLv2", "SSLv3", "TLS1", "TLS1_1",
            "BEAST", "POODLE", "ROBOT", "HEARTBLEED"
    );

    @Override
    public OWASPCategory category() {
        return OWASPCategory.CRYPTOGRAPHIC_FAILURES;
    }

    @Override
    public boolean matches(EngineResult engine, AiResult ai) {
        if (engine == null || engine.surfaceType() != SurfaceType.SSL || !engine.success()) {
            return false;
        }
        List<SslFindings> findings = extractFindings(engine);
        return findings.stream().anyMatch(f -> isCryptoId(f.id()));
    }

    @Override
    public FindingSeverity severity(EngineResult engine, AiResult ai) {
        // If expired cert or weak TLS version → HIGH; weak cipher → MEDIUM
        List<SslFindings> findings = extractFindings(engine);
        boolean hasCritical = findings.stream()
                .anyMatch(f -> isCriticalId(f.id()));
        return hasCritical ? FindingSeverity.HIGH : FindingSeverity.MEDIUM;
    }

    @Override
    public String findingLabel(EngineResult engine, AiResult ai) {
        List<SslFindings> findings = extractFindings(engine);
        // Return the first matching finding's description
        return findings.stream()
                .filter(f -> isCryptoId(f.id()))
                .map(SslFindings::finding)
                .findFirst()
                .orElse("SSL/TLS Cryptographic Issue");
    }

    private boolean isCryptoId(String id) {
        if (id == null) return false;
        return CRYPTO_IDS.stream().anyMatch(id::startsWith);
    }

    private boolean isCriticalId(String id) {
        if (id == null) return false;
        return id.startsWith("cert_notAfter") || id.startsWith("cert_expired")
                || id.startsWith("SSLv2") || id.startsWith("SSLv3")
                || id.startsWith("TLS1") || id.startsWith("HEARTBLEED");
    }

    /**
     * Safely extracts and casts SslFindings from the EngineResult rawResult map payload.
     * Prevents ClassCastExceptions if a mismatch happens during matrix loop iterations.
     */
    @SuppressWarnings("unchecked")
    private List<SslFindings> extractFindings(EngineResult engine) {
        if (engine == null || engine.rawResult() == null) {
            return Collections.emptyList();
        }

        Object findingsObj = engine.rawResult().get("findings");
        if (findingsObj instanceof List<?>) {
            List<?> rawList = (List<?>) findingsObj;
            // Check first element safely to verify true list payload type
            if (!rawList.isEmpty() && !(rawList.get(0) instanceof SslFindings)) {
                return Collections.emptyList();
            }
            return (List<SslFindings>) rawList;
        }

        return Collections.emptyList();
    }
}