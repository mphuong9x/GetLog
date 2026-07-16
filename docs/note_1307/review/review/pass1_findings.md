# Pass 1 — Deep Review: Software Package/Version/File/Assignment (Backend)

**Date:** 2026-07-13
**Scope:**
`MProject.Application/Services/Software/`: SoftwarePackageService.cs, SoftwareVersionService.cs, SoftwareFileService.cs, StationSoftwareAssignmentService.cs
`MProject.Api/Controllers/Software/`: SoftwarePackagesController.cs, SoftwareVersionsController.cs, SoftwareFilesController.cs, SoftwareAssignmentsController.cs
**Method:** every scope file read in full; interfaces, models, DBContext config, InstallationJobService (poll/reconcile), TusUploadHandler, seeder, and tests read as context. Report-only — no code changed.

Key context facts the findings rely on (verified in code):

- Every `ISoftDeletable` entity gets a global query filter `!IsDeleted` (DBContext.cs:804-811). `SoftwarePackage`, `SoftwareVersion`, `SoftwareFile`, `StationSoftwareAssignment` are soft-deletable; `InstallationJob` and `PcInstallationRecord` are **not**. EF applies the filter to *navigations* too — this is exactly the bug class already fixed in InstallationJobService with `IgnoreQueryFilters` (Poll/GetActiveJobs/ResolveManifest/GetOwnedJob).
- Pending Uninstall jobs are delivered to the agent **unconditionally** (InstallationJobService.cs:124-131, 283), while new Install jobs are gated by update window + test-app-running (InstallationJobService.cs:238, 908-912).
- Unique indexes are soft-delete-filtered: `(SoftwarePackageId, VersionNumber)` (DBContext.cs:380-382), `(StationResourceId, SoftwarePackageId)` (DBContext.cs:482-485), `(SoftwareVersionId, RelativePath)` (DBContext.cs:460-462). `UX_InstallationJobs_Computer_Version_Active` is partial on active statuses (DBContext.cs:532-535).
- The audit logger shares the scoped `IAppDbContext`, so audit rows join the ambient transaction.

---

## Summary

**Request changes.**

The multi-active assignment semantics (IsActive = independent Enable flag, Deactivate emits Uninstall) are implemented correctly for the main paths and are well covered by tests. However: (1) `ActivateAsync` does not cancel the Uninstall jobs its own `DeactivateAsync` created — a re-enabled app still gets uninstalled; (2) three services in scope repeat the soft-delete/navigation query-filter bug that was already diagnosed and fixed in InstallationJobService, so uninstall emission silently misses records of soft-deleted versions; (3) `DeleteVersionAsync` cancels pending Uninstall jobs, undoing the deliberate "uninstall jobs survive version delete" design; (4) `CompleteUploadAsync` can replace the files of a version that is concurrently being Released. Separately, an entire controller (SoftwareFilesController) and a package flag (`AutoRemoveOnUnassign`) have no callers/readers and are candidates for deletion.

## Good Decisions (keep these)

- **Multi-active model carried through consistently**: non-unique partial index with an explaining comment (DBContext.cs:486-492), PollAsync deploys only `IsActive` assignments, Deactivate keeps the pin so re-activation redeploys the same version (tested).
- **Cancel-then-create with intermediate `SaveChangesAsync`** in Deactivate/Remove/Pin (StationSoftwareAssignmentService.cs:173, 208) — required to satisfy the partial unique index `UX_InstallationJobs_Computer_Version_Active`; subtle and correct.
- **`RollbackToPreviousVersionAsync` optimistic guard** (StationSoftwareAssignmentService.cs:343-351) re-validates the plan inside the transaction — stale watchdog plans become no-ops instead of mis-rollbacks.
- **Version-code conflict retry** (SoftwareVersionService.cs:36-66, 244-312) paired with the filtered unique index handles concurrent creates without locks; blob refcount increments happen *inside* the same transaction in `CreateDerivedVersionAsync`.
- **Upload integrity pipeline**: server-side hash verification with tamper cleanup (`VerifyUploadedBlobHashAsync`, SoftwareFileService.cs:466-502), `DecrementBlobReferenceAsync` floors at zero + drift logging, `TouchEditableVersionAsync` row-lock (`ExecuteUpdateAsync`) as a Draft-state guard inside the transaction (SoftwareFileService.cs:93-113).
- **`AssignAsync` approval compensation** (StationSoftwareAssignmentService.cs:100-120): assignment is rolled back (soft-deleted) if approval submission fails.
- Consistent `AsNoTracking` + projection on all read paths; paginated lists with clamped `PagedRequest`.

## Findings

### F1 — Re-activating an assignment leaves its pending Uninstall jobs alive; the agent uninstalls an enabled app
- **Severity:** CRITICAL (incorrect behavior)
- **Evidence:** `DeactivateAsync` creates Uninstall jobs (StationSoftwareAssignmentService.cs:174, 476-497). `ActivateAsync` (StationSoftwareAssignmentService.cs:134-151) only flips `IsActive` — it never touches jobs. PollAsync delivers pending Uninstall jobs unconditionally (InstallationJobService.cs:124-131, 283), while the replacement Install job is gated by update window + test-app (InstallationJobService.cs:238, 908-912).
- **Failure scenario:** operator disables a package by mistake and re-enables it seconds later, before the agent polls. Next poll still delivers the Uninstall job → app is removed from the station; the reinstall then waits for the next update window (potentially hours), during which the launcher has no app to run.
- **Tests:** `Activate_AfterDeactivate_ReactivatesWithoutTouchingOthers` exists but seeds no installed records/jobs, so this path is uncovered.
- **Minimal fix:** in `ActivateAsync`, cancel Pending/Downloading/Installing jobs with `JobType == Uninstall` for this station + package (reuse the `CancelActiveJobsForPackageAsync` shape, with `IgnoreQueryFilters` — see F2), then save. Add a test for Deactivate→Activate with an installed record.

### F2 — Queries that traverse `SoftwareVersion` navigations silently skip soft-deleted versions → Deactivate/Remove emit no Uninstall job for them
- **Severity:** HIGH (incorrect behavior, partially backstopped)
- **Evidence:** the global `!IsDeleted` filter applies to navigations. Affected queries in scope:
  - `CancelActiveJobsForPackageAsync` — `j.SoftwareVersion.SoftwarePackageId` (StationSoftwareAssignmentService.cs:445)
  - `CreateUninstallJobsAsync` — `r.SoftwareVersion.SoftwarePackageId` (StationSoftwareAssignmentService.cs:480)
  - `MarkPackageRecordsUninstalledAsync` — same traversal (StationSoftwareAssignmentService.cs:465)
  - `DeletePackageAsync` active-jobs + installed queries (SoftwarePackageService.cs:222-239) — safe for the versions deleted in the *same* call (flags are flushed only at line 242, after the queries), but records of *previously* soft-deleted versions are missed.
  InstallationJobService already fixed this exact class with `IgnoreQueryFilters` on 4 queries; these services did not get the fix.
- **Failure scenario:** version V is deleted (`DeleteVersionAsync` unpins but leaves the app installed, record `Installed`). Later the user Deactivates or Removes the assignment — the domain contract says "Deactivate must emit an Uninstall job", but `CreateUninstallJobsAsync` cannot see the record → no job. The app is only removed later by the PollAsync orphan reconcile (which does use `IgnoreQueryFilters`), and that path is gated by update window + test-app-running — delayed, and never immediate.
- **Minimal fix:** add `.IgnoreQueryFilters()` to the four queries above (root entities `InstallationJob`/`PcInstallationRecord` are not soft-deletable, so this only unhides the version navigation; keep the explicit `!j.Computer.IsDeleted` predicates that are already there).

### F3 — `DeleteVersionAsync` cancels pending **Uninstall** jobs and never re-emits them
- **Severity:** HIGH (incorrect behavior, contradicts an explicit design decision)
- **Evidence:** the cancel query filters only on status, not `JobType` (SoftwareVersionService.cs:521-533). The system deliberately keeps Uninstall jobs alive for soft-deleted versions (`IgnoreQueryFilters` in InstallationJobService; catalog design: an app leaves the launcher only when its Uninstall job runs). `DeleteVersion_CancelsActiveJobsAndLeavesTerminalJobsAlone` only seeds default-type (Install) jobs, so the Uninstall case is untested.
- **Failure scenario:** admin removes an assignment (Uninstall job Pending), then deletes the version before the agent polls. The pending Uninstall job is cancelled with `version_deleted`; the app stays installed and in the launcher catalog until the window-gated poll reconcile eventually catches it.
- **Minimal fix:** add `&& j.JobType != InstallationJobType.Uninstall` to the cancel query in `DeleteVersionAsync`. (`DeletePackageAsync` is OK: it cancels but immediately re-creates Uninstall jobs from still-`Installed` records.)

### F4 — `CompleteUploadAsync` can replace the files of a version that is concurrently Released (TOCTOU)
- **Severity:** HIGH (race → released-version content mutates)
- **Evidence:** the Draft check runs *outside* the transaction with no lock (SoftwareFileService.cs:205), then `PreverifyNewUploadedBlobsAsync` does storage I/O (hashing — can take seconds for large uploads), then the transaction swaps all files (SoftwareFileService.cs:209-294). Contrast with `RegisterUploadedFileAsync`, which re-asserts Draft *inside* the transaction via the row-locking `TouchEditableVersionAsync` (SoftwareFileService.cs:65-67, 93-113). `ReleaseVersionAsync` takes no lock either (SoftwareVersionService.cs:441-467).
- **Failure scenario:** user A hits upload-complete while user B releases the version. Release lands during the preverify window → the now-Released version's file set is silently replaced (agents deploy content nobody reviewed at release time). The mirror race releases a version whose files are being deleted → Released with 0 files.
- **Minimal fix:** call `await TouchEditableVersionAsync(version.Id)` as the first statement inside the `ExecuteInTransactionAsync` lambda of `CompleteUploadAsync` (the helper already exists and throws if the version left Draft).

### F5 — `DeleteFileAsync` clears `EntryPointPath` but leaves `IconPath`/`WatchProcessPath`/`HealthCheckUrl` dangling
- **Severity:** MEDIUM
- **Evidence:** SoftwareFileService.cs:355-356 handles only the entry point. `CreateDerivedVersionAsync` documents the full invariant — entry point, icon, watch path all must reference existing files, and `HealthCheckUrl` is nulled when the entry point is cleared (SoftwareVersionService.cs:234-242, 267).
- **Failure scenario:** deleting the icon or watch-process file from a Draft, then releasing, ships a manifest whose `IconPath`/`WatchProcessPath` point at nothing; a version whose entry point was cleared keeps a stale `HealthCheckUrl`.
- **Minimal fix:** mirror the derived-version clearing in `DeleteFileAsync`. (Moot if U1 below is accepted and the endpoint is deleted.)

### F6 — Permission tiers are inconsistent across sibling mutations
- **Severity:** MEDIUM (authz consistency)
- **Evidence:**
  - `DELETE /software-files/{id}` requires `software.manage` (SoftwareFilesController.cs:31-37) while `upload-complete` — which soft-deletes and replaces **all** files of the version — requires only `software.package.manage` (SoftwareVersionsController.cs:99-105). The seeder migration grants the three fine-grained permissions to roles that had `software.manage`, but not the reverse (AppDbSeeder.cs:541-598) — so a Crud-only role can wipe every file yet cannot delete one.
  - `DELETE /software-versions/{id}` needs only Crud (SoftwareVersionsController.cs:107-113) and works on **Released** versions (unpins stations, cancels jobs), while the milder `deprecate` requires `software.version.publish` (SoftwareVersionsController.cs:115-121). A Crud-only user cannot deprecate but can hard-delete a released, deployed version.
- **Minimal fix:** `DeleteFile` → `ManageSoftwarePackagesCrud`; for `DeleteVersion`, require `PublishSoftwareVersion` when the version is not Draft (or reject deleting Released versions and point to deprecate).

### F7 — `GetOverviewAsync` counts disabled assignments as "deployed stations"
- **Severity:** MEDIUM
- **Evidence:** SoftwarePackageService.cs:355-360 filters `!IsDeleted && TargetVersionId != null` only. Since the 2026-07-09 multi-active change, `IsActive` is the Enable flag and PollAsync deploys only active assignments (InstallationJobService.cs:113-115) — a station with all pins disabled is not deployed.
- **Minimal fix:** add `&& x.IsActive` to the `deployedStationCount` query.

### F8 — `ClonePackageAsync` is not atomic: version-clone failure leaves an empty package plus a success audit
- **Severity:** MEDIUM
- **Evidence:** the package is committed and the `SoftwarePackage.clone` audit written (SoftwarePackageService.cs:113-118) before `CloneVersionAsync` runs (line 124) in its own separate transaction. Any failure there (e.g. transient DB error, blob FK) returns 500 with a half-created clone; retry fails on the duplicate-name check.
- **Minimal fix:** cheapest robust option — pre-validate everything, then create the package and clone the version, and write the audit *after* both succeed; document that a failed clone may leave an empty (deletable) package. Full atomicity would require `CreateDerivedVersionAsync` to join an ambient transaction (it currently opens its own — nesting would throw), which is a larger change than warranted.

### F9 — `PropagateAsync` discards partial results when a target fails mid-loop
- **Severity:** MEDIUM
- **Evidence:** each target version is committed in its own transaction inside the loop (SoftwareVersionService.cs:164-210); a non-conflict exception on target N propagates → the API returns 500 and the `results` for targets 1..N-1 (already committed) are lost.
- **Failure scenario:** propagate to 10 packages, target 7 fails → caller sees only an error, retries, and targets 1-6 get duplicate derived versions (new version codes, same content).
- **Minimal fix:** wrap the per-target body in try/catch and append a `{ Status = "failed", Reason = ... }` result item instead of throwing.

### F10 — `ReleaseVersionAsync` loads every file entity just to check existence
- **Severity:** MEDIUM (clear perf win)
- **Evidence:** `.Include(x => x.Files)` (SoftwareVersionService.cs:443-444) materializes and change-tracks the full file list (factory packages = entire app folders, hundreds–thousands of rows) only for `version.Files.Any(f => !f.IsDeleted)` (line 451).
- **Minimal fix:** drop the Include; use `await _context.SoftwareFiles.AnyAsync(f => f.SoftwareVersionId == versionId && !f.IsDeleted)`.

### F11 — 35-line `SoftwareVersionResponse` projection duplicated
- **Severity:** MEDIUM (maintainability, copy-paste > 15 lines ×2)
- **Evidence:** `GetVersionByIdAsync` (SoftwareVersionService.cs:561-594) and `GetLatestReleasedVersionAsync` (SoftwareVersionService.cs:723-759) project field-for-field identically. A new version field must be added in both (and `Metadata` in a third place, the summary projection).
- **Minimal fix:** one `private static readonly Expression<Func<SoftwareVersion, SoftwareVersionResponse>> Project` — the same pattern StationSoftwareAssignmentService already uses (StationSoftwareAssignmentService.cs:420-433).

### F12 — Blob refcount decrements run after/outside the delete transactions
- **Severity:** MEDIUM (crash-consistency, safe direction)
- **Evidence:** `DeletePackageAsync` (SoftwarePackageService.cs:265-268) and `DeleteVersionAsync` (SoftwareVersionService.cs:541-544) decrement after the soft-delete commit; a crash in between leaves refcounts permanently high → blobs never GC'd (disk leak, never dangling). `CompleteUploadAsync` shows the preferred pattern — decrement inside the transaction (SoftwareFileService.cs:236).
- **Minimal fix:** move the decrement loops inside the respective transactions (`DeleteVersionAsync` would need its `SaveChangesAsync` wrapped in `ExecuteInTransactionAsync` like its package-level sibling).

## Unnecessary Code (candidates for deletion)

### U1 — `SoftwareFilesController`: all four endpoints have no callers in the repo
- FE lists files via `GET /software-versions/{id}/manifest` (MProjectFrontend/src/api/software.ts:247-249), uploads via TUS + `upload-init`/`upload-complete`; there is no per-file delete or download UI (`grep deleteFile|download-url|software-files` over MProjectFrontend + MProjectAgent: zero hits). Agents download via presigned URLs in the agent/v1 manifest.
- Dead surface: `GetFilesByVersion`, `DeleteFile`, `GetDownloadUrl`, `GetAdminDownloadUrl` (SoftwareFilesController.cs:23-53) plus service code `DeleteFileAsync`, `GetDownloadUrlAsync`, `GetAdminDownloadUrlAsync`, `GetFilesByVersionAsync`, `LoadFileForDownloadAsync`, `BuildDownloadResponseAsync`, `VerifyComputerEligibilityAsync` (SoftwareFileService.cs:341-446, 672-690) — ~180 LOC + their tests.
- If kept instead of deleted, note that `VerifyComputerEligibilityAsync` ignores `IsActive` (a disabled assignment still authorizes downloads, StationSoftwareAssignmentService semantics say it shouldn't) and allows downloading **Draft** version files (only Deprecated is blocked, SoftwareFileService.cs:376-377) — plus F5/F6 above.
- **Recommendation:** confirm no external API consumer, then delete controller + service methods + tests; or wire the FE to them deliberately.

### U2 — `SoftwarePackage.AutoRemoveOnUnassign` is write-only
- Persisted, defaulted (DBContext.cs:363-365), CRUD'd and audited (SoftwarePackageService.cs:55, 106, 156), exposed to the FE — but **no business logic reads it**. `RemoveAssignmentAsync`/`DeactivateAsync` always emit Uninstall jobs (deliberate 2026-07-09 decision), and the tests document the flag is ignored (`RemoveAssignment_WithoutAutoRemove_StillCreatesUninstallJobs...`, StationSoftwareAssignmentServiceTests.cs:400-445).
- **Recommendation:** decide once — the always-uninstall behavior is the spec, so drop the column/flag end-to-end (entity, requests, responses, FE form, migration). Keeping a visible toggle that does nothing misleads operators.

### U3 — `CompleteUploadAsync` loads the stale file set twice
- SoftwareFileService.cs:213-222: first query projects `{Id, BlobSha256}`, second reloads the same rows as tracked entities by Id. One tracked query serves both the soft-delete loop and `decrementsByHash`.

## Simpler Alternative

No redesign needed — the shape (service + thin controller) is right and the controllers are honest pass-throughs mandated by the permission attributes. The smaller implementation is reached by subtraction:

1. Delete U1 (~180 LOC + tests) and U2 (flag across entity/model/FE) once confirmed.
2. Deduplicate the version projection (F11) and the stale-file double query (U3).
3. The F1/F2/F3 fixes are each a few lines (one cancel loop, `IgnoreQueryFilters` on four queries, one `JobType` predicate) — no new abstractions required.

## Complexity Report

| File | LOC | Notes |
|---|---|---|
| SoftwarePackageService.cs | 371 | DeletePackageAsync ~107 lines, multiple jobs (flagged scale, not redesigned) |
| SoftwareVersionService.cs | 761 | largest; duplication F11; CreateDerivedVersionAsync is dense but justified |
| SoftwareFileService.cs | 692 | ~180 LOC dead if U1 confirmed |
| StationSoftwareAssignmentService.cs | 547 | Deactivate/Remove share ~25 near-identical lines (differ in error codes/audit — acceptable) |
| Controllers (4 files) | 370 | thin, consistent `[RequirePermission]` usage; resource-scoped lookups on per-assignment ops |
| **Total** | **~2,741** | Dependencies: no new packages; service-to-service coupling only ISoftwareVersionService ← SoftwarePackageService (clone) |

## Final Recommendation

**Request changes.** Fix order:

1. **F1** (CRITICAL) — ActivateAsync must cancel pending Uninstall jobs; add the Deactivate→Activate test.
2. **F2 + F3** (HIGH) — apply the known `IgnoreQueryFilters` fix to the four navigation queries; exclude Uninstall jobs from DeleteVersionAsync's cancel. These are the same bug family already fixed once in InstallationJobService — worth a repo-wide grep for `\.SoftwareVersion\.` in Where clauses afterwards.
3. **F4** (HIGH) — one-line lock reuse (`TouchEditableVersionAsync`) inside CompleteUploadAsync's transaction.
4. Decide U1/U2 (delete or wire up), then the MEDIUMs opportunistically.

The core lifecycle (create → upload → release → pin → deploy; clone/propagate; approval-gated assign) is sound, tested, and consistent with the multi-active design. The defects cluster at the *interaction seams* — soft-delete × query filters, deactivate × re-activate, upload × release — not in the primary flows.

---

## Tóm tắt (Tiếng Việt)

- Đã đọc **toàn bộ 8 file** trong scope + context (DBContext, InstallationJobService, TUS handler, seeder, tests). Kết luận: **Request changes** — 1 CRITICAL, 3 HIGH, 8 MEDIUM, 3 nhóm code thừa.
- **CRITICAL (F1):** Deactivate tạo job Uninstall, nhưng **Activate lại không hủy** job đó → bật lại app xong agent vẫn gỡ app, cài lại phải chờ update window. Cần hủy job Uninstall pending trong ActivateAsync.
- **HIGH (F2):** 4 query đi qua navigation `SoftwareVersion` bị global filter `!IsDeleted` nuốt bản ghi của version đã xóa mềm → Deactivate/Remove **không phát job Uninstall** cho app thuộc version đã xóa (chỉ được reconcile vớt lại, trễ + bị gate). Cùng họ bug đã fix bằng `IgnoreQueryFilters` ở InstallationJobService.
- **HIGH (F3):** DeleteVersionAsync hủy luôn cả job **Uninstall** đang pending (không lọc JobType) → app kẹt lại trên trạm, trái thiết kế "job Uninstall sống sót khi xóa version".
- **HIGH (F4):** upload-complete kiểm tra Draft **ngoài** transaction → đua với Release có thể thay toàn bộ file của version đã Released. Fix 1 dòng: gọi TouchEditableVersionAsync trong transaction.
- **Code thừa:** cả `SoftwareFilesController` (4 endpoint, ~180 LOC) không có ai gọi (FE dùng manifest + TUS); cờ `AutoRemoveOnUnassign` chỉ ghi không đọc (remove luôn gỡ app theo thiết kế 2026-07-09) → nên xóa sau khi xác nhận không có client ngoài.
- Phần lõi (create → upload → release → pin → deploy, clone/propagate, approval) chắc chắn, test tốt; lỗi tập trung ở các "đường nối" giữa soft-delete × query filter × job lifecycle.
