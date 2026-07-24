package com.vulnwatch.worker.model;

/**
 * Resolved identity of a monitored repository, looked up directly from
 * Postgres using the internal GUID carried in ScanJob.repoId().
 *
 * This exists because ScanJob only carries the internal MonitoredRepository
 * row Id (a GUID) — never the GitHub-facing "owner/repo" string, branch,
 * or installation id. The worker resolves those itself rather than requiring
 * an API change.
 */
public record RepositoryMetadata(
        String id,
        String fullName,        // "owner/repo"
        String defaultBranch,
        String installationId,  // GitHub App installation id, empty if public/unlinked
        boolean isPrivate,
        String userId
) {
    public boolean requiresAuth() {
        return isPrivate && installationId != null && !installationId.isBlank();
    }
}