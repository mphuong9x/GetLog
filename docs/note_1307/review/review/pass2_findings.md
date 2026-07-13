# Backend Deep Review — Pass 2: Software Deployment Domain

**Date:** 2026-07-13
**Scope:** `MProject.Application/Services/Software/` (14 files) + `MProject.Api/Controllers/Software/` (PcInstallationsController, ConfigBaselinesController, OverrideFilesController).
**Context read (not in scope):** `AgentController`, `AgentAuthenticationHandler`, `AuthorizationService`/`ScopeResolver`/`RbacGrantQueryService`, `DBContext` (global query filters, indexes), `BlobReferenceExtensions`, `DbContextTransactionExtensions`, `StationSoftwareAssignmentService`, storage services, and all related tests in `MProject.Tests`.

> Note: `docs/skills/review_rule.md` does not exist in the repository. This report follows the
> output format and severity scale given in the review request (CRITICAL / HIGH / MEDIUM; LOW omitted).

---

## Summary

The job state machine (enqueue → poll → ack/progress → complete), the watchdogs, and the BlobGc
sweep are in good shape: race conditions on job creation are handled with a filtered unique index
plus a Postgres-specific retry, BlobGc is TOCTOU-safe via a conditional `ExecuteDelete` and an
advisory lock, and the deliberate `IgnoreQueryFilters` usage for Uninstall jobs of soft-deleted
versions is correct and well-tested — **it must be preserved**.

12 findings survived verification: **2 HIGH** and **10 MEDIUM**, no CRITICAL. The two HIGH items
are both "the deliberate soft-delete design leaks at one seam": the admin job listing re-applies
the global `!IsDeleted` filters that the rest of the pipeline intentionally bypasses (jobs vanish
from the UI), and the baseline validator silently ignores its `stationResourceId` parameter, so
config-baseline validation never accounts for override files even though both callers pass the
station explicitly. Everything else is consistency-level: a swallowed storage exception, a
watchdog last-write-wins race, a paging/count mismatch, size-cap and authz asymmetries in the
override stack, and one N+1.

Every entity implementing `ISoftDeletable` gets an automatic `!IsDeleted` global filter
(DBContext.cs:804-811). Relevant to this review: `SoftwareVersion`, `SoftwareFile`, `OverrideFile`,
`Computer`, `StationSoftwareAssignment`, `ConfigBaseline(Rule)` **are** filtered;
`InstallationJob`, `PcInstallationRecord`, `Agent`, `Blob` are **not**. Several findings below are
direct consequences of navigations to filtered entities inside projections.

---

## Good Decisions (keep these)

1. **Deliberate `IgnoreQueryFilters` for the uninstall-of-deleted-version flow** —
   `InstallationJobService.GetActiveJobsAsync` (:75), `PollAsync` (:125, :141, :155, :167),
   `ResolveManifestAsync` (:304), `GetOwnedJobAsync` (:936), and
   `PcInstallationService.GetInstallationsByComputerAsync` (:166) / `GetOutdatedPcsAsync` (:227).
   Without it, `Include(x => x.SoftwareVersion)` would return null navs for soft-deleted versions
   and the agent could never be told to uninstall them. Covered by
   `Poll_DeliversUninstallJob_WhenPackageWasDeleted` and related tests. **Do not "simplify" away.**
2. **Race-safe poll enqueue** — filtered unique index `UX_InstallationJobs_Computer_Version_Active`
   (`Status IN (0,1,2)`, DBContext.cs:531-535) + catch of the specific `PostgresException`
   unique-violation with fallback to the canonical manifest (InstallationJobService.cs:270-280).
   Idempotent poll confirmed by `Poll_IsIdempotent_NoDuplicateJobsOnRepeatedCalls`.
3. **BlobGc reference-guards and ordering** — candidates checked against live `SoftwareFiles`,
   `OverrideFiles`, **and** `AgentReleases` (BlobGcService.cs:111-113); the actual delete re-checks
   all guards atomically inside a conditional `ExecuteDeleteAsync` (:244-251), so the
   select-then-delete TOCTOU window is closed at the DB. DB row is deleted **before** the storage
   object, so a failure leaves only a harmless orphaned object, never a dangling DB reference
   (:138-155). `ReferenceCount` is treated as a diagnostic, not a source of truth (:133-136,
   :161-166) — the right call given increments/decrements are not transactional with their callers.
   `Restrict` FKs from `OverrideFile`/`SoftwareFile` to `Blob` backstop any remaining race.
4. **Multi-instance safety** — `pg_try_advisory_lock` around the GC sweep with careful
   connection-lifetime handling (BlobGcService.cs:77-104, 174-203).
5. **Terminal-state idempotency in `CompleteAsync`** — runs in a transaction, tolerates duplicate
   and stale agent callbacks with explicit logging instead of throwing
   (InstallationJobService.cs:462-482); domain events only for Completed/Failed. Covered by
   `Complete_WhenJobAlreadyCancelled_IgnoresLateAgentCallback`.
6. **Presign pipeline** — cache TTL = half the URL expiry, in-flight coalescing via
   `ConcurrentDictionary<Lazy<Task>>`, bounded concurrency (semaphore of 8)
   (InstallationJobService.cs:28-32, 829-869); all three properties are unit-tested.
7. **Override precedence** — `Computer(2) > Station(1) > Model(0)` via
   `OrderByDescending((int)o.Scope)` (OverrideResolver.cs:54) matches the enum values; exactly one
   scope id enforced by check constraint `CK_OverrideFiles_ScopeMatchesId`, and duplicates blocked
   by the `NULLS NOT DISTINCT` filtered unique index (DBContext.cs:282-293). Precedence is tested
   (`Poll_ComputerScopeWins_OverStationScope`).
8. **Agent authentication enforces liveness** — the token handler itself requires
   `AgentStatus.Active && !Computer.IsDeleted` (AgentService.cs:656-660), so every agent-facing
   endpoint is gated even where a service omits its own status check.
9. **Uninstall retry backoff** (30 min, InstallationJobService.cs:42, :139-148) works because the
   `SaveChangesAsync` override maintains `UpdatedAt` for plain `IEntity` types too
   (DBContext.cs:840-851) — verified, not an accident.
10. **Update-window logic** handles overnight windows (`start > end`) correctly
    (InstallationJobService.cs:914-926), and baseline uniqueness/rule uniqueness are backed by
    filtered DB unique indexes, not just read-then-write checks (DBContext.cs:309-313, 338-342).

---

## Findings

### F1 — HIGH · `GetJobsAsync` silently drops jobs of soft-deleted versions/computers and desyncs `Total`
**File:** MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:569-610
**Category:** correctness / EF global-query-filter surprise

**Evidence.** `baseQuery` (:573-575) has no navigations, so `CountAsync` (:577) counts **all**
jobs (`InstallationJob` is not soft-deletable → unfiltered). The items projection then navigates
`x.Computer.Hostname` (:586) and `x.SoftwareVersion...` (:588-589) — both `Computer` and
`SoftwareVersion` are `ISoftDeletable`, and EF applies global filters to navigations inside
projections. A job whose version (or computer) is soft-deleted is dropped from `Items` but still
counted in `Total`. This is exactly the population the deliberate `IgnoreQueryFilters` design
creates: an **active Uninstall job for a deleted version is invisible in the admin job list**
while the agent is executing it, and page counts never add up. The sibling read paths
(`PcInstallationService.GetInstallationsByComputerAsync`:166, `GetOutdatedPcsAsync`:227) already
apply `IgnoreQueryFilters` for precisely this reason — `GetJobsAsync` is the one seam that missed it.

**Failure scenario.** Package deleted → `PollAsync` enqueues Uninstall job (by design) → operator
opens *Installations → Jobs*: `Total = 12`, list shows 11 rows; the running uninstall can neither
be seen nor cancelled from the UI.

**Minimal fix.** Add `.IgnoreQueryFilters()` to `baseQuery` in `GetJobsAsync`. No test covers
`GetJobsAsync` today — add one with a soft-deleted version (see Test Gaps).

---

### F2 — HIGH · `ConfigBaselineValidator` accepts `stationResourceId` but never uses it — baseline validation ignores override files
**File:** MProjectBackend/MProject.Application/Services/Software/ConfigBaselineValidator.cs:34-101 (parameter :37)
**Category:** correctness / silently-ignored input

**Evidence.** The `stationResourceId` parameter is never referenced in the method body. Both
callers pass it deliberately: the API endpoint (`ConfigBaselinesController.Validate`:80-89 takes
it as a query param) and the pin gate
(`StationSoftwareAssignmentService.EnforceConfigBaselineAsync`:320-321 passes the station being
pinned). Meanwhile the whole `OverrideResolver` machinery exists to replace exactly these config
files per model/station/computer before they reach agents (`InstallationJobService.EffectiveBlob`
:706-718 substitutes overridden blobs for `OverridablePaths`). The validator reads only the base
`SoftwareFiles` content (:58-63), so:

**Failure scenario.** Station S has an Active station-scoped override for `FTU/Config.ini` that
sets `[Motor] Speed=9000`, outside the baseline range. Pinning the version to station S passes
`EnforceConfigBaselineAsync` (base file has `Speed=5000`), deploys, and every PC on S runs an
out-of-baseline config. Conversely, an override that *fixes* a bad base value still blocks the pin.

**Minimal fix.** In `ValidateVersionForModelAsync`, when `stationResourceId` is provided, resolve
active overrides for the version's package at that scope (reuse `IOverrideResolver` or a direct
query on `OverrideFiles` by package + station chain) and substitute the overridden blob's content
for matching `TargetRelativePath`s before evaluating rules. If station-aware validation is
intentionally out of scope for now, remove the parameter from the interface and both call sites
instead of silently accepting it — the current signature promises behavior that does not exist.

---

### F3 — MEDIUM · Watchdog can overwrite a concurrently-completed job back to Failed (no concurrency guard)
**File:** MProjectBackend/MProject.Application/Services/Software/InstallationJobWatchdogService.cs:98-132
**Category:** race condition / read-then-write

**Evidence.** Candidates are loaded tracked (:98-106), mutated in memory (:120-123), and saved
with a full-entity `SaveChangesAsync` (:132). `InstallationJob : BaseEntity` has **no** concurrency
token (only `VersionedEntity` types get `IsConcurrencyToken`, DBContext.cs:784-793). If the agent's
`CompleteAsync` transaction commits `Completed` between the candidate query and the save, the
watchdog blindly overwrites the row to `Failed`, stamps `LastErrorCode = "timeout"`, and sets the
computer to `Error` (:135-152) — even though the install succeeded and the
`PcInstallationRecord` says Installed. The inverse direction is already handled
(`CompleteAsync` treats an already-terminal job as an idempotent no-op,
InstallationJobService.cs:466-482) — this direction is not.

**Failure scenario.** Agent stalls 29 minutes on a slow download, watchdog sweep starts, agent
completes at minute 30 while the sweep batch is in memory → job flips Completed → Failed, computer
shows Error, operators chase a phantom failure.

**Minimal fix.** Replace the tracked update with a per-job conditional set-based update:
`context.InstallationJobs.Where(j => j.Id == job.Id && InFlightStatuses.Contains(j.Status)).ExecuteUpdateAsync(...)`
and only mark the computer Error when the update affected a row. (The in-memory-provider branch
can keep the tracked path.)

---

### F4 — MEDIUM · Baseline validator swallows storage exceptions and mislabels them "file not found"
**File:** MProjectBackend/MProject.Application/Services/Software/ConfigBaselineValidator.cs:103-119 (catch :115-118)
**Category:** swallowed exception

**Evidence.** `LoadContentAsync` catches every non-cancellation exception and returns `null`;
the caller then reports `"File '{path}' was not found in this version."` (:82-84). A MinIO outage,
a permissions error, or an over-5MB config (`DownloadStringAsync` throws on `maxBytes`,
StorageServiceExtensions.cs:34-37) all surface as a rule failure with a factually wrong message
and **zero logging**. Because this feeds `EnforceConfigBaselineAsync`, a transient storage blip
blocks version pinning with a misleading "file was not found" explanation.

**Minimal fix.** Inject `ILogger`, log the exception with the storage path, and use a distinct
message ("could not read '{path}': …") so operators can tell infrastructure failures from actual
baseline violations. Failing closed is fine; failing closed *silently and mislabeled* is not.

---

### F5 — MEDIUM · `GetDriftedComputersAsync`: items vs. total mismatch for deleted versions, and unbounded in-memory paging
**File:** MProjectBackend/MProject.Application/Services/Software/PcInventoryService.cs:73-151
**Category:** correctness (query-filter surprise) + performance

**Evidence.** Same mechanism as F1: the first query (:75-81) computes `total`/page ids without
touching `SoftwareVersion`, but the `rows` query (:101-120) projects
`r.SoftwareVersion.VersionNumber` / `SoftwarePackage.Name`, so drifted records referencing a
soft-deleted version are filtered out. A computer whose only drifted record points at a deleted
version (a real state: version deleted while still installed, uninstall pending) is counted in
`total` and consumes a page slot but produces no item after the `GroupBy` (:122-142).
Secondarily, :75-81 loads **all** drifted computers (id + hostname) into memory just to page —
`Distinct/OrderBy/Skip/Take` is fully SQL-translatable.

**Minimal fix.** Add `.IgnoreQueryFilters()` to the `rows` query (matching
`GetInstallationsByComputerAsync`), and move `Skip/Take` into the first query.

---

### F6 — MEDIUM · Override upload accepts files the system can never read back (5MB read cap vs. 50MB upload cap)
**Files:** MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs:24, 50-103, 251-271; MProjectBackend/MProject.Api/Controllers/Software/OverrideFilesController.cs:57
**Category:** boundary validation

**Evidence.** `MaxFileSize = 5MB` (:24) is enforced only in `GetFileContentAsync` (:259-260, where
`DownloadStringAsync` throws above the cap). `UploadOverrideFileAsync` validates extension and
`FileSize > 0` but has **no upper bound**; the controller allows 50MB (`[RequestSizeLimit(50_000_000)]`).
A 6-50MB `.json`/`.dat` override uploads fine, is served to agents via presigned URLs, but every
UI attempt to view it (`GET /override-files/{id}/content`) throws `InvalidOperationException`
→ 500. Config overrides above 5MB are almost certainly a client error that should be rejected at
the door.

**Minimal fix.** In `UploadOverrideFileAsync`: `if (request.FileSize > MaxFileSize) throw new
ArgumentException(...)` — and drop the controller's `RequestSizeLimit` to match.

---

### F7 — MEDIUM (latent) · Non-seekable upload stream silently produces an empty stored blob
**File:** MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs:85-99
**Category:** correctness / silent data corruption path

**Evidence.** `ComputeSha256Async` consumes the stream (:85), then `if (fileStream.CanSeek)
Position = 0` (:86-87). If the stream is **not** seekable the code proceeds anyway: the Blob row
is created with the correct hash and size, but `UploadAsync` (:94-98) writes an exhausted stream —
a zero-byte object under a hash that claims otherwise. Agents verifying by SHA-256 will hard-fail
the file forever, and dedup means no re-upload is attempted (the blob "exists"). Today the only
caller passes a buffered, seekable `IFormFile` stream (OverrideFilesController.cs:63), so this is
latent — but it is one new caller away from silent corruption.

**Minimal fix.** `if (blobNeedsUpload && !fileStream.CanSeek) throw new ArgumentException("Stream
must be seekable");` (or buffer to a temp stream before hashing).

---

### F8 — MEDIUM · N+1 permission checks in the override-file list
**File:** MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs:223-224
**Category:** performance / query in loop

**Evidence.** After the page query, `CanManageOverrideFileAsync` runs per item; each call issues a
lookup of the override's `ResourceId` (OverrideFilePermissionService.cs:32-36), an `IsActiveUser`
query (:46-51), and a permission evaluation — the resource-decision cache helps repeat calls but
each distinct override id is a distinct cache key. With `MaxPageSize = 100` that is up to ~200-300
queries per page render.

**Minimal fix.** The DTO already carries `ResourceId` (:377): check `IsActiveUser` once, then
evaluate `ManageOverrideFiles` per distinct resource (or reuse
`IAuthorizedResourceQueryService.GetVisibleResourceIdsAsync(userId, ManageOverrideFiles)` — one
query — and set `CanManage = visible == null || visible.Contains(dto.ResourceId)`).

---

### F9 — MEDIUM · Authz asymmetry: override detail/content endpoints require a *global* grant while the list honors scoped grants; no resource-level read check in the service
**Files:** MProjectBackend/MProject.Api/Controllers/Software/OverrideFilesController.cs:40-54; MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs:235-271
**Category:** authorization consistency

**Evidence.** Bare `[RequirePermission(AppPermissions.ReadOverrideFiles)]` resolves to
`CheckPermissionAsync(userId, action, resourceId: null)`; with a null resource the scope chain is
empty (ScopeResolver.cs:20-23) and only **null-scoped** role assignments match
(RbacGrantQueryService.cs:67). So `GET /override-files/{id}` and `GET /override-files/{id}/content`
demand a global grant, while `GET /override-files` (list) grants visibility from *scoped*
assignments via `GetVisibleResourceIdsAsync` (OverrideFileService.cs:194-212). Net effect: a user
with a model-scoped `ReadOverrideFiles` grant sees rows in the list but gets 403 opening any of
them; meanwhile `GetOverrideFileAsync`/`GetFileContentAsync` themselves perform **no**
resource-level check at all (`GetFileContentAsync` doesn't even take a `userId`), so the entire
per-resource ACL story for single-item reads rests on the coarse attribute. Fail-closed, so not a
bypass — but inconsistent with the sibling list endpoint and with the delete path (which checks
`CanManageOverrideFileAsync` in the service, :283-284).

**Minimal fix.** Mirror the delete pattern: add a `CanReadOverrideFileAsync(userId, id)` (permission
check against the override's `ResourceId`) inside `GetOverrideFileAsync`/`GetFileContentAsync`
(pass `CurrentUserId` from the controller), keeping the attribute as the coarse gate.

---

### F10 — MEDIUM · Baseline import/validate read config content of arbitrary software versions without any check on the caller's access to that version
**Files:** MProjectBackend/MProject.Application/Services/Software/ConfigBaselineService.cs:194-268 (import), 270-289 (validate); MProjectBackend/MProject.Api/Controllers/Software/ConfigBaselinesController.cs:66-71, 80-89
**Category:** authorization / cross-resource information disclosure

**Evidence.** `ImportFromVersionAsync` takes any `versionId`, downloads its `ProgramConfig.json`
and copies its FtuDataConfigs values into rules — gated only by baseline-manage permission on the
*baseline's model* (:196), with no relation required between that model and the version's package,
and no read permission on the version. `ValidateAsync` (controller gate: global
`ReadConfigBaselines`) similarly evaluates any version and returns messages containing actual
config values (`"Expected 'X' but found 'Y'"`, BaselineEvaluator.cs:31). Any user holding either
permission can probe config values of software versions they otherwise have no access to.
Also note `ValidateAsync`'s `userId` parameter is entirely unused (:271) — the method performs no
check itself.

**Minimal fix.** In both paths, verify the version's package is related to the model (e.g., a
`StationSoftwareAssignment` for the package exists under one of the model's stations) or check a
read permission against the version/package resource; drop or use the dead `userId` parameter.

---

### F11 — MEDIUM · Manual installation records don't reset drift state, unlike the job-completion path
**File:** MProjectBackend/MProject.Application/Services/Software/PcInstallationService.cs:262-268 (also 118-121)
**Category:** correctness / sibling inconsistency

**Evidence.** `InstallationJobService.MarkInstalledAsync` resets `DriftStatus = Healthy` and clears
`DriftSummary` when a job completes (InstallationJobService.cs:967-968).
`PcInstallationService.MarkInstalled` (manual attestation) and `UpdateStatusAsync`'s
Installed branch set status/notes/timestamp but leave `DriftStatus`/`DriftSummary` untouched.
With auto-heal enabled, a record still flagged `Drift` makes the next `PollAsync` immediately
enqueue a redeploy (InstallationJobService.cs:201-211) — right after a human recorded that the
machine was fixed manually — and the drift dashboard keeps showing a stale summary until the next
inventory run.

**Minimal fix.** Reset `DriftStatus` (to `Healthy`, matching the job path — or `Unknown` if you
prefer "pending re-inventory") and `DriftSummary = null` in `MarkInstalled` and in
`UpdateStatusAsync` when transitioning to `Installed`.

---

### F12 — MEDIUM · Import target-file lookup uses an unanchored `EndsWith`, can bind rules to the wrong file
**File:** MProjectBackend/MProject.Application/Services/Software/ConfigBaselineService.cs:220-224
**Category:** correctness / string matching

**Evidence.** The `ProgramConfig.json` lookup two queries above is properly anchored
(`f.RelativePath == "ProgramConfig.json" || f.RelativePath.EndsWith("/ProgramConfig.json")`, :201),
but the custom-config lookup is `f.RelativePath.EndsWith(customFileName)` with no separator anchor.
With `CustomConfigFileName = "Config.ini"`, a version containing both `Backup/OldConfig.ini` and
`FTU/Config.ini` can resolve to `Backup/OldConfig.ini` (whichever row the DB returns first —
`FirstOrDefaultAsync` with no ordering), and all imported rules then target the wrong
`TargetRelativePath`, failing validation forever after.

**Minimal fix.** Match the anchored pattern used at :201:
`f.RelativePath == customFileName || f.RelativePath.EndsWith("/" + customFileName)`.

---

## Unnecessary Code

1. **No-op `Include` under a projection** —
   ConfigBaselineValidator.cs:60: `.Include(f => f.Blob)` is ignored because the query ends in a
   `.Select(...)` projection that already navigates `f.Blob.StoragePath`. Delete the line.
2. **Dead `userId` parameter** — ConfigBaselineService.cs:270-271 (`ValidateAsync`): accepted,
   never used (see F10). Either use it for a permission check or remove it from
   `IConfigBaselineService`.
3. **Dead `stationResourceId` parameter** — ConfigBaselineValidator.cs:37 (see F2): today it is
   plumbed through the interface, the controller, and the pin gate, and does nothing.
4. **Redundant `!IsDeleted` predicates on globally-filtered entities** (harmless, listed for
   awareness, *not* recommended for blind removal since they read as intent):
   OverrideFileService.cs:208, 242, 255, 280; StationRollbackWatchdogService.cs:87;
   PcInstallationService.cs:130, 140, 149; ConfigBaselinePermissionService.cs:28, 54, 61, 74;
   BlobGcService.cs:111-113 subquery `!sf.IsDeleted` etc. All of these entities already carry the
   automatic `!IsDeleted` global filter (DBContext.cs:804-811). Keep them only if the team treats
   explicit predicates as documentation; otherwise they imply (wrongly) that the filter is absent.
   **Important inverse caution:** none of the `IgnoreQueryFilters()` calls listed under Good
   Decisions #1 are redundant — removing any of them breaks the deleted-version uninstall flow.

No dead branches or unreachable code found in scope. No always-constant parameters found besides
the two dead parameters above.

---

## Simpler Alternative

Small, local consolidations only — no redesign warranted:

1. **Shared watchdog loop scaffolding.** `InstallationJobWatchdogService.ExecuteAsync` (:42-78) and
   `StationRollbackWatchdogService.ExecuteAsync` (:36-70) are line-for-line identical except for
   the options type and log strings; `BlobGcService.ExecuteAsync` differs only in delay
   computation. A tiny generic base (`IntervalSweepService<TOptions>` with an abstract
   `SweepAsync`) removes ~70 duplicated lines across the three (plus
   `ComputerLivenessWatchdogService` outside this scope).
2. **One projection expression for `PcInstallationRecordResponse`.** The identical 12-line `Select`
   appears twice (PcInstallationService.cs:176-187 and :239-250). Extract a
   `private static readonly Expression<Func<PcInstallationRecord, PcInstallationRecordResponse>>`
   like the `ToDto()` pattern already used in OverrideFileService.cs:372.
3. **One "active jobs with full include tree" query helper** in `InstallationJobService` — the
   same `IgnoreQueryFilters + Include(SoftwareVersion→SoftwarePackage) + Include(SoftwareVersion→Files→Blob) + Where(computer, ActiveStatuses)`
   block appears in `GetActiveJobsAsync` (:74-83) and `ResolveManifestAsync` (:303-315). A private
   `IQueryable<InstallationJob> ActiveJobsWithManifestData(Guid computerId)` makes the deliberate
   `IgnoreQueryFilters` single-sourced and harder to lose in future edits.

---

## Complexity Report

- **`InstallationJobService.PollAsync` (:96-294, ~200 lines)** — does five jobs: gate computation,
  orphan-uninstall detection/enqueue, install/update enqueue with drift handling, unique-violation
  recovery, and manifest assembly. It is well-tested and correct; flagging for size only. Natural
  seams: `EnqueueOrphanUninstallsAsync`, `EnqueueAssignmentJobsAsync`.
- **`BlobGcService.SweepAsync` (:79-204, ~125 lines)** — sweep + advisory-lock lifecycle in one
  method; acceptable, but the lock acquire/release could live in a small `IAsyncDisposable`.
- **`BuildManifestJobsAsync` (:726-820, ~95 lines)** — borderline; uninstall short-circuit and file
  loop are clear enough to leave.
- **In-memory-provider forks** (`IsInMemoryProvider()` branches in InstallationJobService :534-567,
  :637-656, watchdog :134-154, BlobGc :90-104, :221-242, BlobReferenceExtensions) — test-support
  code interleaved with production paths. It is the accepted cost of using the InMemory provider
  where `ExecuteUpdate/ExecuteDelete` are unsupported; consistent across the codebase, no action.
- Duplication: the three near-identical `BackgroundService` loops and the duplicated
  `PcInstallationRecordResponse` projection (see Simpler Alternative). Nothing else exceeds the
  15-line/2× threshold in scope.

### Test Gaps (informational)
- `GetJobsAsync` has **zero** tests — F1 would have been caught by a "job listing shows uninstall
  job for deleted version, Total == Items" test.
- `GetDriftedComputersAsync` has only the happy path (PcInventoryServiceTests.cs:133) — no
  deleted-version or paging case (F5).
- No test asserts baseline validation honors overrides (F2) — `ConfigBaselineValidatorTests` all
  call the validator without `stationResourceId`.
- No watchdog-vs-complete race test (F3) — hard to test with InMemory, but a conditional-update
  implementation is testable ("sweep does not fail a job completed after candidate selection").

---

## Out-of-Scope Observations (context files, not counted as findings)

- **`AgentController.DownloadLocalBlob` (AgentController.cs:132-140)** passes agent-supplied
  `path` straight to `IStorageService.DownloadAsync`. `LocalStorageService.ResolvePath` does
  confine to the storage root (LocalStorageService.cs:148-159), so there is no filesystem
  traversal — but any active agent can fetch **any** object in the store if it knows the path.
  Paths are content-addressed (SHA-256), which makes enumeration impractical, yet this still
  bypasses the per-computer manifest scoping. Consider restricting to `blobs/`-prefixed paths or
  validating the requested hash against the agent's current manifest.
- `StationSoftwareAssignmentService.MarkPackageRecordsUninstalledAsync` (:461-474) navigates
  `r.SoftwareVersion` without `IgnoreQueryFilters`, so records referencing soft-deleted versions
  are not flipped to Uninstalled on repin/rollback — same family as F1/F5, in a file outside this
  pass's scope.

---

## Final Recommendation

**Fix-forward; no redesign needed.** The domain core (state machine, watchdog timers, GC guards,
override precedence) is sound and deliberately engineered — in particular the `IgnoreQueryFilters`
usage for uninstalls of deleted versions is a bug fix that must survive future refactors
(recommend the query-helper extraction in Simpler Alternative #3 to protect it).

Order of work:
1. **F1** (one-line `IgnoreQueryFilters` + test) and **F2** (decide: implement station-aware
   validation or remove the parameter) — both HIGH, both cheap to start.
2. **F3, F4, F5** — reliability of the watchdog/validator/drift reporting.
3. **F6-F12** — consistency batch (override stack caps + authz symmetry + drift reset + import
   anchoring); each is a small, local change.
4. Optionally the three consolidations in Simpler Alternative and the listed test gaps.

---

## Tóm tắt (Vietnamese)

- Đã đọc toàn bộ 17 file trong phạm vi + các interface/model/test liên quan; không có lỗi CRITICAL.
- **2 lỗi HIGH:** (1) `GetJobsAsync` thiếu `IgnoreQueryFilters` nên job Uninstall của version đã
  xóa mềm bị ẩn khỏi danh sách admin và `Total` lệch với số dòng thực tế; (2) validator baseline
  nhận `stationResourceId` nhưng bỏ qua hoàn toàn — việc kiểm tra baseline không tính đến
  override file theo station/computer, có thể cho phép pin version mà config thực tế vi phạm baseline.
- **10 lỗi MEDIUM:** watchdog có thể ghi đè job vừa Completed thành Failed (không có concurrency
  token); validator nuốt exception storage và báo sai "file not found"; báo cáo drift lệch
  total/items với version đã xóa; upload override không giới hạn 5MB trong khi đọc lại bị chặn;
  stream không seek được sẽ tạo blob rỗng; N+1 khi check quyền danh sách override; endpoint chi
  tiết override đòi quyền global trong khi list cho phép quyền theo scope; import/validate baseline
  đọc config của version bất kỳ không kiểm tra quyền; ghi nhận cài đặt thủ công không reset trạng
  thái drift; import dùng `EndsWith` không neo nên có thể chọn nhầm file.
- **Điểm tốt cần giữ nguyên:** `IgnoreQueryFilters` cho flow uninstall version đã xóa (KHÔNG được
  "đơn giản hóa"), unique index + retry chống race khi poll, BlobGc dùng advisory lock + conditional
  delete chống TOCTOU, thứ tự ưu tiên override Computer > Station > Model đúng và có test.
- Khuyến nghị: sửa 2 lỗi HIGH trước (đều rẻ), sau đó nhóm MEDIUM; không cần redesign.
