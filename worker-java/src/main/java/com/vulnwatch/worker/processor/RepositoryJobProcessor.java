package com.vulnwatch.worker.processor;

import com.vulnwatch.worker.ai.repository.SpringAiRepositoryEnricher;
import com.vulnwatch.worker.engine.repository.ScanEngine;
import com.vulnwatch.worker.persistence.RepositoryPersistence;
import com.vulnwatch.worker.service.GithubService;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.model.RepositoryIntel;
import com.vulnwatch.worker.model.DependencyFinding;
import com.vulnwatch.worker.publisher.RepositoryIntelPublisher;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Handles jobs where ScanJob.scanType() == "Repository".
 *
 * Pipeline:
 *   1. Fetch the repo file tree from GitHub
 *   2. Find ALL supported manifests (not just the first)
 *   3. For each manifest: parse → AI-enrich
 *   4. Merge all ecosystem results into one DependencyScanResult
 *   5. Persist to DB
 *   6. Publish notification event to Redis for .NET API
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class RepositoryJobProcessor implements JobProcessor {

    private final GithubService gitHubService;
    private final Map<String, ScanEngine> scanners;   // keyed by manifest filename
    private final SpringAiRepositoryEnricher aiEnrichmentService;
    private final RepositoryPersistence repo;
    private final RepositoryIntelPublisher redisPublisher;

    @Override
    public void process(ScanJob job) {
        log.info("[{}] Starting repository scan for repoId={}", job.scanId(), job.repoId());

        try {

            List<String> filePaths = gitHubService.getFilePaths(job.repoId());
            log.debug("[{}] Found {} files in repo", job.scanId(), filePaths.size());

            //  Find ALL matching scanners
            // LinkedHashMap preserves insertion order (root manifests found first)
            Map<ScanEngine, String> matched = resolveAllScanners(filePaths);

            if (matched.isEmpty()) {
                log.warn("[{}] No supported manifests found. Registered types: {}",
                        job.scanId(), scanners.keySet());
                redisPublisher.publishFailure(job, "No supported manifest found");
                return;
            }

            log.info("[{}] Found {} ecosystem(s): {}", job.scanId(), matched.size(),
                    matched.keySet().stream()
                            .map(ScanEngine::manifestFilename)
                            .toList());

            // Parse + enrich per ecosystem
            // Keyed by ecosystem name ("npm", "maven", etc.) for the merged result
            Map<String, List<DependencyFinding>> enrichedByEcosystem = new LinkedHashMap<>();

            for (Map.Entry<ScanEngine, String> entry : matched.entrySet()) {
                ScanEngine scanner = entry.getKey();
                String manifestPath = entry.getValue();

                log.info("[{}] Processing {} manifest at {}", job.scanId(),
                        scanner.manifestFilename(), manifestPath);

                try {
                    String manifestContent = gitHubService.getFileContent(job.repoId(), manifestPath);
                    List<String> dependencies = scanner.parseDependencies(manifestContent);

                    log.info("[{}] {} → {} dependencies", job.scanId(),
                            scanner.manifestFilename(), dependencies.size());

                    if (dependencies.isEmpty()) {
                        log.warn("[{}] {} manifest found but no dependencies parsed — skipping",
                                job.scanId(), scanner.manifestFilename());
                        continue;
                    }

                    List<DependencyFinding> enriched = aiEnrichmentService.enrich(dependencies, job);
                    enrichedByEcosystem.put(scanner.ecosystem(), enriched);

                    log.info("[{}] {} enrichment done. Vulnerabilities: {}", job.scanId(),
                            scanner.ecosystem(),
                            enriched.stream().filter(DependencyFinding::hasVulnerabilities).count());

                } catch (Exception e) {
                    // One failed ecosystem should not kill the entire scan
                    log.error("[{}] Failed to process {} manifest: {}", job.scanId(),
                            scanner.manifestFilename(), e.getMessage(), e);
                }
            }

            if (enrichedByEcosystem.isEmpty()) {
                redisPublisher.publishFailure(job, "All manifest processing failed");
                return;
            }

            // Merge and persist
            RepositoryIntel result = RepositoryIntel.of(job, enrichedByEcosystem);
            repo.save(result);
            log.info("[{}] Scan results persisted. Ecosystems: {} | Total deps: {} | Vulnerable: {}",
                    job.scanId(), enrichedByEcosystem.keySet(),
                    result.totalDependencies(), result.vulnerableCount());

            // Notify .NET API via Redis
            redisPublisher.publishSuccess(job, result);
            log.info("[{}] Notification event pushed to Redis", job.scanId());

        } catch (Exception e) {
            log.error("[{}] Repository scan failed: {}", job.scanId(), e.getMessage(), e);
            redisPublisher.publishFailure(job, e.getMessage());
        }
    }

    /**
     * Walks ALL file paths and collects every scanner whose manifest is present.
     * Returns a map of scanner → manifest path, preserving root-first order.
     *
     * e.g. a monorepo with both package.json and pom.xml returns both.
     *
     * Shallowest (root-most) manifest wins per filename — if a deeper path is
     * encountered for an already-matched filename, it is replaced only if the
     * new path is shallower.
     */
    private Map<ScanEngine, String> resolveAllScanners(List<String> filePaths) {
        Map<String, ScanEngine> matchedByFilename = new LinkedHashMap<>();
        Map<ScanEngine, String> result = new LinkedHashMap<>();

        // Sort ascending by path depth so root-level manifests are processed first
        List<String> sorted = filePaths.stream()
                .sorted((a, b) -> depth(a) - depth(b))
                .toList();

        for (String path : sorted) {
            String filename = path.contains("/")
                    ? path.substring(path.lastIndexOf('/') + 1)
                    : path;

            ScanEngine scanner = scanners.get(filename);
            if (scanner == null) continue;

            if (!matchedByFilename.containsKey(filename)) {
                matchedByFilename.put(filename, scanner);
                result.put(scanner, path);
            } else {
                // Replace if this path is shallower than the currently stored one
                String existing = result.get(matchedByFilename.get(filename));
                if (depth(path) < depth(existing)) {
                    ScanEngine previous = matchedByFilename.get(filename);
                    result.remove(previous);
                    matchedByFilename.put(filename, scanner);
                    result.put(scanner, path);
                }
            }
        }

        return result;
    }

    /** Returns the number of path segments (depth) for a given path string. */
    private static int depth(String path) {
        return (int) path.chars().filter(c -> c == '/').count();
    }
}