package com.vulnwatch.worker.orchestrator.mapper;

import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.ScanJob;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.Collections;
import java.util.EnumSet;
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
 *
 * 5. Recursion guard for ScanType == "Subdomain": SUBDOMAINS is never allowed
 * to resolve for a subdomain-targeted job, in any of the three ways it could
 * otherwise slip through — explicit inclusion in SurfaceTypes, an empty/absent
 * list falling back to "run everything", or a list that becomes empty once
 * SUBDOMAINS is stripped out of it. All three fall back to
 * SUBDOMAIN_TARGET_DEFAULT_SURFACES instead of the unfiltered "run everything"
 * behavior, since "everything" for a Domain-scoped job includes SubdomainEngine.
 *
 * This is the single place both DomainScanOrchestrator.primeScanContext(...) and
 * DomainScanOrchestrator.scan(...) get their surface set from — scan() re-resolves
 * from the raw ScanJob independently of whatever a caller passed to
 * primeScanContext(), so a guard applied anywhere else would not actually reach
 * scanner selection.
 */
@Slf4j
@Component
public class SurfaceTypeMapper {

    private static final Set<SurfaceType> SUBDOMAIN_TARGET_DEFAULT_SURFACES = Collections.unmodifiableSet(
            EnumSet.of(SurfaceType.DNS, SurfaceType.SSL, SurfaceType.PORTS, SurfaceType.HTTP_HEADERS));

    public Set<SurfaceType> resolve(ScanJob job) {
        List<String> raw = job.surfaceTypes();
        boolean isSubdomainTarget = "Subdomain".equalsIgnoreCase(job.scanType());

        if (raw == null || raw.isEmpty()) {
            if (isSubdomainTarget) {
                log.debug("No surface selection on Subdomain-targeted job [scanId={}] — defaulting to {}",
                        job.scanId(), SUBDOMAIN_TARGET_DEFAULT_SURFACES);
                return SUBDOMAIN_TARGET_DEFAULT_SURFACES;
            }
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
            if (isSubdomainTarget) {
                log.warn("None of the requested surfaces {} could be parsed on a Subdomain-targeted job [scanId={}] — defaulting to {}",
                        raw, job.scanId(), SUBDOMAIN_TARGET_DEFAULT_SURFACES);
                return SUBDOMAIN_TARGET_DEFAULT_SURFACES;
            }
            log.warn("None of the requested surfaces {} could be parsed [scanId={}] — returning empty set for orchestrator fallback",
                    raw, job.scanId());
            return Set.of();
        }

        if (isSubdomainTarget) {
            // Restrict to exactly the surfaces valid for a subdomain target — not just
            // "remove SUBDOMAINS". This matters because DomainScanOrchestrator.selectScanners
            // has a SECOND fallback-to-everything path: if the resolved set is non-empty but
            // matches zero registered scanners (e.g. a stray "Dependency" or "Secrets" value,
            // which have no domain-side Scanner), it silently runs every scanner — including
            // SubdomainEngine — rather than running nothing. Filtering down to the known-valid
            // subdomain surfaces up front means that path can never be reached from here: the
            // result is always empty (→ safe default below) or a set that matches at least one
            // real scanner.
            Set<SurfaceType> restricted = resolved.stream()
                    .filter(SUBDOMAIN_TARGET_DEFAULT_SURFACES::contains)
                    .collect(Collectors.toCollection(LinkedHashSet::new));

            if (resolved.contains(SurfaceType.SUBDOMAINS) || !restricted.equals(resolved)) {
                log.warn("Restricting surfaces for a Subdomain-targeted job [scanId={} requested={} allowed={}] — "
                                + "recursive subdomain scanning and non-domain surfaces are not allowed",
                        job.scanId(), resolved, restricted);
            }

            if (restricted.isEmpty()) {
                // Nothing requested was valid for a subdomain target. Returning empty here
                // would hit the orchestrator's "empty == run everything" (or "no match == run
                // everything") rules and reintroduce SubdomainEngine right back in — fall back
                // to the safe default set instead.
                return SUBDOMAIN_TARGET_DEFAULT_SURFACES;
            }

            return restricted;
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