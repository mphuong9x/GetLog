# Deployment database and schema contract

Production migrations belong to the deployment scripts. `Database:AutoMigrate=true`
is rejected outside Development. Startup validates migration history and the
model's required tables/columns before seed writes, HTTP listeners or hosted
workers start. `/health/ready` repeats that check and returns 503 on failure.

## Artifact

`prepare-deploy.ps1` builds the API and uses its offline `--deployment-contract`
command to generate both `schema.sql` and `migration-safety.json` version 2.
The manifest contains every migration, its compiled EF operation classification,
the expected head, SQL SHA-256 and migration assembly SHA-256. Verification
regenerates SQL and classifications from the binary. Editing SQL and updating its
hash by hand cannot make it a valid artifact. Version 1 manifests must be rebuilt.

`-SkipSchema` during packaging verifies the existing SQL/manifest against the
current build; it does not silently reuse stale output. Ship the API, SQL,
manifest, `deployment-contract.ps1`, `deployment-maintenance.ps1` and
`agent-release-key-contract.ps1` together. Server tooling needs the .NET 8
runtime already used by the API, not an SDK or EF CLI.

Agent release packaging, deployment preparation and server installation accept
only a structurally valid RSA `PUBLIC KEY` or `RSA PUBLIC KEY` PEM for public
distribution. Private-key markers, mixed blocks and malformed DER fail before
artifact or configuration writes. The normalized public PEM may ship in the
server artifact; the signing key remains a separate `-SignWith` input and must
never be copied into that artifact.

## Target and configuration

Install/update require matching host, `-DatabasePort` (default 5432), database,
user and credential. The connection string builder preserves semicolons, quotes,
equals signs and surrounding whitespace. The scripts compare preserved
`appsettings.json` and `appsettings.Production.json`, then use Npgsql to verify
the actual PostgreSQL database/user/port before stop, backup, schema or restore.
PostgreSQL tools receive explicit host/port/user/database arguments.

The supported deployment configuration uses a direct single PostgreSQL host,
the public schema, simple database identifiers and an IIS ApplicationPoolIdentity.
Database/environment/content-root overrides in process, user, machine, IIS pool,
inherited web.config or applicationHost configuration are rejected when they
prevent a provable target. Custom process arguments/paths, external/shared IIS
configuration, custom identities and flattened JSON keys require configuration
resolution before this workflow can run. No override values are printed.

After validation, install writes a non-secret `deployment-target.json` containing
host, port, database and user. The API compares its effective connection target
with that file before startup, including when a later environment change tries
to redirect it. Preserve this file in a recovery snapshot.

## Migration safety and rollback

Unknown EF operations and SQL expressions, including raw SQL and helper-generated
SQL, fail closed. Pending destructive/unclassified migrations require both
`-AllowDestructiveMigration` and `-MigrationReviewReason` referencing a reviewed
window. Updates additionally require backup and restore drill; neither skip flag
can bypass those requirements. Initial creation of a missing database still
requires the explicit review for historical destructive/unclassified migrations.
Use update for an existing database with those pending migrations.

Update applies schema after its recovery checks and while the local pool is
stopped. SQL is reverified and held read-only during application. The installer
retains the offline marker until configuration/schema validation finishes.
Failed archive extraction, file deletion/copy or database restore keeps the pool
stopped and offline; it never reports recovery success from a `finally` block.
A failed update without a complete recovery pair also stays in maintenance.

Deployment `-SkipSchema` is an explicit compatibility rollback: the older binary's
entire migration prefix and required physical columns must remain present.
Additional history/tables/columns are allowed; no downward migration is run.
This is a structural check, not proof that arbitrary later business changes are
semantically compatible. Normal forward deployment requires exact artifact history.

## DEP-02 maintenance and recovery admission

The v2 SQL/manifest/target contract above is unchanged. The additive offline
`maintenance` capability and `deployment-fence.json` are required by this recovery
workflow. Production startup requires the fence even when deployment metadata is
missing; Development remains usable without it. Existing binaries without the
capability/proof are rejected before backup/schema. Establishing that first
maintenance-capable baseline requires a separately reviewed all-writer outage;
the scripts do not infer that an existing database has only one writer.

Supported automatic full-DB recovery is deliberately bounded to one Windows
machine with a fixed-disk fence, participating API processes, and explicit Local
blob/Git/TUS roots for every process. `writers.json` is an administrator-reviewed
inventory of physical API paths, target identity and independent writers (which
must be empty). Every process must use the same fence; do not run an unregistered
binary, SQL writer, storage job or shared-storage producer during this window.
MinIO, SMB/network fences, reparse-point fences and independent writers are
unsupported and fail closed. In particular, a disconnected SMB lease is not
proof that a remote database writer has stopped. Multi-machine support needs a
separate fencing authority and partition/recovery evidence.

Each normal API holds a shared OS file handle from before seeding through
request/worker shutdown and disposal. Deployment serializes on `operation.lock`,
durably creates a unique maintenance window, stops the local pool and waits for
exclusive `writers.lock`. Other participating processes reject all requests and
shut down. No backup, schema operation or restore is legal before that exclusive
handle is acquired, including `-SkipBackup`. A process crash leaves the marker.
The app pool only has read/traverse rights to this control directory; update must
not recursively grant it Modify through DataRoot.

IIS may preload a validation-only host while maintenance is active. That host
does not seed or register application hosted services and admits only GET
`/health/live` and `/health/ready`; all other paths, including GET writers, return
503. It never turns into a writer in place. Admission requires strict HTTPS
live/ready checks on the Agent route and ready on the console route, with the
maintenance protocol response header. Redirects, a global TLS bypass and invalid
chain/name/expiry are rejected; certificate revocation checking is enabled.
After validation the host is stopped, the marker is removed, and IIS restarts
normally. No database rewind is allowed after that admission boundary, even if a
subsequent start fails. `-SkipHealthCheck` now means staged/offline, not admitted.

The protected recovery directory contains the complete binary/config/key archive
(including hidden files), database dump and `recovery.json`. The sidecar binds
their hashes, exact code/config tree, site/pool identity, maintenance window,
external tree fingerprints, HTTPS binding certificate/store/flags, and IIS
preload/initialization/start/idle settings. The active recovery set is not pruned
before admission. Blob/Git/TUS bytes remain frozen rather than being silently
rewound: drift or inaccessible roots prohibit full DB restore. Station payloads
and external systems are not restored by this server workflow; it issues no new
station work during maintenance and does not claim a distributed transaction.

Rollback restores archived configuration exactly; it never overlays the failed
deployment's live appsettings. Code updates retain the exact bound certificate
and its existing private key/trust state, and never import a replacement merely
because it expires later. Missing/invalid original certificate/key leaves
recovery fenced. Certificate rotation is a separate trust rollout.

An interrupted restore may be resumed with `update-server.ps1 -RecoverFrom
<protected-recovery.json>` and the same explicit installation/database parameters.
It works even after partial binary deletion, resolves target/config from the
verified archive, requires the *same still-open maintenance window*, and repeats
strict old-version validation before admission. A recovery set from another
window/site/target or one supplied after admission is rejected. Failed extraction,
restore or recovery health leaves the pool stopped and durable marker present;
never remove the marker manually to get past such a failure.

The IIS Application Initialization module is an explicit prerequisite. The
installer declares AlwaysRunning, autoStart, idleTimeout=0, one worker process,
root preload and doAppInitAfterRestart. HTTP navigation redirects to the canonical
HTTPS console; credential-bearing HTTP methods are rejected before auth/SPA.
Loopback HTTP health remains diagnostic only and cannot admit a deployment.
Local deployment health also requires an absolute HTTPS URL and normal Node TLS
chain, hostname and expiry validation; a certificate error fails the deployment.

## Validation gates

`test-deployment-contract.ps1` exercises production function bodies with synthetic
files and IIS/database stubs. `test-deployment-input-guards.ps1` uses synthetic
public DER and fake private markers to cover release-key, active-backup retention
and strict local TLS guards without creating credentials.
`test-deployment-postgres.ps1` creates a new loopback SCRAM cluster with random
credentials and runs focused backend tests; `-FullSuite` runs the backend suite.
It never accepts an operational connection string. See
[QK-03 acceptance evidence](QK03_EVIDENCE.md) for the focused commands and
rollback boundary.

Release DB, server smoke and query performance gates verify the same artifact
contract. Feature requirements use migration membership, not equality with the head.
The IIS rollback drill requires two real version-2 artifacts with different heads
(`-SourceArtifactDir` and `-NewArtifactDir`); it no longer forges SQL or a permissive
manifest. Run IIS/certificate/restore drills only in an explicitly isolated lab.

`build-deployment-lab-artifacts.ps1` compiles two real migration revisions and
generates/verifies each v2 contract using that binary; its old-source backports
are recorded explicitly and do not edit migrations. `test-deployment-iis.ps1`
(also reached through `test-server-migration-rollback.ps1`) provisions its own
PostgreSQL cluster, two uniquely named IIS API instances, certificates and ports.
It accepts no operational connection string. The default idle observation is
1260 seconds without HTTP. The release server smoke uses fixture-only trusted
loopback HTTPS and retains its existing schema/startup/SPA/auth checks.
See [DEP-02 task evidence](DEP02_EVIDENCE.md) for commands, results and limitations;
this document does not mark the project risk checklist complete.

## Changed files

Paths below are relative to the repository root.

- Backend contract/startup: `MProjectBackend/MProject.Infrastructure/DeploymentSchema.cs`,
  `MProjectBackend/MProject.Api/Infrastructure/DeploymentCommand.cs`,
  `MProjectBackend/MProject.Api/Program.cs`,
  `MProjectBackend/MProject.Infrastructure/AppDbSeeder.cs`,
  `MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs`.
- Deployment/configuration: `scripts/deployment-contract.ps1`, `scripts/prepare-deploy.ps1`,
  `scripts/install-server.ps1`, `scripts/update-server.ps1`, `scripts/setup-iis-local.ps1`.
- Existing release/recovery gates: `scripts/test-database-backup-restore.ps1`,
  `scripts/test-server-migration-rollback.ps1`, `scripts/test-production-support-release-db.ps1`,
  `scripts/test-production-support-server-artifact-smoke.ps1`,
  `scripts/test-production-support-query-performance.ps1`.
- Focused tests/fixture: `MProjectBackend/MProject.Tests/DeploymentContractTests.cs`,
  `MProjectBackend/MProject.Tests/DeploymentSchemaPostgresTests.cs`,
  `MProjectBackend/MProject.Tests/DeploymentScriptTests.cs`,
  `MProjectBackend/MProject.Tests/PostgresTestDatabase.cs`,
  `scripts/test-deployment-contract.ps1`, `scripts/test-deployment-postgres.ps1`.
- Contract documentation: `docs/code-review/DEPLOYMENT_CONTRACT.md` (this file).
