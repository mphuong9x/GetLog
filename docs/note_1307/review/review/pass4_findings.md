# Backend Deep Review — Pass 4: Assets Domain (Agent Protocol, Computers, Models, Stations)

**Date:** 2026-07-14
**Scope:**
- `MProject.Application/Services/Assets/` (8 files: AgentService, AgentCommandService,
  AgentReleaseService, ComputerLivenessWatchdogService, ComputerStatusMapper, ComputerService,
  ModelService, StationService).
- `MProject.Api/Controllers/Assets/` (5 files: AgentController, AgentReleasesController,
  ComputersController, ModelsController, StationsController).

**Context read (not in scope):** `IAppDbContext` + `DbContextTransactionExtensions` +
`BlobReferenceExtensions`, `AgentModels` / `PagedRequest` request models, `AgentSecurityOptions` /
`ComputerLivenessOptions`, `AgentAuthenticationHandler`, `PermissionAuthorizationFilter` /
`RequirePermissionAttribute`, `AuthorizedResourceQueryService`, `LocalStorageService`,
`BlobGcService`, the Assets entities (`Computer`, `Agent`, `AgentRelease`, `AgentCommand`,
`StationTestCounter`, …), `DBContext` index/filter configuration, and the tests
(AgentServiceTests, AgentCommandServiceTests, AgentReleaseServiceTests,
ComputerLivenessWatchdogServiceTests, ComputerStatusMapperTests, ComputerServiceTests,
ModelServiceTests, LocalStorageServiceTests).

> Note: `docs/skills/review_rule.md` does not exist in the repository (same situation as pass 3).
> This report follows the output format and severity scale used by passes 2–3
> (CRITICAL / HIGH / MEDIUM; LOW omitted). All paths below are relative to `MProjectBackend/`.

---

## Summary

The agent protocol core is in good shape. Bearer tokens are HMAC-hashed with versioned peppers,
salted, compared in fixed time, and rotated with a grace-period pending-token scheme; enrollment
tokens are BCrypt-hashed, expire in 24h, and are single-use; announce is rate-limited, refuses MAC
reuse (including soft-deleted MACs, via `IgnoreQueryFilters`), and is disabled unless an installer
token is configured. The heartbeat write path deliberately avoids read-modify-write
(`ExecuteUpdateAsync` with coalesce semantics) and wraps agent + computer + runtime-status changes
in one transaction (rollback is tested). The blob download endpoint is traversal-safe
(`LocalStorageService.ResolvePath` root-prefix guard, unit-tested), and the agent self-update
release flow has a real ref-guard: `UX_AgentReleases_Active` guarantees at most one active release
at the DB level, and `BlobGcService` excludes blobs referenced by any non-deleted `AgentRelease`.

**1 HIGH and 9 MEDIUM findings survived verification; no CRITICAL.** The HIGH is in the hot path:
once an agent's token passes the 30-day rotation threshold, **every** heartbeat regenerates and
overwrites the pending token, so a retried or out-of-order heartbeat can invalidate the rotated
token the agent just persisted — the agent is then permanently locked out (announce refuses known
MACs; re-enrollment needs an admin). The MEDIUMs cluster into: the liveness watchdog flipping
computers Offline without re-checking liveness in the UPDATE (race with concurrent heartbeats,
plus spurious `Computer.WentOffline` events); agent-supplied input that is not validated
(duplicate/negative slot indexes and a nullable `Slots` list in test-metrics → 500s; unbounded
string lengths persisted every heartbeat); a deterministic 500 when re-creating a computer whose
MAC belongs to a soft-deleted row; the blob endpoint serving any stored object to any enrolled
agent; the assets-wide authorization gate requiring *global* grants (making the visibility
filtering inside `GetComputersAsync` unreachable through the API — the pass-3 F6 pattern);
`ActivateAsync` being non-atomic where its sibling `PublishAsync` is transactional; unacked agent
commands being redelivered forever with a 16-command window that can starve newer commands; and
the legacy status mapping masking `Offline` behind `Updating`/`Error`.

**Global query filter recap (relevant to this scope):** `Computer`, `Model`, `Station`,
`ModelUserManager`, `AgentRelease`, `Resource` are soft-deletable and get the automatic
`!IsDeleted` filter (DBContext.cs:806-811). `Agent`, `AgentCommand`, `StationTestCounter`,
`ComputerRuntimeStatus`, `Blob` are **not** filtered — which is why the agent queries explicitly
check `!x.Computer.IsDeleted` (the filter does not propagate through navigations used in another
entity's `Where`). The `Computer.MacAddress` unique index is **not** IsDeleted-filtered
(DBContext.cs:152-154), unlike the Model/Station/ModelUserManager unique indexes — see F4.

---

## Good Decisions (keep these)

1. **Agent token hygiene** — secrets are 32-byte random, stored only as salted HMAC-SHA256 under
   validated ≥32-byte peppers with key versioning for pepper rotation
   (AgentService.cs:60-128, 717-751); verification is fixed-time (:741) and old-pepper tokens are
   transparently re-hashed to the current version (tested:
   `Authenticate_VersionedPepper_VerifiesOldTokenAndRotatesToCurrentVersion`). The installer token
   is also compared fixed-time (:274).
2. **Rotation with grace** — rotation issues a *pending* token while keeping the current one valid
   for `TokenRotationGraceDays`, and only promotes the pending hash when the agent first uses it
   (AgentService.cs:410-422, 664-680; tested:
   `Heartbeat_WhenTokenExpired_IssuesPendingTokenWithoutInvalidatingCurrent`). The scheme is right;
   F1 is about its idempotency, not its design.
3. **Heartbeat write path is race-conscious** — the relational branch uses `ExecuteUpdateAsync`
   with `agentVersion ?? c.AgentVersion` coalescing instead of read-modify-write
   (AgentService.cs:605-613), guards `Updating` from being clobbered by an agent-side operational
   override unless the override is `Maintenance` (:618-626, 635-637), and wraps agent + computer +
   runtime-status writes in one transaction (:429-441; rollback tested:
   `Heartbeat_WhenComputerUpdateFails_RollsBackAgentHeartbeat`).
4. **Static specs keep last-known-good** — a transient null in `SystemSpecsReport` cannot wipe
   stored inventory (AgentService.cs:551-558; tested:
   `Heartbeat_WithSpecs_PersistsAndPreservesLastKnownInventory`).
5. **Release ref-guard is real** — `UX_AgentReleases_Active` (unique on `IsActive=true AND
   IsDeleted=false`, DBContext.cs:226-230) makes "two active releases" impossible at the DB level;
   `PublishAsync` deactivates + inserts inside a transaction (AgentReleaseService.cs:97-116); blob
   content is deduped by SHA-256; and `BlobGcService` excludes blobs referenced by any non-deleted
   `AgentRelease` both when selecting candidates and again inside the conditional
   `ExecuteDeleteAsync` (BlobGcService.cs:113, 250).
6. **Provisioning surface is defended** — announce/enroll are `[AllowAnonymous]` but rate-limited
   per endpoint + IP + token (AgentController.cs:41-83, 220-240), self-announce hard-fails for
   known/pre-registered/soft-deleted MACs with distinct messages (AgentService.cs:166-193, all
   adversarially tested), and enrollment tokens are BCrypt-hashed, 24h-expiring, and cleared on
   success (:309-311, 356-358).
7. **Path traversal is blocked** — `LocalStorageService.ResolvePath` normalizes separators,
   resolves with `Path.GetFullPath`, and requires the result to stay under the root (case-insensitive
   on Windows) (LocalStorageService.cs:136-162; covered by LocalStorageServiceTests). See F5 for the
   remaining *entitlement* gap.
8. **Dashboard command allowlist** — `ComputersController.EnqueueCommand` only forwards a fixed set
   of operator-issuable command types (ComputersController.cs:18-25, 150-151); acks are
   ownership-checked against the agent's computer, idempotent, and the result is length-capped at
   512 both in code and in the column (AgentCommandService.cs:86-97, 115-120; DBContext.cs:186-188).
9. **Station moves are transactional and complete** — `MoveToStationAsync` blocks while an install
   is downloading/installing, cancels pending jobs with an audit-friendly reason, closes the open
   `ComputerStationHistory` entry (single-open-entry enforced by the filtered unique index,
   DBContext.cs:622-625), reparents the resource, and invalidates the authz scope cache — all in one
   transaction (ComputerService.cs:119-165, 167-217).

---

## Findings

### F1 — HIGH · Heartbeat regenerates the pending rotation token on *every* heartbeat past the threshold, so a retried/out-of-order heartbeat can permanently lock out the agent
**File:** MProject.Application/Services/Assets/AgentService.cs:410-422
**Category:** correctness / race condition / hot path

**Evidence.** The rotation trigger is only `tokenIssuedAt == null || now - tokenIssuedAt >
_tokenRotateAfter` (:412). `TokenIssuedAt` is not updated until the agent first *uses* the new
token (`PromotePendingToken`, :758-765). Therefore, once the threshold is crossed, **every**
heartbeat authenticated with the old token generates a fresh secret and **overwrites**
`PendingTokenHash/Salt/IssuedAt/ExpiresAt` (:414-420) — the previously returned
`RotatedAgentToken` becomes unverifiable the moment the next old-token heartbeat is processed.
The method never checks `IsPendingTokenUsable` (which exists, :753-756) before regenerating.

**Failure scenario.** Agent sends heartbeat H1 with the old token; the response times out
client-side, so it retries with H2 (still the old token). Server state after processing: pending =
token B (H2's). If the delayed H1 response then arrives and the agent adopts token A from it (or
any out-of-order adoption on a flaky factory network), the agent's stored token matches neither the
current hash (old secret) nor the pending hash (B) → every subsequent call is 401
(`AuthenticateAsync`, :664-676). There is no self-service recovery: announce refuses known MACs
(:188-192) and enrollment needs an admin-issued token. One machine silently drops off the fleet
until someone re-provisions it. With a 30-day rotation across every station, each agent crosses
this window monthly.

**Minimal fix.** Make rotation idempotent: only generate a new pending token when none is usable —
`if (tokenIssuedAt == null || now - tokenIssuedAt.Value > _tokenRotateAfter) { if
(!IsPendingTokenUsable(agent, now)) { ...generate... } }`. (The already-issued secret can't be
re-returned since only its hash is stored — returning `RotatedAgentToken = null` on subsequent
heartbeats is fine: the old token stays valid, and if the agent never adopted the pending token it
retries after the 7-day grace expires.) No test covers double-heartbeat rotation; the existing
rotation test issues exactly one heartbeat.

---

### F2 — MEDIUM · Liveness watchdog flips computers Offline without re-checking liveness in the UPDATE; a computer that heartbeats between select and update is marked Offline and gets a spurious WentOffline event
**File:** MProject.Application/Services/Assets/ComputerLivenessWatchdogService.cs:75-114
**Category:** correctness / read-then-write race / watchdog-vs-heartbeat consistency

**Evidence.** The sweep selects stale ids with the full predicate (`Online && LastSeenAt < cutoff`,
:75-83) but the write applies **only** `staleIds.Contains(c.Id)` (:102-107 relational, :93-97
in-memory) — the status/recency predicate is not repeated. A heartbeat that lands between the two
statements sets `LiveStatus = Online, LastSeenAt = now` (AgentService.cs:605-613) and is then
immediately overwritten to `Offline`. The computer reads Offline until its *next* heartbeat, and
`Computer.WentOffline` domain events are emitted for **all** `staleIds` (:109-110) regardless of
what actually flipped, so downstream consumers (alerting/notifications) get a false offline signal
for a machine that is demonstrably online. `ComputerLivenessWatchdogServiceTests` covers batching,
null-LastSeen, and events, but not this interleaving.

**Minimal fix.** Repeat the predicate in the write —
`.Where(c => staleIds.Contains(c.Id) && c.LiveStatus == ComputerLiveStatus.Online && c.LastSeenAt < cutoff)`
— and emit domain events only for rows actually flipped (for the relational branch, select the ids
again inside the transaction or use the same filtered predicate for both; the current
`count`-vs-events mismatch already exists whenever the race fires).

---

### F3 — MEDIUM · Test-metrics ingestion trusts agent input: duplicate SlotIndex crashes with a 500, negative/absurd values and unbounded slot counts are persisted, `Slots: null` NREs
**Files:** MProject.Application/Services/Assets/AgentService.cs:854-894; MProject.Application/Models/AgentModels.cs:243-256
**Category:** input validation on agent-supplied data (named domain focus)

**Evidence.** `ReportTestMetricsRequest`/`TestCounterSlotReport` carry no validation attributes and
the service adds none:

- **Duplicate `SlotIndex` in one request → 500.** `StationTestCounter` has composite PK
  `(ComputerId, SlotIndex)` (DBContext.cs:164-165). For a slot index not yet in the DB, each
  duplicate entry takes the `Add` branch (:877-881); the second `Add` with an identical key throws
  EF's "another instance with the same key is already being tracked" `InvalidOperationException` →
  unhandled 500. A buggy agent hits this deterministically on every report.
- **No range checks.** `SlotIndex = -5`, `PassCount = -1`, `Rj45Count = int.MinValue` are all
  persisted verbatim (:883-886) and then served to the dashboard by `GetTestCountersAsync`
  (:896-913). `CollectedAt` is only defaulted when exactly `default` (:867) — any garbage timestamp
  (year 9999) is stored.
- **No cap on `Slots.Count`.** A single request can insert e.g. 100k counter rows per computer
  (PK allows every distinct int), bloating a table the UI reads unpaged.
- **`{"slots": null}` → NRE.** System.Text.Json overwrites the `= new()` default with null;
  `request.Slots.Count` (:864) throws → 500.

`AgentServiceTests.ReportTestMetrics_UpsertsCounterPerSlot` covers only the happy path.

**Minimal fix.** At the top of `ReportTestMetricsAsync`: treat null `Slots` as empty; reject (400
via `ArgumentException`) `SlotIndex` outside a sane range (e.g. 0..255) and negative counters;
de-duplicate by `SlotIndex` (last-write-wins) before the upsert loop; cap slot count per request.

---

### F4 — MEDIUM · Re-creating a computer whose MAC belongs to a soft-deleted row is a deterministic 500 (existence check is IsDeleted-filtered, the unique index is not)
**File:** MProject.Application/Services/Assets/ComputerService.cs:52-58 (vs DBContext.cs:152-154)
**Category:** correctness / global-query-filter surprise / error handling

**Evidence.** `CreateComputerAsync` checks `AnyAsync(x => !x.IsDeleted && x.MacAddress == mac)`
(:53-54) — which, combined with the global filter, can never see soft-deleted rows — but the unique
index on `Computer.MacAddress` (DBContext.cs:152-154) has **no** `IsDeleted = false` filter (unlike
the Model/Station/ModelUserManager unique indexes, DBContext.cs:563, 578-580, 606-608). Deleting a
computer keeps its MAC reserved forever at the DB level. The sibling flow knows this:
`AnnounceAsync` uses `IgnoreQueryFilters()` and returns an explicit "MAC cannot be reused" message
for deleted rows (AgentService.cs:166-179, tested by `Announce_MacFromSoftDeletedComputer_...`).
The admin flow does not: the pre-check passes, `SaveChangesAsync` (:105) throws
`DbUpdateException` → raw 500 with no hint that a deleted computer holds the MAC. This is
deterministic, not just a race (the concurrent-duplicate race also lands here as a 500).

**Minimal fix.** Mirror `AnnounceAsync`: perform the duplicate check with `IgnoreQueryFilters()`
and throw a clean `InvalidOperationException` distinguishing "already exists" from "was previously
provisioned and deleted" (or decide MACs are reusable and filter the unique index — one or the
other, consistently with the announce flow). Wrap the insert's `DbUpdateException` into the same
message for the race case.

---

### F5 — MEDIUM · `GET agent/v1/blobs/local` serves any stored object to any enrolled agent — no entitlement check against the Blobs table or the agent's assignments
**Files:** MProject.Api/Controllers/Assets/AgentController.cs:132-140; MProject.Infrastructure/Storage/LocalStorageService.cs:43-59
**Category:** security / resource-level authorization (defense-in-depth)

**Evidence.** The endpoint takes a raw `[FromQuery] string path` and hands it to
`_storage.DownloadAsync(path)` with no check that the path corresponds to a `Blob` row, let alone
one the calling agent is entitled to (its manifest's software files, its override files, or the
active agent release). Traversal outside the storage root is blocked (ResolvePath, :136-162), but
**everything under the root** is fair game: every software package version, every override file /
config baseline of every team, and any leftover objects. Mitigation in practice: storage paths are
content-addressed (`agent-releases/{sha256}.zip`, sha-based software paths), so fetching a blob
requires knowing its 256-bit hash — effectively a capability URL. But any path an agent ever
learned (old manifests, a decommissioned machine's logs) remains fetchable by **any** active agent
forever, and a compromised station token becomes a skeleton key for all distributable content.

**Minimal fix.** Before streaming, require the path to match a known blob:
`await _context.Blobs.AnyAsync(b => b.StoragePath == path)` (cheap, indexed by PK/sha lookup —
add an index on `StoragePath` if needed), and prefer scoping the check to blobs reachable from the
agent's computer (its installation-job manifest files, its override files, the active agent
release). At minimum the existence check kills "serve arbitrary non-blob files placed under the
root".

---

### F6 — MEDIUM · All Assets endpoints require *global* grants, so the per-user visibility filtering inside `GetComputersAsync` is unreachable through the API (pass-3 F6 pattern)
**Files:** MProject.Api/Controllers/Assets/ComputersController.cs:44-50 (and every `[RequirePermission]` in ComputersController/ModelsController/StationsController/AgentReleasesController); MProject.Api/Filters/PermissionAuthorizationFilter.cs:57-62; MProject.Application/Services/Assets/ComputerService.cs:276-303
**Category:** authorization consistency / dead delegation path

**Evidence.** Every `[RequirePermission(...)]` in this scope is declared without a
`resourceIdRouteKey`, so the filter resolves `resourceId = null` (PermissionAuthorizationFilter.cs:
59-62) and — per the pass-3 analysis of `RbacGrantQueryService` — only **null-scoped (global)**
grants pass. Consequently a user holding team-/model-scoped `computer.read` (the seeded scoped
`Member`-style delegation the authz system is built for) gets 403 on *every* assets endpoint.
Meanwhile `GetComputersAsync` carefully calls `GetVisibleResourceIdsAsync` and filters the listing
(ComputerService.cs:279-303) — but for any caller who passed the global gate,
`GetVisibleResourceIdsAsync` returns `null` = "all" (AuthorizedResourceQueryService.cs:84), so the
filter is always a no-op via HTTP; it is exercised only by unit tests. The detail endpoints
(`GetComputer`, `GetStationHistory`, `GetRuntimeStatus`, `GetTestCounters`) do no resource-level
check at all, which is *consistent* with a global-only contract but contradicts the effort spent
on visibility filtering in the list. Fail-closed today, but the same "delegation advertised by the
service layer, unreachable through the API" inconsistency flagged as pass-3 F6.

**Minimal fix.** Decide the contract once: if assets access is global-only, delete the
`visibleResourceIds` filtering from `GetComputersAsync` (and its `currentUserId` parameter); if
scoped access is intended, add `resourceIdRouteKey`/lookup to the detail routes and keep the list
filtering. Should be resolved together with pass-3 F6.

---

### F7 — MEDIUM · `ActivateAsync` is non-atomic: deactivate-all commits before the new activation, unlike the transactional sibling `PublishAsync`
**File:** MProject.Application/Services/Assets/AgentReleaseService.cs:124-135 (vs :97-116)
**Category:** correctness / missing transaction / sibling asymmetry

**Evidence.** `ActivateAsync` calls `DeactivateAllAsync(ct)` — which issues its **own**
`SaveChangesAsync` (:150-156) — then sets `release.IsActive = true` and saves again (:132-133).
`PublishAsync` wraps the identical deactivate-then-activate sequence in
`ExecuteInTransactionAsync` (:97-116). If the second save fails (transient DB error, process
death), the system is left with **zero** active releases: agents silently stop receiving updates
(`ResolveAgentUpdateAsync` returns null, AgentService.cs:487-491) and nothing surfaces the state.
Two concurrent `ActivateAsync` calls both pass `DeactivateAllAsync` and both insert an active row;
`UX_AgentReleases_Active` (DBContext.cs:226-230) correctly rejects the loser — but as an uncaught
`DbUpdateException` → raw 500. `AgentReleaseServiceTests.Activate_MakesTargetSoleActive` covers
only the sequential happy path.

**Minimal fix.** Wrap `DeactivateAllAsync` + activation in `ExecuteInTransactionAsync` exactly as
`PublishAsync` does, and translate the unique-index violation into the same clean conflict error
in both methods. (Related, conscious-decision item: `DeleteAsync` (:137-148) happily deletes the
*active* release, leaving no active release — if that is not intended, guard it here too.)

---

### F8 — MEDIUM · Agent commands are redelivered forever with no expiry or retry cap; 16 permanently-unacked commands starve everything behind them
**File:** MProject.Application/Services/Assets/AgentCommandService.cs:54-72 (and EnqueueAsync :28-52)
**Category:** correctness / unbounded queue semantics on the hot path

**Evidence.** `ClaimPendingForDeliveryAsync` selects `AckedAt == null` ordered by `EnqueuedAt`,
takes `MaxCommandsPerHeartbeat = 16`, stamps `DeliveredAt` only if unset, and returns them —
**every** heartbeat, forever, until the agent acks. There is no delivery count, no TTL, no
dead-letter transition. Two concrete consequences: (1) an agent that cannot ack a specific command
(older agent version receiving a command type it doesn't recognize, or a payload that crashes its
handler before ack) receives the same 16 commands on every heartbeat indefinitely, and any command
enqueued after the 16th stuck one is **never delivered** (`Take(16)` on `OrderBy(EnqueuedAt)`);
(2) `EnqueueAsync` has no per-computer pending cap, so repeated dashboard clicks against an offline
computer accumulate rows without bound, all of which fire in sequence when the machine returns
(e.g. N stacked `Restart` commands). Tests cover delivery/ack/ownership but not redelivery aging.

**Minimal fix.** Add an expiry to the claim query (e.g. skip commands where
`EnqueuedAt < now - TTL`, marking them `Expired` so the dashboard sees the outcome), and cap
pending commands per computer in `EnqueueAsync` (reject or collapse duplicates of the same type).

---

### F9 — MEDIUM · Agent-supplied strings are persisted with no length limits — every heartbeat can write arbitrarily large `Hostname`/`LastError`/`AgentVersion`/spec strings
**Files:** MProject.Application/Services/Assets/AgentService.cs:400-404, 554-556, 605-613; MProject.Application/Models/AgentModels.cs:59-83; MProject.Domain/Entities/Assets/Computer.cs:11-19 (no MaxLength anywhere; DBContext configures none for Computer/ComputerRuntimeStatus)
**Category:** input validation on agent-supplied data / resource abuse

**Evidence.** `AgentHeartbeatRequest.Hostname/IpAddress/AgentVersion/LastError` and
`SystemSpecsReport.OsVersion/CpuModel` are free-form strings with no `[MaxLength]`, and neither the
entities nor `DBContext` set column lengths for `Computer` or `ComputerRuntimeStatus` (contrast:
`AgentCommand.AckResult` is capped at 512 in code *and* column, AgentCommandService.cs:115-120;
`AgentRelease.Notes/Signature` at 2048, DBContext.cs:215-220 — so the codebase's own convention is
to cap externally-supplied text). `NormalizeOptional` only trims (:809-812). A single compromised
or buggy agent can ship a multi-megabyte `LastError` on every heartbeat (bounded only by Kestrel's
~28 MB body limit), inflating the `Computers` row + WAL on the hottest write path and breaking any
UI that renders these fields. The same applies to announce/enroll (`MacAddress`, `Hostname` — a
bogus 1 MB "MAC" becomes a unique-indexed value).

**Minimal fix.** Cap at the edge like `TrimResult` does for ack results: truncate or reject
Hostname/IpAddress/AgentVersion/MacAddress at ~256 and LastError/OsVersion/CpuModel at ~1024 in
`AgentService` normalization helpers (one shared clamp), and add matching `HasMaxLength`
configuration for the columns.

---

### F10 — MEDIUM · Legacy status mapping masks `Offline` behind `Updating`/`Error`, so a machine that dies mid-update reads "Updating" indefinitely — and the mapping is duplicated in `ComputerService.ProjectToDto`
**Files:** MProject.Application/Services/Assets/ComputerStatusMapper.cs:7-23; MProject.Application/Services/Assets/ComputerService.cs:249-254
**Category:** watchdog-vs-mapper consistency (named domain focus) / duplicated semantics

**Evidence.** `DeriveLegacy` gives `OperationalStatus` absolute precedence: `Updating` → `Updating`
and `Error/CrashLoop` → `Error` regardless of `LiveStatus`. The watchdog only flips `LiveStatus`
(ComputerLivenessWatchdogService.cs:102-107) and never touches `OperationalStatus`, so a computer
that goes dark while `OperationalStatus == Updating` shows legacy status **"Updating" forever** —
the one signal an operator needs ("this machine stopped responding mid-update") is hidden. The
same precedence is re-implemented as a nested conditional inside the `ProjectToDto` expression
(ComputerService.cs:249-254, necessary because an expression tree can't call the mapper) — the two
already differ at the edges (the mapper throws on an unknown `OperationalStatus`, the projection
falls through to `Unknown`), which is exactly the drift this duplication invites. Listing/detail
DTOs also expose the raw `LiveStatus`, so the dashboard *can* see offline — the inconsistency is in
the derived legacy field that the heartbeat response and DTO both advertise.

**Minimal fix.** Decide precedence deliberately: either `Offline` wins over `Updating` (and
arguably over `Error`) in `DeriveLegacy` — a two-line change — or document that legacy status is
operational-first and offline must be read from `LiveStatus`. Add a
`ComputerStatusMapperTests` case for `(Offline, Updating)` either way, and a comment on
`ProjectToDto` stating it must mirror `DeriveLegacy` (or generate the expression from one shared
definition).

---

## Unnecessary Code

1. **`AnnounceAsync`'s assignment-state conditional is dead.** AgentService.cs:252-255 computes
   `assignmentState` from `computer.Resource.ParentResourceId`, but the resource was created 50
   lines earlier with `ParentResourceId = null` (:199) and nothing in between can assign it — the
   response is always `PendingAssignment` with `StationResourceId = null`. Replace with the
   constants (or leave a comment if a future pre-assignment flow is planned).
2. **Service-level `EnsureAuthorizedAsync` duplicates the controller gate — in two of four
   services.** ModelService.cs:32-37 and StationService.cs:36-41 re-check the same global
   permission that `[RequirePermission]` already enforced on every calling action
   (ModelsController/StationsController), while `ComputerService` and `AgentReleaseService` rely on
   the controller alone. Grep confirms no non-controller caller of the model/station mutations. The
   double check is harmless (and useful for unit tests) but the *asymmetry* is the problem: pick
   one convention. If service-level checks are the defense-in-depth standard, `ComputerService`'s
   mutations and `AgentReleaseService` are missing them; if not, the two existing ones are dead
   weight on every call.
3. **`GetComputersAsync`'s visibility filtering is unreachable via the API** — see F6; it is live
   code only for unit tests today.
4. `ComputerService.AssignToStationAsync`/`UnassignFromStationAsync` (:109-117) are two-line
   pass-throughs to `MoveToStationAsync` — acceptable as interface seams for two distinct
   endpoints; noted for completeness only, not removal.

---

## Simpler Alternative

Small, local consolidations only — no redesign warranted:

1. **Shared DTO projection expressions.** `ModelService.GetModelAsync` and `GetModelsAsync` carry
   two verbatim ~18-line `ModelDto` projections (ModelService.cs:200-216 vs :232-247), and
   `StationService.GetStationAsync`/`GetStationsAsync` two verbatim ~14-line `StationDto`
   projections (StationService.cs:177-190 vs :206-218). `ComputerService` already shows the right
   pattern (`static readonly Expression<Func<...>> ProjectToDto`, ComputerService.cs:239-274) —
   apply it to both services so the pairs can't drift.
2. **One clamp helper for agent-supplied strings.** `NormalizeOptional` is duplicated verbatim in
   AgentService.cs:809-812 and ComputerService.cs:374-377 (grep-confirmed identical); folding F9's
   length caps into a single shared `NormalizeAndClamp(value, max)` fixes the duplication and the
   validation gap in one place.
3. **One definition for legacy status.** Per F10, derive `ComputerStatusMapper.DeriveLegacy` and
   the `ProjectToDto` conditional from a single source (or pin them together with tests) instead of
   maintaining parallel switch logic.

---

## Complexity Report

- **`AgentService.RecordHeartbeatAsync` (AgentService.cs:391-483, ~93 lines)** — does six jobs:
  liveness stamp, token rotation, computer update, runtime upsert, command claim, update-offer +
  station-name lookup. Over the ~80-line threshold; the helpers are already well-factored, so this
  is orchestration breadth rather than tangle. Flagged, no redesign proposed (F1's fix lands here).
- **`AgentService` as a file (~935 lines)** — provisioning, authentication/crypto, heartbeat,
  runtime status, test metrics, and maintenance mode in one class. Cohesive enough per-method;
  worth splitting only if it keeps growing (the crypto/token block, :707-815, is the natural seam).
- **`AgentService.AnnounceAsync` (:145-266, ~120 lines)** — linear validate → refuse → create flow;
  long but single-purpose and heavily tested. Not flagged for change.
- Nothing else in scope exceeds the ~80-line/multiple-jobs threshold; the >15-line×2 copy-paste
  instances are exactly the projections listed under Simpler Alternative.

### Test Gaps (informational)
- **No `StationServiceTests` at all** — station create/update/delete (including the
  name-uniqueness check and the parent-change cache invalidation) is untested.
- **No double-heartbeat rotation test (F1)**; the rotation test issues a single heartbeat.
- **No watchdog interleaving test (F2)** — heartbeat between select and flip.
- **No test-metrics adversarial tests (F3)** — duplicate slot, negative values, null `Slots`.
- **No `CreateComputerAsync` test for a soft-deleted MAC (F4)** — the announce flow has one; the
  admin flow doesn't.
- **No concurrency/atomicity test for `ActivateAsync` (F7)**, and no test that `DeleteAsync` of the
  active release leaves agents update-less.
- `ComputerStatusMapperTests` does not cover `(Offline, Updating)` / `(Offline, Error)` (F10).

---

## Out-of-Scope Observations (context files, not counted as findings)

- **`IsNewerVersion` fails closed on unparseable versions** (AgentService.cs:506-511): an agent
  reporting a non-`System.Version` string (e.g. `1.2.3-beta`) or no version at all is never offered
  an update. Conservative and probably intended, but worth knowing when a mis-built agent "won't
  update".
- **Publish-vs-GC TOCTOU (BlobGcService)**: `PublishAsync` can reuse an existing unreferenced blob
  row older than the GC grace period while the nightly sweep is deleting it; the FK `Restrict`
  makes the loser fail loudly rather than corrupt, and the window is a re-publish of ≥7-day-old
  orphaned content during the sweep — negligible, noted for awareness.
- **`GetPresignedUploadUrlAsync` on LocalStorageService returns a raw filesystem path**
  (LocalStorageService.cs:61-64) — leaks server directory layout if any caller ever surfaces it to
  a client; today's in-scope callers don't use it.

---

## Final Recommendation

**Fix-forward; no redesign.** The protocol's crypto, provisioning defenses, transactional heartbeat
write, and release ref-guard are sound and should be preserved. Order of work:

1. **F1** — make rotation idempotent (guard on `IsPendingTokenUsable`); it is a two-line change on
   the hottest path with the worst failure mode (silent fleet lockout). Add the double-heartbeat
   test.
2. **F2** — repeat the liveness predicate in the watchdog's UPDATE and align domain events with
   actually-flipped rows.
3. **F3 + F9** — one validation pass over agent-supplied input (test-metrics slots + string length
   clamps), sharing the clamp helper from Simpler Alternative #2.
4. **F4, F7** — align the MAC existence check with the unfiltered unique index, and make
   `ActivateAsync` transactional like `PublishAsync` (translate the unique violation to a clean
   conflict in both).
5. **F5, F8** — blob entitlement check; command TTL + per-computer cap.
6. **F6, F10** — contract decisions (global-vs-scoped assets authorization, together with pass-3
   F6; offline-vs-updating precedence) — small code, but they need an explicit call.
7. Optionally the consolidations (shared projections, shared normalize/clamp, single legacy-status
   definition) and the dead announce conditional.

---

## Tóm tắt (Tiếng Việt)

- Đã đọc **toàn bộ** 13 file trong phạm vi (8 service Assets, 5 controller) cùng context (storage,
  filter phân quyền, entity, index DB, options, test); **không có lỗi CRITICAL**.
- **1 lỗi HIGH (F1):** sau khi token quá hạn xoay vòng (30 ngày), **mỗi** heartbeat đều sinh mới và
  ghi đè pending token — heartbeat retry/về trễ có thể vô hiệu hóa token mà agent vừa lưu → agent
  bị khóa vĩnh viễn, phải nhờ admin re-enroll. Sửa chỉ cần guard bằng `IsPendingTokenUsable`.
- **9 lỗi MEDIUM:** (F2) watchdog flip Offline không re-check trong UPDATE → máy vừa heartbeat vẫn
  bị đánh Offline + bắn event WentOffline giả; (F3) test-metrics không validate: trùng SlotIndex →
  500, giá trị âm/timestamp rác được lưu, `Slots: null` → NRE; (F4) tạo computer trùng MAC của máy
  đã soft-delete → 500 do unique index không lọc IsDeleted; (F5) endpoint tải blob không kiểm tra
  quyền theo tài nguyên — agent nào cũng tải được mọi object trong storage nếu biết path; (F6) mọi
  endpoint Assets đòi quyền **global** nên lọc visibility trong `GetComputersAsync` chết qua API
  (lặp lại pattern F6 của pass 3); (F7) `ActivateAsync` không transaction — có thể rơi vào trạng
  thái **không có release active**; (F8) command chưa ack bị gửi lại mãi mãi, 16 command kẹt chặn
  toàn bộ hàng đợi; (F9) chuỗi từ agent (Hostname/LastError…) không giới hạn độ dài; (F10) mapping
  legacy che mất Offline sau Updating/Error — máy chết giữa lúc update hiển thị "Updating" mãi.
- **Điểm tốt cần giữ:** token HMAC + pepper theo version, so sánh fixed-time, rotation có grace;
  heartbeat ghi bằng ExecuteUpdate + transaction (đã test rollback); ref-guard release thật sự
  (unique index 1-active + BlobGc loại trừ AgentReleases); chống path traversal chuẩn; rate-limit
  announce/enroll; allowlist command từ dashboard.
- **Khuyến nghị:** sửa F1 trước (2 dòng, hậu quả nặng nhất), rồi F2, gộp F3+F9 thành một đợt
  validate input agent, tiếp F4/F7, F5/F8, cuối cùng chốt contract cho F6/F10. Không cần redesign.
