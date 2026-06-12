package com.vulnwatch.worker.engine.parsers;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

@Slf4j
@Component
public class DnsParser implements Parser<ScanContext> {

    @Override
    public ScanContext parse(File file) throws IOException {

            ObjectMapper mapper = new ObjectMapper();
            JsonNode root = mapper.readTree(file);

            List<String> a = new ArrayList<>();
            List<String> aaaa = new ArrayList<>();
            List<String> ns = new ArrayList<>();
            List<String> mx = new ArrayList<>();
            List<String> soa = new ArrayList<>();
            List<String> txt = new ArrayList<>();
            List<String> dnskey = new ArrayList<>();
            List<String> rrsig = new ArrayList<>();


            for (JsonNode node : root) {
                String type = node.path("type").asText();

                switch (type) {

                    case "A" -> {
                        String ip = node.path("address").asText(null);
                        if (ip != null) a.add(ip);
                    }

                    case "AAAA" -> {
                        String ip = node.path("address").asText(null);
                        if (ip != null) aaaa.add(ip);
                    }

                    case "NS" -> {
                        String target = node.path("target").asText(null);
                        if (target != null) ns.add(target);
                    }

                    case "MX" -> {
                        String exchange = node.path("exchange").asText(null);
                        if (exchange != null) mx.add(exchange);
                    }

                    case "SOA" -> {
                        String mname = node.path("mname").asText(null);
                        if (mname != null) soa.add(mname);
                    }

                    case "DNSKEY" -> {
                        String name = node.path("name").asText(null);
                        if (name != null) dnskey.add(name);
                    }

                    case "RRSIG" -> {
                        String name = node.path("name").asText(null);
                        if (name != null) rrsig.add(name);
                    }

                    case "TXT" -> {
                        JsonNode strings = node.path("strings");

                        if (strings.isArray()) {
                            for (JsonNode s : strings) {
                                txt.add(s.asText());
                            }
                        } else if (!strings.isMissingNode()) {
                            txt.add(strings.asText());
                        }
                    }

                    default -> {
                    }
                }
            }
            return new ScanContext(
                    a,
                    aaaa,
                    ns,
                    mx,
                    soa,
                    txt,
                    dnskey,
                    rrsig
            );

    }
}
