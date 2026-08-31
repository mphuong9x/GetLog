# Server migration and rollback runbook

The update-server.ps1 script owns production migrations. Application startup
must not auto-migrate outside Development.

## Normal update

Run from an elevated PowerShell prompt in the generated server artifact:

    .\update-server.ps1 -DatabasePassword '<from secret store>' -JwtKey '<from secret store>'

The script performs these gates before declaring success:

1. compares migration-safety.json with the EF migration history;
2. blocks pending destructive/contract migrations unless
   -AllowDestructiveMigration is explicitly supplied;
3. creates a PostgreSQL custom-format dump;
4. restores that dump into an isolated temporary database, compares the source
   and restored migration/sentinel/ProductionSupport fingerprints, and enforces
   the four-hour restore target;
5. applies schema and code;
6. requires strict-TLS readiness checks.

If health fails, the IIS pool remains stopped while both the exact code archive
and the database dump are restored. The old pool is started only after both
restores finish.

Do not combine -AllowDestructiveMigration with -SkipBackup or
-SkipBackupRestoreDrill; the script rejects that combination.

## Required staging drill

Before an approved destructive/contract release, clone production into an
isolated staging server and run:

    .\update-server.ps1 -DatabasePassword '<staging secret>' -JwtKey '<staging secret>' -AllowDestructiveMigration -FaultAfterSchema

The injected failure occurs after schema/code deployment. A passing drill has
all of these outcomes:

- update exits non-zero because of the intentional fault;
- database restore drill succeeded before deployment;
- automatic rollback reports both code and database restored;
- old /health/ready returns 2xx with strict TLS;
- a representative pre-migration record and the migration-history head match
  the pre-deployment snapshot.

Record the artifact version, dump filename, staging host, migration head and
health result in the release ticket. Never put database passwords, JWT keys,
dumps, or production data in this repository.

For a non-IIS database-only drill against the locally configured development
database, run scripts/test-database-backup-restore.ps1. It dumps read-only from
the configured source, restores into a uniquely named temporary database, and
compares migration, sentinel, ProductionSupportRequest, ProductionSupportLog,
ProductionSupportUpdate and ProductionSupportCreateReceipt fingerprints. It
also records restore duration against the four-hour RTO, drops the temporary
database, and deletes its temporary dump by default. The exact comparison
requires a stable source fingerprint; if relevant source rows change while
pg_dump is running, rerun the drill in a quiet window.

For a disposable end-to-end IIS/PostgreSQL drill on an elevated development or
staging Windows host, run scripts/test-server-migration-rollback.ps1. It creates
uniquely named site/app-pool/database/certificate/ports, injects a post-schema
fault, verifies exact code/database rollback and old strict-TLS readiness, then
removes only its fixture-owned resources in `finally`.

## Expand/contract policy

Prefer two releases:

1. expand: add nullable/new structures and deploy code compatible with both
   schemas;
2. backfill and verify;
3. contract: remove obsolete structures only in a separately approved window
   after the old code is no longer eligible for rollback.

migration-safety.json is generated from migration Up methods and treats drop,
rename, and alter operations as contract changes requiring the explicit gate.

## Production-support rollout and mixed versions

The production-support feature has a stricter matrix because Backend/Frontend and
Agent/Launcher can temporarily run different generations. Follow
`docs/production-support-compatibility-matrix.md` and its machine-readable fixture.

Deploy the additive Backend + Frontend server bundle first, smoke legacy Agent/Launcher
operations, then install the signed atomic Agent + Launcher bundle on the manual pilot.
The OTA manifest remains protocol v1 even though Launcher IPC is v2. Do not advertise any
support capability until preview TTL, SQLite durability/recovery, HTTP reconcile, real-tree
scan and station bundle smoke gates all pass.

The current additive schema head is
`20260830070559_AddProductionSupportDomain`. Before applying it, confirm the generated
`migration-safety.json` reports no destructive/contract operation and the idempotent
`schema.sql` creates `ProductionSupportRequests`, `ProductionSupportLogs`,
`ProductionSupportUpdates` and `ProductionSupportCreateReceipts`. Record the pre-deployment
dump/restore drill and migration head in the release ticket; implementation work does not
authorize applying this migration to production.

After support traffic exists, roll stations back first when practical and preserve the
configured Agent StateDirectory. Deploy previous server code with `-SkipSchema`; retain the
expanded schema and never execute a support Down migration. A server-first emergency is
tolerated only as a degraded state: Agent delivery pauses with
`support.server_version_unsupported`, while core IPC operations and the local queue remain
intact.

## Production-support retention and observability

`ProductionSupport:OnlineRetentionMonths` defaults to 24 and the Backend refuses to
start when configured below 24. V1 deliberately has no automatic support-data purge;
archive or deletion requires a separately approved process after the online-retention
window. The backup schedule remains the control for the 24-hour RPO: record proof that
the newest successful off-host backup is no older than 24 hours alongside the restore
drill result. A successful restore drill alone does not prove backup frequency.

Protect `/metrics` with `Metrics:ApiKey` or at the reverse proxy. The Backend exports
bounded-label metrics for create/update logical outcomes (including replay and active
dedup), support HTTP latency and status class, reroute/force-resolve counts, active
Unassigned/ContextMismatch count and oldest age, plus presence, estimated rows and total
bytes (table + indexes + TOAST) for all four support tables. Metric labels never include
notes, usernames, machine IDs or request IDs.

Before pilot, configure alerts for sustained support 5xx, missing support tables,
Unassigned/ContextMismatch age above the operations SLA, and table-byte growth above the
capacity threshold approved for the database volume. Record the `/metrics` scrape,
alert-rule version, newest-backup timestamp, exact fingerprint result and measured restore
seconds in the release ticket.
