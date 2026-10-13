# CNT-01 acceptance evidence

Date: 2026-09-14. Scope: **SYN-08 and SYN-10 only**.
Starting repository HEAD: `8c490ba1c36dc4bcc4e7b991b1649cb727d9fe8b`.
Implementation is an uncommitted working-tree change; no deployment was performed.

## Owner decision and review inputs

The historical retention rule was not settled in the existing review material.
The owner explicitly answered: **"Approve retention while any reference exists"**
to the proposal to retain current and historical references, including soft-deleted
records, until a separately authorized purge removes those references.

This implementation retains bytes while **any persisted, typed reference** below
remains, irrespective of visibility, age, status, or cached reference count.
GC does not purge history, rendered-cache rows, or reservation rows. There is no
new purge schedule or implicit retention deadline. A future purge needs separate
authorization; expiration/soft deletion alone is not such a purge.

Read before production-code edits: root `AGENTS.md`, CNT-01 in
[IMPLEMENTATION_TASKS.md](IMPLEMENTATION_TASKS.md), all of
[report 03](reports/03-software-content-upload-and-blob-lifecycle-report.md),
the canonical [SYN-08](reports/11-final-synthesis-report.md#syn-08) and
[SYN-10](reports/11-final-synthesis-report.md#syn-10) entries in report 11, and
existing LocalStorage, software/blob GC/reference, reservation/cleanup, TUS,
render, document, release, and PostgreSQL lifecycle tests. Also consulted
`docs/PROJECT_REVIEW_CHECKLIST.md` (UPL-001/002, STO-002). The referenced
frontend PROJECT_MEMORY/checklist files are absent; no PROJECT_MEMORY.md was
found in this checkout. The shared install-token owner exception is preserved.

## Reference and call-path inventory

| Durable reference | Included states / behavior |
| --- | --- |
| `SoftwareFiles.BlobSha256` | Current, replaced, cloned, Draft/Released, soft-deleted files and files behind soft-deleted versions/packages. Real RESTRICT FK. |
| `OverrideFiles.BlobSha256` | Draft, PendingApproval, Active, migration history and soft-deleted overrides. Real RESTRICT FK. |
| `AgentReleases.BlobSha256` | Active/inactive and soft-deleted release archives. Real RESTRICT FK. |
| `UserDocuments.BlobSha256` | Private/shared documents and soft-deleted rows. Real RESTRICT FK. Shares retain bytes through their document. |
| `RenderedConfigBlobs.RenderedSha256` | Every retained cache/history row, even stale or never recently read. Real RESTRICT FK. |
| `RenderedConfigBlobs.BaseSha256` | Every retained base SHA; scalar reference without a FK. Render creation takes the SHA transaction lock and requires a surviving base row. |
| `UploadReservations.Sha256 / StoragePath` | Pending, Uploaded, Consumed and Expired, including retry/session and PurgedAt metadata. No FK. Every retained row protects its SHA and path. Reservation creation/publication/cleanup share the SHA transaction lock. |
| Other `Blobs.StoragePath` aliases | A different Blob row naming the same storage key prevents physical deletion. |
| Jobs, assignments, computers, approvals, download capabilities | Byte access resolves the version/file, override, release or rendered-cache rows above. Those rows remain references even after soft deletion. |
| Audit JSON and descriptive SHA strings | Descriptive snapshots, not independent byte references: no byte dereference/restoration path using only those strings was found. Admin file restoration uses the retained SoftwareFile row. |

Inventory checked against domain entities, `DBContext.cs`, its model snapshot,
all Blob/reference writes, software clone/delete/restore, override migration,
approval and download-authorizer paths. `ReferenceCount` remains a convenience
counter and is never proof that storage can be deleted. Query filters are
explicitly bypassed for retention checks.

Publication paths are software TUS/finalization (including empty files and known
SHA recovery), document TUS/finalization, rendered configuration, rewritten
defaults, and agent-release archives. Both existing providers implement
`IStorageService`; no new storage abstraction, namespace migration or schema
migration was introduced.

## Defect reproduction before the corresponding fix

| Command (repository root unless stated) | Observed result |
| --- | --- |
| `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore --filter FullyQualifiedName~LocalCasPublicationTests -v minimal` | 6 failed / 1 passed. Direct FileMode.Create exposed/truncated the canonical file during interrupted, canceled, invalid or concurrent writes. Log: `.tmp/cnt01-local-before.log`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-postgres.ps1 -Filter FullyQualifiedName~BlobLifecyclePostgresTests` | Historical SoftwareFile FK reproduced PostgreSQL 23001 after physical object deletion. 1 failed. Log: `.tmp/cnt01-postgres-before.log`. |
| Set `MPROJECT_TEST_MINIO_EXE` as below; `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore --filter FullyQualifiedName~MinioCasPublicationTests -v minimal` | Against the original MinIO adapter: 3 failed / 6 passed, including same-size incorrect content and issuing a mutable canonical PUT URL. Real disposable MinIO, not an emulated store. Log: `.tmp/cnt01-minio-before.log`. |

The final tests extend these initial reproductions. Report 03's known-SHA
size-only finalization defect is also covered with **real MinIO/filesystem and
PostgreSQL**: fixture-only legacy corruption is rejected even with
`VerifyUploadHash=false`; its existing bytes/references are preserved. After
explicit fixture removal of the conflicting object, re-upload/finalization
recovers the expected SHA and a fresh storage reader receives the correct bytes.

## Changed contracts and ordering

- CAS keys `blobs/{first2}/{remaining62}` and
  `agent-releases/{sha}.zip` are immutable publication destinations.
  Both adapters copy to a private file, check complete source length when known,
  flush it, and hash the staged bytes before any commit. Invalid CAS keys fail.
  Other key namespaces retain their existing UploadAsync behavior.
- LocalStorage closes verified staging in the destination directory and commits
  with no-overwrite File.Move. A losing writer hashes the existing winner before
  treating the retry as success. Filesystem path aliases cannot bypass CAS
  validation. Read handles permit deletion/rename sharing so concurrent readers
  do not turn an otherwise valid retry into a Windows sharing violation.
- MinIO uses atomic **single PUT**, signed `If-None-Match: *` and
  `x-amz-checksum-sha256`. A private staged file is streamed to MinIO; the server
  rejects incomplete/checksum-invalid requests before commit. Success or a
  concurrent 412 winner is followed by a full GET/SHA and size check. No failed
  verification deletes or overwrites a canonical object.
- The internal conditional URL is never returned to clients. Software init now
  returns the existing transport field with value `"tus"`; the frontend routes
  it through the existing authenticated TUS uploader. Legacy local-path markers
  remain understood. CAS presigned client PUT issuance is refused by MinIO.
  TUS must match the reservation's exact stored key, including recovery of a SHA
  originally stored in the agent-release namespace.
- Known and new SHA finalization always verifies bytes. The legacy
  `Storage:VerifyUploadHash` setting is retained for configuration compatibility
  but cannot disable CAS verification. Publication and FK creation run under the
  existing PostgreSQL SHA transaction lock; multiple SHA locks are sorted.
- GC retains the existing session-level sweep lock, commits a durable deletion
  claim, then obtains the SHA lock and Blob row FOR UPDATE in a fresh transaction.
  It rechecks all references and runs the relational DELETE **uncommitted** before
  touching storage. Thus existing RESTRICT FKs, including a fixture-only unknown
  FK, must permit deletion, and FK insertions remain excluded through physical
  deletion and commit. Failures before commit leave the durable claim for retry.
  An ambiguous commit acknowledgement is resolved from durable row state on retry.
  Candidate and error-recording failures are isolated; other candidates continue.
- Scalar reference writers participate in the same lock. Cached render reads
  refresh read grace; publication checks both base and output. GC winning after
  a base read causes reference creation to fail rather than retain a missing base.
  Existing current/soft-deleted rows keep ordinary downloads safe from GC.
- Old claims with surviving history are released under the sweep/SHA lock without
  deleting that history. Missing bytes and lost delete/upload responses can be
  retried. An Uploaded reservation whose object is missing becomes Pending for
  re-upload. Wrong existing canonical bytes are refused, not automatically repaired.

The SDK 7 automatic multipart route drops the conditional header at multipart
completion, so using it above its 5 MiB threshold would weaken the invariant.
The implementation uses MinIO's single-PUT contract instead. Sources:
[SDK 7 ObjectOperations](https://github.com/minio/minio-dotnet/blob/7.0.0/Minio/ApiEndpoints/ObjectOperations.cs),
[MinIO limits](https://github.com/minio/minio/blob/RELEASE.2025-09-07T16-13-09Z/docs/minio-limits.md).
This is scoped to the existing MinIO adapter, not a claim about arbitrary S3 servers.

## Real fixtures and final acceptance commands

Windows x64, PowerShell 5.1.26100.9444, .NET SDK 10.0.400 targeting net8.0,
PostgreSQL 18.1. Each PostgreSQL run uses the existing script to initialize a
new random-port loopback cluster with generated credentials, migrates only its
disposable databases, and cleans them afterward. Each MinIO case starts its own
loopback server and random bucket with disposable data and synthetic credentials.
Local tests use real temporary directories and FileStreams.

The community Windows binary URL returned HTTP 410, so MinIO was built from the
official release source `RELEASE.2025-09-07T16-13-09Z`, using Go 1.24.13.
The resulting binary reports `DEVELOPMENT.GOGET` because release ldflags were
not supplied; it is not represented as an official prebuilt release binary.

- Source: `https://codeload.github.com/minio/minio/zip/refs/tags/RELEASE.2025-09-07T16-13-09Z`
  SHA-256 `91ff05ab9a16274ae07b364eb0f4e27a1bc904d4f45d7386d9910cbe8635875d`.
- Go: `https://dl.google.com/go/go1.24.13.windows-amd64.zip`
  SHA-256 `40b16bc8f00540a2cb02dff4de72b73e966fdd8d65f95e33d8e4080b48a2459a`.
- Build working directory:
  `C:\dev\MProject\.tmp\cnt01-tools\build-source\minio-RELEASE.2025-09-07T16-13-09Z`.
  Command: `C:\dev\MProject\.tmp\cnt01-tools\go-fast\go\bin\go.exe build -o C:\dev\MProject\.tmp\cnt01-tools\minio.exe .`.
  Environment: `CGO_ENABLED=0`, `GOTOOLCHAIN=local`,
  `GOCACHE=C:\dev\MProject\.tmp\cnt01-tools\gocache`,
  `GOPATH=C:\dev\MProject\.tmp\cnt01-tools\gopath`.
- Binary SHA-256:
  `07dde76d133f601d57d2811df799da2aaed565f8f2aec6be95f20596cd7af2de`.
  Public-source build caches and run logs remain under ignored `.tmp`; binaries
  and credentials are not committed.

Run these from `C:\dev\MProject`:

```powershell
$env:MPROJECT_TEST_MINIO_EXE='C:\dev\MProject\.tmp\cnt01-tools\minio.exe'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-postgres.ps1 -Filter 'FullyQualifiedName~BlobLifecyclePostgresTests|FullyQualifiedName~PostgresConcurrencyIntegrationTests|FullyQualifiedName~CasPublicationTests|FullyQualifiedName~MinioPublicationWireTests'
```

Final outcome: **61 passed, 0 failed, 0 skipped**, exit 0 (4 m 54 s).
Includes 30 provider publication/wire cases, all 15 new relational acceptance
tests and 16 existing concurrency tests. The stream barrier stops after an actual
8 KiB partial copy, including cancellation and 20 rounds of eight LocalStorage
writers. Log: `.tmp/cnt01-combined-acceptance.log`.

```powershell
$env:MPROJECT_TEST_POSTGRES_CS=$null
$env:MPROJECT_AUDIT_POSTGRES_CS=$null
$env:MPROJECT_TEST_MINIO_EXE='C:\dev\MProject\.tmp\cnt01-tools\minio.exe'
dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore -v minimal
```

Final outcome: **1211 passed, 0 failed, 15 skipped**, exit 0 (4 m 14 s).
All 15 skipped cases require PostgreSQL and passed in the separate command above.
This run includes all 30 LocalStorage/MinIO publication and wire-fault cases,
including the 20-round LocalStorage concurrency stress test.
Log: `.tmp/cnt01-backend-acceptance.log`.
External database variables are cleared; the PostgreSQL acceptance above is
required separately. New provider/relational tests explicitly skip when their
fixture is absent; a skipped provider suite is not an acceptance pass.
Some pre-existing PostgreSQL tests return early without the fixture; their real
relational checks passed in the separate PostgreSQL run as well.

From `MProjectFrontend`:

```powershell
node node_modules/vitest/vitest.mjs run src/api/software-upload.test.ts --pool=threads --maxWorkers=1 --reporter=dot
```

Outcome: **7 passed, 0 failed**, exit 0.
Log: `.tmp/cnt01-frontend.log`. Covers the new `tus` marker, stored-content reuse,
reservation finalization and existing retry/resume transport.

Intermediate outcomes, retained for review:

- Original focused service run: 10 failed / 82 passed; expanded run: 5 failed /
  209 passed. Old retention assumptions and fake-storage setup were updated.
- Provider run after initial fixes: MinIO 9/9 passed; HTTP wire + Local run
  10 passed / 1 failed exposed Windows read/rename sharing contention. Read
  sharing was corrected; final Local stress runs 20 rounds of eight writers.
- A combined provider run had 3 failures / 17 passes because MinIO liveness
  preceded IAM/metadata readiness. Fixture now waits for cluster readiness.
  Rerun: **28 passed / 0 failed** (`.tmp/cnt01-providers-ready.log`).
- PostgreSQL incremental runs: 7/7, then 11/11; existing tests with old GC
  expectations failed (expired retention and render rejection), then
  **29/29 passed** after adopting the approved policy/blocking contract
  (`.tmp/cnt01-postgres-final2.log`). One intermediate test-barrier typo caused
  compilation failure and was corrected before rerunning. With the two added
  known-SHA repair cases, the same two-class relational filter passed **31/31**
  (`.tmp/cnt01-postgres-acceptance.log`, 4 m 38 s) before the final combined gate.
- Sandbox full suite: **1188 passed, 4 failed, 15 skipped**; all four failures
  were LibGit2Sharp access denial on generated temporary repositories.
  `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore --filter FullyQualifiedName~GitRepositoryServiceTests -v minimal`
  outside that sandbox: **13/13 passed**, exit 0 (`.tmp/cnt01-git-fixture.log`).
- First frontend command without `--pool=threads --maxWorkers=1` timed out
  starting its forks worker; no tests ran. The thread-pool command above passed.
- Disposable PostgreSQL initdb required elevation after Windows restricted-token
  error 87. A prior tool auto-review usage-limit rejection interrupted execution;
  work resumed after the owner said to continue. No production fallback was used.
- `git diff --check`: exit 0; line-ending warnings only. New-file trailing
  whitespace and unresolved-placeholder checks passed.
- The interrupted old regression run left a stopped fixture directory. After
  confirming its exact workspace path and `pg_ctl -D <fixture>/data status`
  reported no server running (exit 3), native PowerShell
  `Remove-Item -LiteralPath 'C:\dev\MProject\.tmp\dep01-postgres-95c87023227d47edb6b2d9e186a724d3' -Recurse -Force`
  removed only that generated fixture, exit 0. Completed runs cleaned their own
  clusters. No external cluster was inspected or stopped.

Provider atomicity uses real filesystem rename or actual MinIO requests.
Wire fault injection forwards to real MinIO and disconnects/corrupts transport;
it does not emulate object-store commits. Relational tests use real PostgreSQL
FKs/transactions and SQL barriers, inspect lock waits, and use independent
contexts. Existing mocked service tests remain regression checks, not proof of
provider atomicity or relational concurrency.

## Acceptance coverage and limits

Covered independently on LocalStorage and MinIO: interruption, cancellation,
incomplete length, wrong SHA, same-size known-SHA repair, idempotent retry,
concurrent/mixed writers, both CAS namespaces and fresh GET/hash after recovery.
MinIO additionally covers 6 MiB payloads crossing the SDK multipart threshold,
transport corruption/interruption, lost successful PUT response, and replay of
the captured signed conditional request after finalization.

PostgreSQL coverage includes all ten reference categories/states in the matrix;
both orders of FK/GC, committed and uncommitted history purge; publication and
finalization by two reservations; render/GC in both orders, base-GC after read,
missing cached output recovery, historical tombstone re-upload; unknown-FK poison
candidate isolation; real Windows sharing-denied delete; and real MinIO failed
DELETE/lost DELETE response with durable retry and an independent successful
candidate.

Limits: single-node disposable MinIO on Windows; no distributed MinIO, remote
filesystem, crash/power-loss durability, IIS/proxy rollout or Agent install canary
was exercised. File/HTTP interruption and lost responses are tested, not a whole
machine power loss. Largest tested payload is 6 MiB plus a suffix; configured
10 GiB uploads and disk/throughput capacity were not benchmarked. MinIO now needs
local spool space in addition to TUS temporary storage and performs full checksum
reads. Readiness/throughput and certificate settings need the target environment's
normal deployment validation. The sandbox denied a volume-type query; these tests
make no cross-filesystem atomicity claim.

## Rollout and rollback instructions

1. Before deploying, stop old API instances from issuing canonical MinIO PUT URLs
   and publishing with the old adapter. Drain existing uploads and wait out every
   previously issued raw PUT URL (source software TTL is 30 minutes; use the actual
   maximum issued TTL). Alternatively use a separately authorized credential
   revocation plan. Old URLs cannot be made immutable by this code change. Do not
   run old and new publishers concurrently while claiming the new invariant.
2. Deploy API and frontend together. Keep the current namespaces/schema. Verify
   local staging/spool capacity and exercise a disposable upload/download through
   the target reverse proxy. Retention increases storage lifetime; a later history
   purge requires a separate owner decision and rollout.
3. For code rollback, stop publication and set `BlobGc:Enabled=false` before
   reverting API/frontend changes together. Old GC is unsafe for historical refs;
   do not re-enable it merely because the old binary starts. There is no schema
   down-migration for CNT-01. Reverting tests/docs alone does not change runtime.
4. Code/DB rollback cannot restore deleted or corrupt bytes. Any real repair needs
   separately authorized inspection, a paired database/object backup or verified
   source, and SHA verification. Never delete historical references to unblock
   re-upload. Conflicting canonical bytes require an explicit repair decision;
   this implementation refuses them.
5. A process crash may leave private LocalStorage staging files. They are not
   finalized objects; any production cleanup must be scoped and separately
   authorized. This change does not scan, repair or purge production storage.

**No production connection, data inspection, purge, repair, migration or deployment
was performed.** All destructive test operations target fixture-owned directories,
objects and PostgreSQL databases only.
