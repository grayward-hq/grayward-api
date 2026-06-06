package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.model.payload.SslPayload;
import org.springframework.stereotype.Component;

import javax.net.ssl.*;
import java.net.InetSocketAddress;
import java.security.cert.X509Certificate;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.List;

@Component
public class SslEngine implements Scanner {

    private static final int TIMEOUT_MS = 10_000;

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.SSL;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        List<String> issues = new ArrayList<>();

        try {
            SSLSocketFactory factory = (SSLSocketFactory) SSLSocketFactory.getDefault();

            try (SSLSocket socket = (SSLSocket) factory.createSocket()) {
                socket.connect(new InetSocketAddress(domain, 443), TIMEOUT_MS);
                socket.setSoTimeout(TIMEOUT_MS);
                socket.startHandshake();

                SSLSession session = socket.getSession();
                String protocol = session.getProtocol();
                String cipherSuite = session.getCipherSuite();

                if (protocol.equals("TLSv1") || protocol.equals("TLSv1.1") || protocol.equals("SSLv3")) {
                    issues.add("Weak protocol in use: " + protocol);
                }

                String certSubject = null;
                String certExpiry = null;
                int daysUntilExpiry = 0;
                boolean isSelfSigned = false;
                boolean isExpired = false;

                java.security.cert.Certificate[] certs = session.getPeerCertificates();
                if (certs.length > 0 && certs[0] instanceof X509Certificate cert) {
                    Instant expiry = cert.getNotAfter().toInstant();
                    daysUntilExpiry = (int) ChronoUnit.DAYS.between(Instant.now(), expiry);
                    certSubject = cert.getSubjectX500Principal().getName();
                    certExpiry = expiry.toString();
                    isExpired = daysUntilExpiry < 0;
                    isSelfSigned = cert.getIssuerX500Principal()
                            .equals(cert.getSubjectX500Principal());

                    if (isExpired) {
                        issues.add("Certificate is EXPIRED");
                    } else if (daysUntilExpiry < 30) {
                        issues.add("Certificate expires in " + daysUntilExpiry + " days");
                    }
                    if (isSelfSigned) {
                        issues.add("Self-signed certificate detected");
                    }
                }

                return new EngineResult("Ssl", true, null,
                        new SslPayload(protocol, cipherSuite, certSubject, certExpiry,
                                daysUntilExpiry, isSelfSigned, isExpired, issues));
            }

        } catch (Exception e) {
            return EngineResult.failure("Ssl", "SSL probe failed: " + e.getMessage());
        }
    }
}