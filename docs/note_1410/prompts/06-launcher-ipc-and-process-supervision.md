# Astra review: Launcher IPC and supervised process control

Review the local operator-to-Agent control boundary and the process lifecycle it triggers on a production station. Do not modify production code.

## Scope

- Contract: all `MProjectAgent.Ipc.Contracts` source and fixtures.
- Launcher: `MProjectLauncher/{App.xaml.cs,MainWindow.xaml.cs,IpcClient.cs,LauncherLayoutPolicy.cs,MachineNameQrCode.cs}` and relevant XAML/project references.
- Agent IPC/process code: `Services/{IpcServer,IpcPeerAuthorizer,IpcRequestHandler,AgentStatusProvider,ProcessSupervisor,LaunchPolicy,EntryPointCommandBuilder,InteractiveProcessLauncher,RecentEventsBuffer,AppCatalogStore-related call sites}.cs` plus process/status models and runtime/catalog storage.
- Supporting package/runtime code: `LauncherBootstrapper`, `ServiceInstaller`, station bundle scripts only where they set executable locations or transport contracts.
- Tests: IPC server/client/peer-authorizer/protocol/wire/request-handler tests; launcher layout tests; launch policy/entry point/process-supervisor recovery tests; e2e wire check.

## Exclusions

Do not deep-review remote server command delivery (`01`), deployment files/jobs (`02`), Agent binary OTA (`05`), or production-support business/outbox semantics (`07`). For support actions, examine only IPC framing, capability negotiation, timeout, peer authorization, and returned error/response safety.

## Context to read first

Read `AGENTS.md`, checklist IPC-001 and AGT-001, `IpcContracts.cs`, Agent and Launcher project manifests, and runtime composition. The Agent runs as a Windows service; local interactive-session correctness matters more than architectural elegance.

## Review objectives

- Verify named-pipe ACLs and peer/session authorization match the explicitly intended active-interactive-console plus LocalSystem policy.
- Verify request/response framing, UTF-8 validation, version/capability negotiation, frame bounds, connection limits, read/write/operation deadlines, cancellation, and Launcher single-flight behavior.
- Verify user-visible status is safe and accurate under pipe failure, partial response, multiple Launcher instances, older/newer bundle versions, and service restart.
- Verify `run`, `stop`, and `restart` select the intended active package and do not create orphan, duplicate, wrong-session, or untracked processes.
- Verify legacy entry-point construction and interactive launch semantics work under service identity/session 0 without unsafe shell interpretation.

## Required tracing

1. WPF refresh/action path in `MainWindow` → `IpcClient.SendAsync` → named pipe framing/deserialization → UI error/status state.
2. `IpcServer.CreatePipe`/`HandleConnectionAsync`/`DispatchAsync` → `IpcPeerAuthorizer.IsAuthorized` → v1/v2 contract checks → `IpcRequestHandler.HandleAsync` → bounded response.
3. `IpcRequestHandler.RunAsync`/`StopAsync`/`RestartAsync` → `AppCatalogStore`/`AgentStatusProvider` → `ProcessSupervisor` state and runtime persistence.
4. `ProcessSupervisor` → `LaunchPolicy` → `EntryPointCommandBuilder` → `InteractiveProcessLauncher`, including `.bat`, `.cmd`, `.jar`, and `.py` entries plus watch process/health-check behavior.
5. `LauncherBootstrapper` / station bundle layout → Launcher startup/restart and IPC contract version compatibility.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
