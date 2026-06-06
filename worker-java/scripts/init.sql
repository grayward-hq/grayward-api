-- =============================================================================
-- VulnWatch — local Postgres bootstrap script
-- =============================================================================
-- Purpose : creates the minimum schema the Java worker needs so you can run
--           it locally against a fresh Postgres instance without standing up
--           the .NET API first.
--
-- The .NET API uses EF Core migrations which are the source of truth for
-- production.  This script is for LOCAL WORKER TESTING ONLY.
--
-- Flow reminder:
--   1. .NET API creates a Scan row (Status = Queued).
--   2. .NET API pushes a ScanJob onto Redis  ("scan-jobs" list).
--   3. Java worker pops the job, runs scanners, then:
--        a. DomainPersistence  → INSERT INTO "Findings", UPDATE "Scans"
--        b. OWASPPersistence   → INSERT INTO "owasp_mapping",
--                                UPDATE "Scans" (OWASPScore / OWASPTier)
--        c. OWASPPersistence   → UPDATE "Scans" (OWASPPostureSummary)
--
-- Usage:
--   psql -U postgres -d vulnwatchdb -f init.sql
--
-- Or from docker-compose (mount this file as the entrypoint init):
--   volumes:
--     - ./init.sql:/docker-entrypoint-initdb.d/init.sql
-- =============================================================================

-- Enable uuid generation (needed when Postgres generates UUIDs directly)
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- =============================================================================
-- Minimal Users table (AspNetIdentity compatible stub)
-- The worker never writes here; Scans.UserId is a FK to this table.
-- =============================================================================
CREATE TABLE IF NOT EXISTS "AspNetUsers" (
                                             "Id"                   UUID        NOT NULL DEFAULT uuid_generate_v4() PRIMARY KEY,
    "UserName"             TEXT        NOT NULL,
    "NormalizedUserName"   TEXT        NOT NULL,
    "Email"                TEXT        NOT NULL,
    "NormalizedEmail"      TEXT        NOT NULL,
    "EmailConfirmed"       BOOLEAN     NOT NULL DEFAULT FALSE,
    "PasswordHash"         TEXT,
    "SecurityStamp"        TEXT,
    "ConcurrencyStamp"     TEXT,
    "PhoneNumber"          TEXT,
    "PhoneNumberConfirmed" BOOLEAN     NOT NULL DEFAULT FALSE,
    "TwoFactorEnabled"     BOOLEAN     NOT NULL DEFAULT FALSE,
    "LockoutEnd"           TIMESTAMPTZ,
    "LockoutEnabled"       BOOLEAN     NOT NULL DEFAULT FALSE,
    "AccessFailedCount"    INT         NOT NULL DEFAULT 0,
    "CreatedAt"            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt"            TIMESTAMPTZ,
    "FirstName"            TEXT,
    "LastName"             TEXT,
    "ProfilePicUrl"        TEXT
    );

-- =============================================================================
-- Domains table (stub — worker reads DomainId, updates SslCertExpiry)
-- =============================================================================
CREATE TABLE IF NOT EXISTS "Domains" (
                                         "Id"                 UUID        NOT NULL DEFAULT uuid_generate_v4() PRIMARY KEY,
    "UserId"             UUID        NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE,
    "Name"               TEXT        NOT NULL,
    "VerificationStatus" TEXT        NOT NULL DEFAULT 'Verified',
    "SslCertExpiry"      TIMESTAMPTZ,
    "CreatedAt"          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt"          TIMESTAMPTZ,
    -- Additional columns the .NET API uses (included so FK from Scans works):
    "VerificationToken"        TEXT,
    "TokenIssuedAt"            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "VerificationMethod"       TEXT        NOT NULL DEFAULT 'Dns',
    "MonitoringEnabled"        BOOLEAN     NOT NULL DEFAULT FALSE,
    "NextScheduledAt"          TIMESTAMPTZ,
    "ScanFrequency"            TEXT,
    "ScanCoverage"             INT         NOT NULL DEFAULT 0,
    "SurfaceTypes"             INT         NOT NULL DEFAULT 0
    );

-- =============================================================================
-- Scans table
-- Written by .NET API (Queued), updated by Java worker (Completed/Failed +
-- SecurityScore + OWASPScore + OWASPTier + OWASPPostureSummary).
-- =============================================================================
CREATE TABLE IF NOT EXISTS "Scans" (
                                       "Id"                  UUID        NOT NULL DEFAULT uuid_generate_v4() PRIMARY KEY,
    "UserId"              UUID        NOT NULL REFERENCES "AspNetUsers"("Id") ON DELETE RESTRICT,
    "DomainId"            UUID        REFERENCES "Domains"("Id") ON DELETE CASCADE,
    "MonitoredRepositoryId" UUID,
    "IdempotencyKey"      UUID        NOT NULL UNIQUE,
    "TargetType"          TEXT        NOT NULL DEFAULT 'Domain',
    "Coverage"            INT         NOT NULL DEFAULT 0,
    "SurfaceTypes"        INT         NOT NULL DEFAULT 0,
    "Status"              TEXT        NOT NULL DEFAULT 'Queued',
    -- Written by DomainPersistence.updateScan
    "SecurityScore"       INT,
    "CompletedAt"         TIMESTAMPTZ,
    "StartedAt"           TIMESTAMPTZ,
    -- Written by OWASPPersistence.saveMapping  (NEW columns)
    "OWASPScore"          INT,
    "OWASPTier"           TEXT,
    -- Written by OWASPPersistence.saveNarrative (NEW column)
    "OWASPPostureSummary" TEXT,
    "CreatedAt"           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt"           TIMESTAMPTZ
    );

CREATE INDEX IF NOT EXISTS "IX_Scans_DomainId"        ON "Scans"("DomainId");
CREATE INDEX IF NOT EXISTS "IX_Scans_UserId"          ON "Scans"("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Scans_IdempotencyKey" ON "Scans"("IdempotencyKey");

-- =============================================================================
-- Findings table
-- Written exclusively by Java worker (DomainPersistence.insertFindings).
-- Id is pre-assigned in Java so that owasp_mapping can reference it.
-- =============================================================================
CREATE TABLE IF NOT EXISTS "Findings" (
                                          "Id"                UUID        NOT NULL PRIMARY KEY,   -- pre-assigned by Java
                                          "ScanId"            UUID        NOT NULL REFERENCES "Scans"("Id") ON DELETE CASCADE,
    "Surface"           TEXT        NOT NULL,   -- SurfaceType.getLabel()
    "Severity"          TEXT        NOT NULL,   -- Critical | High | Medium | Low | None
    "Title"             TEXT        NOT NULL,
    "CveId"             TEXT,
    "AiExplanation"     TEXT,
    "TechnicalPayload"  TEXT,                   -- JSON string
    "RemediationSteps"  TEXT,
    "Status"            TEXT        NOT NULL DEFAULT 'Open',
    "CreatedAt"         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt"         TIMESTAMPTZ
    );

CREATE INDEX IF NOT EXISTS "IX_Findings_ScanId" ON "Findings"("ScanId");

-- =============================================================================
-- owasp_mapping table
-- Written exclusively by Java worker (OWASPPersistence.saveMapping).
-- snake_case column names must match the Java SQL exactly.
-- The ON CONFLICT clause in Java targets (scan_id, finding_id).
-- =============================================================================
CREATE TABLE IF NOT EXISTS "owasp_mapping" (
                                               "id"            UUID        NOT NULL DEFAULT uuid_generate_v4() PRIMARY KEY,
    "scan_id"       UUID        NOT NULL REFERENCES "Scans"("Id") ON DELETE CASCADE,
    "finding_id"    UUID        NOT NULL REFERENCES "Findings"("Id") ON DELETE CASCADE,
    "category_code" TEXT        NOT NULL,   -- A01 … A07
    "category_name" TEXT        NOT NULL,   -- e.g. Broken Access Control
    "status"        TEXT        NOT NULL,   -- COMPLIANT | PARTIAL | NON_COMPLIANT
    "severity"      TEXT        NOT NULL,   -- CRITICAL | HIGH | MEDIUM | LOW | NONE
    "finding_label" TEXT,
    "created_at"    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "updated_at"    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT "UQ_owasp_mapping_scan_finding" UNIQUE ("scan_id", "finding_id")
    );

CREATE INDEX IF NOT EXISTS "IX_owasp_mapping_scan_id" ON "owasp_mapping"("scan_id");

-- =============================================================================
-- Seed: one user + one domain + one scan ready for the worker to pick up.
--
-- Use these IDs when pushing a manual ScanJob onto Redis for end-to-end testing.
-- The scan is pre-created with Status = 'Queued' exactly as the .NET API does it.
-- =============================================================================
DO $$
DECLARE
v_user_id   UUID := '00000000-0000-0000-0000-000000000001';
    v_domain_id UUID := '00000000-0000-0000-0000-000000000002';
    v_scan_id   UUID := '00000000-0000-0000-0000-000000000003';
    v_idem_key  UUID := '00000000-0000-0000-0000-000000000004';
BEGIN
    -- Seed user (idempotent)
INSERT INTO "AspNetUsers"
("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
 "EmailConfirmed", "FirstName", "LastName")
VALUES
    (v_user_id, 'dev@local', 'DEV@LOCAL', 'dev@local', 'DEV@LOCAL',
     TRUE, 'Dev', 'User')
    ON CONFLICT ("Id") DO NOTHING;

-- Seed domain (idempotent)
INSERT INTO "Domains"
("Id", "UserId", "Name", "VerificationStatus")
VALUES
    (v_domain_id, v_user_id, 'example.com', 'Verified')
    ON CONFLICT ("Id") DO NOTHING;

-- Seed scan (idempotent) — worker will update this row
INSERT INTO "Scans"
("Id", "UserId", "DomainId", "IdempotencyKey",
 "TargetType", "Coverage", "SurfaceTypes", "Status")
VALUES
    (v_scan_id, v_user_id, v_domain_id, v_idem_key,
     'Domain', 1, 127, 'Queued')
    ON CONFLICT ("Id") DO NOTHING;

RAISE NOTICE 'Seed complete — ScanId: %', v_scan_id;
END;
$$;

-- =============================================================================
-- Verification queries — run these to confirm the schema is correct:
-- =============================================================================
-- SELECT column_name, data_type, is_nullable
--   FROM information_schema.columns
--  WHERE table_name = 'Scans'
--  ORDER BY ordinal_position;
--
-- SELECT column_name, data_type
--   FROM information_schema.columns
--  WHERE table_name = 'owasp_mapping';
--
-- SELECT * FROM "Scans" WHERE "Id" = '00000000-0000-0000-0000-000000000003';
-- =============================================================================