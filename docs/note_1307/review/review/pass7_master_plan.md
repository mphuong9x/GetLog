# Backend Deep Review — Pass 7: Cross-Cutting Master Plan

**Date:** 2026-07-15
**Inputs:** `docs/review/pass0_findings.md` … `pass6_findings.md` (all read in full), plus fresh
Grep/read verification of every cross-module duplication hypothesis (evidence quoted below).
**Report-only** — no code changed.

> Note: `docs/skills/review_rule.md` does not exist in the repository (`docs/skills/` is empty —
> the same situation passes 2–6 recorded). This pass applies the decision rules as restated in the
> review request: prefer removal over cleanup; no new abstraction unless it replaces **3+ real
> duplications**; flag inconsistency only where it can cause a real bug.
> Paths are relative to `MProjectBackend/` unless noted. Effort: **S** ≤ 1h, **M** ≤ ½ day, **L** ≥ 1 day.

---

## A. Deduplicated master findings

Merging rules applied: findings describing the same *mechanism* across modules are merged into one
family row (instances listed); findings mooted by a pass-0/1 deletion are marked resolved-by-removal.
Origin IDs are `P<pass>-F<n>`.

### CRITICAL

| ID | Origin | Finding | Where | Fix | Effort |
|----|--------|---------|-------|-----|--------|
| M1 | P1-F1 | Re-activating an assignment leaves its pending **Uninstall** jobs alive → agent uninstalls an enabled app; reinstall waits for the next update window | StationSoftwareAssignmentService.cs:134-151 | In `ActivateAsync`, cancel Pending/Downloading/Installing `Uninstall` jobs for the station+package (with `IgnoreQueryFilters`, per M2); add Deactivate→Activate test with installed record | S |

### HIGH

| ID | Origin | Finding | Where | Fix | Effort |
|----|--------|---------|-------|-----|--------|
| M2 | P1-F2, P2-F1, P2-F5, P2-oos, P5-F1(read half), P6-F15 | **Soft-delete query-filter family**: navigations/Includes to `ISoftDeletable` entities silently drop rows. Instances: uninstall-emission queries (StationSoftwareAssignmentService.cs:445, 465, 480; SoftwarePackageService.cs:222-239), admin job list Total≠Items (InstallationJobService.cs:569-610), drift report (PcInventoryService.cs:75-151), approval reads via required `Requester`/`Policy`/`Actor` navs (ApprovalService.cs:272-342). Root enabler: the EF warning for exactly this hazard is globally suppressed (Program.cs:50-51) | multiple | Add `IgnoreQueryFilters()` / project scalars with `??` fallbacks at each instance; **remove the global warning suppression** and triage what it raises; repo-wide grep for `.SoftwareVersion.` / `.Requester` in Where/Select as a closing sweep | M |
| M3 | P1-F3 | `DeleteVersionAsync` cancels pending **Uninstall** jobs (no JobType filter), contradicting the deliberate "uninstall survives version delete" design | SoftwareVersionService.cs:521-533 | Add `&& j.JobType != InstallationJobType.Uninstall`; test with a seeded Uninstall job | S |
| M4 | P1-F4 | `CompleteUploadAsync` checks Draft **outside** the transaction → race with Release can replace a Released version's files | SoftwareFileService.cs:205-294 | Call existing `TouchEditableVersionAsync(version.Id)` first inside the transaction | S |
| M5 | P2-F2 | `ConfigBaselineValidator` accepts `stationResourceId` and ignores it → baseline validation never accounts for override files; out-of-baseline configs deploy | ConfigBaselineValidator.cs:34-101 | Decide: substitute resolved overrides before evaluating rules (reuse `IOverrideResolver`), **or** delete the parameter from interface + both call sites | M |
| M6 | P3-F1 + P3-F6 (+P4-F6 contract) | `CreateAclEntryAsync` never checks the actor holds the permission it grants → privilege escalation via ACL; currently contained only by the controller's global gate, which is itself the inconsistency (scoped delegation advertised by services, unreachable via API) | AuthorizationMutationService.cs:110-154; AuthorizationController.cs:15-18 | Mirror `EnsureRolePermissionsGrantableAsync` for Allow entries **first**; then make the global-vs-scoped contract decision once for Authorization + Assets + Org (see D5); add the escalation test | M |
| M7 | P4-F1 | Every heartbeat past the 30-day threshold regenerates the pending rotation token → retried/out-of-order heartbeat permanently locks the agent out | AgentService.cs:410-422 | Guard regeneration with existing `IsPendingTokenUsable`; add double-heartbeat test | S |
| M8 | P5-F1 | Soft-deleting a user makes their pending approval requests invisible everywhere while they keep the one-pending-per-target slot and lock the target in `PendingApproval` | UserService.cs:493-519; ApprovalService.cs:272-342 | Both halves: cancel the user's Pending requests in `DeleteUserAsync` (mirror OverrideFile delete, incl. `OnRejectedAsync`); stop depending on filtered required navs in the three read paths (part of M2 sweep) | M |
| M9 | P5-F2 | `SubmitAsync` accepts requests with an **empty approver set**; the seeded SOFTWARE_ASSIGNMENT policy (exact station-scope match, no global fallback, no hierarchy walk) makes this the default outcome | ApprovalService.cs:88-117; ApprovalApproverResolver.cs:135-143 | Fail-fast: resolve step-0 approvers at submit, throw 409 when empty; decide whether Resource scope accepts null-scoped grants like Team scope does | M |
| M10 | P6-F1 | Git smart-HTTP can't serve the real `git` CLI the UI advertises: no gzip request decompression, 30 MB Kestrel cap on push, no child-process lifecycle (zombie `git` per aborted push) | GitRepositoryService.cs:233-289; GitRepositoriesController.cs:54-88 | Gzip-wrap request body / `UseRequestDecompression`; `[DisableRequestSizeLimit]` on ReceivePack; close stdin, wire `RequestAborted`, kill process tree on failure; first real clone/push integration test. Do inside the git batch (M35) | M–L |
| M11 | P6-F2 | Restoring a user writes the full entity **including BCrypt PasswordHash** into the audit log, retrievable via the audit API | AdminService.cs:63 | Pass a projection `{Id, Username, Name, Status}`; add `[JsonIgnore]` to `User.PasswordHash`; consider cleanup of existing `user.restore` rows | S |
| M12 | P6-F3 | Tus completion rollback deletes the storage object even when a concurrent registration now references it (dedup'd SHA path) → silent data loss for the winner | TusUploadHandler.cs:205-247 | Re-check `Blobs.AnyAsync(b => b.Sha256 == checksum)` before the rollback delete; map the concurrent-blob-insert unique violation to retry/409 | S |

### MEDIUM

| ID | Origin | Finding | Where | Fix | Effort |
|----|--------|---------|-------|-----|--------|
| M13 | P2-F3 + P4-F2 | **Watchdog read-then-write family**: job watchdog can overwrite a concurrently-Completed job to Failed; liveness watchdog flips just-heartbeated computers Offline + emits spurious `WentOffline` events | InstallationJobWatchdogService.cs:98-152; ComputerLivenessWatchdogService.cs:75-114 | Conditional set-based updates repeating the full predicate; emit events only for rows actually flipped | M |
| M14 | P4-F4 + P5-F7 + P6-F11 | **Unfiltered unique index vs filtered pre-check family**: recreating a soft-deleted Computer's MAC / Department's name / User's username is a deterministic unmapped 500 (pre-check runs under the global filter; index has no `IsDeleted=false` filter) | DBContext.cs:71-76, 152-154 | One decision, applied to all three: filter the indexes (name reusable) **or** `IgnoreQueryFilters()` pre-checks + 409 mappings; one migration | M |
| M15 | P3-F7 + P4-F7(race) + P5-F7(races) + P6-F7(c) | **Unique-violation-race family**: concurrent duplicate creates (role assignment, ACL entry, agent release activate, product-group name, user-team, repo member) surface as raw 500 instead of 409 | multiple | Catch `DbUpdateException`/map constraint names in `GlobalExceptionHandler`; extend `DbConstraintNameTests` | M |
| M16 | P4-F3 + P4-F9 | Agent-supplied input unvalidated: duplicate `SlotIndex` → 500, `Slots:null` → NRE, negative counters persisted; no length caps on Hostname/LastError/etc. on the hottest write path | AgentService.cs:854-894, 400-613; AgentModels.cs | One validation pass + one shared clamp helper (also replaces the `NormalizeOptional` ×2 duplication); matching `HasMaxLength` config | M |
| M17 | P4-F10 + **X1 (new)** | Legacy status mapping now exists in **3 places and has already drifted**: `ComputerStatusMapper.DeriveLegacy`, `ComputerService.ProjectToDto:249-254`, and `DepartmentOwnershipService.cs:187-191` — the third copy **omits `CrashLoop → Error`** (a crash-looping computer shows Online/Offline on the department page) and doesn't coalesce `Agent.AgentVersion` | ComputerStatusMapper.cs:7-23; ComputerService.cs:239-274; DepartmentOwnershipService.cs:176-204 | Reuse `ComputerService.ProjectToDto` (make it `internal static`) in DepartmentOwnershipService; decide Offline-vs-Updating precedence once; add `(Offline, Updating)` + `CrashLoop` mapper tests | S–M |
| M18 | P3-F2 | Owner shortcut evaluated before ACL Deny → explicit Deny can't revoke an owner's access | AuthorizationService.cs:154-189 | Check for matching Deny before returning `owner_shortcut`; add owner-vs-Deny test | S |
| M19 | P3-F3 | `GetVisibleResourceIdsAsync` ignores ACL Deny → denied resources still listed | AuthorizedResourceQueryService.cs:40-95 | Load Deny scopes (+descendants) and `ExceptWith` them; add Deny test | S–M |
| M20 | P3-F4 | `CreateUserAsync` with teams skips the team-scoped `Member` role that `AddTeamMemberAsync` grants → same (user, team) state, different permissions | UserService.cs:324-385 | Add the same scoped Member assignment per team id | S |
| M21 | P3-F5 | Authorization mutation + audit + cache invalidation are three separate saves, no transaction → grant can persist without audit row | AuthorizationMutationService.cs:79-171 | Wrap entity + audit in `ExecuteInTransactionAsync`; invalidate after commit | M |
| M22 | P2-F4 | Baseline validator swallows storage exceptions and mislabels them "file not found", blocking pins with a wrong message and zero logs | ConfigBaselineValidator.cs:103-119 | Inject ILogger, log, distinct message | S |
| M23 | P2-F6 + P2-F7 | Override upload: 50 MB accepted but 5 MB read-back cap → unviewable files; non-seekable stream would store an empty blob under a valid hash | OverrideFileService.cs:24, 85-103 | Enforce `MaxFileSize` at upload; reject non-seekable streams (or buffer) | S |
| M24 | P2-F8 + P5-F3 | N+1 hot paths: override list runs per-row permission checks (~200-300 queries/page); approvals inbox loads all pending requests and runs full resolver per row (~600-800 queries/render) | OverrideFileService.cs:223-224; ApprovalService.cs:269-312 | Batch via `GetVisibleResourceIdsAsync`; invert inbox to resolve the user's grants once and filter in SQL (or at minimum `AsNoTracking` + cap) | M |
| M25 | P2-F9 + P2-F10 | Override detail/content demand a *global* grant while the list honors scoped grants (403 on rows you can see); baseline import/validate read any version's config with no access check on that version | OverrideFilesController.cs:40-54; ConfigBaselineService.cs:194-289 | Resource-level read checks mirroring the delete path; relate baseline's model to the version or check read permission; use-or-drop the dead `userId` param | M |
| M26 | P2-F11 | Manual install records don't reset `DriftStatus`/`DriftSummary` (job path does) → auto-heal redeploys right after a human fixed the machine | PcInstallationService.cs:118-121, 262-268 | Mirror the job-completion reset | S |
| M27 | P2-F12 | Baseline import file lookup uses unanchored `EndsWith` → rules can bind to `Backup/OldConfig.ini` | ConfigBaselineService.cs:220-224 | Anchor like the sibling query at :201 | S |
| M28 | P4-F5 | `GET agent/v1/blobs/local` serves any stored object to any enrolled agent (capability-URL only) | AgentController.cs:132-140 | Require path to match a `Blob` row; prefer scoping to the agent's manifest/overrides/release | S |
| M29 | P4-F7 | `ActivateAsync` non-atomic (sibling `PublishAsync` is transactional) → transient failure leaves **zero** active releases; concurrent activate → raw 500 | AgentReleaseService.cs:124-135 | Wrap in `ExecuteInTransactionAsync`; map the unique violation; decide whether `DeleteAsync` may delete the active release | S |
| M30 | P4-F8 | Unacked agent commands redeliver forever; 16 stuck commands starve everything behind them; no per-computer pending cap | AgentCommandService.cs:28-72 | TTL/expire in the claim query; cap or collapse duplicates in `EnqueueAsync` | M |
| M31 | P5-F4 + P5-F5 + P5-F6 | Approver-resolution edges: arbitrary team pick for multi-team requesters; leader-without-membership silently excluded (remove-member doesn't revoke leadership); per-step `ScopeStrategy` snapshotted but ignored after step 0 | ApprovalApproverResolver.cs:35-52, 213-231; TeamService.cs:139-169, 314-356 | Deterministic team choice; pick + enforce the membership↔leadership invariant on both sides; honor or delete per-step ScopeStrategy | M |
| M32 | P5-F8 + P5-F9 | Handler save-ownership asymmetric (one hook self-saves mid-unit-of-work); removing an assignment leaves a phantom pending request (Approve → 404) — the "cancel pending approvals on target deletion" convention exists only in the OverrideFile flow | SoftwareAssignmentApprovalHandler.cs:40-67; StationSoftwareAssignmentService.cs:190-228 | Pin the contract on `IApprovalTargetHandler` (service saves); cancel pending requests in `RemoveAssignmentAsync`/`UnassignAsync`; align both hooks' missing-target behavior | M |
| M33 | P5-F10 + P6-F10 | Exception pipeline: `NotSupportedException` from plain user input → 500; DbContext wraps `DbUpdateConcurrencyException` in IOE, killing the handler's dedicated branch and echoing raw IOE messages (incl. framework ones) to clients as 409 | ApprovalService.cs:44-48; DBContext.cs:854-865; GlobalExceptionHandler.cs | Map NotSupported → 400; rethrow `ConcurrencyException` (or original); reserve message-echo for domain exception types | S–M |
| M34 | P6-F4 | Unversioned-`/api` rewrite middleware is provably dead (implicit UseRouting runs first; empirically confirmed) — FE only works via its axios client-side rewrite | Program.cs:380-389 | Add explicit `app.UseRouting()` after the rewrite **or** delete the middleware and declare `/api/v1` the contract; one integration test | S |
| M35 | P6-F5, F6, F7, F9, F14, F16 (+M10) | **Git hardening batch** (the feature is markedly less hardened than the rest): bare `Exception` → 500 for 403/404 cases; soft-delete leaves the `.git` dir squatting the name forever (recreate → 500); zero model config (NOT NULL Description fed nullable → 500, no (owner,slug)/(repo,user) uniqueness, unbounded text); auth filter swallows every exception as 401 + culture-sensitive `ToLower()`; repos default **Public** (anonymous clone); per-branch full-history commit counts | GitRepositoryService.cs; GitBasicAuthFilter.cs; DBContext.cs:55-56; RepositoryModels.cs:27-28 | One batch: exception types, dir trash-on-delete + create rollback, migration (nullable Description, filtered unique indexes, max lengths), narrow catch + `ToLowerInvariant`, Private default, drop/cache branch counts — plus the first clone/push integration tests | L |
| M36 | P6-F8 | AdminService restore resurrects only the root entity: teams come back without Resource/memberships/assignments; SoftwareFile restore skips the blob refcount re-increment (permanent drift; can resurrect a file whose content was GC'd) | AdminService.cs:29-88 | Per-type inverse-of-delete (needs one product decision per entity); refuse SoftwareFile restore when blob gone; tests (none exist) | M |
| M37 | P6-F12 | TusTempCleanup default target is `Path.GetTempPath()` → recursively deletes the machine-wide temp dir; per-file aging can corrupt multi-file resumable uploads | TusTempCleanupService.cs:34-60 | Refuse to run against the OS temp root (require config); prefer tus `Expiration` + `RemoveExpiredFilesAsync` | S |
| M38 | P6-F13 | Rate-limit partitions key on RemoteIpAddress with default `KnownProxies` → behind any non-loopback proxy, all users share one 10-req/min auth bucket | Program.cs:151-168 | Bind `KnownProxies`/`KnownNetworks` from config with fail-fast; document in appsettings | S |
| M39 | P6-F17 + P5-F11 | Read-path hygiene: audit-log queries have no supporting index, track entities, tables grow forever; `GetMyRequestsAsync` unpaged + tracked (jsonb comparer churn) | AuditLogsController.cs:67-99; ApprovalService.cs:314-342 | Index `(TargetType, TargetId, CreatedAt DESC)`; `AsNoTracking` sweep; paginate `mine`; pick a retention story | M |
| M40 | P1-F6 | Permission-tier inconsistency: Crud-only role can hard-delete a **Released** deployed version but cannot deprecate it (and upload-complete wipes all files under a weaker permission than single-file delete — moot if U1 deletion lands) | SoftwareVersionsController.cs:99-121 | Require `PublishSoftwareVersion` for non-Draft delete (or reject deleting Released) | S |
| M41 | P1-F7 | Package overview counts disabled assignments as deployed stations | SoftwarePackageService.cs:355-360 | Add `&& x.IsActive` | S |
| M42 | P1-F8 + P1-F9 | Clone non-atomic (empty package + success audit on failure); Propagate discards committed partial results on mid-loop failure (retry → duplicate derived versions) | SoftwarePackageService.cs:113-124; SoftwareVersionService.cs:164-210 | Audit after both succeed; per-target try/catch appending `failed` results | S–M |
| M43 | P1-F10 | `ReleaseVersionAsync` materializes every file entity for an existence check | SoftwareVersionService.cs:443-451 | Drop the Include; `AnyAsync` | S |
| M44 | P1-F12 | Blob refcount decrements run outside the delete transactions (crash → refcounts permanently high) | SoftwarePackageService.cs:265-268; SoftwareVersionService.cs:541-544 | Move decrements inside the transactions | S |

Resolved by removal (no fix needed if Phase 2 lands): **P1-F5** (DeleteFile dangling icon/watch refs) and the file-delete half of **P1-F6** — mooted by deleting `SoftwareFilesController` (U1); **P1-F11** (SoftwareVersionResponse projection ×2) — copy #2 lives in `GetLatestReleasedVersionAsync`, which pass 0 deletes (#6).

---

## B. Cross-module duplication — hypothesis verification (this pass's own work)

Every count below was Grep/read-verified today.

### B1. PagedResult construction — **confirmed, 25 hand-rolled sites**
`new PagedResult<...>` appears **25×** (23 in services across Software/Assets/Organization/Identity/
Approvals, 2 in controllers). Every site follows the same shape: `CountAsync` → `Skip(request.Skip)
.Take(request.PageSize)` → a byte-identical 7-line object initializer (`Items/Total/Page/PageSize`).
Clamping is already centralized in `PagedRequest` (PagedResult.cs:13-34).
**Verdict: qualifies (25 ≥ 3).** Propose the *minimal* helper only — a static factory
`PagedResult<T>.Create(items, total, request)` replacing the 7-line initializer (~150 LOC net).
Do **not** build an `IQueryable` extension: ~10 of the 25 sites paginate in memory, group, or
branch (ApprovalService inbox, PcInventoryService GroupBy, ComputerService/OverrideFileService
dual-return), and forcing them through a query-shaped abstraction would be worse than the
duplication. Mechanical, low value → Phase 4.

### B2. Permission-check preamble — **confirmed, 7 identical copies; recommend removal, not extraction**
The 6-line private helper `EnsureAuthorizedAsync(actorId, action)` (CheckPermission → throw
`UnauthorizedAccessException`) is duplicated verbatim in **7 services**: UserService:38,
RoleService:37, TeamService:38, ProductGroupService:29, DepartmentService:29, StationService:36,
ModelService:32 — and absent in ComputerService, AgentReleaseService, DepartmentOwnershipService,
ApprovalService. Passes 4–5 grep-confirmed no non-controller callers of the doubled methods: every
call re-checks the same **global** permission `[RequirePermission]` already enforced.
**Verdict: per "prefer removal over cleanup", the primary recommendation is to delete the
service-level double checks**, not to extract a shared helper — *after* the M6 global-vs-scoped
contract decision (if the contract moves per-action authz into services, invert: keep them and
delete the 7-fold duplication by one extension on `IPermissionService`). Either endpoint of the
decision eliminates the 7 copies. Note: several service tests assert the unauthorized-throw
behavior and would move to controller-level coverage.

### B3. Entity→DTO projection duplicated list vs detail — **confirmed, 5 DTOs, one already drifted**
- `SoftwareVersionResponse` ×2 (SoftwareVersionService:561-594 vs :723-759) — **resolved by
  deleting** `GetLatestReleasedVersionAsync` (pass-0 #6).
- `PcInstallationRecordResponse` ×2 (PcInstallationService:176-187, :239-250).
- `ModelDto` ×**3** (ModelService:200-216, :232-247, **+ DepartmentOwnershipService:81-94** —
  cross-module copy the per-module passes couldn't see).
- `StationDto` ×**3** (StationService:177-190, :206-218, **+ DepartmentOwnershipService:125-138**).
- `ComputerDto` ×2 (ComputerService `ProjectToDto`:239-274 vs DepartmentOwnershipService:176-204)
  — **already drifted**: the copy omits `CrashLoop → Error`, doesn't coalesce
  `Agent.AgentVersion`, and sources `CreatedAt` from `Resource` (M17/X1).
**Verdict: qualifies — but no new abstraction is needed.** The codebase already has the pattern
(`static readonly Expression<Func<T, Dto>>` in ComputerService, StationSoftwareAssignmentService,
OverrideFileService). Apply it: one expression per DTO, made `internal static` so
DepartmentOwnershipService reuses it instead of re-rolling.

### B4. "Get by id or throw NotFound" — **rejected, no helper**
119 `throw new KeyNotFoundException` sites across 29 service files, but the shape is a uniform
idiomatic 2-liner (`FirstOrDefaultAsync(...) ?? throw new KeyNotFoundException("<entity> not
found")`) with per-site predicates, Includes, and messages. A generic helper would save ~1 line per
site while obscuring query composition. Not near-identical beyond the trivial idiom → per the
decision rules, **no abstraction**. The only action item is GitRepositoryService, which deviates
from the idiom by throwing bare `Exception` (covered by M35).

### B5. Watchdog/background-service scaffolding — **confirmed, 5 near-identical copies + 1 dangerous outlier**
6 `BackgroundService` implementations. **Five** share a near-identical ~45-line scaffold
(fields + ctor + `ExecuteAsync` loop: options-monitored interval, cancel-safe delay, Enabled gate,
try/catch-isolated sweep): InstallationJobWatchdogService, StationRollbackWatchdogService,
ComputerLivenessWatchdogService, DomainEventDispatcherService (identical modulo options type/log
strings/interval unit) and BlobGcService (differs only in daily-schedule delay computation).
The sixth — **TusTempCleanupService:23-30 — is the outlier and a real bug**: its loop has **no**
try/catch around `CleanupAsync`, and in .NET 6+ an unhandled `BackgroundService` exception
**stops the host** by default (`BackgroundServiceExceptionBehavior.StopHost`). A transient
`IOException` from `Directory.EnumerateFiles` (directory deleted mid-scan, network temp path) can
take the whole API down — the other five services all isolate exactly this.
**Verdict: qualifies (5 ≥ 3).** One small base class (`IntervalSweepService<TOptions>` with
abstract `SweepAsync`, virtual delay computation for BlobGc) removes ~180 duplicated LOC, folds
TusTempCleanupService in (fixing its missing error isolation as a side effect), and gives M13's
conditional-update fixes a single home. This is the one new abstraction this pass proposes.

### B6. Bonus (from pass 3, cross-module, qualifies): active-grants-for-subject predicate ×4
The security-critical subject + time-window predicate (`(User && SubjectId==userId) || (Team &&
teamIds.Contains(...))` + Start/EndTime window) is hand-written in RbacGrantQueryService:64-69,
AclQueryService:68-72, AuthorizedResourceQueryService:43-47/:56-60, and AuthService.GetMeAsync:202-205.
4 copies of the predicate whose drift would be a security bug (they already disagree with
TeamService on `EndTime > now` vs `>= now`). One shared `WhereActiveForSubject` extension. → Phase 3.

---

## C. Cross-module consistency — inconsistencies that can cause real bugs

Only bug-capable divergences listed (uniformity-only nits deliberately dropped):

1. **Legacy computer status ×3, drifted (M17/X1)** — proven: CrashLoop machines show the wrong
   status on the department-ownership page *today*.
2. **Soft-delete unique indexes**: Model/Station/Team/ProductGroup/RoleAssignment/AgentRelease
   indexes are `IsDeleted`-filtered; **Computer.MacAddress, Departments.Name, Users.Username are
   not** while their pre-checks run under the filter → three deterministic 500s (M14).
3. **Soft-delete navigation family (M2)** — the same mechanism produced bugs in four modules;
   InstallationJobService fixed it once, siblings didn't inherit the fix, and the EF warning that
   would have flagged every instance is globally suppressed.
4. **"Cancel pending approvals when the target/actor disappears"** is implemented in the
   OverrideFile delete flow only — user deletion (M8) and assignment removal (M32) both strand
   Pending requests.
5. **Global-vs-scoped authz gate** — services implement scoped delegation
   (visibility filtering, scoped `EnsureManagePermission`), controllers pin global grants, so the
   scoped paths are dead via HTTP in three modules (P3-F6, P4-F6, dept-ownership). Fail-closed, but
   it is the only thing containing the M6 escalation → the two must be decided together.
6. **Watchdog error isolation** — 5 services isolate sweep exceptions; TusTempCleanupService
   doesn't → host-stop risk (B5).
7. **Username casing**: `ToLower()` in GitBasicAuthFilter vs `ToLowerInvariant()` in
   AuthService/UserService — same user can fail git Basic auth under Turkish-İ class locales (M35).
8. **Sibling transactionality**: `ActivateAsync` non-atomic vs transactional `PublishAsync` (M29);
   mutation+audit+invalidation split saves (M21); handler save-ownership split (M32).
9. **Read-then-write duplicate checks** backed by unique indexes surface races as raw 500s in five
   modules while `RegisterAsync`/`CreateUserAsync` translate them cleanly (M15).

---

## D. Deletion list (pass 0 confirmed + later passes)

### D1. Safe to delete now (pass 0, double-grep-verified, ~225 LOC)
1. `RepositoryAction.cs` (whole file)
2. `AssignmentEffect.cs` (whole file)
3. `CreateSoftwarePackageResponse` (SoftwareModels.cs:71)
4. `UpdateFileContentRequest` (ResourceModels.cs:57)
5. `DeleteFolderAsync` — interface + 2 impls (+ `RemoveBatchAsync`/`BatchDeleteSize` cascade) + 8 test fakes
6. `GetLatestReleasedVersionAsync` — interface + impl (**also resolves P1-F11 duplication**)
7. `CheckPermissionDetailedAsync` — interface + impl + `PermissionResult.cs` whole file + 5 test fakes
8. `InvalidatePermissionMap` — interface + impl + 5 test fakes (pass 3 re-confirmed)
9. `GenerateNextVersionCodeAsync` — off the interface, method → private
10. `MProject.Api.http` (scratch file)

Plus untracked disk artifacts: `Backup/`, `.vs/`, `.verify-build/` (~200 MB); git-index casing fix
for `Mproject.Domain/`.

### D2. Delete after one confirmation each
| Item | Confirm | Origin |
|------|---------|--------|
| `SoftwareFilesController` + service methods (~180 LOC + tests) | no external API consumer (FE/agent confirmed not using it) | P1-U1 — also moots P1-F5 and half of M40 |
| `SoftwarePackage.AutoRemoveOnUnassign` (entity/DTO/FE/migration) | always-uninstall is the spec (tests already document the flag is ignored) | P1-U2 |
| `AppPermissions.ApproveSoftwareAssignment` | not reserved for planned per-target approval check | P0 |
| `InstallationJobType.Rollback`, `ComputerStatus.Registered`, `ResourceStatus.Archived` enum members | wire/DB consumers (FE mapping, old rows) | P0 |
| `ConfigBaselineValidator.stationResourceId` param | only if M5 decides *against* station-aware validation | P2 |
| `ApprovalAction.IsAuto` column + DTO field | auto-approval not on roadmap | P5 |
| `Repository.DefaultBranch` column | unless wired into `GetCommitsAsync` in M35 | P6 |

### D3. Small dead-code sweep (no confirmation needed, pass 2–6 verified)
- No-op `.Include(f => f.Blob)` under a projection (ConfigBaselineValidator.cs:60)
- Dead `userId` param in `ConfigBaselineService.ValidateAsync` (use for M25 or drop)
- Dead announce `assignmentState` conditional (AgentService.cs:252-255)
- `"general"` rate-limit policy defined, attached to nothing (Program.cs:199-205)
- `httpContext.Items["ClaimsPrincipal"]` written, never read (TusUploadHandler.cs:102)
- Duplicate JWT validation + repeated permission check in TusUploadHandler (use `httpContext.User`)
- `AddScoped<IAppDbContext, AppDbContext>()` → forward to the `AddDbContext` instance (Program.cs:54)
- Unreachable `DbUpdateConcurrencyException` handler branch (falls out of M33)
- `LocalStorageOptions.RootPath = "D:\\limitfile"` default → validate-or-throw
- The 7 `EnsureAuthorizedAsync` copies (B2 — pending the M6 contract decision)

### D4. Explicitly KEEP (earlier "dead?" candidates, adjudicated)
- `IApprovalNotificationService`/NoOp + counting test fake — pass 5 verdict: correct seam, keep.
- `IApprovalTargetHandler` abstraction — earning its keep; fix its contract (M32), don't fold.
- All `IgnoreQueryFilters()` calls in InstallationJobService/PcInstallationService — deliberate
  bug fixes; protect via the query-helper extraction (Phase 3), never "simplify" away.
- Redundant explicit `!IsDeleted` predicates under global filters — harmless; optional one-sweep
  consistency pass only.

---

## E. Phased fix plan

Each sub-phase is sized for one working session and **ends with: `dotnet build MProject.slnx` +
run the full test suite** (500+ tests). Order within a phase = order listed.

### Phase 1 — CRITICAL + hotfix-grade HIGHs (1 session)
Small diffs, worst failure modes; no design decisions needed.
1. **M1** ActivateAsync cancels pending Uninstall jobs (+ Deactivate→Activate test).
2. **M7** Token rotation guarded by `IsPendingTokenUsable` (+ double-heartbeat test).
3. **M11** Audit projection for user restore (+ `[JsonIgnore]` on PasswordHash).
4. **M12** Blob-existence re-check before tus rollback delete.
5. **M3** `JobType != Uninstall` in DeleteVersionAsync's cancel (+ test).
6. **M4** `TouchEditableVersionAsync` inside CompleteUploadAsync's transaction.
→ build + full test suite.

### Phase 2 — Deletions (1 session)
1. D1 safe-now list (10 items, ~225 LOC incl. test-fake lines).
2. D3 dead-code sweep (mechanical items only; leave the EnsureAuthorizedAsync copies for 3b).
3. D2 items whose confirmations have arrived (batch U1/U2 together — they touch the same FE surface).
4. Disk artifacts + `.gitignore` check.
→ build + full test suite (deleting fakes must compile).

### Phase 3 — HIGH bugs + dedup that prevents their recurrence
**3a — soft-delete/query-filter sweep (1 session):**
M2 all instances (`IgnoreQueryFilters`/projections) + M8 both halves + remove the EF warning
suppression (Program.cs:50-51) and triage what it surfaces + extract the
`ActiveJobsWithManifestData` query helper (P2 Simpler-#3) so the deliberate `IgnoreQueryFilters`
usage is single-sourced. Closing grep for `\.SoftwareVersion\.|\.Requester` in query lambdas.
→ build + full test suite.

**3b — authorization contract (1 session):**
M6 (ACL grantability check first, then the one global-vs-scoped decision spanning pass-3/4/5
surfaces, incl. the fate of the 7 `EnsureAuthorizedAsync` copies per B2), M9 (empty-approver
fail-fast + Resource-scope null-grant decision), M18, M19, B6 shared `WhereActiveForSubject`
predicate. Add the escalation + Deny tests.
→ build + full test suite.

**3c — git hardening batch (1–2 sessions):**
M10 + M35 + M14's git-adjacent piece (repo indexes) + first clone/push integration tests against
TestServer. Treat the feature as pre-production until this lands.
→ build + full test suite.

**3d — cross-module dedup (1 session):**
B5 `IntervalSweepService<TOptions>` base folding the 5 watchdog scaffolds + TusTempCleanupService
(also fixes its host-stop hole; do M37's path guard here), B3 DTO projections (one
`internal static` expression per DTO; fixes M17's drift as a side effect + decide Offline-vs-Updating
precedence), M5 decision (validator: implement or delete the parameter).
→ build + full test suite.

### Phase 4 — MEDIUMs (grouped by module, 1 session each)
**4a — deployment/software:** M13 (watchdog conditional updates), M22, M23, M26, M27, M40–M44,
M29, M30. → build + full test suite.
**4b — identity/approvals/organization:** M20, M21, M24, M25, M31, M32, M33. → build + full test suite.
**4c — platform/config/hygiene:** M14 (index decision + migration), M15 (409 mappings +
`DbConstraintNameTests`), M16 (agent input validation + shared clamp), M28, M34, M36, M38, M39,
B1 `PagedResult.Create` factory (optional, mechanical). → build + full test suite.

Test-debt to fold into whichever phase touches the area: StationService (zero tests),
DepartmentService/ProductGroupService/DepartmentOwnershipService (zero tests), AdminService (zero
tests), GitRepositoryService (2 tests), TusUploadHandler (zero tests), `GetJobsAsync` (zero tests).

---

## Tóm tắt (Tiếng Việt)

- **Đây là pass tổng hợp xuyên module**: đã đọc đủ 7 file findings (pass 0–6), gộp trùng lặp thành
  **1 bảng master duy nhất: 1 CRITICAL (M1 — Activate không hủy job Uninstall), 11 HIGH, 32 MEDIUM
  (sau khi gộp các finding cùng cơ chế thành "family")**. File `docs/skills/review_rule.md` vẫn
  không tồn tại (như pass 2–6 đã ghi nhận) — áp dụng đúng luật trong yêu cầu: ưu tiên xóa, chỉ đề
  xuất abstraction khi thay được 3+ chỗ trùng thật.
- **Kiểm chứng 5 giả thuyết trùng lặp xuyên module bằng grep** (kết quả mới của pass này):
  1. `new PagedResult` viết tay **25 chỗ** → đề xuất 1 factory tối thiểu (không làm extension trên
     IQueryable vì ~10 chỗ phân trang trong RAM/GroupBy);
  2. helper `EnsureAuthorizedAsync` **trùng nguyên văn ở 7 service** trong khi 4+ service khác không
     có → khuyến nghị **xóa** (controller đã gate, không có caller ngoài controller), chốt cùng
     quyết định contract global-vs-scoped (M6);
  3. projection list-vs-detail trùng ở **5 DTO** — đặc biệt `DepartmentOwnershipService` chép tay
     bản thứ 3 của ModelDto/StationDto và bản thứ 2 của ComputerDto **đã lệch thật**: máy CrashLoop
     hiển thị sai trạng thái trên trang phòng ban (phát hiện mới X1, gộp vào M17) → dùng lại pattern
     `static Expression` sẵn có, không cần abstraction mới;
  4. "get by id or throw" có 119 chỗ nhưng là idiom 2 dòng, message khác nhau → **bác bỏ**, không
     làm helper;
  5. **5 background service trùng ~45 dòng scaffold** → đề xuất abstraction MỚI duy nhất của pass
     này: `IntervalSweepService<TOptions>`; gộp luôn `TusTempCleanupService` — service này đang
     **thiếu try/catch quanh sweep, exception sẽ dừng cả host** (.NET StopHost), là bug thật.
- **Nhất quán (chỉ ghi chỗ gây bug thật):** 3 unique index không lọc IsDeleted (MAC/tên
  Department/Username → 500 chắc chắn khi tạo lại); họ bug soft-delete × navigation lan 4 module
  trong khi warning EF bị tắt toàn cục; quy ước "hủy approval khi target/user biến mất" chỉ có ở
  OverrideFile; `ToLower` vs `ToLowerInvariant` ở git auth; watchdog nào cũng cách ly lỗi trừ Tus.
- **Danh sách xóa:** ~225 LOC xóa ngay (10 mục pass 0, xóa `GetLatestReleasedVersionAsync` đồng
  thời giải quyết luôn duplication P1-F11 — đúng tinh thần "xóa thay vì dọn"), ~180 LOC
  `SoftwareFilesController` + cờ `AutoRemoveOnUnassign` chờ xác nhận, cùng loạt dead-code nhỏ pass
  2–6. Giữ lại: notification seam, handler abstraction, mọi `IgnoreQueryFilters` chủ đích.
- **Kế hoạch 4 phase, mỗi phiên kết thúc bằng build + chạy toàn bộ test:** Phase 1 = CRITICAL + 5
  HIGH vá nhỏ (M1, M7, M11, M12, M3, M4); Phase 2 = xóa code chết; Phase 3 = 4 phiên (3a sweep
  query-filter, 3b contract phân quyền + chống leo thang ACL, 3c gia cố git, 3d dedup watchdog/DTO);
  Phase 4 = 3 phiên MEDIUM theo module. **Chưa sửa gì — report-only đúng yêu cầu.**
