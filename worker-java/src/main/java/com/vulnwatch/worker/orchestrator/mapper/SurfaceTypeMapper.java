package com.vulnwatch.worker.orchestrator.mapper;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.ScanJob;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.LinkedHashSet;
import java.util.List;
import java.util.Objects;
import java.util.Set;
import java.util.stream.Collectors;

/**
 * Resolves which SurfaceType values a ScanJob actually wants run.
 *
 * Deliberately defensive:
 * 1. Processes the "SurfaceTypes" list parameter.
 * 2. Each individual value is matched two ways — first against the
 * Java enum constant name (fromString: "DNS"), then against the
 * label ("fromLabel": "Dns") to accommodate changing casing conventions.
 * 3. Unrecognized entries are logged and skipped rather than failing
 * the whole job.
 * 4. If nothing could be resolved at all (empty list, null list, or only
 * blank/unparseable values), returns an EMPTY set. The orchestrator
 * treats an empty set as "run every registered scanner" so a misconfigured
 * job never silently does nothing.
 */
@Slf4j
@Component
public class SurfaceTypeMapper {

    public Set<SurfaceType> resolve(ScanJob job) {
        List<String> raw = job.surfaceTypes();

        if (raw == null || raw.isEmpty()) {
            log.debug("No surface selection present on job [scanId={}] — returning empty set for orchestrator fallback", job.scanId());
            return Set.of();
        }

        Set<SurfaceType> resolved = raw.stream()
                .filter(Objects::nonNull)
                .map(String::trim)
                .filter(value -> !value.isEmpty())
                .map(value -> {
                    SurfaceType type = tryParse(value);
                    if (type == null) {
                        log.warn("Unrecognized surface value '{}' in job [scanId={}] — skipping it", value, job.scanId());
                    }
                    return type;
                })
                .filter(Objects::nonNull)
                .collect(Collectors.toCollection(LinkedHashSet::new));

        if (resolved.isEmpty()) {
            log.warn("None of the requested surfaces {} could be parsed [scanId={}] — returning empty set for orchestrator fallback",
                    raw, job.scanId());
            return Set.of();
        }

        return resolved;
    }

    private SurfaceType tryParse(String value) {
        try {
            return SurfaceType.fromString(value);
        } catch (Exception ignored) {
            // Fall through to label-based lookup
        }
        try {
            return SurfaceType.fromLabel(value);
        } catch (Exception ignored) {
            return null;
        }
    }
}