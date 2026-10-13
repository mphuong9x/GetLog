# MProject code-review plan

## Purpose and review rules

This plan is based on the checked-in repository as inspected on 2026-09-07. It is for a deep review by Astra, not a finding report and not a request to change production code. Review production reliability and recovery before security, consistency, observability, maintainability, performance, and design elegance.

Every reviewer must read `AGENTS.md` and the relevant parts of `docs/PROJECT_REVIEW_CHECKLIST.md` first. The checklist is historical evidence and a source of regression hypotheses; current source, contracts, and tests are authoritative. The owner has accepted a shared installer token for a managed provisioning batch. Do not report token reuse, lack of unique per-PC installer tokens, or a requirement for manual enrollment as defects. Continue to review unrelated authorization, token handling, and upload/blob capability boundaries.

Do not review generated output, `.git/`, `.vs/`, `bin/`, `obj/`, `node_modules/`, `dist/`, `coverage/`, `artifacts/`, `Old_Software/`, `Sample_Software/`, or `Cpp_Software/`, unless an active source path explicitly requires a generated artifact or fixture for contract verification. Do not change production code.

## Verified system map

| Runtime responsibility | Verified implementation and ownership |
| --- | --- |
| Server composition and web boundary | `MProjectBackend/MProject.Api/Program.cs` composes ASP.NET Core, Npgsql `AppDbContext`, JWT and Agent authentication, TUS, controller routes, SPA hosting, and all server hosted services. The React build is served from `MProject.Api/wwwroot` in deployment. |
| Business and persistence layers | API controllers call Application services. `MProject.Application` depends on Domain plus shared contracts; `MProject.Infrastructure` implements `AppDbContext` and Local/MinIO storage. PostgreSQL schema/migrations are owned by Infrastructure. |
| Production station service | `MProjectAgent/Program.cs` runs a .NET 8 Windows-service-capable worker. It persists Agent state, cache index, job journal, catalog/runtime state, support outbox, and performs polling, deployments, process supervision, telemetry, IPC, and self-update. |
| Operator desktop app | `MProjectLauncher` is a .NET Framework 4.8 WPF app. It communicates locally with the Agent over `MProjectAgent.Launcher`; it does not call the server API directly. |
| Browser console | `MProjectFrontend` is React/Vite. `src/api/services/axios-client.ts` turns API calls into `/api/v1` paths, attaches JWTs, serializes refresh, and the deployed SPA is same-origin by default. |
| Shared compatibility boundaries | `MProjectAgent.Ipc.Contracts` is dependency-free `netstandard2.0` Agent/Launcher wire contract. `MProject.TestResults` is the Agent/API parser contract. `MProject.ProductionSupport.Contracts` is the Agent/API production-support contract. |
| Storage and state | PostgreSQL owns jobs, station assignments, Agent/Computer state, release metadata, approvals, blobs/references, support records, and test results. Local or MinIO object storage owns content-addressed blobs. Station-side SQLite/files own cache and support-outbox state. |
| Deployment and updates | `scripts/prepare-deploy.ps1` builds schema, API, SPA, and Agent package. `scripts/install-server.ps1` installs IIS/PostgreSQL configuration; `scripts/update-server.ps1` backs up, preflights migrations, applies schema, deploys and rolls back. `scripts/package-agent.ps1`, `scripts/sign-agent-release.ps1`, and `scripts/install-agent.ps1` package, sign, install, and bootstrap stations. |

The server's critical background services are TUS temp cleanup, blob GC, installation-job watchdog, station rollback watchdog, computer liveness watchdog, domain-event dispatcher, and production-support metrics collection. The Agent's important concurrent services are `AgentWorker`, `ProductionSupportDeliveryWorker`, `IpcServer`, and `LauncherBootstrapper`.

## Documentation differences that affect review

- No project architecture JSON/map or `PROJECT_MEMORY.md` was present in the inspected working tree. `AGENTS.md` and `docs/PROJECT_REVIEW_CHECKLIST.md` provide the checked-in guidance. The checklist itself warns that some earlier documentation under ignored locations may be absent from another clone.
- `MProjectBackend/MProject.slnx` contains the backend projects and two shared projects; it does not represent the full shipping system. Agent, Launcher, and their tests are built and packaged by scripts.
- `MProjectFrontend/README.md` describes the generic Vite application, while the actual deployment places the SPA inside the API publish directory and serves it same-origin.
- The checklist records many remediated findings plus open items such as OTA signing-key rotation, Agent `ReloadConfig`, concurrent single-device login, live secret rotation, and domain-event delivery semantics. Review current behavior independently; do not reproduce a historical finding without current evidence.

## Execution order

Run `00` first to establish contracts, then execute `01` through `06` in order because they touch production station PCs. `07` and `08` can follow independently. Run `09` before any remediation decision that changes permissions or approval routes, and `10` before deployment. Run final synthesis only after every intended report is available.

| Order | Review unit | Purpose, boundary, and risk |
| --- | --- | --- |
| 00 | Architecture, composition, and contract ownership | Verify actual dependency directions, runtime registrations, persistence ownership, shared-contract compatibility, and documented/runtime drift. This is a map review, not a sweep of every controller. |
| 01 | Agent enrollment, authentication, and remote control plane | Protects the server-to-station trust boundary: announce/enroll, durable Agent tokens, rotation/revocation, heartbeat, command delivery, and station-scoped blob access. A fault can authorize the wrong PC or act on it. |
| 02 | Station deployment job lifecycle | Traces assignment/pin/deactivate through job generation, Agent polling, durable journal, install/uninstall, progress/completion, watchdogs, rollback, inventory, and station state. It is the main production execution state machine. |
| 03 | Software content, uploads, releases, and blob lifecycle | Reviews content integrity from browser upload reservation through TUS, object storage, version release, download URLs, reference ownership, and garbage collection. This is separate from job execution because it owns artifact state and storage correctness. |
| 04 | Configuration, overrides, baselines, and config approval | Reviews effective configuration calculation, approval activation, rendered configs, stale detection, redeploy signalling, and target-path safety. It changes deployed station behavior but has its own data and state transitions. |
| 05 | Agent release packaging and self-update | Follows signed release creation and activation through heartbeat offer, download, exact staging, bootstrap apply, readiness, retry, and rollback. A regression can disable a fleet. |
| 06 | Launcher IPC and supervised process control | Reviews the local privilege/session boundary, protocol compatibility, process construction, process lifecycle, and how operator actions affect deployed payloads. It is distinct from remote deployment and OTA. |
| 07 | Production-support durable workflow | Traces an operator report over IPC into the Agent SQLite outbox, delivery/recovery, authenticated server API, PostgreSQL records, and web recovery UI. The separate outbox and human workflow justify dedicated context. |
| 08 | Test-log ingestion and monitoring | Traces shared parser behavior, Agent scan/cursors, authenticated ingestion, data deduplication/querying, test-monitor API, and console polling. It is a measurement workflow whose defects can hide station failures. |
| 09 | User identity, RBAC, approvals, and browser contract | Reviews browser JWT/refresh behavior, backend security state and scope checks, role/ACL mutations, approval snapshot/handlers, and UI permission gates. It owns human authorization; unit 04 owns configuration-target effects. |
| 10 | Server build, install, migration, and recovery lifecycle | Reviews the artifact supply path, IIS configuration, credentials/TLS configuration, schema safety, database/code backups, rollback, and health checks. It owns deploy-time recovery rather than application workflows. |

## Unit specifications

### 00 — Architecture, composition, and contract ownership

- **Exact scope:** composition roots, manifests, `AppDbContext`, shared contracts, dependency references, and build/deployment entry scripts listed in `prompts/00-architecture.md`.
- **Exclusions:** business-rule internals and controller-by-controller authorization; those belong to later units.
- **Read first:** `AGENTS.md`, checklist overview and documentation section, `MProjectBackend/MProject.slnx`, all shipping `.csproj` files, `MProjectFrontend/package.json`.
- **Trace:** API `Program.cs` registrations to implementation, Agent `Program.cs` registrations, Launcher → IPC contract, frontend API client → API hosting, and scripts → produced artifacts.
- **Why separate:** every later report relies on accurate ownership and contract direction; broad source reading here would duplicate deep workflow reviews.
- **Operational risk/value:** catches missing registrations, drift, contract versioning, and unsafe startup ownership before deeper review; high leverage, medium direct station risk.
- **Overlap:** may identify a cross-unit concern but must route its detailed review to the owning unit.

### 01 — Agent enrollment, authentication, and remote control plane

- **Exact scope:** Agent/server authentication, announce/enroll, token storage and transitions, heartbeat, commands, liveness and Agent blob capabilities.
- **Exclusions:** assignment/job state machine (`02`), TUS content lifecycle (`03`), Agent release package mechanics (`05`).
- **Read first:** Agent options, server and Agent program composition, Agent auth handler, current checklist sections SEC-002, SEC-004, AGT-002, and shared-token exception in `AGENTS.md`.
- **Trace:** `AgentController` → `AgentService`; `ServerClient`/`AgentWorker` → announce/enroll/heartbeat/poll; `TokenStore`/`AgentTokenTransitionManager`; command claim/ack; agent-blob capability validation and authorizer.
- **Why separate:** it establishes identity and authorization for every remote station action.
- **Operational risk/value:** unauthorized PC control, stale credentials, wrong-agent data/command access, dead-agent masking; critical production-station value.
- **Overlap:** provide only the authentication facts needed by `02`, `05`, `07`, and `08`; do not review their workflow logic.

### 02 — Station deployment job lifecycle

- **Exact scope:** station assignment and pin decisions, installation jobs, manifests, Agent installation/uninstallation, station process/start state, inventory, watchdogs, and rollback.
- **Exclusions:** artifact upload/release integrity (`03`), configuration rendering (`04`), Agent binary OTA (`05`), and local operator IPC authorization (`06`).
- **Read first:** job/assignment models and tests, Agent models, checklist JOB-001/JOB-002/REL-002/AGT-001/SEC-001.
- **Trace:** `SoftwareAssignmentsController` → `StationSoftwareAssignmentService` → `InstallationJobService` → `AgentController` → `AgentWorker` → `JobExecutor` → storage/journal/cache/install directory/process supervisor → ACK/progress/complete → records, watchdogs, and rollback.
- **Why separate:** it is a cross-system state machine with crashes, duplicate polls, cancellations, and physical station impact.
- **Operational risk/value:** wrong version or uninstall, stuck updating state, concurrent filesystem/process actions, silent failures, bad rollback; highest review value.
- **Overlap:** manifest blob authorization belongs to `01`; blob byte lifecycle belongs to `03`.

### 03 — Software content, uploads, releases, and blob lifecycle

- **Exact scope:** frontend upload client, TUS endpoint/reservations, software files and versions, storage providers, blob references, archive/download paths, and GC.
- **Exclusions:** job consumption after a valid manifest (`02`), config/override files (`04`), Agent release OTA archives (`05`), user-document feature except TUS capability isolation required to validate shared endpoint.
- **Read first:** checklist UPL-001/UPL-002/REL-001/STO-001/STO-002 and storage configuration.
- **Trace:** `software.ts` upload calls → `SoftwareFilesController` → `SoftwareFileService.InitializeUploadAsync` → `TusUploadHandler` → `UploadReservationService` → storage/blob row → `CompleteUploadAsync`/`RegisterUploadedFileAsync` → `SoftwareVersionService.ReleaseVersionAsync` → manifest/download; then `BlobGcService` deletion/tombstone path.
- **Why separate:** source bytes and their database/object-store lifecycle have different failure/recovery behavior from deployment jobs.
- **Operational risk/value:** content substitution, corrupt or orphaned payloads, immutable release violation, quota abuse, deleted live bytes; critical input integrity value.
- **Overlap:** Agent capability endpoint is verified in `01`; this unit only verifies that artifact state supports safe use of it.

### 04 — Configuration, overrides, baselines, and config approval

- **Exact scope:** config parameter/baseline/override APIs and services, effective file rendering, approval handlers for configuration, manifest integration, config drift and redeploy marking, and relevant UI components.
- **Exclusions:** generic approval engine and user-role authority (`09`); ordinary content release and TUS (`03`); installation execution after a resolved config is sent (`02`).
- **Read first:** checklist CFG-001/CFG-002, `ConfigValueLayering`, `ConfigTargetPaths`, and application config models.
- **Trace:** config page/API → `ConfigValueOverrideService.BulkSetAsync`/`SetAsync`/`ReconfirmAsync` → `ApprovalService`/`ConfigValueOverrideApprovalHandler` → `ConfigValueRedeploy`; `InstallationJobService.ResolveOverridesAsync` → `ConfigRenderService.RenderAsync` → rendered config blob → Agent manifest/install target.
- **Why separate:** configuration has layered values, approval snapshots, stale semantics, and a redeploy side effect that cannot be assessed from a generic approval review.
- **Operational risk/value:** wrong station configuration, approval stuck or bypassed, unsafe target path, stale values silently applied; high station correctness value.
- **Overlap:** report platform-level approval defects only in `09`; state the concrete config impact here.

### 05 — Agent release packaging and self-update

- **Exact scope:** Agent release publish/activate/download, signature verification, packaging/signing scripts, update offer in heartbeat, staging, bootstrap apply/recovery, and Launcher bundle alignment.
- **Exclusions:** server-wide IIS/database deploy (`10`), payload software deployments (`02`), generic Agent credentials (`01`) except the heartbeat offer authentication dependency.
- **Read first:** checklist OTA-001/OTA-002 and `MProject.StationBundleVersion.props`.
- **Trace:** `package-agent.ps1`/`sign-agent-release.ps1` → `AgentReleasesController.Publish` → `AgentReleaseService` → Agent heartbeat `ResolveAgentUpdateAsync` → `AgentUpdater.MaybeStageUpdateAsync`/`StageFromFileAsync` → `ApplyUpdateCommand`/bootstrap → next heartbeat/readiness and result telemetry.
- **Why separate:** it changes the service executable and its recovery protocol across an entire fleet.
- **Operational risk/value:** fleet outage, unverified binary, failed rollback, incompatible Agent/Launcher contract; critical production value.
- **Overlap:** deployment scripts only for Agent artifacts belong here; IIS/server rollback belongs to `10`.

### 06 — Launcher IPC and supervised process control

- **Exact scope:** IPC contract, named-pipe server/client/authentication, WPF request behavior, Agent request handler, catalog/runtime status, command building, interactive launch, and process supervisor.
- **Exclusions:** remote command delivery (`01`), deployment content and files (`02`), support outbox business logic (`07`) except IPC request framing/capability checks.
- **Read first:** checklist IPC-001 and AGT-001, IPC contract, Agent/Launcher project manifests.
- **Trace:** WPF `MainWindow` → `IpcClient` → `IpcServer` → `IpcPeerAuthorizer` → `IpcRequestHandler` → `ProcessSupervisor`/`LaunchPolicy`/`EntryPointCommandBuilder`/`InteractiveProcessLauncher`; trace version/capability mismatch and response bounds both directions.
- **Why separate:** a local user/session boundary controls production payload processes under a service account.
- **Operational risk/value:** unauthorized local control, deadlocked UI/pipe, orphaned or wrongly launched station process, incompatible bundle; high station safety value.
- **Overlap:** production-support operation semantics are `07`; this unit owns only its IPC transport surface.

### 07 — Production-support durable workflow

- **Exact scope:** production-support contracts, Agent preview/report/update, SQLite outbox and delivery state machine, Agent API, server records/recovery/query, metrics, and Support Requests UI.
- **Exclusions:** generic IPC security/transport (`06`), generic RBAC implementation (`09`) except permission outcomes required by this workflow, raw test parsing (`08`).
- **Read first:** `MProject.ProductionSupport.Contracts`, Agent support options, relevant compatibility fixtures, production-support config and checklist.
- **Trace:** Launcher support request → IPC handler → `ProductionSupportAgentService` → `SupportOutboxStore` → `ProductionSupportDeliveryService`/API client → `AgentProductionSupportController` → `ProductionSupportService` → PostgreSQL → `ProductionSupportRecoveryService`/query → browser hooks/page.
- **Why separate:** a durable offline queue with human triage has its own idempotency graph, retry policies, external contract, and recovery decisions.
- **Operational risk/value:** lost or duplicated repair requests, wrong station context, unrecoverable queue, unauthorized triage, misleading support state; high operational value.
- **Overlap:** reference `06` for a pipe security defect and `09` for a generic authorization defect instead of duplicating it.

### 08 — Test-log ingestion and monitoring

- **Exact scope:** test-result shared parser, Agent scanners/reporters/cursors, Agent ingestion API/service, test result persistence/querying, test-monitor controller, frontend API/page.
- **Exclusions:** production-support log selection/reporting (`07`), generic Agent heartbeat auth (`01`) except identifying the authenticated boundary.
- **Read first:** checklist LOG-001, `MProject.TestResults` corpus fixtures, test-log options, and test-monitor configuration.
- **Trace:** test log file → `TestResultScanner`/`TestMetricsReporter` → `AgentWorker`/`ServerClient` → `AgentController.ReportTestResults`/`ReportTestMetrics` → `AgentService` → `TestResultRecord` → `TestResultQueryService` → `TestResultsController` → `testResults.ts`/`TestMonitor.tsx`.
- **Why separate:** scanner cursor/replay behavior and operational metric accuracy are distinct from support workflows and affect production decision-making.
- **Operational risk/value:** missed, duplicated, stale, misparsed, or incorrectly attributed failures hide factory problems; high observability and correctness value.
- **Overlap:** test records used to enrich support requests are context-only in `07`.

### 09 — User identity, RBAC, approvals, and browser contract

- **Exact scope:** JWT/refresh/session security state, user/role/ACL/scope resolution, permission filters, approval engine/snapshots/target handlers, and frontend auth, access rules, approval pages, and sensitive mutation views.
- **Exclusions:** specific configuration target state (`04`), station job behavior (`02`), Agent authentication (`01`), production-support outbox delivery (`07`).
- **Read first:** checklist SEC-003, AUTH-001 through AUTH-004, FE-001 through FE-005, and API/Frontend auth boundary files.
- **Trace:** sign-in/refresh/401 queue → `AuthController` → `AuthService`/`RefreshTokenService`/access token validator; `RequirePermission` → `PermissionAuthorizationFilter` → permission/scope resolvers; target mutations → `ApprovalService` → target handlers → UI inbox/detail/request pages and browser gates.
- **Why separate:** this is the human authorization boundary that determines who can publish, assign, approve, recover, or inspect system state.
- **Operational risk/value:** privilege escalation, revoked user access, invalid approval activation, UI/API mismatch, incorrect scope data; critical security and production-governance value.
- **Overlap:** review target-specific approval consequences in `04`; report common engine/filter defects here.

### 10 — Server build, install, migration, and recovery lifecycle

- **Exact scope:** API startup guard and configuration, artifact builds, schema generation, IIS install/update paths, migration safety, backup/restore, TLS/certificate paths, health checks, CI build definitions, and operational scripts.
- **Exclusions:** business workflows implemented in services, front-end feature behavior, Agent self-update (`05`) except how an Agent package is placed in a server artifact.
- **Read first:** checklist DEP-001, OPS-001, SEC-002, SEC-005, the deployment scripts' comments, and API configuration/startup code.
- **Trace:** `prepare-deploy.ps1` → schema/API/SPA/Agent output → `install-server.ps1` → `Program.cs` startup/seeding/hosted services; `update-server.ps1` → preflight → code/DB backups → migration → deployment → health → automatic/manual rollback; validate the companion drills.
- **Why separate:** deploy-time data/code mismatch and failed recovery have a blast radius beyond a single request or station.
- **Operational risk/value:** destructive migration without recovery, secret/TLS misconfiguration, deployment drift, false health success, concurrent host ownership; critical recoverability value.
- **Overlap:** actual release ZIP self-update behavior is `05`; actual artifact blob/reference behavior is `03`.

## Quality gate performed before authoring prompts

| Check | Result |
| --- | --- |
| Based on real behavior rather than folders | Pass. Units are stateful workflows and trust/failure boundaries. |
| Review scopes support deep inspection | Pass. Content, configuration, OTA, IPC, support, and telemetry are separated from deployment jobs. |
| No unnecessary fragmentation | Pass. Each smaller shared project is reviewed through its owning cross-system workflow. |
| Duplicate responsibilities minimized | Pass. The deliberate overlaps above state which unit owns the detailed finding. |
| Critical workflows covered | Pass. Enrollment, install/uninstall, uploads, config distribution, OTA, IPC, support, telemetry, approval, and server recovery are explicit. |
| Production-station risk prioritized | Pass. Units 01–06 precede operational and browser-focused units. |
| Prompts are repository-specific and independent | Pass. Each names real paths, services, controllers, contracts, and traces. |
| Generic checklist noise avoided | Pass. Objectives focus on observed state transitions, persistence, contracts, and recovery behavior. |
| Execution order efficient | Pass. Architecture is established once, station-risk workflows follow, then independent operational workflows and deployment. |
