package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.model.payload.HttpPayload;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.TimeUnit;

@Component
public class HttpEngine implements Scanner {

    private static final List<String> REQUIRED_HEADERS = List.of(
        "Strict-Transport-Security",
        "Content-Security-Policy",
        "X-Frame-Options",
        "X-Content-Type-Options",
        "Referrer-Policy",
        "Permissions-Policy"
    );

    private final OkHttpClient http = new OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .followRedirects(true)
        .build();
    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.HTTP_HEADERS;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        List<String> present = new ArrayList<>();
        List<String> missing = new ArrayList<>();
        List<String> issues = new ArrayList<>();

        try {
            Request request = new Request.Builder()
                .url("https://" + domain)
                .head()
                .build();

            try (Response response = http.newCall(request).execute()) {
                int statusCode = response.code();
                String serverHeader = response.header("Server", "not disclosed");

                for (String header : REQUIRED_HEADERS) {
                    if (response.header(header) != null) {
                        present.add(header);
                    } else {
                        missing.add(header);
                    }
                }

                String exposedTechnology = response.header("X-Powered-By");
                if (exposedTechnology != null) {
                    issues.add("X-Powered-By header exposes server technology: " + exposedTechnology);
                }

                return new EngineResult("HttpHeaders", true, null,
                        new HttpPayload(statusCode, serverHeader, present, missing,
                                exposedTechnology, issues));
            }

        } catch (Exception e) {
            return EngineResult.failure("HttpHeaders", "HTTP probe failed: " + e.getMessage());
        }
    }
}