# Backend Deep Review — Pass 3: Identity & Authorization Domain

**Date:** 2026-07-13
**Scope:**
- `MProject.Application/Services/Identity/` (11 files: AuthService, RefreshTokenService,
  AuthorizationService, AuthorizationMutationService, AuthorizationCacheInvalidator,
  AuthorizationCacheKeys, AuthorizationAuditLogger, PermissionService, RoleService, UserService,
  ResourceLookupService).
- `MProject.Application/Authorization/` (9 files: AclQueryService, RbacGrantQueryService,
  SubjectResolver, ScopeResolver, PermissionEvaluator, AuthorizedResourceQueryService,
  OwnerAccessResolver, OwnerWhitelist, PermissionCodeNormalizer).
- `MProject.Api/Controllers/Identity/` (AuthController, AuthorizationController, RolesController,
  UsersController).

**Context read (not in scope):** `IAppDbContext`, `IAuthorization*` interfaces and the models in
`MProject.Application/Models/` (AuthorizationModels, AuthorizationQueryModels, PagedResult), the
`PermissionAuthorizationFilter` / `RequirePermissionAttribute` / `BaseController` request pipeline,
`AppPermissions` / `AppRoles` / `ResourceTypes` constants, `AppDbSeeder` (role seeding),
`TeamService` (sibling membership path), `DBContext` global query filters + unique indexes +
concurrency tokens, and the identity/authorization tests in `MProject.Tests`
(AdversarialPermissionTests, AuthorizationMutationServiceTests, AuthorizedResourceQueryServiceTests,
PermissionServiceTests, RefreshTokenServiceTests, OwnerAccessResolverTests,
AuthorizationCacheInvalidatorTests).

> Note: `docs/skills/review_rule.md` does not exist in the repository. This report follows the
> output format and severity scale used by pass 2 (CRITICAL / HIGH / MEDIUM; LOW omitted).

---

## Summary

The permission-evaluation core is well built. The decision order in `PermissionEvaluator`
(ACL-deny-user → ACL-deny-team → ACL-allow-user → ACL-allow-team → RBAC-user → RBAC-team) is
correct and adversarially tested; the decision cache is versioned per user / per permission-map /
per resource and its TTL is clamped to the nearest grant `EndTime` so time-boxed grants can't be
cached past expiry; refresh-token rotation detects reuse and revokes the whole family, claims the
rotation with a conditional `ExecuteUpdate`, and validates the pepper at construction; every
authorization read query uses `AsNoTracking` and enforces the `!IsDeleted` + time-window predicate;
and deleted/inactive subjects fail closed through the global query filter in `SubjectResolver`.

**1 HIGH and 6 MEDIUM findings survived verification; no CRITICAL.** The HIGH is a
privilege-escalation asymmetry: `CreateAclEntryAsync` — unlike its sibling
`CreateRoleAssignmentAsync` — never checks that the actor actually holds the permission it is
granting, so an `assignment.manage` holder can grant any permission via an ACL entry. It is
currently gated by the controller's global-admin requirement (finding F6), but that gate is exactly
the delegation the service is otherwise designed to support, so the two findings must be read
together. The MEDIUMs are: the owner shortcut overriding an explicit ACL *Deny*; the "visible
resources" query silently ignoring ACL *Deny*; users created with teams not receiving the
team-scoped `Member` role that the team-membership endpoint grants; non-atomic mutation + audit +
cache-invalidation; the scoped-delegation path being unreachable through the API; and a
read-then-write duplicate check whose only backstop is a unique index that surfaces as a raw 500.

**Global query filter recap (relevant to this scope):** every `ISoftDeletable` entity gets an
automatic `!IsDeleted` filter (DBContext.cs:804-811). In this scope `User`, `Team`, `UserTeam`,
`Resource`, `RoleAssignment`, `AclEntry` **are** filtered; `Role`, `Permission`, `RolePermission`,
`RefreshToken`, `AuthorizationAuditLog` are **not** (Role/Permission are not soft-deletable, so the
RBAC/ACL joins through `Role.RolePermissions.Permission` see no filter surprise).

---

## Good Decisions (keep these)

1. **Deny-precedence evaluation order** — `PermissionEvaluator.Evaluate` (PermissionEvaluator.cs:16-67)
   resolves user-deny before team-deny before any allow, and RBAC only after ACL. Tie-breaking is
   deterministic (`Priority` desc → `ResourceDistance` → `CreatedAt`, PermissionEvaluator.cs:70-87).
   Adversarially covered (`IndividualAcl_DenyOverridesRbacAllow`,
   `GlobalAcl_DenyStillWins_OverScopedAllow_AtSamePriority`, tie-break tests).
2. **Time-boxed grants can't outlive their cache** — `ComputeEffectiveCacheTtl`
   (AuthorizationService.cs:209-242) clamps the decision-cache TTL to the nearest match `EndTime`
   and refuses to cache (`ttl <= 0`) once all matches are expired. Tested
   (`ComputeEffectiveCacheTtl_*`, `DecisionCache_SkipsEntryWhenAllMatchesExpired`).
3. **Versioned cache invalidation** — decision keys embed a per-user version, a global
   permission-map version, and a per-resource version (AuthorizationService.cs:131-148); role/ACL
   mutations bump the subject's user version (and, for teams, every current member's version, via
   `InvalidateSubjectAsync`), and role-permission edits fan out through `InvalidateRoleAsync`
   (AuthorizationCacheInvalidator.cs:67-103). `EvaluateAsync_AfterInvalidateUser_...` and the
   `GetMe_Caches...` tests confirm no stale-allow after mutation.
4. **Refresh-token rotation is reuse-safe** — presenting an already-rotated token revokes the whole
   family (RefreshTokenService.cs:87-91); the rotation is *claimed* with a conditional
   `ExecuteUpdateAsync` guarded on `RotatedAt == null && RevokedAt == null` inside a transaction, and
   a lost race also revokes the family (RefreshTokenService.cs:97-129, 134-154). The token is stored
   only as an HMAC-SHA256 hash under a validated ≥32-byte pepper (RefreshTokenService.cs:231-235,
   258-282). Covered by `Rotate_ConcurrentUseOfSameToken_...` and `Rotate_ReuseOf...RevokesFamily`.
5. **Status changes revoke sessions** — deactivation, password reset, password change, and
   refresh-with-inactive-user all call `RevokeAllActiveForUserAsync`
   (UserService.cs:434-438, 453-454; AuthService.cs:133, 167-168).
6. **Fail-closed subject resolution** — `SubjectResolver.ResolveAsync` relies on the global filter to
   drop soft-deleted users and returns `IsUserActive = false` for missing users
   (SubjectResolver.cs:19-31); `IsAllowedAsync`/`AreAllowedAsync`/`EvaluateAsync` all short-circuit to
   deny when the subject is inactive.
7. **Cycle-safe hierarchy walks** — `ScopeResolver` (visited-set + depth cap 30) and
   `AuthorizationCacheInvalidator.InvalidateResourceScopeAsync` (BFS with visited set) both terminate
   on cyclic parent references; `GetDescendantResourceIdsAsync` uses a path-guarded recursive CTE.
   Tested (`CyclicParent_DoesNotLoopAndStillEvaluates`, `...HierarchyHasCycle_ReturnsEachResourceOnce`).
8. **Role-assignment grantability** — `CreateRoleAssignmentAsync` enforces self-elevation guard,
   scoped `assignment.manage`, system-role global gate, and — crucially —
   `EnsureRolePermissionsGrantableAsync` so an actor can only assign a role whose permissions the
   actor itself holds at that scope (AuthorizationMutationService.cs:34-84, 266-285). This is the
   correct pattern; see F1 for the ACL path that omits it.

---

## Findings

### F1 — HIGH · `CreateAclEntryAsync` never checks the actor holds the permission it grants (privilege escalation via ACL)
**File:** MProject.Application/Services/Identity/AuthorizationMutationService.cs:110-154
**Category:** authorization / privilege escalation / sibling asymmetry

**Evidence.** `CreateRoleAssignmentAsync` gates every grant through
`EnsureRolePermissionsGrantableAsync(actorId, role.Id, scope)` (line 54), which enumerates the
role's permissions and requires `AreAllowedAsync` to return true for **each** at the target scope —
so an actor can never hand out a permission it does not itself hold. `CreateAclEntryAsync` performs
**no** equivalent check. Its entire gate is:

- `EnsureNoSelfElevationAsync` (can't target yourself or a team you're in) — line 112,
- `EnsureManagePermission(actorId, request.ResourceId)` (actor has `assignment.manage` at that
  resource scope) — line 113,
- existence checks for permission / resource / subject — lines 115-121.

Nothing verifies the actor is allowed the `PermissionId` being granted. So any principal that can
reach this method and holds `assignment.manage` at scope S can mint an `AclEntry` with
`Effect = Allow` granting **any** permission (e.g. `software.manage`, `computer.manage`,
`assignment.manage` itself) to any other user or team at S — permissions the actor does not hold.
The self-elevation guard only blocks the actor's own id and its current teams, so the actor grants
to a second account/team it controls, or elevates `assignment.manage` outward.

**Why it matters / current gating.** In the default seed only the global `Admin` (who already holds
every permission) has `assignment.manage`, and F6's class-level attribute forces the controller to a
*global* `assignment.manage`, so today the gap is not reachable by a non-admin. But the service is
explicitly built for delegation — scoped `EnsureManagePermission`, the scoped-manager unit tests,
the `Member`/`TeamLeader` seeded roles — and the moment an admin grants a "user-admin" principal
`assignment.manage` (globally or scoped), that principal can escalate to arbitrary permissions
through ACL entries while `CreateRoleAssignmentAsync` correctly contains it. This is a
defense-in-depth hole on the most sensitive surface in the system, and the two sibling methods
diverging is itself the bug.

**Failure scenario.** Org defines role `UserAdmin = { assignment.manage }`, granted globally to HR.
HR cannot assign any role beyond `{ assignment.manage }` (grantable check), but can
`POST /authorization/acl-entries` with `PermissionId = software.manage`, `Effect = Allow`,
`SubjectId = <accomplice>` — accomplice now manages all software. No audit-visible role change.

**Minimal fix.** Mirror the role path: after the existence checks in `CreateAclEntryAsync`, resolve
the permission's action and require the actor to hold it —
`var action = (await _context.Permissions.Where(p => p.Id == request.PermissionId).Select(p => p.Action).FirstAsync());`
then `if (request.Effect == AclEffect.Allow && !await _authorizationService.IsAllowedAsync(actorId, action, request.ResourceId)) throw new UnauthorizedAccessException(...)`.
(Deny entries need no grantability check — denying is not an escalation.) No test exercises this
today (see Test Gaps).

---

### F2 — MEDIUM · Owner shortcut is evaluated before ACL Deny, so an explicit Deny cannot revoke an owner's access
**Files:** MProject.Application/Services/Identity/AuthorizationService.cs:154-189 (and :79-90 in `AreAllowedAsync`); MProject.Application/Authorization/OwnerWhitelist.cs
**Category:** authorization / evaluation ordering

**Evidence.** In `EvaluateAsync`, `IsOwnerActionAllowedAsync` is checked at line 154 and returns
`owner_shortcut` **before** scope resolution and the ACL/RBAC queries (lines 175-189) ever run. The
`PermissionEvaluator` — the only place ACL `Deny` is honored — is therefore never reached for an
owner. `AreAllowedAsync` has the same ordering (owner probe at line 82, evaluator at 115). The owner
whitelist grants `ReadOverrideFiles` + `ManageOverrideFiles` on `OverrideFile` resources
(OwnerWhitelist.cs:10-15). Net effect: if an admin places an explicit ACL `Deny` for
`overridefile.manage` on a user for an override file that user **owns**, evaluation still returns
Allow. Deny is the system's strongest signal everywhere else (F1 in the evaluator, adversarial
tests), but it is silently powerless against ownership.

**Failure scenario.** A user uploads an override file (becomes `OwnerId`); later the user is found to
be mishandling that config and an admin adds an ACL `Deny` on `overridefile.manage` for that
user+resource to lock them out without deleting the file. The user retains full manage access.

**Minimal fix.** Evaluate an owner shortcut only after confirming there is no applicable ACL `Deny` —
either move the owner probe to run after the ACL query (treat "owner" as an allow source the
evaluator considers, so `PickAcl(Deny)` still wins), or, cheaper, in the owner branch first check
for a matching user/team `Deny` before returning `owner_shortcut`. Tests currently assert only that
owner bypasses *absence* of a grant (`AuthorizationService_OwnerShortcut_AllowsWithoutAclOrRbac`);
none assert Deny-vs-owner.

---

### F3 — MEDIUM · `GetVisibleResourceIdsAsync` ignores ACL Deny, so denied resources still appear in listings
**File:** MProject.Application/Authorization/AuthorizedResourceQueryService.cs:40-95 (Deny omission at :53-63)
**Category:** authorization / read-path inconsistency with the evaluator

**Evidence.** The visibility query unions RBAC scopes (any grant, :40-51) with ACL scopes filtered to
`Effect == AclEffect.Allow` (:53-55). ACL `Deny` entries are never queried, so they cannot subtract
a resource from the returned set. The single-item evaluator (`PermissionEvaluator`) treats `Deny` as
top priority; the list path does not. A user who has a broad Allow (RBAC at a parent scope, or an
inherit-to-children ACL Allow) plus a targeted `Deny` on one child still gets that child back from
`GetVisibleResourceIdsAsync`. `OverrideFileService.GetOverrideFilesAsync` uses exactly this list to
decide which rows to show (OverrideFileService.cs:194-212), so a resource the user is explicitly
denied is listed (its per-row `CanManage` is re-checked, but the row's existence and metadata are
already disclosed). This is the inverse consistency gap to F2: there Deny is over-ridden, here Deny
is ignored.

**Failure scenario.** User has model-scoped `ReadOverrideFiles` (Allow, inherit) over a product line
but is `Deny`-listed on one station's override files. The station's override rows still appear in
`GET /override-files`.

**Minimal fix.** Also load `Effect == Deny` scopes (+ their descendants when `InheritToChildren`) and
remove them from the computed set before returning: `result.ExceptWith(denyRootsAndDescendants)`.
Keep parity with the evaluator's precedence (Deny wins). Covered indirectly by
`AuthorizedResourceQueryServiceTests` only for Allow/RBAC; add a Deny case.

---

### F4 — MEDIUM · Users created with teams don't get the team-scoped `Member` role that the membership endpoint grants
**Files:** MProject.Application/Services/Identity/UserService.cs:324-385 (team loop :361-372); compare MProject.Application/Services/Organization/TeamService.cs:110-131
**Category:** correctness / seeded-role consistency

**Evidence.** `TeamService.AddTeamMemberAsync` adds the `UserTeam` **and** a `Member` role assignment
scoped to `team.ResourceId` (TeamService.cs:110-131) — that scoped `Member` role is where a team
member's team-scoped rights (`ManageOverrideFiles`, `ManageModels`, `ReadConfigBaselines`, …) come
from (AppDbSeeder.cs:504-517). `UserService.CreateUserAsync` adds the `UserTeam` rows directly
(UserService.cs:361-371) and only assigns the global read-only `Viewer` role
(`AssignDefaultViewerRoleAsync`, :387-417); it never adds the `Member` role. So a user created via
*admin → Create User* with team ids ends up in the team but without the scoped `Member` grant, while
an otherwise-identical user added via *Add Team Member* gets it. The two entry points produce
different effective permissions for the same (user, team) state.

**Failure scenario.** Admin creates user U with team T in one call. U is a member of T on the org
chart but cannot manage T's override files/models; the team lead must remove and re-add U through the
team endpoint to repair the grant.

**Minimal fix.** In `CreateUserAsync`, for each `teamId` resolve the team's `ResourceId` and add the
same scoped `Member` role assignment that `AddTeamMemberAsync` creates (guard on
`AppRoles.Member` being seeded, matching TeamService's `is not seeded` throw). Fail-closed today
(under-privileged, not over), but it is a real seeded-role inconsistency.

---

### F5 — MEDIUM · Mutation, audit log, and cache invalidation are three separate awaited steps with no transaction
**Files:** MProject.Application/Services/Identity/AuthorizationMutationService.cs:79-82, 104-107, 149-152, 168-171; MProject.Application/Services/Identity/AuthorizationAuditLogger.cs:29-44
**Category:** correctness / non-atomic multi-step write

**Evidence.** Each mutation does `_context.SaveChangesAsync()` to persist the entity, then calls
`_auditLogger.LogAsync(...)` which issues its **own** `SaveChangesAsync` (AuthorizationAuditLogger.cs:43),
then awaits `InvalidateCacheForSubjectAsync` (which itself runs DB queries to expand team members).
There is no ambient transaction across the three. If the audit `SaveChangesAsync` throws (or the
process dies) after the grant is committed, the privileged change is persisted **without an audit
record and without cache invalidation** — the actor's prior deny stays cached up to 3 minutes and
the exception surfaces to the caller as if the operation failed, inviting a retry. For an
authorization audit trail this is a compliance-relevant gap: the security-sensitive write and its
audit are not atomic.

**Failure scenario.** `CreateRoleAssignmentAsync` commits the assignment (line 80); the audit insert
fails on a transient DB error (line 81). The grant is live, no audit row exists, the subject's
decision cache is never bumped, and the API returns 500. The operator retries; the unique index now
reports "already exists".

**Minimal fix.** Wrap entity-write + audit-write in one transaction (the codebase already has
`ExecuteInTransactionAsync`, used by `RefreshTokenService`), committing both or neither, and perform
cache invalidation after a successful commit (invalidation failure should be logged, not swallow the
success). At minimum, add the audit entity to the same `ChangeTracker` and let a single
`SaveChangesAsync` persist grant + audit together.

---

### F6 — MEDIUM · Class-level `[RequirePermission(ManageAssignments)]` forces a *global* grant, making the service's scoped-delegation path unreachable via the API
**Files:** MProject.Api/Controllers/Identity/AuthorizationController.cs:15-18; MProject.Api/Filters/PermissionAuthorizationFilter.cs:57-80; MProject.Application/Services/Identity/AuthorizationMutationService.cs:208-217
**Category:** authorization consistency / likely-dead delegation path

**Evidence.** The controller carries `[RequirePermission(AppPermissions.ManageAssignments)]` at class
scope with no `resourceIdRouteKey`. `PermissionAuthorizationFilter.TryResolveResourceIdAsync` returns
`null` when the route key is empty (:59-62), so the gate is `CheckPermissionAsync(userId,
"assignment.manage", resourceId: null)`. With a null resource the scope chain is empty and only
**null-scoped (global)** role assignments match (RbacGrantQueryService.cs:67). Meanwhile the service's
`EnsureManagePermission(actorId, scopeResourceId)` is written to accept a *scoped*
`assignment.manage` (AuthorizationMutationService.cs:208-216), and `AuthorizationMutationServiceTests`
exercises `CreateRoleAssignment_ScopedManager_CanAssignWithinScope`. Because the class gate demands a
global grant, a purely scoped manager receives 403 before the service runs — the scoped-delegation
logic is exercised only by unit tests, never reachable through HTTP. This is fail-closed (more
restrictive than the service), but it is a real inconsistency and, per F1, it is the only thing
currently containing that escalation; "fixing" one without the other is dangerous.

**Minimal fix.** Decide the intended contract. If assignment management is deliberately global-admin
only, drop the scoped `scopeResourceId` overload of `EnsureManagePermission` and the scoped-manager
tests (they advertise a capability that cannot be used). If scoped delegation is intended, the
per-action authorization must move into the service (which already does it) and the class attribute
should not pin a global grant — but only **after** F1 is fixed, or scoped managers gain the ACL
escalation.

---

### F7 — MEDIUM · Duplicate-existence checks on create are read-then-write races; the only backstop surfaces as a raw 500
**Files:** MProject.Application/Services/Identity/AuthorizationMutationService.cs:56-65 (role), :123-132 (ACL)
**Category:** race condition / error handling

**Evidence.** Both creates do `AnyAsync(... !IsDeleted)` and then, several statements later,
`Add` + `SaveChangesAsync`. Two concurrent identical requests both pass `AnyAsync` and both insert.
Correctness is saved by the filtered unique indexes (`UX` on RoleAssignment
(SubjectType,SubjectId,ScopeResourceId,RoleId) `WHERE IsDeleted=false`, DBContext.cs:100-103; and the
`AreNullsDistinct(false)` unique index on AclEntry, :120-124), so no duplicate is persisted — but the
losing insert throws a `DbUpdateException` that is **not** caught here (unlike `RegisterAsync`/
`CreateUserAsync`, which translate it to `ArgumentException`). The user gets a 500 instead of a clean
409/"already exists", and — per F5 — the audit/invalidation steps are skipped.

**Minimal fix.** Wrap the `SaveChangesAsync` in a `try/catch (DbUpdateException)` that maps the
unique-violation to the same `InvalidOperationException("… already exists.")` the pre-check throws,
so the race and the pre-check produce identical, non-500 responses.

---

## Unnecessary Code

1. **`InvalidatePermissionMap` has no production caller.** `AuthorizationCacheInvalidator.cs:32-35`
   (and its interface member, IAuthorizationCacheInvalidator.cs:12) bumps `authz_perm_version`, which
   participates in every decision-cache key (AuthorizationService.cs:132). Grep across
   `MProject.Application` and `MProject.Api` finds only the definition and test stubs — nothing calls
   it. Role→permission changes are handled instead by `InvalidateRoleAsync` (fan-out to affected
   users), which is the correct and sufficient path, so the global bump is dead. Either wire it in
   where a coarse global flush is genuinely wanted (e.g. a future bulk permission migration) or
   remove it from the interface; leaving it implies a global-invalidation contract that nothing
   honors.
2. **`PermissionService.CheckPermissionAsync` is a pure pass-through.** PermissionService.cs:14-17
   delegates verbatim to `IAuthorizationService.IsAllowedAsync`. It exists to give the API filter a
   narrow interface (`PermissionAuthorizationFilter` depends on `IPermissionService`), so this is an
   acceptable seam — noted only for completeness, not recommended for removal.
3. No dead branches, unreachable code, or always-constant parameters found elsewhere in scope. The
   `!IsDeleted` predicates on `RoleAssignment`/`AclEntry`/`UserTeam` reads are redundant with the
   global query filter (DBContext.cs:804-811) but read as intent and are harmless; **do not** blanket-
   remove them.

---

## Simpler Alternative

Small, local consolidations only — no redesign warranted:

1. **One shared "active grants for subject" predicate.** The identical subject + time-window filter
   `(User && SubjectId==userId) || (Team && activeTeamIds.Contains(SubjectId))` plus
   `(!StartTime || StartTime<=now) && (!EndTime || EndTime>=now)` is hand-written in
   `RbacGrantQueryService` (:64-69), `AclQueryService` (:68-72),
   `AuthorizedResourceQueryService` (:43-47, :56-60), and `AuthService.GetMeAsync` (:202-205). A
   shared `Expression` helper (or extension `IQueryable<T> WhereActiveForSubject(...)`) removes four
   copies of a security-critical predicate and prevents them from drifting apart.
2. **`AclQueryService` and `RbacGrantQueryService` are near-identical scaffolding.** Both build
   `distanceByResourceId`, dedupe the permission list into an `OrdinalIgnoreCase` dictionary, run one
   projection, then fold rows into `*Match` objects (AclQueryService.cs:40-129 vs
   RbacGrantQueryService.cs:40-108). The bodies differ only in the entity, the extra ACL
   `InheritToChildren`/distance handling, and the match type. Not worth forcing into one generic, but
   the shared distance/dictionary setup (~15 lines) could be a small private helper.
3. **`NormalizeName`/`NormalizeUsername`/`ValidatePassword` are duplicated verbatim** between
   `AuthService.cs:266-296` and `UserService.cs:45-95`. Extract to one internal static validator
   reused by both (identical semantics confirmed).

---

## Complexity Report

- **`UserService.GetAllUsersAsync` (:192-263, ~70 lines)** — filter building + paged projection with
  two correlated sub-selects (Teams, Roles) per row. Acceptable; the sub-selects translate to SQL,
  not N+1. Not flagged for redesign.
- **`AuthorizationMutationService.CreateRoleAssignmentAsync` (:34-84)** — multiple sequential authz
  round-trips (self-elevation, manage, grantable) before the write. Correct and readable; the only
  action items are F1 (add the same rigor to the ACL sibling), F5 (atomicity), F7 (race).
- Nothing in scope exceeds the ~80-line/multiple-jobs threshold or the 15-line×2 copy-paste
  threshold beyond the duplications listed under Simpler Alternative.

### Test Gaps (informational)
- **No test asserts the ACL-grant escalation (F1).** `AuthorizationMutationServiceTests` covers the
  role path (`CreateRoleAssignment_RoleContainsPermissionActorLacks_Forbidden`) but has zero
  `CreateAclEntry` escalation cases — a "scoped/global assignment.manage actor cannot grant a
  permission it lacks via ACL" test would have caught it.
- **No owner-vs-Deny test (F2).** `OwnerAccessResolverTests` covers owner-without-grant and
  non-owner-denied, but not owner + explicit ACL `Deny`.
- **No Deny case for `GetVisibleResourceIdsAsync` (F3).**
- **No test for the `CreateUserAsync`-with-teams role outcome (F4).**

---

## Out-of-Scope Observations (context files, not counted as findings)

- **`AuthService.RefreshAsync` commits the rotated child before checking user status**
  (AuthService.cs:127-135): rotation persists a new refresh token, then the status check revokes the
  whole family if the user is inactive. Net state is correct (family revoked, no access token
  issued), but a crash between the two leaves a live refresh token for an inactive user until the
  next refresh re-checks status. Low risk; noted for awareness.
- **`ScopeResolver` truncates the scope chain at a soft-deleted intermediate resource**
  (ScopeResolver.cs:35-47 filters `!r.IsDeleted`): grants inherited from ancestors *above* a
  soft-deleted node silently stop applying. Likely intended (a deleted node breaks the tree) but
  worth a conscious decision.

---

## Final Recommendation

**Fix-forward; no redesign.** The evaluation core, cache versioning, and token rotation are sound and
should be preserved. Order of work:

1. **F1 + F6 together** — add the grantable-permission check to `CreateAclEntryAsync` (mirror
   `EnsureRolePermissionsGrantableAsync`) and decide the delegation contract in one change so the ACL
   escalation is closed *before* any relaxation of the controller's global gate. Add the missing
   escalation test.
2. **F2, F3** — reconcile owner/Deny and visibility/Deny with the evaluator's Deny-wins precedence
   (both are small, high-value authorization-consistency fixes).
3. **F4** — make `CreateUserAsync` and `AddTeamMemberAsync` produce the same (user, team) grant state.
4. **F5, F7** — wrap mutation + audit in one transaction and translate the unique-violation to a clean
   conflict.
5. Optionally the four consolidations/dead-code items (Simpler Alternative + `InvalidatePermissionMap`).

---

## Tóm tắt (Tiếng Việt)

- Đã đọc **toàn bộ** 24 file trong phạm vi (11 service Identity, 9 file Authorization, 4 controller)
  cùng các interface/model/filter/seeder và test liên quan; **không có lỗi CRITICAL**.
- **1 lỗi HIGH:** `CreateAclEntryAsync` **không** kiểm tra actor có thực sự sở hữu quyền đang cấp
  (khác hẳn `CreateRoleAssignmentAsync` vốn có `EnsureRolePermissionsGrantableAsync`). Người nắm
  `assignment.manage` có thể tạo ACL Allow để cấp **bất kỳ** quyền nào cho user/team khác — leo
  thang đặc quyền. Hiện chỉ bị chặn nhờ F6 (controller yêu cầu quyền global), nhưng đó đúng là cơ chế
  ủy quyền mà service được thiết kế để hỗ trợ, nên phải sửa F1 trước khi nới F6.
- **6 lỗi MEDIUM:** (F2) owner shortcut chạy trước ACL nên **Deny tường minh không thu hồi được**
  quyền của chủ sở hữu; (F3) `GetVisibleResourceIdsAsync` **bỏ qua ACL Deny** nên tài nguyên bị Deny
  vẫn hiện trong danh sách; (F4) user tạo kèm team **không được gán role `Member`** theo scope như
  luồng Add Team Member → quyền không nhất quán; (F5) ghi entity + audit + xóa cache **không atomic**
  (nhiều `SaveChanges` rời rạc) → có thể mất audit và cache bẩn; (F6) attribute cấp lớp buộc quyền
  **global** khiến luồng ủy quyền theo scope không dùng được qua API; (F7) kiểm tra trùng kiểu
  read-then-write chỉ dựa vào unique index nên gặp race sẽ trả **500** thay vì lỗi 409 sạch.
- **Điểm tốt cần giữ:** thứ tự Deny-thắng trong `PermissionEvaluator`; TTL cache quyết định bị chặn
  theo `EndTime` gần nhất; invalidation cache theo version (user/permission/resource); rotate refresh
  token phát hiện tái sử dụng + revoke cả family bằng `ExecuteUpdate` có điều kiện; mọi truy vấn
  authz đều `AsNoTracking` + lọc `!IsDeleted` và khung thời gian; user bị xóa/không active fail-closed.
- **Khuyến nghị:** sửa F1+F6 cùng lúc, rồi F2/F3 (đồng bộ Deny), F4 (đồng bộ seed role), cuối cùng
  F5/F7. Không cần redesign.
