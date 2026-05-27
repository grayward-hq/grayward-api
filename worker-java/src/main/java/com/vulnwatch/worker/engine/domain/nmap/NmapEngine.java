package com.vulnwatch.worker.engine.domain.nmap;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;
import org.xml.sax.SAXException;

import javax.xml.parsers.DocumentBuilder;
import javax.xml.parsers.DocumentBuilderFactory;
import javax.xml.parsers.ParserConfigurationException;
import java.io.File;
import java.io.IOException;
import java.nio.file.Path;
import java.util.*;

@Component
@RequiredArgsConstructor
@Slf4j
public class NmapEngine implements Scanner {

    private final CliExecutor cliExecutor;

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

    @Value("${tools.testssl.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.testssl.binary:./testssl}")
    private String binary;

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "/temp/nmap-%s.json".formatted(job.scanId());

        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-oX", outputFileName,
                domain
        );

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            String json = cliExecutor.readAndDelete(outFile);
            List<NmapFindings> findings = extractFindings(outFile.toFile());
            return EngineResult.success(SurfaceType.PORTS, Map.of("findings", findings));
        }catch (Exception e){
            log.error("Error performing %s scan for scan_id:%s".formatted(job.scanType(), job.scanId()));
            return EngineResult.failure(SurfaceType.PORTS, e.getMessage());
        }
    }

    @Override
    public SurfaceType surfaceType() {
        return null;
    }

    private List<NmapFindings> extractFindings(File file) throws ParserConfigurationException, IOException, SAXException {

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
    }
}
