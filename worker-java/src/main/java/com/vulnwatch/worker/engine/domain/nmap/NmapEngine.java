package com.vulnwatch.worker.engine.domain.nmap;

import com.vulnwatch.worker.CliExecutor;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.engine.domain.nmap.models.NmapFindings;
import com.vulnwatch.worker.engine.parsers.NmapParser;
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
    private final NmapParser nmapParser;

    @Value("${tools.nmap.timeout-seconds:150}")
    private int timeoutSeconds;

    @Value("${tools.nmap.binary:nmap}")
    private String binary;

    @Value("${tools.temp:/Users/mitchelntuen/temp}")
    private String tempLocation;
    
    private static final String TARGET_PORTS = "22,25,445,3306,5432,6001-6003,8080";

    @Override
    public EngineResult scan(ScanJob job) {
        String domain = job.domainName();
        String outputFileName = "%s/%s-%s.json".formatted(tempLocation,binary,job.scanId());
        Path outFile = Path.of(outputFileName);

        List<String> command = List.of(
                binary,
                "-oX", outputFileName,
                "-p", TARGET_PORTS,
                "-T4",
                "-n",
                domain
        );

        try {
            cliExecutor.run(command, timeoutSeconds, false);
            List<NmapFindings> findings = nmapParser.parse(outFile.toFile());
            return EngineResult.success(SurfaceType.PORTS, Map.of("findings", findings));
        }catch (Exception e){
            log.error("Error performing %s scan for scan_id:%s, %s".formatted(job.scanType(), job.scanId(), e.getMessage()));
            throw new RuntimeException(e.getCause());
        }finally {
            cliExecutor.deleteSilently(outFile);
        }
    }

    @Override
    public SurfaceType surfaceType() {
        return SurfaceType.PORTS;
    }

}