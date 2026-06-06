package com.vulnwatch.worker.engine.repository;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.*;
import java.util.stream.Stream;
import java.util.stream.StreamSupport;

/**
 * Scans npm projects via package.json.
 *
 * Registered as "package.json" so RepositoryJobProcessor can resolve it
 * when it finds that filename in the repo file tree.
 */
@Slf4j
@Component(NpmScanner.MANIFEST)
@RequiredArgsConstructor
public class NpmScanner implements ScanEngine {

    static final String MANIFEST = "package.json";
    
    private final ObjectMapper mapper;

    @Override
    public String ecosystem() {
        return "npm";
    }

    @Override
    public String manifestFilename() {
        return MANIFEST;
    }

    @Override
    public String findManifest(List<String> filePaths) {
        return filePaths.stream()
                .filter(p -> p.equals(MANIFEST) ||
                        p.endsWith("/%s".formatted(MANIFEST)))
                .min(Comparator.comparingInt(p -> (int) p.chars().filter(c -> c == '/').count()))
                .orElseThrow(() -> new IllegalStateException("%s not found in file list".formatted(MANIFEST)));
    }

    @Override
    public List<String> parseDependencies(String manifestContent) {
        try {
            JsonNode root = mapper.readTree(manifestContent);

            List<String> deps = Stream.concat(
                    extractDeps(root, "dependencies"),
                    extractDeps(root, "devDependencies")
            ).toList();

            log.debug("Parsed {} npm dependencies", deps.size());
            return deps;

        } catch (Exception e) {
            log.error("Failed to parse {}: {}", MANIFEST, e.getMessage());
            throw new IllegalArgumentException("%s parse error".formatted(MANIFEST), e);
        }
    }

    /**
     * Extracts deps from a given section as "name@version" strings.
     * e.g. { "lodash": "^4.17.21" } → "lodash@^4.17.21"
     */
    private Stream<String> extractDeps(JsonNode root, String section) {
        return Optional.of(root.path(section))
                .filter(JsonNode::isObject)
                .map(node -> StreamSupport.stream(
                        Spliterators.spliteratorUnknownSize(node.fields(), Spliterator.ORDERED),
                        false
                ))
                .orElseGet(Stream::empty)
                .map(e -> "%s@%s".formatted(e.getKey(), e.getValue().asText()));
    }
}