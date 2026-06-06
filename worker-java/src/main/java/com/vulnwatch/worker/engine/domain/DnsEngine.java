package com.vulnwatch.worker.engine.domain;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.model.payload.DnsPayload;
import org.springframework.stereotype.Component;

import javax.naming.directory.Attributes;
import javax.naming.directory.InitialDirContext;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
@Component
public class DnsEngine implements Scanner {

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.DNS;
    }

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        Map<String, String> rawRecords = new HashMap<>();
        List<String> issues = new ArrayList<>();
        boolean hasSPF = false, hasDMARC = false, hasMX = false;

        try {
            InitialDirContext ctx = new InitialDirContext();

            try {
                Attributes txt = ctx.getAttributes("dns:/" + domain, new String[]{"TXT"});
                String txtRecords = txt.toString();
                hasSPF = txtRecords.contains("v=spf1");
                if (!hasSPF) issues.add("No SPF record found");
                rawRecords.put("txt", txtRecords);
            } catch (Exception e) {
                issues.add("Could not retrieve TXT records: %s".formatted(e.getMessage()));
            }

            try {
                Attributes dmarc = ctx.getAttributes("dns:/_dmarc.%s".formatted(domain), new String[]{"TXT"});
                hasDMARC = true;
                rawRecords.put("dmarc", dmarc.toString());
            } catch (Exception e) {
                issues.add("No DMARC record found at _dmarc.%s".formatted(domain));
            }

            try {
                Attributes mx = ctx.getAttributes("dns:/%s".formatted(domain), new String[]{"MX"});
                hasMX = true;
                rawRecords.put("mx", mx.toString());
            } catch (Exception e) {
                issues.add("No MX records found");
            }

            return new EngineResult(SurfaceType.DNS.getLabel(), true, null,
                    new DnsPayload(hasSPF, hasDMARC, hasMX, issues, rawRecords));

        } catch (Exception e) {
            return EngineResult.failure(SurfaceType.DNS.getLabel(), "DNS lookup failed: %s".formatted(e.getMessage()));
        }
    }
}