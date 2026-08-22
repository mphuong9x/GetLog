# MProject Durable Project Memory

Last verified: 2026-08-22

This document is durable project context for future Codex sessions. It contains
architecture, invariants, commands, and known traps. It intentionally excludes
credentials, machine-specific secrets, transient artifact status, commit
status, and claims such as "not yet tested" that become stale quickly.

The current remediation backlog and close criteria are maintained in
`PROJECT_REVIEW_CHECKLIST.md`. Reverify each item against current code before
changing its status, and do not mark an item complete without its required test.

## 1. Status Vocabulary and Source of Truth

- **Invariant**: an intended system constraint that should not change casually.
- **Current**: verified in the working tree on the date above.
- **Verify first**: historically true or environment-dependent; inspect current
  code/config/runtime before relying on it.
- Source priority is current code/tests/scripts, then current runbooks, then the
  verified legacy specification, then Claude memory.
- Important local documents are ignored by the root `.gitignore`: `docs/`,
  `.claude/`, `PRODUCT.MD`, `Old_program/`, and sample payloads. They may be
  absent in a fresh clone.

## 2. Repository Map

The Git root is the parent of this directory.

| Path | Role | Main technology |
| --- | --- | --- |
| `MProjectFrontend/` | Browser operations console | React 19, TypeScript, Vite 7, React Router 7, TanStack Query 5, Ant Design 6, Tailwind 4 |
| `MProjectBackend/MProject.Api/` | HTTP API, auth, agent control plane, SPA host | ASP.NET Core 8 |
| `MProjectBackend/MProject.Application/` | Use cases, authorization, approvals, software/deploy services | .NET 8 |
| `MProjectBackend/MProject.Domain/` | Entities, enums, options | .NET 8 |
| `MProjectBackend/MProject.Infrastructure/` | EF Core context/migrations, PostgreSQL, storage, seeding | .NET 8, Npgsql, MinIO/local storage |
| `MProjectBackend/MProject.Tests/` | Backend unit/integration-style tests | xUnit, EF InMemory/SQLite |
| `MProjectAgent/` | Station Windows Service: enroll, poll, download, deploy, supervise, inventory, logs, OTA | .NET 8 Worker Service |
| `MProjectAgent.Ipc.Contracts/` | Serializer-neutral agent/launcher wire DTOs | netstandard2.0 |
| `MProjectAgent.Tests/` | Agent and IPC compatibility tests | xUnit |
| `MProjectLauncher/` | Operator tray UI for local apps | WPF .NET Framework 4.8 |
| `scripts/` | Package, sign, install, update, IIS, and E2E scripts | Windows PowerShell 5.1 compatible |
| `Old_program/` | Ignored legacy sources used for behavior comparison | Mostly .NET Framework desktop apps |
| `Sample_Software/`, `Cpp_Software/` | Ignored representative payload trees | Managed and native payloads |

The tracked monorepo currently has roughly 900 files. The backend is layered,
not a set of independent services. Avoid adding a new layer for local changes.

## 3. Product Boundary

**Invariant:** MProject replaces the legacy software distribution and management
layer, while existing manufacturing test programs remain executable payloads.

Legacy distribution group:

- `Old_program/UIStore`: station-side WPF downloader/launcher, local cache,
  process state, SFTP access, and CheckSumCustom configuration edits.
- `Old_program/Upload`: administrator desktop uploader and access manager.
- `Old_program/AppUpdater`: UIStore updater.

Legacy test group includes
`Old_program/FTU Program/CPEI_MFG`,
`Old_program/FcdDownload/WebControl_WinForm`, `Old_program/UiTest`, and the
CPEI_MFG-style payload represented under `Sample_Software`. These programs own
test, DHCP/SFIS/golden/error-code/device behavior. Do not reimplement those
engines as part of ordinary MProject distribution work. Older Claude notes use
pre-flattening paths such as `Old_program/system/auto-download`; verify paths in
the current local tree instead.

Payload rules:

- Accept arbitrary folder trees; do not assume a neat `bin/config` layout.
- Treat native C++ and managed executables as first-class payloads.
- Preserve relative paths and set `WorkingDirectory` to the entry-point folder.
- Entry point, watch process, icon, and overridable paths can be manual metadata.
- Keep content-addressed SHA-256 blobs, deduplication, and delta downloads.
- No operator login at a station. Human authentication and RBAC live on the web.

Read `../docs/uistore_parity_spec_verified.md` for observed legacy behavior.
Use the original parity spec only for explicitly recorded design decisions.
Any old statement that agent contracts are freely breakable is **verify first**:
once stations are deployed, rollout compatibility becomes a production concern.

## 4. Browser and API Flow

### Frontend composition

- `src/main.tsx` installs theme, page metadata, authentication, i18n, and toast
  providers.
- `src/App.tsx` owns the shared `QueryClient`, lazy routes, layout, and access
  gates. Queries retry once and do not refetch globally on window focus; live
  screens define their own polling.
- `src/layout/AppLayout.tsx` hosts the sidebar/header/outlet, route-keyed error
  boundary, and the global upload/download transfer center.
- Domain HTTP modules live under `src/api`; query/mutation hooks live under
  `src/hooks/api`. Reuse these rather than calling Axios directly in pages.
- Organization is deliberately one combined screen. `/teams` and `/departments`
  redirect to `/organization`; do not split them back into separate primary
  navigation without a new product decision.

### API addressing

- `src/api/services/axios-client.ts` rewrites `/api/...` to `/api/v1/...`.
- `VITE_API_URL` is optional. Empty means same-origin and is the production
  shape. Vite dev proxies `/api` to `http://localhost:5107`.
- The Vite proxy does not proxy `/agent`; station agents must call the backend
  directly.
- The Vite proxy also does not proxy `/git`. With an empty `VITE_API_URL` during
  development, a displayed clone URL can incorrectly point at Vite port 5173.
- Production backend serves `wwwroot/index.html`, static assets, and a fallback
  for non-reserved SPA routes. Reserved prefixes include `/api`, `/agent`,
  `/git`, `/health`, `/metrics`, and `/swagger`.

### Human authentication and authorization

- Browser access and refresh tokens are stored in `sessionStorage`.
- The Axios interceptor attaches Bearer access tokens, serializes concurrent
  refresh attempts, retries the original request once, and clears auth on a
  failed refresh.
- `AuthContext` restores an expired access token, loads `/api/v1/auth/me`, and
  exposes roles, permissions, team information, and route access checks.
- Frontend access gates are usability controls, not a security boundary. Every
  backend write/read still requires the appropriate authorization filter.
- Some routes deliberately remain visible and render `AdminOnlyNotice` for a
  non-admin. Do not replace that behavior with a generic hidden route without
  checking the product requirement.
- Test Monitor, Agent Releases, Override Files, and Config Baselines currently
  use this visible-notice behavior. Roles is intentionally hidden for a
  non-admin. Installation Jobs remains readable with software read permission,
  while mutation actions require the install-management permission.

### Pagination invariant

Backend `PagedRequest` clamps page size to 100. A request for 200 or 500 silently
returns at most 100. Use existing `getAll*` methods or
`src/api/services/fetch-all-pages.ts` only when the UI genuinely requires the
complete set. User-paged lists should continue fetching one page at a time.

### API identity conventions

- `Model.id` is not `Model.resourceId`, and `Station.id` is not
  `Station.resourceId`. ACL, assignment, hierarchy, and config endpoints often
  require a resource ID. Check the request type instead of substituting one for
  the other.
- API enums are serialized as their string names with exact casing, for example
  `Draft`, `Released`, `Active`, and `PendingApproval`; do not send ordinals.
- Repository management APIs use the singular `/api/v1/repository` family,
  while Git Smart HTTP clone traffic uses `/git/{owner}/{slug}.git`.

### Polling and transfer behavior

- There is no WebSocket, SSE, or SignalR channel. Live behavior is polling:
  computers approximately every 30 seconds, active installation jobs every 3
  seconds, and approval badge state approximately every 60 seconds.
- Installation polling can stop when no active job remains, so a job created by
  another actor may require a manual refresh. Do not assume all operational
  screens are continuously live.
- Software/document uploads use tus with 16 MiB chunks. SHA-256 is normally
  computed in a Web Worker, cached in IndexedDB, and uploaded only when the
  server does not already have the blob.
- The global Transfer Center is intentionally split into action and item
  contexts so byte progress does not rerender the whole application. Progress
  survives route navigation, but not a full browser reload.
- tus requests do not pass through the Axios refresh interceptor. Token expiry
  during a long upload and ambiguous network retries are separate failure paths
  that need focused testing when upload code changes.

## 5. Backend Architecture and Data

### Startup

- `MProject.Api/Program.cs` registers scoped application services and hosted
  watchdog/cleanup/outbox services, JWT and agent authentication, rate limits,
  API versioning, controllers, storage, metrics, exception handling, and SPA
  hosting.
- Storage provider is configurable as local disk or MinIO. Uploads use tus;
  large-file behavior and hash verification are part of the integrity model.
- **Current:** startup calls `AppDbSeeder.SeedAsync`. The seeder acquires a
  PostgreSQL advisory lock, runs `Database.MigrateAsync()`, and then applies
  seed data. This supersedes old notes that the backend never auto-migrates.
- Deployment scripts may also produce/apply an idempotent schema SQL artifact.
  Inspect the target runbook and script before production deployment.

### Main domain clusters

- Identity/authorization: User, Team, Department, Role, Permission,
  RoleAssignment, ACL, Resource, refresh token, authorization audit.
- Assets/fleet: Computer, Agent, AgentRelease, AgentCommand, runtime status,
  station history, test counters, test results, test assets.
- Software: SoftwarePackage, SoftwareVersion, SoftwareFile, Blob,
  StationSoftwareAssignment, PcInstallationRecord, InstallationJob.
- Configuration: ConfigParameter/Target, ConfigValueOverride,
  RenderedConfigBlob, ConfigBaseline/Rule, retained read/deploy support for
  OverrideFile.
- Operations: ApprovalPolicy/Step/Request/Action, DomainEvent outbox,
  repositories, user documents/shares.

### Cross-cutting behavior

- Most domain entities support soft delete through global EF query filters.
  Reconciliation/uninstall/history code sometimes requires
  `IgnoreQueryFilters`; check existing patterns instead of assuming deleted rows
  are absent.
- Blob deletion must consider every live reference, including software files,
  override/config/document references. `ReferenceCount` can drift and is not by
  itself proof that a blob is unreferenced.
- Installation jobs define state transitions and watchdog timestamps, but the
  current entity has no optimistic-concurrency token. Concurrent progress,
  cancel, complete, and watchdog writes must be treated as a current open race.
  Progress keep-alives remain semantically important even if byte count does not
  change.
- `DomainEventDispatcherService` currently logs pending events and marks them
  processed; it does not dispatch handlers or retry side effects. Do not treat
  the current table/hosted service as a transactional outbox.

### RBAC/ACL

- Permission constants in `AppPermissions` are reflected into database rows.
- Admin receives all permissions. Viewer is assigned globally to ordinary
  non-admin users, so adding a permission to Viewer affects a broad audience.
- Current seed behavior is not a reliable revocation mechanism for role grants.
  Removing a line from seed code may not remove a previously seeded database
  grant; plan explicit role/DB cleanup for revocation.
- Resource visibility can be global (`ScopeResourceId == null`) or scoped to a
  resource hierarchy. A global grant means all matching resources.
- Frontend navigation has both permission rules and, in some places, explicit
  admin-only presentation rules. Inspect both before diagnosing a missing menu.

## 6. Software Deployment Flow

The normal end-to-end path is:

1. An engineer creates a package/version and uploads an arbitrary file tree.
2. Server stores blobs by SHA-256, records `SoftwareFile` paths, and captures
   entry-point metadata. A version is released through existing authorization
   and validation gates.
3. A station assignment selects an active package/version. Multiple active
   packages per station are supported; the launcher chooses which local app is
   active when needed.
4. Backend creates/reconciles `InstallationJob` rows for computers at that
   station. Removing an assignment/package or moving a computer can enqueue
   uninstall work.
5. Agent polls `/agent/v1/poll`. It asks `/manifest/resolve` with hashes already
   present in the local content-addressed cache, so the server returns only
   missing downloads plus effective file metadata.
6. Agent acknowledges the job, downloads missing blobs in parallel, verifies
   and caches them, emits throttled byte progress plus keep-alives, then deploys
   under the configured install root (normally `D:\Apps`). Current write-path
   containment is an open risk documented below.
7. Agent completes the server job, updates its local catalog, and applies launch
   policy through `ProcessSupervisor`. Job execution is sequential even though
   blob downloads within a job can be parallel.
8. Agent inventory later compares deployed files to the effective manifest and
   reports drift. Server records installation state for web fleet/deployment UI.

Package metadata includes auto-start, close-on-update, and remove-on-unassign
flags. A pin/deploy target must be a released version. Forking/cloning a new
Draft is the intended edit path, but current code does not fully enforce either
`AutoRemoveOnUnassign` or released-version immutability; see the open risks
below before changing these flows.

### Config layering

- Current config customization is value-oriented: original file, then scoped
  station value, then computer value. Model-scoped keys can be locked depending
  on parameter targeting rules.
- Config render/reader/writer services support structured file formats and
  produce rendered blobs used in manifests.
- Config baselines are validation contracts, not deployment overrides.
- Override-file creation was retired, but read/deploy/migration compatibility
  remains for formats or structural changes not expressible as scalar values.
  Do not delete the retained path without a migration plan for existing data.
- JSON discovery includes nested scalar values and array elements; stored array
  indexes can become stale when arrays are reordered.

## 7. Agent Lifecycle

### Enrollment and authentication

- Agent entry commands include `install`, `uninstall`, `enroll`,
  `apply-update`, `scan-bootstrap`, `harden`, `maintenance`, and `run`.
- First contact can announce with an installer token. Explicit enrollment uses
  a per-computer enrollment token. Normal calls send `X-Agent-Token`.
- Durable agent identity lives in
  `C:\ProgramData\MProjectAgent\agent-state.json`. The server stores only a
  protected token representation. Deleting local state while the server still
  owns the enrollment can leave the machine unable to re-announce.
- Token rotation keeps one previous-token fallback until the new token is
  accepted.

### Worker loops

- Heartbeat defaults to 30 seconds and reports agent version, runtime/process
  state, health/specs, commands, maintenance, and OTA status.
- Poll defaults to 60 seconds and drains pending jobs until none remain.
- Inventory, test metrics, and test-log scan loops have independent schedules
  and can be disabled by configuration.
- A 401 triggers stored-token reload/fallback logic. It does not silently create
  a second enrollment for an already provisioned machine.

### Local storage and supervision

- Agent cache is content-addressed and indexed by SQLite. Recursive uninstall
  deletes have a base-directory guard, but current manifest deployment writes
  are not canonicalized or checked for containment under the install root.
- Local catalog records deployed apps and their entry points for the launcher.
- Entry points can include `.exe`, `.bat`, or shell-associated files. A separate
  watch-process path may identify the long-running process.
- `ProcessSupervisor` manages one active app at a time, persists supervised
  state across reboot, distinguishes operator stop from crash, and limits crash
  restarts. Operator stop must not cause automatic relaunch.
- A Windows Service runs in session 0 and cannot reliably inspect desktop
  windows in the interactive user session. Desktop/window-specific detection
  belongs in the launcher or another interactive process.

### OTA agent release

- Agent versions use four-part version numbers from `MProjectAgent.csproj`.
- Self-update is currently configuration-gated and disabled by default. A
  release pipeline existing in code does not mean every station has OTA enabled.
- Releases are signed; server validates publish-time signatures and agents
  verify downloads with the configured public key before staging update.
- Keep the private signing key local and outside Git/server artifacts. Do not
  regenerate the key pair during an ordinary release.
- Preserve each station's `appsettings*.json` during install/update.
- Roll out server support first, then a station baseline package, then activate
  or publish OTA. Rebuild artifacts and verify version/signature each release;
  do not trust a dated artifact note in memory.

## 8. Launcher and IPC

- Launcher is a single-instance tray application named "M-System TE". Closing
  the window hides it; the tray controls visibility/exit.
- Agent `LauncherBootstrapper` keeps the launcher running in a logged-on
  interactive session and removes stale session-0 launcher processes.
- IPC pipe: `MProjectAgent.Launcher`; one JSON request and response per line;
  operations are `status`, `run`, `stop`, and `restart`.
- Current pipe ACL grants local Builtin Users read/write access and LocalSystem
  full control. Any local user can therefore run/stop/restart a catalog app. The
  single server instance also has no request-size or per-connection read
  deadline, so a client can monopolize it. Treat IPC as a local-machine trust
  boundary and add an operator identity/session policy plus bounded I/O.
- Agent serializes with `System.Text.Json`; launcher serializes with
  Newtonsoft.Json. Keep contracts plain, dependency-free, camel-case-compatible
  POCOs and retain the cross-serializer wire tests.
- Status includes machine/server state, assigned location, local app catalog,
  active package, process state, deploy progress, icons, and recent events.
- Launcher targets .NET Framework 4.8 because factory PCs have it in-box. Build
  it with Visual Studio MSBuild or the packaging script, not `dotnet build`.
- The launcher and agent share the contracts assembly. Before an in-place copy,
  stop the service and launcher so the loose DLL is not locked; deploy all three
  binaries together to avoid `MissingMethodException` presented as "offline".

## 9. Testing, Build, and Operations

### Frontend

From `MProjectFrontend`:

```powershell
yarn install
yarn dev
yarn build
yarn lint
yarn test --run
yarn test:coverage
```

Node 20+ and Yarn 1.22.x are the documented baseline. Both `yarn.lock` and
`package-lock.json` currently exist, and neither cleanly matches the direct
dependencies in `package.json`. Existing `node_modules` also contains stale
packages, so a local build can hide clean-install failures. Dependency work
should first choose Yarn as the declared source, regenerate `yarn.lock`, remove
the npm lock only with explicit scope, and verify from a clean install.

### Backend and agent

From the Git root:

```powershell
dotnet run --project MProjectBackend/MProject.Api/MProject.Api.csproj --launch-profile http
dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore
dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore
dotnet build-server shutdown
```

The local backend HTTP profile is expected on port 5107. If a running backend
locks normal output, compile to a scratch output or use an appropriate compile
target; never kill user-owned IDE processes to release it.

### Packaging and deployment

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package-agent.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/e2e-wire-check.ps1
```

Use `scripts/prepare-deploy.ps1`, `install-server.ps1`, `update-server.ps1`, and
`install-agent.ps1` according to the current runbooks under
`../docs/Deploy guide/`. Supply secrets through approved parameters/environment
or secret stores, never documentation. Production uses a same-origin IIS site;
the browser should not need `VITE_API_URL` for normal use.

After tests/builds, run `dotnet build-server shutdown` and clean up only
processes started by the task. The machine has previously had a broken
Microsoft Store `pwsh` execution alias; use Windows PowerShell 5.1 when that
occurs. Deployment scripts must remain PowerShell 5.1-safe and ASCII-safe.

## 10. Known Traps

- **Npgsql timestamps:** Convert `DateTimeOffset` query parameters to offset
  zero. InMemory tests will not catch the provider runtime exception.
- **Provider translation:** `ILike`, raw SQL, window/grouping queries, and some
  timestamp behavior require a real PostgreSQL verification path when changed.
- **Versioned routes:** Do not use an action placeholder named `{version}` below
  `[Route("api/v{version:apiVersion}/...")]`; it can break controller mapping at
  startup. Use a domain-specific name such as `{agentVersion}` or query input.
- **Locked migrations:** Never use
  `dotnet ef migrations remove --no-build` with stale binaries. Confirm the
  intended migration, startup project, and assembly before any migration edit.
- **Wrong localhost:** `/agent/v1/*` pointed at Vite 5173 returns 404. A 401 from
  backend 5107 at least proves the route was reached and credentials are absent
  or invalid.
- **Global filters:** Soft-deleted assignments/jobs can be hidden from uninstall
  reconciliation unless the established `IgnoreQueryFilters` pattern is used.
- **Progress:** Long installs and large single-file downloads must refresh
  `LastProgressAt`; byte movement is not the only progress signal.
- **Ant Design Drawer/modal:** A modal opened from a Drawer may need the Drawer
  focus trap disabled. Do not work around it with global focus event hacks.
- **Ant Design v6 selectors:** Component root selectors differ from older
  versions. Check rendered DOM before adding CSS overrides.
- **Drawer titles:** Flex children that truncate need `min-width: 0`; long titles
  may need two-line breaking to avoid colliding with `extra` actions.
- **Config arrays:** Parameter identity that embeds an array index becomes stale
  when elements reorder; retain stale detection and avoid silent retargeting.
- **Agent config:** OTA/install must not replace station config, state, or keys.
- **Agent state protection:** the agent token is present in local
  `agent-state.json`; confidentiality depends on the ProgramData directory ACL.
- **Factory TLS trust (rollout pending):** Agent now uses OS certificate trust
  and scripts share the canonical `https://te:8443` hostname contract. Existing
  stations must migrate DNS/hosts, trust and config with strict probe before the
  hardened binary is rolled out; do not OTA it to legacy IP/bypass config.
- **Manifest path containment (lab validation pending):** backend and Agent now
  reject rooted paths, traversal, reserved names, collisions, ADS and existing
  reparse escapes. Fresh installs use immutable PackageId roots and legacy roots
  are bridged from catalog/cache. Physical legacy migration, junction race and
  Windows service-identity E2E remain open rollout work.
- **Shell-associated entry points (current open risk):** legacy parity requires
  entries such as `.bat`, `.jar`, and `.py`. `ProcessSupervisor` requests shell
  execution, but the production Windows-service path calls
  `CreateProcessAsUser` with the file itself and does not honor file association.
  Resolve each supported type to an explicit executable/argument contract and
  test it from a real service/session boundary.
- **Software approval boundary (current state):** approval is optional per
  package. The working tree exposes the nullable package policy, reconciles
  Viewer to a read-only grant set (including removal of `software.download`),
  and rejects direct activation unless the current package policy has an
  Approved request. Historical active assignments still require audit and
  manual disposition before this remediation is closed.
- **Frontend approval contract (current open risk):** package and Deployment
  Matrix mutations are gated by their granular backend permissions, and package
  policy is selectable. The approval drawer still omits target-specific change
  details, and approval lists page a locally sliced snapshot capped at 100.
  Return immutable target snapshots and use server pagination.
- **Role/auth lifecycle (current open risk):** role-permission mutation now
  enforces system-role, grantability and self-impact guards. Disabling a user,
  resetting a password, or logging out still only revokes
  refresh tokens but does not invalidate an already issued access JWT; approval
  resolution must also require an active user. Add an authentication-version
  claim and enforce system-role/self-impact invariants centrally.
- **Upload authorization and cleanup (current open risk):** TUS authorization
  accepts either software-manage or own-document before the upload purpose is
  known. The legacy completion branch can attach a file to a Draft software
  version, while direct blob completion creates no tracked lease/row for later
  garbage collection. Bind upload capability to actor, purpose, target, size,
  hash, and expiry.
- **Assignment/install cancellation (current open risk):** deactivation/removal
  marks active jobs cancelled in the server database but does not stop an
  in-flight Agent deploy or persist uninstall intent for a partial install. A
  terminal callback with a different status is currently treated as successful
  idempotency. Preserve cancellation across server, Agent, catalog, and
  inventory reconciliation.
- **Agent job durability (current open risk):** Agent polls non-pending job states
  but executes only Pending jobs, and completion is acknowledged by the server
  before the local catalog/launch commit. A crash or lost response can leave the
  two sides permanently inconsistent. Use a durable local job journal and an
  idempotent, resumable completion protocol.
- **Test log cursor (current open risk):** scanner pagination re-reads the oldest
  overlap batch. A backlog larger than the batch size can starve every newer
  file, and equal timestamps can be skipped. Use a stable `(mtime, path)` keyset
  cursor and verify eventual delivery over multiple scan cycles.
- **Release/flag semantics (current open risk):** released entry-point metadata
  can still be changed, file/config operations can race release, and
  `AutoRemoveOnUnassign=false` currently does not prevent uninstall. Treat UI
  labels and memory as intent until these invariants are enforced transactionally.
- **Migration rollback (current open risk):** API startup always runs EF
  migrations, including migrations that drop schema. Server update rollback
  restores binaries but not the database, and `-SkipSchema` does not suppress
  startup migration. Use one migration owner and an expand/contract or tested
  database restore strategy.
- **Station bootstrap secret (current open risk):** deployment currently bakes a
  shared InstallerToken into station configuration and does not remove it after
  enrollment. Move to per-machine, one-time enrollment material, remove it after
  use, and enforce restrictive installation/config ACLs.
- **OTA exactness (current open risk):** update and rollback copy files as an
  overlay, so files removed from a release can survive both directions. Agent,
  Launcher, and the loose contracts DLL are copied one at a time, so power loss
  can leave a mixed version that the next startup does not repair. Service
  `RUNNING` is accepted immediately as success. Apply an atomic/exact signed
  bundle while preserving only explicit station state, and require a stable
  readiness signal before commit.
- **Blob authorization/GC (current open risk):** local agent blob download checks
  only that a storage path exists, not that the authenticated station owns an
  active job/release for it. GC also has writer/delete races. Use short-lived
  resource-bound capabilities and a durable claim/tombstone deletion protocol.
- **Factory watchdog config (current open risk):** the base backend settings are
  tuned to 30 minutes inactivity / 180 minutes maximum attempt, but
  `install-server.ps1` currently generates 10 / 30. Because production
  artifacts omit environment settings, a fresh install can silently restore
  the shorter timeouts. Reconcile script and application defaults before the
  next clean install.
- **Incomplete endpoint:** `/agent/v1/reboot-required` currently returns 501;
  do not build a workflow that assumes the report is persisted.
- **Scanner scope:** barcode-scanner handling belongs only on the Test Assets
  workflow; mounting it application-wide would capture input on unrelated
  screens.
- **Offline typography:** global CSS loads Google Fonts from the internet.
  Factory LAN/offline clients may fall back to a different font and layout;
  verify screenshots in the actual network environment for typography changes.

## 11. Frontend Product and Design Context

Primary users are test-line operators, engineers/line leads, and admins in a
factory environment. The interface should feel trustworthy, tidy, and
professional.

- Make current state, ownership, version, and consequences legible before
  offering actions.
- Prefer calm operational density over marketing composition or decorative
  cards. The product is not a generic SaaS dashboard.
- Use the existing `#465fff` accent consistently; use status colors for meaning,
  not decoration.
- Reuse Ant Design, Tailwind, shared application components/tokens, and the
  existing English/Vietnamese i18n system.
- User-visible strings must be translated rather than hard-coded.
- Destructive or outward-facing actions require clear confirmation and should
  remain permission-gated and auditable.
- Target WCAG 2.1 AA, keyboard navigation, visible focus, reduced motion, and
  at least 44x44 px touch targets where shop-floor touch use is plausible.
- Avoid glass effects, purple-blue gradients, generic metric-card grids,
  decorative motion, and low-contrast gray-on-tint text.

Read `../PRODUCT.md` before visual implementation; it is local-only because the
root `.gitignore` currently ignores it.

## 12. Claude Memory Migration Notes

Claude Code's project memory is available at:

`C:\Users\Administrator\.claude\projects\C--Users-Administrator-Desktop-TESS-MProject\memory\`

`MEMORY.md` is an index into focused topic files. It remains useful for feature
history, prior debugging evidence, and locating old design documents. Before
using a topic:

1. Check its modified date and whether it describes an invariant or a momentary
   state.
2. Verify the claim against current code, migrations, tests, and scripts.
3. Do not carry forward commit status, test counts, artifact dates, local
   process state, or credentials.
4. Prefer the current verified code when memory and a runbook disagree.

Known superseded memory example: older deployment notes say the backend does
not auto-migrate. Current `AppDbSeeder` does run `Database.MigrateAsync()` under
an advisory lock.

## 13. Security Hygiene

- Local Claude settings, old deployment notes, and legacy specifications may
  contain plaintext tokens/passwords or old SFTP credentials.
- Never quote, migrate, log, or commit those values. Use placeholders in docs.
- Treat any still-active exposed value as a rotation candidate.
- Keep the agent release private key outside Git and server artifacts. Only the
  public verification key belongs in deployable configuration.
- Do not expose connection strings, JWT keys, refresh-token peppers, installer
  tokens, metrics keys, or agent tokens in durable memory.
