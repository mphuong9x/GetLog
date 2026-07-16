# Backend Deep Review — Pass 6: API Composition, Git, Uploads, Storage & DbContext

**Date:** 2026-07-14
**Scope:**
- `MProject.Api/`: Program.cs; Filters/ (GitBasicAuthFilter, PermissionAuthorizationFilter,
  RequirePermissionAttribute); Infrastructure/ (TusUploadHandler, TusTempCleanupService);
  Middleware/ (GlobalExceptionHandler, DbExceptionExtensions); Controllers/Common/
  (AdminController, AuditLogsController, BaseController, HealthController).
- `MProject.Application/Services/GitRepositoryService.cs`;
  `MProject.Application/Services/Common/` (AdminService, DomainEventDispatcherService).
- `MProject.Infrastructure/` excluding Migrations/: DBContext.cs, Storage/ (LocalStorageService,
  MinioStorageService, MinioBucketInitializer), Options/ (LocalStorageOptions).

**Context read (not in scope):** `IAppDbContext`, `IGitRepositoryService`, `IStorageService`,
`RepositoryModels` / `AuditLogDto` / `PagedResult`, the `Repository`/`RepositoryMember`/`User`
entities, `GitAPIRepositoriesController` + `GitRepositoriesController` (callers of the in-scope
service/filter), `AgentController.DownloadLocalBlob` (stream-disposal consumer),
`AuthorizationAuditLogger`, `SoftwareFileService` (RegisterUploadedFileAsync /
AddBlobReferenceAsync / DeleteFileAsync), `BlobGcService` (reference guards),
`TeamService.DeleteTeamAsync`, `AuthService`/`UserService` (username normalization),
`RepoStorageOptions`, appsettings(.Development).json, the InitDB migration (Repositories table),
frontend `axios-client.ts` / `vite.config.ts` / `software.ts` (API-path and tus-chunk behavior),
and tests: GitRepositoryServiceTests, DomainEventDispatcherServiceTests, LocalStorageServiceTests,
SoftDeleteTests, DbConstraintNameTests.

> Note: `docs/skills/review_rule.md` does not exist in the repository (same situation as passes
> 2–5). This report follows the output format and severity scale used by those passes
> (CRITICAL / HIGH / MEDIUM; LOW omitted). Paths are relative to `MProjectBackend/` unless noted.
> Two claims in this pass were verified **empirically** with a minimal ASP.NET Core repro
> (same SDK family): the `/api`→`/api/v1` rewrite (F4, confirmed broken) and the
> CORS-headers-lost-on-exception hypothesis (refuted — headers survive; not reported).

---

## Summary

The platform plumbing reviewed here is largely in good shape: the production fail-fast guards in
Program.cs are unusually thorough, the tus upload pipeline pins its storage path to the declared
SHA-256 and verifies content hashes, `LocalStorageService.ResolvePath` is traversal-safe and
tested, blob streams are disposed correctly at every consumer, and the handler-mapped DB
constraint names are pinned by a dedicated test. Two of the named domain-focus questions come back
clean: **GitBasicAuthFilter never logs credentials** (its problem is the opposite — it swallows
too much, F9), and **blob storage stream disposal has no leaks** (`await using` /
`FileStreamResult` everywhere, and the MinIO pipe propagates errors into the reader).

**3 HIGH and 14 MEDIUM findings survived verification; no CRITICAL.** They cluster sharply: the
**git repository feature is markedly less hardened than the rest of the codebase**. The smart-HTTP
endpoints cannot serve the stock `git` CLI that the UI itself advertises (no gzip request
decompression, 30 MB Kestrel cap on pushes, no process lifecycle handling — F1); the service
throws bare `Exception` for not-found/ownership denials so they surface as 500s (F5); soft-deleted
repos permanently squat their directory and name (F6); and the `Repository`/`RepositoryMember`
aggregate has **zero model configuration** — no unique (owner, slug), no member uniqueness, no max
lengths, and a NOT NULL `Description` column fed from an optional request field, which is a
deterministic 500 (F7). Outside git: restoring a user writes their **BCrypt PasswordHash into the
audit log**, retrievable via the audit API (F2); the tus complete-handler's rollback can **delete
a blob object another registration now references** (F3); and the unversioned-`/api` rewrite
middleware is **provably dead** for controller endpoints because implicit `UseRouting` runs before
all user middleware — the frontend only works because its axios interceptor rewrites paths
client-side (F4, confirmed with a live repro: versioned 200, unversioned 404).

**On the domain focus:** every DI registration in Program.cs is resolvable and used (the two warts:
`AddScoped<IAppDbContext, AppDbContext>()` creates a *second* DbContext instance per scope instead
of forwarding, and the `"general"` rate-limit policy is defined but attached to nothing);
middleware order is otherwise sound (metrics gate before `UseHttpMetrics`, tus after auth) except
for F4; Tus security is solid on authn/authz/path/size but the temp-cleanup default can sweep the
whole OS temp directory (F12); DbContext configuration matches entity reality for the core domains
(auto Version/timestamps, jsonb comparers, filtered unique indexes) with three residuals — the
unconfigured Repository aggregate (F7), the unfiltered `Users.Username` unique index (F11), and a
globally suppressed EF warning that hides exactly the query-filter bug class pass 5 confirmed (F15).

---

## Good Decisions (keep these)

1. **Production fail-fast guards** (Program.cs:37-45, 244-294): missing connection string, short
   or dev-placeholder JwtKey outside Development, `minioadmin` credentials outside Development,
   disabled upload-hash verification outside Development, missing refresh-token pepper — all throw
   at startup with actionable messages. This is the right place to die.
2. **Tus upload integrity chain**: `OnBeforeCreateAsync` rejects blob uploads whose `storagePath`
   metadata doesn't equal `BuildBlobStoragePath(sha256)` (TusUploadHandler.cs:118-124), the
   completed content is re-hashed server-side and compared (:280-293), every tus request — create,
   PATCH, delete — is JWT-authenticated and permission-gated (`OnAuthorizeAsync`), and rollback
   outcomes are counted in Prometheus metrics (:29-34). No client-controlled path ever reaches
   storage.
3. **`LocalStorageService.ResolvePath` traversal guard** (LocalStorageService.cs:136-162):
   normalize separators, `Path.GetFullPath`, case-aware prefix check against the root — unit-tested
   with malicious keys (LocalStorageServiceTests).
4. **Blob stream disposal is clean end-to-end** (named domain focus): `StorageServiceExtensions`
   uses `await using`; `AgentController.DownloadLocalBlob` returns `File(stream, …)` which disposes;
   tus handler wraps every `GetContentAsync` stream in `await using`; and
   `MinioStorageService.DownloadAsync`'s pipe completes the writer **with the exception** on
   failure (:95-98) so readers observe errors instead of truncation, and a disposed reader
   terminates the background copy.
5. **GitBasicAuthFilter credential hygiene** (named domain focus): no logging of usernames or
   passwords anywhere in the filter; BCrypt verify; soft-deleted users fail closed through the
   global query filter; inactive users rejected after verify.
6. **`DbConstraintNameTests`** pins that every constraint name string in `GlobalExceptionHandler`
   still exists in the current EF model (and optionally in a migrated Postgres) — this is exactly
   how string-matched constraint mappings should be kept honest.
7. **`GetDescendantResourceIdsAsync`** (DBContext.cs:878-945): recursive CTE with a cycle guard
   (`c."Id" <> ALL(h."Path")`), correct transaction enlistment, and an EF fallback for the
   in-memory provider.
8. **Metrics endpoint gate** (Program.cs:402-425): fixed-time comparison, applied before
   `UseHttpMetrics`/`MapMetrics`, and the empty-key-means-open stance is explicitly documented in
   appsettings.json — a deliberate, recorded decision, not an accident.
9. **Rate-limit policies with Retry-After** (Program.cs:157-224): per-IP partitions for auth/agent/
   git, JSON problem body on rejection, and the git policy is actually attached to the git
   controller.
10. **DomainEventDispatcherService**: per-tick error isolation (one failed dispatch doesn't kill
    the loop), options-driven interval/batch/enable, clean cancellation; producers exist
    (StationSoftwareAssignmentService, InstallationJobService, ComputerLivenessWatchdogService) so
    it is not dead scaffolding.
11. **MinioBucketInitializer** treats bucket bootstrap as best-effort with a clear log instead of
    blocking startup — reasonable for an infra dependency that may start later.

---

## Findings

### F1 — HIGH · Git smart-HTTP endpoints cannot serve the stock `git` CLI the UI advertises: gzip request bodies are not decompressed, pushes are capped at Kestrel's 30 MB default, and the child processes have no lifecycle handling
**Files:** MProject.Application/Services/GitRepositoryService.cs:233-246, 261-274, 276-289;
MProject.Api/Controllers/GitRepositoriesController.cs:54-88 (caller context)
**Category:** correctness / interop on the primary consumer path

**Evidence.** The frontend tells users to run
`git clone <API>/git/{owner}/{slug}.git` (RepositoryDetail.tsx:156, Repositories.tsx:42), so the
consumer is the real git CLI. Three transport requirements of git's smart HTTP protocol are
missing:
(a) **gzip request bodies.** git's `remote-curl` sends `Content-Encoding: gzip` on
`POST /git-upload-pack` negotiation bodies larger than ~1 KB (git's own `http-backend.c` contains
the matching inflate step). `ReceiveUploadPackAsync` pipes `Request.Body` raw into
`git upload-pack --stateless-rpc` stdin (:241); there is no `UseRequestDecompression` anywhere
(grep: zero hits for `RequestDecompression|Content-Encoding` in MProjectBackend). Compressed
negotiation bytes reach git verbatim → "protocol error: bad pkt-len".
(b) **Request size.** No Kestrel `MaxRequestBodySize` override and no `[RequestSizeLimit]` /
`[DisableRequestSizeLimit]` on `ReceivePack` (the team knows the attribute — it's used in
OverrideFilesController.cs:57 and AgentReleasesController.cs:35), so `git push` with a pack over
30 MB is aborted with 413 mid-stream. git cannot chunk a push.
(c) **Process lifecycle.** stdin is never closed after the copy, there is no timeout, no
`CancellationToken`/`RequestAborted` wiring, and `using var process` disposes the handle without
killing the child — a client that aborts mid-push leaves `git receive-pack` blocked on stdin
forever (process leak per aborted push).

**Failure scenario.** Clone of a small just-created repo works (negotiation < 1 KB, not gzipped);
the feature demos fine. First clone/fetch of a repo with more refs or a fetch from a clone with
many local refs sends a > 1 KB gzipped negotiation → fails. First push of a real source tree
(> 30 MB pack) → 413. Aborted pushes accumulate zombie `git` processes on the server.

**Minimal fix.** In the two POST actions: if `Request.Headers.ContentEncoding` contains `gzip`,
wrap `Request.Body` in `GZipStream(…, Decompress)` before passing it down (or add
`AddRequestDecompression`/`UseRequestDecompression`); add `[DisableRequestSizeLimit]` (or a
deliberate large limit) to `ReceivePack`; in the service, close `process.StandardInput` after the
copy, pass `HttpContext.RequestAborted`, and `process.Kill(entireProcessTree: true)` in a
try/finally when the copy or wait fails. Add one integration test that shells out to real
`git clone`/`git push` against a TestServer — the current tests (2 slug-validation cases) never
exercise the protocol at all.

---

### F2 — HIGH · Restoring a user serializes the full User entity — including PasswordHash — into the audit log, retrievable via the audit API
**Files:** MProject.Application/Services/Common/AdminService.cs:63;
MProject.Application/Services/Identity/AuthorizationAuditLogger.cs:38-39 (context);
MProject.Domain/Entities/Identity/User.cs:11 (context);
MProject.Api/Controllers/Common/AuditLogsController.cs:120-121
**Category:** security / sensitive data at rest and in API responses

**Evidence.** `RestoreUserAsync` calls `_auditLogger.LogAsync(adminId, "user.restore", "User",
userId, null, user)` with the tracked `User` **entity**. `AuthorizationAuditLogger` JSON-serializes
the object with no redaction (`JsonSerializer.Serialize(after, SerializerOptions)`), and
`User.PasswordHash` carries no `[JsonIgnore]`. The row's `AfterJson` is returned verbatim by
`GET /api/v1/audit-logs` (AuditLogsController maps `AfterJson` straight into the DTO). Grep over
all 40+ `LogAsync` call sites confirms this is the **only** one passing an entity that contains
credential material — every other site passes RoleAssignment/AclEntry/UserTeam entities or
anonymous projections.

**Failure scenario.** Admin restores a deleted user (the AdminController endpoint exists for
exactly this). From then on the user's BCrypt hash sits permanently in `AuthorizationAuditLogs`,
visible to any holder of `assignments.manage` via the API, and to anyone with access to DB
backups or exported audit data. Weak passwords become offline-crackable from an audit table.

**Minimal fix.** Pass a projection instead of the entity:
`new { user.Id, user.Username, user.Name, user.Status }` (AdminService.cs:63). Defense in depth:
add `[JsonIgnore]` to `User.PasswordHash` (nothing legitimately serializes it — verify FE contract
first) and consider a one-off cleanup of existing `user.restore` rows. No AdminService tests exist
to pin either behavior.

---

### F3 — HIGH · Tus completion rollback can delete a blob object that a concurrent registration now references — silent data loss for the winner
**Files:** MProject.Api/Infrastructure/TusUploadHandler.cs:205-219, 233-247;
MProject.Application/Services/Software/SoftwareFileService.cs:61-63, 540-574 (context)
**Category:** correctness / race condition on read-then-write (named review focus)

**Evidence.** The non-blob path checks `blobAlreadyExists` (:205-207) **before** streaming the
content to storage (minutes for large files — `MaxFileSizeBytes` is 10 GiB in config), sets
`weUploaded = true`, then calls `RegisterUploadedFileAsync`. On **any** exception from register,
the catch deletes the storage object outright (:233-247). But register can fail for reasons that
don't make the object garbage: (1) duplicate `RelativePath` in the version
(SoftwareFileService.cs:69-72 → `InvalidOperationException`) — while another upload/registration
of the *same content* (same SHA-256, dedup'd storage path) has already created a live `Blob` row
pointing at the object we're about to delete; (2) the `Blobs.Add` vs concurrent-insert race:
`LoadExistingBlobInfoAsync` runs *outside* the transaction (:61), so two same-content uploads both
take the "add new Blob" branch (:564-573) and the loser dies on the `PK_Blobs` unique violation —
an exception class the rollback treats as "delete the object" and `GlobalExceptionHandler` has no
mapping for (→ 500). `BlobGcService`'s live-reference guard cannot help — the object is deleted
directly, not through GC.

**Failure scenario.** A user double-submits the same file (retry, double-click) or two users
upload identical content to two draft versions. Upload A completes and registers; upload B —
whose existence check predates A's registration — fails register (duplicate path or PK race),
rolls back, and deletes the shared object. A's registered `SoftwareFile` now points at a blob
whose storage object is gone; the failure is silent until an agent's install downloads a 404.

**Minimal fix.** Before the rollback delete, re-check
`await dbContext.Blobs.AnyAsync(b => b.Sha256 == checksum)` and skip the delete when a row exists
(TusUploadHandler.cs:236) — the object is then owned by that row and BlobGc will reap it if the
row dies. Additionally map the `PK_Blobs`/unique-violation from concurrent blob inserts to a
retry-or-409 instead of a 500, or move `LoadExistingBlobInfoAsync` inside the transaction with an
upsert (`ON CONFLICT DO UPDATE ReferenceCount`).

---

### F4 — MEDIUM · The unversioned-`/api` rewrite middleware is provably dead for all controller endpoints — implicit UseRouting runs first (empirically confirmed)
**Files:** MProjectBackend/MProject.Api/Program.cs:380-389, 431-446;
MProjectFrontend/src/api/services/axios-client.ts:20-26, 41-44 (context)
**Category:** dead/broken middleware; API-compat trap

**Evidence.** Program.cs never calls `UseRouting()` explicitly, so `WebApplication` inserts it
**before all user middleware**; endpoint selection for `/api/foo` happens (and fails — every
controller route is `api/v{version:apiVersion}/…`) before the rewrite runs, and the terminal
endpoint middleware 404s. **Empirically confirmed** with a minimal repro reproducing the exact
pattern (rewrite middleware + mapped `api/v1` endpoint, no explicit UseRouting):
`GET /api/v1/test → 200`, `GET /api/test → 404` despite the rewrite executing. The frontend works
only because its axios request interceptor rewrites every `/api/...` URL to `/api/v1/...`
client-side (`toVersionedApiPath`) — a workaround that hides the breakage. The only thing the
middleware actually rescues is `UseTus` (a path-matching middleware placed after it), and the FE
uses the versioned tus URL anyway (software.ts:40).

**Failure scenario.** Any non-FE consumer (curl, scripts, integrations) that trusts the
code-documented unversioned form gets 404s; the next developer who reads Program.cs reasonably
believes unversioned paths work and debugs the wrong layer.

**Minimal fix.** Add an explicit `app.UseRouting();` immediately after the rewrite middleware
(this is the documented pattern for path-mutating middleware in minimal hosting) — or delete the
middleware and declare `/api/v1` the only contract. Either way, add one integration test hitting
an unversioned path.

---

### F5 — MEDIUM · GitRepositoryService throws bare `Exception` for not-found and ownership denials — they all surface as 500s, unlike its own DeleteRepoAsync
**File:** MProject.Application/Services/GitRepositoryService.cs:37, 46, 86-87, 179-180, 388,
398-406, 426-434, 454-462, 468
**Category:** correctness / error mapping (authz denial hidden as server error)

**Evidence.** `GetDetailAsync`, `GetBranchesAsync`, `GetRepoMembers`, `AddRepoMembers`,
`DeleteRepoMember`, `CreateAsync` throw `new Exception("Repository not found" / "You cannot access
list members of this repo" / "User not found" / "Repository already exists")`.
`GlobalExceptionHandler` has no `Exception` branch except the 500 fallback, so a non-owner probing
`POST /repository/{owner}/{repo}/members` gets **500** (and the event is logged as an unhandled
error, `LogError`, polluting telemetry), instead of 403; a missing repo gets 500 instead of 404.
`DeleteRepoAsync` (:127-146) in the same file does it correctly (`KeyNotFoundException`,
`UnauthorizedAccessException`). `GetCommitsAsync` additionally swallows
`RepositoryNotFoundException` into `new Exception("Get commits failed")` (:386-389), discarding
the cause. Note the controller only pre-gates `GetTeamMembers` with `HasAccessAsync`; the
member-mutation endpoints rely entirely on these service throws, so the 500-on-denial is the
*primary* authz response there.

**Minimal fix.** Mechanical substitution: not-found → `KeyNotFoundException`; ownership denial →
`UnauthorizedAccessException`; "already exists" → `InvalidOperationException` (→ 409 under the
existing mapping). Keep the messages.

---

### F6 — MEDIUM · Soft-deleting a repository leaves its bare .git directory on disk forever — the name becomes permanently un-recreatable (500) and repo data is never reclaimed
**File:** MProject.Application/Services/GitRepositoryService.cs:39-52 (create), 127-146 (delete)
**Category:** correctness / resource lifecycle

**Evidence.** `DeleteRepoAsync` only flips `IsDeleted`; nothing removes or renames
`{root}/{ownerId}/{slug}.git`. `CreateAsync`'s duplicate pre-check queries live rows only
(`IsDeleted == false`, :41-43), so recreating a deleted repo's name passes the DB check and then
hits `Directory.Exists(repoPath)` → `EntryExistsException` (:48-51) — a LibGit2Sharp exception
type with no `GlobalExceptionHandler` mapping → **500** with a generic body. There is also no
cleanup when `Init` succeeds but `SaveChangesAsync` fails (:52-65): the orphaned directory then
blocks the name the same way. And since the soft-deleted row keeps `GitPath`, the data of
"deleted" repos (source code) stays on disk indefinitely with no GC.

**Failure scenario.** User deletes repo "firmware", recreates "firmware" → 500, permanently, with
nothing in the response or logs pointing at the stale directory. Disk usage of deleted repos only
ever grows.

**Minimal fix.** On delete, move the directory to a trash path (e.g. suffix
`.deleted-{timestamp}`) or delete it outright; on create, wrap Init+Save so a failed save removes
the just-initialized directory; map `EntryExistsException` (or pre-check with a friendly
`InvalidOperationException`) → 409.

---

### F7 — MEDIUM · Repository/RepositoryMember have zero model configuration: NOT NULL Description fed from an optional field (deterministic 500), no (owner, slug) uniqueness, no member uniqueness, no length caps
**Files:** MProjectBackend/MProject.Infrastructure/DBContext.cs:55-56 (only the DbSets — grep
confirms no `modelBuilder.Entity<Repository…>` anywhere);
Migrations/20260610085200_InitDB.cs:568 (`Description … nullable: false`);
MProject.Application/Models/RepositoryModels.cs:27 (`string? Description`);
MProject.Application/Services/GitRepositoryService.cs:41-47, 439-446
**Category:** DbContext configuration vs entity reality (named domain focus)

**Evidence.** (a) `Repository.Description` is a non-nullable CLR string → the column is
`text NOT NULL`, but `CreateRepositoryRequest.Description` is nullable and `CreateAsync` assigns
it straight through — `POST /repository/create` without a description inserts NULL →
`DbUpdateException` (23502, unmapped) → **500**. (b) No unique index on (OwnerId, Slug) or
(OwnerId, Name): the only duplicate guards are the display-Name pre-check and the
`Directory.Exists` accident; two concurrent creates of the same slug both pass both checks
(`Repository.Init` is idempotent on an existing directory) → two rows with the same (owner, slug)
→ `ResolveRepoPath`/`HasAccessAsync` (`FirstOrDefaultAsync`) resolve arbitrarily. Different
display names normalizing to one slug ("MyRepo" vs "myrepo") 500 on the directory check even
without a race. (c) `RepositoryMembers` has no (RepositoryId, UserId) unique index and
`AddRepoMembers` performs no duplicate/self/owner check (:439-446) → repeated adds create
duplicate live rows (duplicated in `GetRepoMembers`, double rows to soft-delete one at a time).
(d) `Name`, `Slug`, `Description`, `GitPath` are unbounded `text` while every comparable entity in
this DbContext caps lengths.

**Minimal fix.** One migration + a few lines: make `Description` nullable (or default `""` in
`CreateAsync`); filtered unique indexes `(OwnerId, Slug) WHERE "IsDeleted" = false` and
`(RepositoryId, UserId) WHERE "IsDeleted" = false` (plus `AnyAsync` pre-checks for friendly 409s,
and constraint-name mappings in `GlobalExceptionHandler` — extend `DbConstraintNameTests`);
`HasMaxLength` on the string columns; make the create pre-check compare **slugs**, not names.

---

### F8 — MEDIUM · AdminService restore resurrects only the root entity of a cascade: teams come back without their Resource/memberships/role-assignments, users without memberships, software-files without their blob reference count
**Files:** MProject.Application/Services/Common/AdminService.cs:29-88;
MProject.Application/Services/Organization/TeamService.cs:242-286 (delete cascade, context);
MProject.Application/Services/Software/SoftwareFileService.cs:341-367 (refcount decrement,
context); MProject.Application/Services/Software/BlobGcService.cs:111-113 (live-ref guard,
context)
**Category:** correctness / restore is not the inverse of delete

**Evidence.** `DeleteTeamAsync` soft-deletes the team **and** its UserTeams, its team-scoped
TeamLeader/Member role assignments, and its `Resource` (setting `Status = Retired`);
`RestoreEntityAsync<Team>` flips `IsDeleted` on the team row only. The restored team's
`ResourceId` points at a soft-deleted resource, so every resource-scoped authz path and
`Include(t => t.Resource)` silently misses it (the same filtered-navigation class as pass 5 F1) —
a half-alive team nobody can manage by scoped permission. User restore has the same shape
(memberships stay deleted; arguably acceptable, but nothing documents the asymmetry).
SoftwareFile restore flips `IsDeleted` without re-incrementing the blob `ReferenceCount` that
`DeleteFileAsync` decremented — permanent counter drift that trips exactly the alarms other code
maintains carefully (`BlobGc detected ReferenceCount drift … Investigate increment/decrement
paths`; a later delete of the restored file logs the "already at floor" warning). The object
itself survives only thanks to BlobGc's live-row guard; restoring **after** GC has already
reclaimed the blob resurrects a file whose content is gone. Additionally, restoring a Department
that held a ProductGroup can violate the partial unique exclusivity index (DBContext.cs:77-80) →
unmapped 500. There are **no AdminService tests**.

**Minimal fix.** Per type, restore the pieces its delete removed: Team → also un-delete its
Resource (and decide about memberships/assignments explicitly); SoftwareFile → re-increment the
blob reference inside the same transaction and refuse restore when the blob row/object no longer
exists; Department → pre-check the exclusivity index. Document whatever is deliberately *not*
restored. Add tests.

---

### F9 — MEDIUM · GitBasicAuthFilter swallows every exception as 401 — a DB outage is indistinguishable from a wrong password
**File:** MProject.Api/Filters/GitBasicAuthFilter.cs:47-50
**Category:** swallowed exceptions (named review focus)

**Evidence.** The whole user-lookup + BCrypt block is wrapped in `catch { SetUnauthorized(context); }`
— no filter on exception type, no logging. Postgres down, a timeout, or a BCrypt hash-format error
all return `401 + WWW-Authenticate: Basic`.

**Failure scenario.** During a DB blip every `git fetch` prompts users to re-enter passwords
(clients interpret 401 as bad credentials); users "fix" it by retyping until lockout/frustration;
nothing appears in server logs because the exception was eaten — the one failure class that should
page ops presents as user error.

**Minimal fix.** Delete the try/catch (the exception handler will 500 with a correlation id), or
catch narrowly and `LogError` before rethrowing. While in the file: `username.ToLower()` (:35)
should be `ToLowerInvariant()` to match `AuthService`/`UserService` normalization (Turkish-I class
of bugs).

---

### F10 — MEDIUM · AppDbContext wraps DbUpdateConcurrencyException into InvalidOperationException — killing the handler's dedicated concurrency branch and feeding the over-broad IOE→409 mapping
**Files:** MProjectBackend/MProject.Infrastructure/DBContext.cs:854-865;
MProject.Api/Middleware/GlobalExceptionHandler.cs:37-46 (now-dead branch), :124-132;
MProject.Application/Exceptions/ConcurrencyException.cs:5 (context)
**Category:** correctness / exception-mapping pipeline

**Evidence.** `SaveChangesAsync` catches `DbUpdateConcurrencyException` and rethrows
`InvalidOperationException` with a fixed message (:858-864). Consequences: (1) the
`GlobalExceptionHandler` branch that maps `DbUpdateConcurrencyException` — including the entity
name it extracts for logging — is **unreachable** for every save in the app (grep: nothing else
throws the type); the status still ends up 409 via the IOE branch, but the log loses the entity
and the dedicated message; (2) the codebase's own `ConcurrencyException(entityName, entityId)` —
which exists precisely for this and extends `InvalidOperationException` — is bypassed; (3) any
future catch-and-retry around a concurrency conflict has nothing typed to catch. Related breadth
problem in the same pipeline: **every** `InvalidOperationException` maps to 409 **with the raw
message echoed to the client** (:124-132) — including framework-thrown ones (`FirstAsync` →
"Sequence contains no elements") and `LocalStorageService.ResolvePath`'s "Storage path escapes the
configured root path" (LocalStorageService.cs:158) — misclassifying genuine server bugs as client
conflicts, logging them as Warning (never Error), and leaking internal wording. (Pass 5 F10
flagged the sibling `NotSupportedException` hole.)

**Minimal fix.** In `SaveChangesAsync`, rethrow `ConcurrencyException` (or nothing — let the
original type propagate); delete the then-dead wrap message. Longer term, reserve the message-echo
behavior for a domain exception type instead of bare IOE.

---

### F11 — MEDIUM · `Users.Username` unique index is not IsDeleted-filtered while every pre-check runs under the soft-delete filter — recreating a deleted user's username is a deterministic unmapped 500
**Files:** MProjectBackend/MProject.Infrastructure/DBContext.cs:71-73;
MProject.Application/Services/Identity/UserService.cs:337 + AuthService.cs:57 (blind pre-checks,
context)
**Category:** DbContext configuration vs entity reality (pass-5-F7 pattern, new instance)

**Evidence.** `HasIndex(x => x.Username).IsUnique()` carries no `"IsDeleted" = false` filter
(unlike Team/ProductGroup/Model/RoleAssignment/AgentRelease sibling indexes). The
username-existence pre-checks (`AnyAsync(u => u.Username == username)`) run under the global
`!IsDeleted` filter and can never see the soft-deleted row that still owns the name. Delete user
"bob" → create "bob" → pre-check passes → `DbUpdateException` on `IX_Users_Username`, which has no
`GlobalExceptionHandler` mapping → raw 500. (If blocking reuse is *intended* identity hygiene, the
pre-check must use `IgnoreQueryFilters()` and return a clean 409 instead.) `Departments.Name` has
the identical defect — already recorded as pass 5 F7; this pass adds the Users instance because
the index lives in in-scope DBContext code.

**Minimal fix.** Decide the semantics: filtered index (name reusable after delete) **or**
`IgnoreQueryFilters()` pre-checks + an `IsUniqueViolationOf("IX_Users_Username")` → 409 mapping.
Either is a one-liner plus a migration/test.

---

### F12 — MEDIUM · TusTempCleanupService's default target is the OS temp directory — it recursively deletes every file older than 24h there; and file-level cleanup can corrupt long-running resumable uploads
**Files:** MProject.Api/Infrastructure/TusTempCleanupService.cs:34, 43-60;
MProject.Api/Infrastructure/TusUploadHandler.cs:40
**Category:** destructive default / orphaned-partial-uploads focus

**Evidence.** Both the tus store and the cleanup fall back to `Path.GetTempPath()` when
`TusUpload:TempStoragePath` is unset. The cleanup enumerates `"*"` with
`SearchOption.AllDirectories` and deletes any file older than the retention — in the fallback
configuration that is **the machine-wide temp directory**, including other processes' files.
Today appsettings.json sets a dedicated path, so this is latent — but one deleted config line (or
a new environment missing the override) arms it silently. Second issue: even against the dedicated
directory, deletion is per-file by `LastWriteTimeUtc`, and a `TusDiskStore` upload consists of
several files with different write times (`.metadata` written once at create; the data file
touched by every PATCH). A resumable upload paused or trickling past 24h loses its metadata file
while its data file survives — a corrupted store entry that then errors on resume and lingers
until the data file also ages out. tusdotnet ships `ITusExpirationStore` +
`RemoveExpiredFilesAsync` for exactly this.

**Minimal fix.** Refuse to run against `Path.GetTempPath()` (require the config, like the
connection string) or at minimum scope the fallback to a `tus` subdirectory created by the store;
prefer configuring `Expiration` on `DefaultTusConfiguration` and calling
`store.RemoveExpiredFilesAsync` from the background service so upload units expire atomically.

---

### F13 — MEDIUM · Rate-limit partitioning keys on RemoteIpAddress with default KnownProxies — behind any non-loopback reverse proxy all users collapse into one 10-req/min auth bucket
**File:** MProjectBackend/MProject.Api/Program.cs:151-155, 160-168, 226-227
**Category:** availability / deployment trap in middleware configuration

**Evidence.** `ForwardedHeadersOptions` sets `ForwardedHeaders` and `ForwardLimit = 1` but never
touches `KnownProxies`/`KnownNetworks`, which default to loopback only. If the reverse proxy (the
deployment shape this config is clearly written for) reaches Kestrel from a non-loopback address —
the normal case for Docker/compose or a separate proxy host — `X-Forwarded-For` is silently
ignored and `GetClientIp` returns the proxy's IP for **every** client. The `auth` policy then
allows 10 login attempts per minute **for the whole company combined** (and `git` 60/min total);
conversely the per-IP brute-force protection stops distinguishing attackers from everyone else.
Nothing logs that forwarding was skipped.

**Minimal fix.** Bind `KnownProxies`/`KnownNetworks` from configuration (with a startup fail-fast
when a proxy is declared but unparseable), or — if TLS-terminating proxy is guaranteed —
`KnownNetworks` covering the container network. Add a comment in appsettings documenting the
requirement, in the same style as the existing `Metrics:_comment`.

---

### F14 — MEDIUM · Repositories are Public by default — omitting one optional field publishes the repo to anonymous clone
**Files:** MProject.Application/Models/RepositoryModels.cs:28; MProject.Domain/Entities/Repository.cs:13;
MProject.Application/Services/GitRepositoryService.cs:338-341
**Category:** security / fail-open default

**Evidence.** Both the request model and the entity default `Visibility` to
`RepoVisibility.Public`, and `HasAccessAsync` grants **unauthenticated** read (`userId == null`)
to public repos — the git endpoints serve anonymous `git clone` for them by design
(GitRepositoriesController passes `CurrentUserId = null` through). A client that omits
`visibility` in `POST /repository/create` therefore publishes source code to anyone with network
reach, silently.

**Minimal fix.** Default to `Private` in `CreateRepositoryRequest` (and the entity), or make the
field `[Required]`. One-line change; decide before real code lands in the feature.

---

### F15 — MEDIUM · The EF warning for the query-filter/required-navigation hazard is globally suppressed — the exact bug class pass 5 confirmed in production code
**File:** MProjectBackend/MProject.Api/Program.cs:50-51
**Category:** correctness / silenced diagnostic

**Evidence.** `ConfigureWarnings(w => w.Ignore(CoreEventId.
PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))` silences EF's only signal
for "required navigation to a query-filtered entity drops rows". Pass 5 F1 demonstrated this exact
class live (`ApprovalRequest.Requester` making pending approvals invisible after user deletion),
and this pass's F8 adds another instance (`Team.Resource` after restore). The suppression is
global and permanent, so every future model addition inherits the blind spot with no reviewer
prompt.

**Minimal fix.** Remove the suppression and triage the warnings it raises (each is either a real
F1-class bug or should be documented at the navigation with `IsRequired(false)`/explicit
`IgnoreQueryFilters` at the query). If some are accepted, suppress per-navigation understanding,
not globally.

---

### F16 — MEDIUM (performance) · GetBranchesAsync walks the full commit history of every branch on every call
**File:** MProject.Application/Services/GitRepositoryService.cs:185-193
**Category:** performance / clear win

**Evidence.** `TheNumberOfCommits = branch.Commits.Count()` enumerates the entire ancestry of each
branch per request — O(branches × history) libgit2 revwalks for a listing endpoint. A repo with
20 branches × 10k commits does ~200k commit loads per page view.

**Minimal fix.** Drop the count from `BranchDto` (the UI can live without it) or compute it only
for the default branch / lazily per branch detail. If needed cheaply: `git rev-list --count`
equivalent still walks, so caching by tip SHA is the honest option.

---

### F17 — MEDIUM (performance) · Audit-log queries have no supporting index, track their entities, and the table (plus processed DomainEvents) grows forever
**Files:** MProject.Api/Controllers/Common/AuditLogsController.cs:67-99;
MProjectBackend/MProject.Infrastructure/DBContext.cs:654-655, 649-650
**Category:** performance / missing index + missing AsNoTracking + unbounded growth

**Evidence.** The only index on `AuthorizationAuditLogs` is `CreatedAt`, but the endpoint filters
by `TargetType`/`TargetId`/`ActorId` and then sorts by `CreatedAt DESC` — every page view is a
sequential scan + sort once the table grows (and it only grows: ~40 call sites write, nothing
prunes). The page query is tracked (`_db.AuthorizationAuditLogs.AsQueryable()` with no
`AsNoTracking`), up to 200 entities per request. `DomainEvents` similarly keeps processed rows
forever (index on `ProcessedAt` exists, but no cleanup — the dispatcher only marks
`ProcessedAt = now`).

**Minimal fix.** Add index `(TargetType, TargetId, CreatedAt DESC)` (optionally `(ActorId,
CreatedAt DESC)`), add `AsNoTracking()`, and pick a retention story (age-based purge job or table
partitioning) for both tables.

---

## Unnecessary Code

1. **`"general"` rate-limit policy is defined and never attached** (Program.cs:199-205): grep
   shows `EnableRateLimiting` used only with "auth", "git", "agent-announce", "agent-enroll". Dead
   config that suggests protection that isn't there — wire it up (e.g. as a global limiter) or
   delete it.
2. **`httpContext.Items["ClaimsPrincipal"]` is written and never read** (TusUploadHandler.cs:102;
   whole-repo grep: one hit). Delete the line.
3. **TusUploadHandler re-implements JWT validation the pipeline already performed.** `UseTus` is
   mounted *after* `UseAuthentication` (Program.cs:390-393), so `ctx.HttpContext.User` is already
   an authenticated principal for Bearer requests; `ExtractBearerToken` + `ValidateJwt`
   (+ resolving `IOptionsMonitor<JwtBearerOptions>` per request) duplicate it with a second code
   path that can silently drift from the real one (e.g. if token validation ever gains events or
   revocation checks). `OnFileCompleteAsync` additionally re-runs the same permission check that
   `OnAuthorizeAsync` performed moments earlier in the *same request* (:166-171 vs :94-99). Use
   `httpContext.User` in `OnAuthorizeAsync` and drop the duplicate check (keep the `UploaderId`
   stash).
4. **`AddScoped<IAppDbContext, AppDbContext>()` creates a second DbContext per scope**
   (Program.cs:54): a class registration constructs its *own* instance rather than forwarding to
   the `AddDbContext` one. Today nothing mixes the two in one request (grep: concrete
   `AppDbContext` is injected only by HealthController and the seeder), so this is latent — but
   the first component that takes both gets two change trackers and two connections. One-line fix:
   `AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())`. (`IAppDbContext`
   already exposes `Database`, so HealthController could also switch to the interface.)
5. **GlobalExceptionHandler's `DbUpdateConcurrencyException` branch is unreachable** — see F10.
6. **`Repository.DefaultBranch` is a dead column**: written by nothing, read by nothing
   (whole-repo grep: entity + migrations only), while `GetCommitsAsync` hardcodes
   `branchName = "master"`. Either use it (pass it to `GetCommitsAsync`/`GetDetailAsync`) or drop
   it.
7. **Redundant explicit `!IsDeleted` predicates on globally-filtered sets, inconsistently applied**
   in GitRepositoryService (e.g. present in `ResolveRepoPath`/`HasAccessAsync`/`ListAsync`, absent
   in `GetDetailAsync`/`GetUsersForRepoAsync`) — all equivalent under the global filter; the
   inconsistency invites the wrong conclusion that the bare queries are bugs. Same one-sweep
   cleanup pass 5 suggested (Unnecessary Code #2).
8. **`LocalStorageOptions.RootPath` defaults to `"D:\\limitfile"`** — a machine-specific magic
   path. Validate-or-throw like the connection string; the current value silently writes blobs to
   a surprise location if `Storage:Local:RootPath` is missing (`RepoStorageOptions` has the same
   shape with `""` → CWD-relative git storage).
9. **Local-mode "presigned upload URL" returns a raw server filesystem path to the browser**
   (LocalStorageService.cs:61-64; the FE special-cases it via `isLocalUploadUrl`,
   software.ts:289-292) — server disk layout disclosed to clients as an API contract. Return a
   sentinel (e.g. `local:`) instead.

---

## Simpler Alternative

1. **Three copies of the repo-load + owner-check block** (`GetRepoMembers` :394-406,
   `AddRepoMembers` :422-434, `DeleteRepoMember` :450-462 — ~12 lines ×3, byte-identical apart
   from messages): one `private Task<Repository> GetOwnedRepoAsync(Guid currentId, string owner,
   string repo)` removes the duplication and gives F5's exception-type fix a single home.
2. **`HasAccessAsync` ×2** (:327-353 by owner/slug, :474-498 by id) share their entire eligibility
   tail — load the row two ways, then call one shared private `HasAccessCoreAsync(repository,
   userId, requireWrite)`.
3. **`AdvertiseRefsAsync`/`AdvertiseReceiveRefsAsync`** (:220-231 vs :248-259) differ only in the
   service name and pkt-line prefix; `ReceiveUploadPackAsync`/`ReceivePackAsync` (:233-246 vs
   :261-274) differ only in the git verb. Two private helpers parameterized by service name halve
   the surface F1's fixes must touch.
4. **AuditLogsController embeds its whole query/mapping pipeline in the controller** (~95 lines in
   one action, the only controller in scope with no service) — fine to leave, but when F17's index
   and retention land, moving the query into a small service keeps the controller consistent with
   every sibling.

---

## Complexity Report

- **`TusUploadHandler.OnFileCompleteAsync` + `CompleteBlobUploadAsync` (:144-307, ~160 lines
  combined)** — two jobs (content-addressed blob finalize vs. version-file registration) already
  split into two methods; each is linear. Fine after F3's guard lands.
- **`GitRepositoryService` (568 lines)** mixes metadata CRUD, membership management, access
  policy, git plumbing (process spawning), and libgit2 reads in one class; no single method
  exceeds ~50 lines, so no split is demanded — but the F5/F6/F7 fixes would land more safely if
  the smart-HTTP plumbing (4 methods + `CreateGitProcess`) moved to its own class first.
- **`Program.cs` (447 lines)** — top-level composition with inline middleware lambdas; acceptable
  for minimal hosting, and the guard blocks justify their bulk. The rewrite middleware (F4) and
  metrics gate could be extension methods for readability, not correctness.
- No >15-line ×2 copy-paste blocks in scope beyond the three GitRepositoryService clusters listed
  under Simpler Alternative.
- Sibling-behavior nits (not findings): `GitAPIRepositoriesController.DeleteAsync` gates with
  `requireWrite: false` and relies on the service's owner check (safe but reads wrong);
  `AdminController` gates restores behind `AppPermissions.ManageAssignments` (semantic mismatch —
  a restore permission it is not); `GitBasicAuthFilter` uses culture-sensitive `ToLower()` where
  the identity services use `ToLowerInvariant()`; `HealthController /health/ready` is an
  unauthenticated, un-rate-limited DB probe; BCrypt on every git request (~100-250 ms CPU each ×
  60/min/IP allowed) is the price of stateless Basic auth — fine now, worth an auth-cache if git
  traffic grows.

### Test Gaps (informational)
- **Zero tests for:** AdminService (restore semantics, F2/F8), GitBasicAuthFilter (auth outcomes,
  F9), TusUploadHandler (the entire upload entry point, F3), TusTempCleanupService (F12),
  MinioStorageService, GlobalExceptionHandler mappings (DbConstraintNameTests covers only
  constraint-name existence).
- **GitRepositoryServiceTests contains 2 tests, both slug validation.** Nothing covers access
  control (`HasAccessAsync` matrix), member management, the smart-HTTP protocol, or the
  delete/recreate path (F6). Given F1/F5/F6/F7 all live here, this is the highest-value test
  investment in scope.
- The FE's tus chunk size (16 MB, software.ts:37) is the only thing keeping uploads under
  Kestrel's 30 MB request cap — an implicit cross-repo coupling no test or comment records.

---

## Out-of-Scope Observations (context files, not counted as findings)

- `SoftwareFileService.InitializeUploadAsync` sends the local-mode absolute filesystem path to
  clients as `uploadUrl` (see Unnecessary Code #9) — the FE's `isLocalUploadUrl` regex is the only
  consumer contract.
- `AgentController.DownloadLocalBlob` maps a missing object to `FileNotFoundException` → unmapped
  → 500; agents retrying a GC'd blob see server errors instead of 404 (pass 4 scope).
- The `Old_Software/` untracked directory and `uistore_*` spec files at the repo root look like
  workspace leftovers unrelated to the backend (flagging for hygiene only).

---

## Final Recommendation

**Fix-forward; no redesign — but treat the git feature as pre-production.** The core platform
(uploads, storage, DbContext conventions, guards) needs targeted patches only; the git repository
stack needs a hardening pass before real source code lives in it. Order of work:

1. **F2** (one-line audit projection + optional `[JsonIgnore]`) and **F3** (existence re-check
   before rollback delete) — small, high-impact, no design decisions needed.
2. **F1 + F5 + F6 + F7 + F14** as one git-hardening batch: gzip + size limit + process lifecycle,
   exception types, directory lifecycle, schema constraints, private-by-default — plus the first
   real integration tests for the feature (clone/push against TestServer).
3. **F4** — add the explicit `UseRouting()` after the rewrite (or delete the middleware), with one
   integration test; **F10/F11/F15** — exception-pipeline and index/warning hygiene, all
   mechanical.
4. **F8** (restore semantics — needs one product decision per entity type), **F9**, **F12**,
   **F13** (config + docs).
5. **F16/F17** performance items and the Unnecessary Code sweep (dead limiter policy, dead
   `ClaimsPrincipal` write, duplicate JWT validation, `IAppDbContext` forwarding, dead
   `DefaultBranch`).

---

## Tóm tắt (Tiếng Việt)

- Đã đọc **toàn bộ 17 file trong phạm vi** (Program.cs, 3 filter, 2 file Tus, middleware, 4
  controller Common, GitRepositoryService, AdminService, DomainEventDispatcher, DBContext, 3 file
  Storage, Options) + context (2 git controller, audit logger, SoftwareFileService, BlobGc, FE);
  **không có lỗi CRITICAL; 3 HIGH, 14 MEDIUM**. Hai giả thuyết được kiểm chứng bằng repro thật:
  middleware rewrite `/api`→`/api/v1` **chết thật** (404), còn nghi vấn mất header CORS khi có
  exception là **sai** (đã loại bỏ).
- **3 lỗi HIGH:** (F1) git smart-HTTP không dùng được với git CLI thật mà UI hướng dẫn — thiếu
  giải nén gzip body, push bị chặn ở 30 MB mặc định của Kestrel, process git không có
  timeout/kill; (F2) restore user ghi **nguyên PasswordHash (BCrypt) vào audit log**, đọc được qua
  API audit-logs; (F3) rollback của Tus upload có thể **xóa blob object mà bản ghi khác vừa đăng
  ký** (double-click/upload trùng nội dung) → hỏng dữ liệu âm thầm.
- **Cụm git yếu nhất codebase:** lỗi 403/404 trả về 500 (throw `Exception` trần); xóa repo không
  xóa thư mục `.git` → tên repo bị chiếm vĩnh viễn (500) + dữ liệu mồ côi; bảng
  Repository/RepositoryMember **không có cấu hình model nào** (Description NOT NULL nhận null →
  500 chắc chắn, không unique (owner, slug), member trùng lặp); repo mặc định **Public** cho phép
  clone ẩn danh.
- **Các MEDIUM đáng chú ý khác:** AdminService restore chỉ hồi sinh entity gốc (team mất
  Resource/membership, file mất blob refcount); GitBasicAuthFilter nuốt mọi exception thành 401
  (DB sập nhìn như sai mật khẩu); DBContext bọc DbUpdateConcurrencyException làm chết nhánh xử lý
  riêng; index Username không lọc IsDeleted → tạo lại username đã xóa = 500; TusTempCleanup mặc
  định quét **cả thư mục temp của OS**; rate-limit theo IP sập về 1 bucket chung khi đứng sau
  proxy không phải loopback; cảnh báo EF về query-filter + navigation bắt buộc bị tắt toàn cục
  (đúng lớp bug pass 5 F1).
- **Khuyến nghị:** sửa ngay F2+F3 (vài dòng), gộp F1/F5/F6/F7/F14 thành một đợt gia cố git kèm
  integration test clone/push đầu tiên, rồi F4/F10/F11/F15 (cơ học), cuối cùng F8/F9/F12/F13 và
  dọn dead code (rate-limit "general", ClaimsPrincipal, JWT validate trùng, DefaultBranch).
