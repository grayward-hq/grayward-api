package com.vulnwatch.worker.persistence;

import com.vulnwatch.worker.model.RepositoryMetadata;
import lombok.extern.slf4j.Slf4j;
import org.springframework.dao.EmptyResultDataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Repository;

import java.util.Optional;
import java.util.UUID;

/**
 * Resolves GitHub-facing repository identity from the internal Postgres GUID.
 *
 * Read-only, no ORM needed — mirrors the JdbcTemplate pattern already used
 * by DomainPersistence elsewhere in the worker.
 */
@Slf4j
@Repository
public class RepositoryMetadataRepository {

    private static final String SELECT_METADATA = """
            SELECT "Id", "FullName", "DefaultBranch", "InstallationId", "IsPrivate", "UserId"
            FROM "MonitoredRepositories"
            WHERE "Id" = ?
            """;

    private final JdbcTemplate jdbc;

    public RepositoryMetadataRepository(JdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    public Optional<RepositoryMetadata> findById(String repositoryId) {
        try {
            RepositoryMetadata metadata = jdbc.queryForObject(
                    SELECT_METADATA,
                    (rs, rowNum) -> new RepositoryMetadata(
                            rs.getObject("Id", UUID.class).toString(),
                            rs.getString("FullName"),
                            rs.getString("DefaultBranch"),
                            rs.getString("InstallationId"),
                            rs.getBoolean("IsPrivate"),
                            rs.getObject("UserId", UUID.class).toString()
                    ),
                    UUID.fromString(repositoryId)
            );
            return Optional.ofNullable(metadata);
        } catch (EmptyResultDataAccessException e) {
            log.warn("No MonitoredRepository row found [repositoryId={}]", repositoryId);
            return Optional.empty();
        }
    }
}