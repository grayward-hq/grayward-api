package com.vulnwatch.worker.engine.domain.subfinder.utility;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainRecord;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Objects;
import java.util.stream.Stream;

@Component
@Slf4j
public class JsonlParser {
    private final ObjectMapper mapper = new ObjectMapper();

    public List<SubdomainRecord> parse(Path path) throws IOException {
        try(Stream<String> lines = Files.lines(path)) {
            return lines
                    .filter(line -> !line.isBlank())
                    .map(this::parseRecord)
                    .filter(Objects::nonNull)
                    .toList();

        }
    }

    private SubdomainRecord parseRecord(String line){
        try{
            JsonNode node = mapper.readTree(line);
            return SubdomainRecord.builder()
                    .input(node.get("input").asText())
                    .host(node.get("host").asText())
                    .source(node.get("source").asText())
                    .build();
        } catch (Exception e){
            log.error("Error parsing line:%s".formatted(line));
            return null;
        }
    }

}
