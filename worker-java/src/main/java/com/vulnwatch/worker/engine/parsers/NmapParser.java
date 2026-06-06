package com.vulnwatch.worker.engine.parsers;

import com.vulnwatch.worker.engine.domain.dnsrecon.model.ScanContext;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.enums.FindingSeverity;
import org.springframework.stereotype.Component;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;

import javax.xml.parsers.DocumentBuilder;
import javax.xml.parsers.DocumentBuilderFactory;
import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Set;

@Component
public class NmapParser implements Parser<List<NmapFindings>> {

    private static final Map<Integer, String> RISKY_PORTS = Map.of(
            22, "SSH exposed",
            3306, "MySQL exposed",
            5432, "PostgreSQL exposed",
            8080, "HTTP alternate/admin port",
            25, "SMTP exposed",
            445, "SMB exposed"
    );

    private static final Set<Integer> X11_PORTS =
            Set.of(6001, 6002, 6003);

    @Override
    public List<NmapFindings> parse(File file) throws IOException {
        try {
            List<NmapFindings> findings = new ArrayList<>();

            DocumentBuilderFactory factory =
                    DocumentBuilderFactory.newInstance();

            DocumentBuilder builder =
                    factory.newDocumentBuilder();

            Document document = builder.parse(file);

            NodeList ports =
                    document.getElementsByTagName("port");

            for (int i = 0; i < ports.getLength(); i++) {

                Element portElement =
                        (Element) ports.item(i);

                int port =
                        Integer.parseInt(
                                portElement.getAttribute("portid")
                        );

                String protocol =
                        portElement.getAttribute("protocol");

                Element stateElement =
                        (Element) portElement
                                .getElementsByTagName("state")
                                .item(0);

                String state =
                        stateElement.getAttribute("state");

                Element serviceElement =
                        (Element) portElement
                                .getElementsByTagName("service")
                                .item(0);

                String service =
                        serviceElement != null
                                ? serviceElement.getAttribute("name")
                                : "unknown";

                // Only care about open ports
                if (!"open".equals(state)) {
                    continue;
                }

                // Direct risky ports
                if (RISKY_PORTS.containsKey(port)) {

                    NmapFindings nmapFindings = NmapFindings.builder()
                            .port(port)
                            .protocol(protocol)
                            .service(service)
                            .severity(FindingSeverity.HIGH)
                            .finding(RISKY_PORTS.get(port))
                            .build();
                    findings.add(nmapFindings);
                }

                // X11 exposure
                if (X11_PORTS.contains(port)) {
                    NmapFindings nmapFindings = NmapFindings.x11Findings(port, protocol, service);
                    findings.add(nmapFindings);

                }
            }
            return findings;
        } catch (Exception e){
            throw new IOException();
        }
    }
}
