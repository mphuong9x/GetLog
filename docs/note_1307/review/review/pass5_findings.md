# Backend Deep Review — Pass 5: Approvals & Organization

**Date:** 2026-07-14
**Scope:**
- `MProject.Application/Services/Approvals/` (5 files: ApprovalService, ApprovalApproverResolver,
  NoOpApprovalNotificationService, OverrideFileApprovalHandler, SoftwareAssignmentApprovalHandler).
- `MProject.Application/Services/Organization/` (4 files: DepartmentService,
  DepartmentOwnershipService, TeamService, ProductGroupService).
- `MProject.Api/Controllers/Approvals/ApprovalsController.cs` +
  `MProject.Api/Controllers/Organization/` (DepartmentsController, DepartmentOwnershipController,
  ProductGroupsController, TeamsController).

**Context read (not in scope):** `IApprovalService` / `IApprovalTargetHandler` /
`IApprovalApproverResolver` / `IApprovalNotificationService` / `IPermissionService` /
`IResourceLookupService`, the Approvals request/response models + `PagedRequest`/`PagedResult`,
the Approvals entities (`ApprovalPolicy/Step/Request/Action/StepSnapshot`) and Organization
entities (`Department`, `Team`, `UserTeam`, `RoleAssignment`, `User`), `DBContext` (soft-delete
query filters, `Version` concurrency token, unique indexes), `GlobalExceptionHandler`,
`PermissionAuthorizationFilter` / `RequirePermissionAttribute`, `BaseController`,
`ResourceNameNormalizer`, `AppDbSeeder` (seeded approval policies), and the cross-flow callers
`UserService.DeleteUserAsync`, `OverrideFileService.DeleteOverrideFileAsync`,
`StationSoftwareAssignmentService` (Assign/Unassign/Remove). Tests read: ApprovalServiceTests,
ApprovalApproverResolverTests, OverrideFileApprovalHandlerTests,
SoftwareAssignmentApprovalHandlerTests, TeamServiceTests.

> Note: `docs/skills/review_rule.md` does not exist in the repository (same situation as passes
> 3–4). This report follows the output format and severity scale used by passes 2–4
> (CRITICAL / HIGH / MEDIUM; LOW omitted). All paths below are relative to `MProjectBackend/`.

---

## Summary

The approval core is well-defended where it matters most: the DB enforces at most one pending
request per target (`UX_ApprovalRequests_Target_Pending`, mapped to a clean 409 in
`GlobalExceptionHandler`), `ApprovalRequest.Version` is a real concurrency token so two approvers
racing on the same step degrade to a clean 409 instead of double-advancing, the steps snapshot is
frozen as jsonb at submit time so later policy edits can't mutate in-flight requests, and
self-approval is blocked in both the approver computation and `CanActAsync`. The organization
services get the one genuinely scoped authorization decision in this scope right: team membership
management accepts *either* `team.manage` scoped to the team's resource *or* an active TeamLeader
assignment, and both paths are tested.

**2 HIGH and 9 MEDIUM findings survived verification; no CRITICAL.** Both HIGHs are silent
flow-breakers in approver/requester resolution (the named domain focus): (F1) soft-deleting a user
makes their pending approval requests invisible everywhere (the `Include`d required `Requester`
navigation is query-filtered, so the rows drop out of inbox/detail/by-target) while the requests
keep occupying the one-pending-per-target slot and keep the OverrideFile locked in
`PendingApproval` — `DeleteUserAsync` cancels nothing; (F2) `SubmitAsync` never checks that the
resolved approver set is non-empty, and the seeded SOFTWARE_ASSIGNMENT policy requires a
TeamLeader assignment scoped *exactly* to the station resource (no hierarchy walk-up, and — unlike
Team scope — no global-grant fallback), so with the role-assignment tooling that exists
(team-scoped only, unless an admin uses the raw authz API) the default outcome is a request nobody
can ever act on. The MEDIUMs cluster into: approver-resolution edge cases (arbitrary team pick for
multi-team requesters; leaders-who-aren't-members silently excluded; per-step `ScopeStrategy`
ignored after step 0); inbox performance (load-everything + up-to-4-queries-per-row + in-memory
pagination); the Department name unique index missing the `IsDeleted` filter its Team/ProductGroup
siblings have (deterministic 500 on re-creating a deleted department's name); unmapped
`NotSupportedException` turning bad user input into 500s; the handler save-ownership asymmetry;
removed assignments leaving un-approvable phantom requests; and unbounded/tracked read queries.

**On the domain-focus questions:** the `IApprovalTargetHandler` abstraction **is earning its
keep** (4 lifecycle hooks × 2 target types with a 3rd — SoftwareVersion — already present in the
enum and resolver; the alternative is target-type switches inside four `ApprovalService` methods),
but its save-ownership contract needs pinning (F8). `NoOpApprovalNotificationService` is **not**
harmful dead scaffolding: the interface is invoked at the four correct decision points, the call
sites are what's hard to get right, and tests substitute a counting fake — keep it. Department
ownership reads are global-gate-only (`department.read` with no `resourceIdRouteKey`), consistent
with the pass-3/4 F6 contract across the codebase.

**Global query filter recap (relevant to this scope):** every `ISoftDeletable` entity gets an
automatic `!IsDeleted` filter (DBContext.cs:804-811) — that includes `User`, `Team`, `UserTeam`,
`Department`, `RoleAssignment`, `ApprovalPolicy`, `ApprovalRequest`, `OverrideFile`,
`StationSoftwareAssignment`. Two consequences recur below: explicit `!IsDeleted` predicates in
this scope are redundant (but harmless), and `Include` through a *required* navigation to a
filtered entity (`ApprovalRequest.Requester`, `.Policy`, `ApprovalAction.Actor`) silently drops
rows (F1).

---

## Good Decisions (keep these)

1. **The approval state machine is race-proof at the DB level.** One pending request per target is
   enforced by the partial unique index `UX_ApprovalRequests_Target_Pending`
   (DBContext.cs:702-706) and translated to a clean 409 (GlobalExceptionHandler.cs:47-55);
   `ApprovalRequest.Version` is a concurrency token (DBContext.cs:784-792), so two approvers
   racing `ApproveAsync` on the same step produce one winner and one 409
   (GlobalExceptionHandler.cs:37-46) instead of a double step-advance.
2. **StepsSnapshot frozen at submit.** The policy's steps are copied into a jsonb snapshot on the
   request (ApprovalService.cs:91-98, DBContext.cs:731-738), so editing or re-seeding a policy
   cannot change who approves an in-flight request.
3. **Self-approval is blocked twice.** `GetApproversAsync` excludes the requester from the
   approver set (ApprovalApproverResolver.cs:257-260) and `CanActAsync` short-circuits on
   `userId == RequesterId` (:272-275); the inbox additionally excludes one's own requests
   (ApprovalService.cs:275-276). Tested (`Resolver_GetApprovers_ExcludesRequester`,
   `Resolver_CanActAsync_ReturnsFalseForRequester`).
4. **Handler dispatch is the right shape.** `CanSubmitAsync` gates before any mutation
   (ApprovalService.cs:50-55); target status transitions and the request insert flush in a single
   `SaveChangesAsync` on one DbContext (:116-117), so a duplicate-pending violation also rolls
   back the OverrideFile status flip — atomic without an explicit transaction.
5. **Reject requires a comment** (ApprovalService.cs:190-193) → 400 via the `ArgumentException`
   mapping; approve does not — the asymmetry is correct for the domain.
6. **Detail-view authorization is resource-level and complete:** requester OR current-step approver
   OR `approvals.view_all` (ApprovalService.cs:344-351); action endpoints re-verify eligibility on
   every call via `LoadPendingForActionAsync` → `CanActAsync` (:439-455) rather than trusting a
   controller attribute.
7. **Membership management is the one properly scoped authz in this scope** —
   `EnsureCanManageTeamMembershipAsync` accepts `team.manage` *scoped to the team's resource* or
   an active TeamLeader assignment (TeamService.cs:402-425), controller deliberately carries no
   global `[RequirePermission]` for Add/RemoveMember (TeamsController.cs:71-83). Both paths
   tested, including the cross-team negative cases.
8. **Team deletion cleans up its blast radius:** soft-deletes memberships, TeamLeader/Member
   assignments scoped to the team resource, and the resource itself, invalidating the authz cache
   per affected user (TeamService.cs:242-286).
9. **ProductGroup exclusivity is checked in code and backstopped by a partial unique index**
   (DepartmentService.cs:144-160; DBContext.cs:77-80), and org name uniqueness is normalized
   consistently through `ResourceNameNormalizer` (trim + upper-invariant).
10. **Cross-flow cleanup exists where authored deliberately:** deleting an OverrideFile cancels
    and soft-deletes its pending approval requests (OverrideFileService.cs:289-303) — exactly the
    discipline F1/F9 ask for elsewhere.
11. **Ownership reads are clean:** DepartmentOwnershipService is read-only, `AsNoTracking`
    everywhere, real SQL pagination on all three list endpoints, and composes ID subqueries
    server-side in the paged paths (DepartmentOwnershipService.cs:112-118, 156-169).

---

## Findings

### F1 — HIGH · Soft-deleting a user makes their pending approval requests invisible and unfinalizable, while the requests keep locking their targets
**Files:** MProject.Application/Services/Approvals/ApprovalService.cs:272-274, 289-301, 337-342 (with MProject.Application/Services/Identity/UserService.cs:493-519 as trigger)
**Category:** correctness / global-query-filter surprise on required navigations

**Evidence.** `GetInboxAsync`, `GetMyRequestsAsync`, and `GetByIdAsync` all
`Include(r => r.Policy).Include(r => r.Requester)`. `User` is `ISoftDeletable` and globally
filtered (DBContext.cs:804-811), and `ApprovalRequest.Requester` is a **required** navigation
(non-nullable `RequesterId`, ApprovalRequest.cs:14-15). EF applies the query filter to the
included entity; for a required navigation the parent row is dropped from the results (this is
the documented "query filter + required navigation" hazard — and even under LEFT-JOIN semantics
the untouched `pendingRequest.Requester.Name` at :298 would NRE, so both readings break).
`UserService.DeleteUserAsync` (:493-519) soft-deletes the user and their memberships but does
**not** touch their approval requests.

**Failure scenario.** An employee submits an override-file activation, then leaves; an admin
deletes the user. From that moment: the request vanishes from every approver's inbox (row dropped
by the `Requester` inner join), `GetByIdAsync` returns 404, `GetLatestByTargetAsync` → 404. Yet
the request row is still `Pending`: it occupies `UX_ApprovalRequests_Target_Pending` for that
target and the OverrideFile stays `PendingApproval` (not editable as Draft, not re-submittable —
`CanSubmitAsync` requires Draft, OverrideFileApprovalHandler.cs:35-38). Nobody can approve,
reject, or cancel it through the UI (the action endpoints would work — `LoadPendingForActionAsync`
uses no Includes — but no one can discover the ID). The only recovery is deleting the override
file itself (which does cancel requests, OverrideFileService.cs:289-303) — undiscoverable from
the symptom. The same drop applies to `ApprovalAction.Actor` in the detail view (:340): decision
history rows by since-deleted actors disappear from `Actions`, silently rewriting the audit trail
shown to users.

**Minimal fix.** Two independent halves, either of which removes the worst outcome:
(1) in `UserService.DeleteUserAsync`, auto-cancel the target user's Pending approval requests
(mirroring `OverrideFileService.DeleteOverrideFileAsync`, including the handler's `OnRejectedAsync`
to unlock the target); (2) in `ApprovalService`, stop depending on filtered required navigations —
project `RequesterName`/`PolicyCode` in the query (as `BuildResponseAsync` :478-481 already does,
with `?? string.Empty`) or use `IgnoreQueryFilters()` on the user/policy lookup. No test covers a
deleted requester.

---

### F2 — HIGH · SubmitAsync accepts requests whose resolved approver set is empty — with the seeded SoftwareAssignment policy this is the default outcome
**Files:** MProject.Application/Services/Approvals/ApprovalService.cs:88-117; MProject.Application/Services/Approvals/ApprovalApproverResolver.cs:135-143 (vs :145-167); MProject.Infrastructure/AppDbSeeder.cs:646-682
**Category:** correctness / approver resolution (named domain focus)

**Evidence.** `SubmitAsync` resolves the scope (:88-89) but never calls `GetApproversAsync` to
verify anyone can act on step 0. For `ApprovalScopeKind.Resource` the resolver matches role
assignments with `ra.ScopeResourceId == scopeId` **exactly** (:140-141): no walk up the resource
hierarchy (station → model → team), and — unlike the Team branch, which accepts
`ScopeResourceId == null || == teamResourceId` (:157-159) — **no fallback to global (null-scoped)
grants**. The seeded `SOFTWARE_ASSIGNMENT_APPROVAL` policy uses exactly this path
(`TargetOwningResource` → the station resource; AppDbSeeder.cs:671-679). But nothing in the
product creates station-scoped TeamLeader assignments: `TeamService` only creates team-scoped
Member/TeamLeader grants; only the raw `AuthorizationMutationService` admin API can produce one.

**Failure scenario.** A package is configured with `AssignmentApprovalPolicyId`; a user assigns it
to a station. Submit succeeds (`CanSubmitAsync` checks only ownership + inactive), the request is
Pending — and `GetApproversAsync` returns `[]` for every user, so the request appears in **no
inbox** and `CanActAsync` is false for everyone, including global admins holding the TeamLeader
role unscoped. The assignment sits inactive; re-assigning the package is blocked ("already
assigned to this station", StationSoftwareAssignmentService.cs:64-69). Nothing warns anyone. The
same zero-approver terminal state is reachable for the OverrideFile flow when the requester's team
loses its (member-)leaders or the team is deleted mid-flight (see F5). Recovery is cancel — if the
requester figures out why nothing is happening.

**Minimal fix.** At the end of `SubmitAsync`, resolve step 0's approvers and throw
`InvalidOperationException("No eligible approver …")` (→ 409) when empty — one query, fail-fast at
the only moment the requester can react. Separately decide whether Resource scope should include
null-scoped grants like Team scope does (one `||` in :140-141); today's asymmetry means a global
TeamLeader can approve team-scoped requests but not station-scoped ones, which is backwards as a
privilege model. No test covers `TargetOwningResource` resolution or Resource-scope
`GetApproversAsync` at all.

---

### F3 — MEDIUM · Inbox is O(pending × 4) queries: loads every pending request, runs the full approver resolution per row, and paginates in memory
**File:** MProject.Application/Services/Approvals/ApprovalService.cs:269-312
**Category:** performance / query in loop (hot path)

**Evidence.** `GetInboxAsync` materializes **all** pending requests platform-wide (`ToListAsync`,
:289-291 — no cap, tracked entities with two Includes), then per request awaits
`_resolver.CanActAsync` (:296) which runs `GetApproversAsync` — 1 role-assignment query + up to 1
team-resource lookup + 1 team-members query + 1 scope-membership query (ApprovalApproverResolver.cs:
179-255). Pagination (`Skip/Take`, :302) happens after, in memory. 200 pending requests ≈ 600–800
queries per inbox render, per user, per refresh. `GetInboxAsync_PaginatesAndFiltersAfterResolver`
pins the in-memory-pagination behavior but nothing bounds the initial load.

**Minimal fix.** Resolve the *user's* side once instead of the request's side N times: compute the
user's active role-assignment (RoleId, ScopeResourceId) pairs and team/department memberships in
2–3 queries, then filter pending requests against those in SQL (ScopeKind/ScopeId and the
snapshot's step-0 role are all on the request row / jsonb). If that's too invasive now, at least
add `AsNoTracking()`, cap the initial query, and batch the per-request checks. (Total-count
semantics can stay as-is.)

---

### F4 — MEDIUM · RequesterTeam scope picks an arbitrary team for multi-team requesters
**File:** MProject.Application/Services/Approvals/ApprovalApproverResolver.cs:35-52
**Category:** correctness / non-determinism in approver routing

**Evidence.** The RequesterTeam branch is `FirstOrDefaultAsync` over the requester's active
`UserTeams` join with **no `OrderBy`** (:37-45) — with more than one active membership the
database returns whichever row it likes. The chosen team becomes the persisted `ScopeId` that
gates every subsequent `CanActAsync`.

**Failure scenario.** A user in Team A and Team B submits an override file for a Team-A station
(the seeded OVERRIDE_FILE_PUBLISH policy routes by requester team, AppDbSeeder.cs:633-641). The
resolver happens to pick Team B: only Team B's leaders can approve a change to Team A's station;
Team A's leaders never see it. Cancel + resubmit may route to the other team. Plan-dependent,
unreproducible-bug territory.

**Minimal fix.** Make the choice deterministic and honest: order by e.g. `ut.CreatedAt` (or the
team name) as a stopgap, and if multi-team membership is a supported state, either accept an
explicit team on `SubmitApprovalRequest` or resolve the team from the *target's* owning hierarchy
instead of the requester. Add a two-team test either way.

---

### F5 — MEDIUM · Team-scope approvers must be team *members*, but leadership doesn't require membership — removing a member silently de-approvers a leader
**Files:** MProject.Application/Services/Approvals/ApprovalApproverResolver.cs:213-231; MProject.Application/Services/Organization/TeamService.cs:314-356 (AssignTeamLeader — no membership requirement), :139-169 (RemoveTeamMember — doesn't touch leadership)
**Category:** correctness / cross-service invariant drift (approver resolution focus)

**Evidence.** For Team scope, `GetApproversAsync` intersects role-holders with **active UserTeam
membership** (:221-229). But `AssignTeamLeaderAsync` never checks the user is a member, and
`RemoveTeamMemberAsync` soft-deletes the membership while leaving the TeamLeader assignment alive
(it even remains authoritative for `EnsureCanManageTeamMembershipAsync`, TeamService.cs:412-419 —
the ex-member leader can still manage the team). So the two components disagree about what a
"leader" is: leadership without membership grants management power but silently zero approval
power.

**Failure scenario.** Team X has one leader. An admin removes that leader's *membership*
(reorganization, or `UserService.RemoveUserFromTeam`). Nothing warns anyone. The next override-file
submission from Team X resolves zero approvers (feeds F2's terminal state); existing pending
requests become unactionable. The leader still shows up as able to manage members, deepening the
confusion.

**Minimal fix.** Pick the invariant and enforce it on both sides: either require active membership
in `AssignTeamLeaderAsync` and revoke team-scoped leadership in `RemoveTeamMemberAsync` (mirroring
what `DeleteTeamAsync` already does at :260-272), or drop the membership intersection for holders
of *team-scoped* leader assignments in the resolver (keep it for global holders, which is its real
purpose). Currently untested either way.

---

### F6 — MEDIUM · Per-step ScopeStrategy is snapshotted but ignored: the scope resolved from step 0 governs every step
**Files:** MProject.Application/Services/Approvals/ApprovalService.cs:88-89; MProject.Application/Services/Approvals/ApprovalApproverResolver.cs:115-128 (uses only `step.ApproverRoleId` + `request.ScopeKind`)
**Category:** correctness / config trap (dead configuration surface)

**Evidence.** `SubmitAsync` resolves scope **once**, from `orderedSteps[0].ScopeStrategy` (:88-89),
and stores it on the request. `GetApproversAsync` receives each step's snapshot but reads only
`ApproverRoleId`; eligibility always filters by the request-level `ScopeKind/ScopeId`. Yet
`ApprovalStep.ScopeStrategy` is a per-step column, snapshotted per step
(ApprovalService.cs:91-98), and admins can configure a multi-step policy like
step 0 = RequesterTeam ("team lead"), step 1 = Global ("QA sign-off") — step 1 would silently be
restricted to the requester's team members holding the QA role, i.e. the configured strategy is
ignored with no error.

**Failure scenario.** Any future multi-step policy with differing strategies misroutes steps ≥ 1;
today's seeded policies are single-step, so this is latent — but the schema, snapshot, and enum
all advertise per-step scoping.

**Minimal fix.** Either resolve/filter per step (re-run `ResolveScopeAsync` with the current
step's strategy inside `GetApproversAsync` — the requester/target ids are on the request), or
delete `ScopeStrategy` from `ApprovalStep`/snapshot and make it a policy-level field so the model
can't express what the engine won't honor. A validation error on policy creation would also do.

---

### F7 — MEDIUM · Department name unique index is not IsDeleted-filtered: re-creating a deleted department's name is a deterministic unmapped 500
**Files:** MProjectBackend/MProject.Infrastructure/DBContext.cs:74-76 (vs :94-97, :545); MProject.Application/Services/Organization/DepartmentService.cs:41-42, 70-71
**Category:** correctness / global-query-filter vs unfiltered index (pass-4 F4 pattern)

**Evidence.** `Departments.Name` is unique with **no** partial filter (DBContext.cs:74-76), while
the sibling `Team (DepartmentId, Name)` and `ProductGroup Name` unique indexes both carry
`"IsDeleted" = false` (:94-97, :545). The service's duplicate pre-check
(`AnyAsync(d => d.Name.ToUpper() == name)`, DepartmentService.cs:41) runs under the global
`!IsDeleted` filter, so it can never see the soft-deleted row that still holds the name. Result:
`DeleteDepartmentAsync` → `CreateDepartmentAsync("SAME NAME")` → pre-check passes →
`DbUpdateException` on the index — and `GlobalExceptionHandler` has no mapping for
`IX_Departments_Name` (it maps only the approval-pending, team-name, installation-job, and MAC
indexes) → raw 500 with no hint. Deterministic, admin-facing. (Related, race-only variants also
land as unmapped 500s: ProductGroup name, `UserTeams (UserId, TeamId)` on concurrent AddMember,
`RoleAssignments` on concurrent AssignTeamLeader, and the Department↔ProductGroup exclusivity
index.)

**Minimal fix.** Add the `"IsDeleted" = false` filter to the Department name index (migration),
matching its siblings — deleted names become reusable and the pre-check semantics become truthful.
Alternatively (or additionally) add an `IsUniqueViolationOf("IX_Departments_Name")` mapping → 409.

---

### F8 — MEDIUM · Handler save-ownership is asymmetric: SoftwareAssignment's OnApproved flushes the service's whole unit-of-work mid-method; every other hook relies on the service save
**Files:** MProject.Application/Services/Approvals/SoftwareAssignmentApprovalHandler.cs:52 (vs :57-67); MProject.Application/Services/Approvals/OverrideFileApprovalHandler.cs:42-61; MProject.Application/Services/Approvals/ApprovalService.cs:160-164, 216-218
**Category:** maintainability / implicit contract (handler abstraction focus)

**Evidence.** `IApprovalTargetHandler` doesn't say who saves. In practice: OverrideFile handler
mutates and returns (service saves); SoftwareAssignment `OnRejectedAsync` mutates and returns
(service saves); but SoftwareAssignment `OnApprovedAsync` calls `_db.SaveChangesAsync` itself
(:52) — which, because it shares the request-scoped DbContext, also flushes the service's
in-flight `ApprovalRequest` status change and the new `ApprovalAction` **before**
`ApproveAsync` reaches its own save (:164, then a no-op). It works today (same commit boundary,
and the flush is what makes the subsequent `InvalidateResourceLookup` at :54 safe), but the
contract is invisible: the next handler author has a 50% chance of copying the wrong sibling, and
a future transaction wrapped around `ApproveAsync` would interact surprisingly with the inner
save.

**Minimal fix.** Document the contract on `IApprovalTargetHandler` ("handlers mutate; the service
owns SaveChanges; post-commit side effects like cache invalidation belong after the service
save"), move the `InvalidateResourceLookup` call after the outer save (e.g. return it as a
post-commit action, or invalidate from `ApprovalService` keyed by target type), and delete the
inner `SaveChangesAsync`. Pure contract cleanup — no behavior change intended.

---

### F9 — MEDIUM · Removing a software assignment leaves its pending approval request un-approvable and un-cancellable-by-anyone-but-the-requester — unlike the OverrideFile delete flow
**Files:** MProject.Application/Services/Approvals/SoftwareAssignmentApprovalHandler.cs:40-46 (OnApproved throws for a deleted target) vs :57-62 (OnRejected tolerates it); MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:190-228 (RemoveAssignmentAsync cancels nothing)
**Category:** correctness / cross-flow cleanup gap

**Evidence.** `RemoveAssignmentAsync` soft-deletes an assignment without touching approval
requests, while its OverrideFile counterpart explicitly cancels + soft-deletes pending requests
(OverrideFileService.cs:289-303). After a remove, the pending request remains in every eligible
approver's inbox; **Approve** hits `OnApprovedAsync`'s filtered lookup → `KeyNotFoundException` →
404 with the request left Pending (the status flip rolls back with the failed save); **Reject**
happens to work because `OnRejectedAsync` returns silently on a missing target. So the phantom
either lingers forever or must be discovered-by-erroring and rejected. (It doesn't block
re-assignment — new assignment = new target id — the cost is inbox noise plus a confusing 404 on
a visible action.)

**Minimal fix.** In `RemoveAssignmentAsync` (and arguably `UnassignAsync`), cancel pending
approval requests for the assignment exactly as `DeleteOverrideFileAsync` does. Inside this
scope's files, also align the handler pair: either both hooks tolerate a vanished target (treat
approve-of-deleted as reject-with-log) or both throw — the current split is what turns a cleanup
gap into a user-facing 404.

---

### F10 — MEDIUM · Unhandled target types and bogus enum values reach `NotSupportedException` → 500 on plain user input
**Files:** MProject.Application/Services/Approvals/ApprovalService.cs:44-48, 465-473; MProject.Api/Middleware/GlobalExceptionHandler.cs:133-141 (no NotSupportedException mapping)
**Category:** correctness / error mapping on user input

**Evidence.** `POST api/v1/approvals/submit` binds `TargetType` from the body; any user holding
`approvals.submit` can send `"targetType": 2` (`SoftwareVersion` — a real enum value with **no
registered handler**; only OverrideFile and SoftwareAssignment are registered, Program.cs:138-139)
or any out-of-range integer (enum model binding accepts undefined values; `[Required]` doesn't
range-check). `SubmitAsync` throws `NotSupportedException` (:46-47), which `GlobalExceptionHandler`
doesn't map → logged as an unhandled error and returned as 500. Same for the resolver's
`NotSupportedException`s (unknown strategy/scope) if ever reached. A validation failure of client
input should be 4xx; as-is it also pollutes error telemetry with expected garbage.

**Minimal fix.** Map `NotSupportedException` → 400 in `GlobalExceptionHandler` (one `else if`), or
throw `ArgumentException` from the handler-lookup misses in `ApprovalService`. Optionally add an
`Enum.IsDefined` check in `SubmitAsync` for a crisper message.

---

### F11 — MEDIUM · Unbounded, tracked read queries: GetMyRequestsAsync returns a user's entire history; approval reads never use AsNoTracking
**File:** MProject.Application/Services/Approvals/ApprovalService.cs:314-332 (also :272-291, :337-342)
**Category:** performance / missing pagination + missing AsNoTracking

**Evidence.** `GetMyRequestsAsync` loads every request the user ever submitted — no `Take`, no
paging parameter on the endpoint (`GET approvals/mine`) — with two Includes, fully change-tracked.
The inbox and detail queries are likewise tracked despite being read-only (`AsNoTracking` is used
consistently in `GetPolicyCoverageAsync` :419 and `PopulateModelStationAsync` :503, so the
omission is drift, not convention). Tracking here is extra painful because `StepsSnapshot`'s
jsonb `ValueComparer` serializes the list to JSON during change-detection sweeps
(DBContext.cs:724-730). Requests accumulate forever (nothing deletes finalized ones), so "mine"
grows without bound for active users.

**Minimal fix.** Add `AsNoTracking()` to `GetInboxAsync`/`GetMyRequestsAsync`/`GetByIdAsync`
(safe: `ApproveAsync`/`RejectAsync`/`CancelAsync` load their own tracked instances), and give
`GetMyRequestsAsync` the same `PagedRequest` treatment the inbox has (default 20/max 100 already
exists in the model).

---

## Unnecessary Code

1. **Service-level `EnsureAuthorizedAsync` duplicates the controller gate — in three of four org
   services.** DepartmentService.cs:29-34, TeamService.cs:38-43, ProductGroupService.cs:29-34
   re-check the same *global* permission that `[RequirePermission]` already enforced on every
   calling action; `DepartmentOwnershipService` (and `ApprovalService`, apart from the deliberate
   `ViewAllApprovals` check) rely on the controller alone. Grep confirms no non-controller callers
   of the doubled methods. Same asymmetry flagged in pass 4 (Unnecessary Code #2) — pick one
   convention codebase-wide.
2. **Redundant `!IsDeleted` predicates under global filters, inconsistently applied.** The
   resolver's RequesterTeam branch filters `!ut.IsDeleted && !t.IsDeleted`
   (ApprovalApproverResolver.cs:40-41) while the adjacent RequesterDepartment branch (:56-62)
   doesn't — both are equally safe because `UserTeam`/`Team` are globally filtered; the asymmetry
   just invites the wrong conclusion that one branch is buggy. Same for
   `GetPolicyCoverageAsync`'s `!p.IsDeleted` (ApprovalService.cs:420), TeamService's
   role-assignment/user-team predicates, and `EnsureProductGroupAvailableAsync`
   (DepartmentService.cs:148-155). Harmless individually; worth one sweep for consistency.
3. **`ApprovalAction.IsAuto` is write-only scaffolding** — always set `false`
   (ApprovalService.cs:147, 206), surfaced in the DTO, asserted false in one test; no auto-approve
   path exists. Keep only if auto-approval is on the roadmap; otherwise it's a column and DTO field
   documenting a feature that doesn't exist.
4. **`NoOpApprovalNotificationService` — verdict: keep (domain focus).** Not dead in the harmful
   sense: the interface is invoked at the four correct decision points (submit → approvers, step
   advance → next approvers, finalize → requester), DI-substituted, and tests exercise the seam
   with a counting fake. The expensive part of a notification feature is exactly these call sites.
   One real gap while it's here: `CancelAsync` notifies nobody (approvers who saw the item in
   their inbox never learn it's gone) — add the hook when a real transport lands.
5. **Handler abstraction — verdict: keep (domain focus).** Two implementations today, but the
   enum + resolver already anticipate `SoftwareVersion` (ApprovalTargetType.cs:5,
   ApprovalApproverResolver.cs:73-76), and the alternative is a target-type switch in each of
   `SubmitAsync`/`ApproveAsync`/`RejectAsync`/`CancelAsync`. The interface is thin and the
   dictionary-from-DI dispatch (ApprovalService.cs:38) is idiomatic. Fix its contract (F8), don't
   fold it.

---

## Simpler Alternative

Small, local consolidations only — no redesign warranted:

1. **`UpdateDepartmentAsync` runs two identical Teams queries** — one for names, one for ids
   (DepartmentService.cs:77-78); a single `Select(t => new { t.Id, t.Name })` halves it and the
   user-count query can key off the same list.
2. **`GetByIdAsync`'s 20-field manual copy** from `baseResp` into `ApprovalRequestDetailResponse`
   (ApprovalService.cs:358-378) exists only because `MapToResponse` news up the base type — make
   `MapToResponse` populate a caller-supplied/generic instance (`where T :
   ApprovalRequestResponse, new()`) and the copy block disappears; today every new response field
   must be added in two places (`ModelStation` already had to be threaded through specially).
3. **`GetSummaryAsync` should compose its ID sets like its own siblings do.**
   DepartmentOwnershipService.cs:48-61 materializes model- and station-resource-ID lists and ships
   them back as `Contains` parameters, while `GetStationsAsync`/`GetComputersAsync` (:112-118,
   156-169) compose the same subqueries server-side. Using the composed `IQueryable` + `CountAsync`
   three times makes the summary one round-trip cheaper and immune to large-fleet parameter bloat.
4. **`GetTeamLeadersAsync`'s per-row user subqueries** (TeamService.cs:303-304 — two correlated
   `Users` lookups per leader) collapse into a single join with the `Users` set.

---

## Complexity Report

- **`ApprovalService.SubmitAsync` (:41-131, ~90 lines)** — validate → policy select → scope →
  snapshot → insert → handler → audit → notify → response. Linear and readable; at the ~80-line
  threshold. Flagged, no split proposed (F2's emptiness check lands here).
- **`ApprovalApproverResolver.GetApproversAsync` (:115-261, ~146 lines)** — two sequential
  switches on `ScopeKind` (assignment-set narrowing, then membership filtering). The densest logic
  in scope and the home of F2/F5/F6; if those fixes land, extracting per-kind private methods
  would keep it navigable. Not a redesign candidate.
- **`TeamService` (~427 lines, 12 public methods)** — cohesive CRUD + membership + leadership;
  fine as one file.
- No >15-line ×2 copy-paste blocks found in scope. `ApproveAsync`/`RejectAsync` share a ~25-line
  skeleton (load, action record, finalize fields, audit, notify) but differ in the semantically
  load-bearing middle; folding them would trade clarity for line count — leave as is.
- Sibling-behavior nits (not findings): `GetTeamMembersAsync` returns `[]` for a nonexistent team
  while `GetTeamLeadersAsync` 404s (TeamService.cs:198-212 vs :288-291);
  `RemoveTeamMemberAsync` sets `IsDeleted` without `DeletedAt/DeletedBy` (:147) while every other
  soft delete in the file stamps all three; resolver uses `EndTime > now` where TeamService uses
  `EndTime >= now`; `DepartmentOwnershipService` methods take no `CancellationToken` while every
  approval method does.

### Test Gaps (informational)
- **No DepartmentServiceTests, ProductGroupServiceTests, or DepartmentOwnershipServiceTests at
  all** — the only Organization coverage is TeamServiceTests.
- **Resolver: no tests for `TargetOwningResource` scope resolution or Resource-/Department-scope
  `GetApproversAsync`** — precisely the F2 semantics. RequesterTeam and Global are covered.
- No multi-step-policy test (step advance, F6's per-step strategy), no multi-team requester test
  (F4), no leader-without-membership test (F5), no deleted-requester test (F1), no
  concurrent-approve test (the concurrency token's 409 path is unexercised).
- Handler tests cover happy-path lifecycle only; no approve/reject-after-target-deleted (F9).

---

## Out-of-Scope Observations (context files, not counted as findings)

- **`UserService.DeleteUserAsync` doesn't clean role assignments** — a deleted user's TeamLeader/
  Member/global assignments stay active (relevant to F1/F5's approver hygiene; memberships are
  cleaned, assignments aren't).
- **`StationSoftwareAssignmentService.AssignAsync`'s compensation is not transactional** — insert
  + save, then `SubmitAsync`, `catch` → soft-delete + save (:100-120). A crash between the saves
  strands an inactive assignment that blocks re-assignment until manually removed.
- **`GetPolicyCodeAsync` uses `FirstAsync` under the global filter** (ApprovalService.cs:457-463):
  if a policy row were ever soft-deleted, approving its in-flight requests would throw a bare
  `InvalidOperationException`. No deletion path exists today (policies are seed-only) — noting for
  whenever policy management gets an API.

---

## Final Recommendation

**Fix-forward; no redesign.** The state machine, concurrency handling, snapshot design, and
scoped membership authz are sound. The theme of this pass is *silent unreachability* — requests
that exist but that no one can see or act on. Order of work:

1. **F1** — cancel pending requests on user deletion + stop depending on filtered required
   navigations in the three read paths. Worst failure mode (invisible, target-locking), routine
   trigger.
2. **F2** — fail-fast on an empty approver set at submit; decide the null-scoped-grant question
   for Resource scope. Pairs naturally with **F5** (membership/leadership invariant).
3. **F7** — filter the Department name index (one migration) and/or map it in the middleware.
4. **F9 + F8** — assignment-removal cleanup mirroring the OverrideFile flow; pin the handler
   save-ownership contract while touching both handlers.
5. **F10, F11** — error-mapping and read-path hygiene (small, mechanical).
6. **F3** — inbox query inversion when inbox volume justifies it; **F4, F6** — deterministic team
   pick and the per-step-scope decision (small code, needs an explicit product call).
7. Optionally the consolidations (double authz checks, redundant `!IsDeleted` sweep, detail-DTO
   mapping, summary query composition) and the org-service test debt.

---

## Tóm tắt (Tiếng Việt)

- Đã đọc **toàn bộ 14 file** trong phạm vi (5 service Approvals, 4 service Organization, 5
  controller) cùng context (entity, DBContext filter/index, middleware, seeder, test); **không có
  lỗi CRITICAL**; file `docs/skills/review_rule.md` không tồn tại (giống pass 3–4) nên dùng format
  của các pass trước.
- **2 lỗi HIGH:** (F1) xóa mềm user khiến các approval request đang Pending của họ **biến mất khỏi
  mọi inbox/chi tiết** (Include navigation bắt buộc bị query filter loại bỏ) nhưng vẫn chiếm slot
  "1 pending/target" và khóa OverrideFile ở PendingApproval — không ai duyệt/hủy được; (F2)
  `SubmitAsync` không kiểm tra danh sách approver rỗng — với policy SOFTWARE_ASSIGNMENT (đòi
  TeamLeader gán đúng station resource, không fallback global, không leo hierarchy) thì mặc định
  tạo ra request **không ai có quyền duyệt**, âm thầm treo vĩnh viễn.
- **9 lỗi MEDIUM:** inbox N+1 (mỗi request tốn ~4 query + phân trang trong RAM); chọn team tùy ý
  khi requester thuộc nhiều team; leader không còn là member bị loại khỏi approver một cách âm
  thầm; ScopeStrategy từng step bị bỏ qua sau step 0; index tên Department thiếu filter IsDeleted
  → tạo lại tên phòng ban đã xóa = 500; xóa assignment để lại request "ma" (Approve → 404);
  handler lưu DB không nhất quán (OnApproved tự SaveChanges); NotSupportedException → 500 với
  input người dùng; GetMyRequests không phân trang + thiếu AsNoTracking.
- **Trả lời câu hỏi trọng tâm:** abstraction `IApprovalTargetHandler` **đáng giữ** (2 handler,
  enum đã chờ sẵn loại thứ 3; thay thế bằng switch sẽ tệ hơn) nhưng cần chốt contract ai gọi
  SaveChanges; `NoOpApprovalNotificationService` **không phải code chết** — seam đúng chỗ, có test,
  nên giữ; authz department ownership theo global gate, nhất quán với pattern F6 của pass 3–4.
- **Khuyến nghị:** sửa F1 → F2+F5 → F7 → F9+F8 → F10+F11, sau đó F3/F4/F6; bổ sung test cho
  DepartmentService/ProductGroupService/DepartmentOwnershipService (hiện chưa có test nào).
