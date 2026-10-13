# Astra review: server deployment, database, and recovery lifecycle

Review the operational path that builds, installs, updates, validates, and rolls back the server. Do not modify production code.

## Scope

- API startup/configuration: `MProjectBackend/MProject.Api/{Program.cs,appsettings.json,appsettings.Development.json,Security/StartupCredentialGuard.cs,DesignTimeAppDbContextFactory.cs}` and relevant options/seed/migration code.
- Persistence/deploy support: Infrastructure `DBContext.cs`, migrations and storage options/implementations only as schema/deploy dependencies; backend operational scripts/readme.
- Build/install/update scripts: `build-local.ps1`, `setup-minio-service.ps1`, `scripts/{prepare-deploy,install-server,update-server,deploy-local,setup-iis-local,test-server-migration-rollback,test-database-backup-restore,test-production-support-server-artifact-smoke,test-production-support-release-db,test-production-support-query-performance,test-agent-tls}.ps1`.
- CI/package inputs: `.github/workflows/{frontend.yml,secret-scan.yml}`, frontend `package.json`, lockfile(s), API project publish guards, and server artifact layout.
- Relevant tests: startup credential guard, production watchdog configuration, migration/rollback and operational scripts/tests, API health behavior.

## Exclusions

Do not deep-review ordinary API business services, browser feature behavior, station payload deployment (`02`), Agent release switch protocol (`05`), or blob-content algorithm (`03`). Reference them only where deployment order, schema, config, or recovery depends on them.

## Context to read first

Read `AGENTS.md`, checklist DEP-001, OPS-001, SEC-002, SEC-005, `MProjectBackend/scripts/README.md`, API startup, server config, and all script headers/comments. Production uses the deployment scripts to own schema application; do not assume Development startup migration policy applies to IIS production.

## Review objectives

- Validate reproducible artifact inputs, clean build/lockfile behavior, release configuration exclusion, signed Agent public-key placement, and the server artifact's API/SPA/layout ownership.
- Validate initial install provisioning: secret handling, IIS/app pool/bindings, certificate/DNS/hosts trust path, storage/filesystem permissions, PostgreSQL connection/schema, hosted-service configuration, and strict health checks.
- Validate update preflight, migration classification/safety manifest, code/database backup, deployment ordering, configuration preservation, fault behavior, post-update validation, and exact restoration of code plus data.
- Validate recovery on destructive migration, failed schema, locked files, partial copy, failed health check, missing deployment tools, secret/config absence, certificate mismatch, and two hosts running workers against one DB.
- Prefer concrete operational improvements and regression drills over speculative platform redesign.

## Required tracing

1. `prepare-deploy.ps1` → idempotent EF schema generation/migration-safety manifest → API publish → frozen frontend build → server artifact and Agent package content.
2. `install-server.ps1` → IIS/site/app pool/bindings/certificate/permissions → schema application → API/SPA copy/config write → `Program.cs` startup guards, seeding/migration selection, hosted service configuration → health probes.
3. `update-server.ps1` → existing-install preflight → migration-safety decision → PostgreSQL dump + API archive → pool stop/offline marker → schema/code/frontend deployment → health → fault injection/rollback → backup pruning.
4. Script defaults for installation-job watchdog, storage, CORS, forwarded headers, metrics, TLS, and credential material → corresponding `Program.cs` options/guards; find default drift.
5. Deployment drill scripts/tests → the exact conditions each proves and does not prove, including database restore and server artifact smoke checks.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
