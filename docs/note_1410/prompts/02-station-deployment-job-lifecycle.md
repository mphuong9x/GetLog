# Astra review: station deployment job lifecycle

Review the end-to-end workflow that decides what a station should run and applies that decision on a Windows production PC. Do not modify production code.

## Scope

- Server controllers/models: `SoftwareAssignmentsController.cs`, `PcInstallationsController.cs`, `AgentController.cs`, installation/assignment models and Domain entities/enums.
- Application: `Services/Software/{StationSoftwareAssignmentService,InstallationJobService,InstallationJobWatchdogService,StationRollbackWatchdogService,PcInstallationService,PcInventoryService}.cs`, `Services/Assets/AgentCommandService.cs`, and relevant configuration/override resolver call sites needed to build a manifest.
- Agent: `Services/{AgentWorker,JobExecutor,JobCancellationRegistry,DeployProgressRegistry,InstallDirectoryService,InstallDirGuard,InstallFileSystem,InstallDirScanner,BlobCacheService,ProcessSupervisor,LaunchPolicy,EntryPointCommandBuilder,InteractiveProcessLauncher,InventoryReporter}.cs`, `Storage/{JobJournalStore,CacheIndex,AppCatalogStore,RuntimeStateStore}.cs`, and Agent models.
- Relevant station UI/tests: installation jobs and deployment-matrix pages/hooks; `InstallationJobServiceTests`, `InstallationJobConcurrencyTests`, watchdog/rollback tests, `JobCancellationBarrierE2ETests`, `JobJournalRecoveryTests`, install-root tests, uninstall tests, supervisor-recovery tests, and inventory tests.

## Exclusions

Do not review raw TUS/blob lifecycle or release publication (`03`), configuration semantics beyond the resolved manifest dependency (`04`), Agent binary OTA (`05`), or IPC authorization (`06`). The Agent control-plane authentication mechanism is `01`.

## Context to read first

Read `AGENTS.md`, checklist SEC-001, JOB-001, JOB-002, REL-002, and AGT-001, then job/assignment entities and the two `Program.cs` files. Treat station integrity, crash recovery, and correct rollback as more important than refactoring style.

## Review objectives

- Validate assignment/pin/activate/deactivate/remove transitions and the intended `AutoRemoveOnUnassign` behavior.
- Validate job ownership, execution ID, state transitions, cancellation barriers, duplicate request handling, and reconciliations across PostgreSQL and Agent journal files.
- Validate manifest resolution, parallel download/deploy, guarded package roots, cache/catalog reference accounting, uninstall behavior, and legacy root migration.
- Validate process stop/restart/start ordering, watchdog fault handling, inventory feedback, station operational state, and automatic rollback decisions.
- Analyze interrupted network, Agent crash/restart, server restart, concurrent assignment changes, stale poll results, duplicate ACK/progress/complete, long-running valid install, and partial filesystem failure.

## Required tracing

1. Browser `AssignPackageModal`/`DeploymentMatrix` → `SoftwareAssignmentsController` → `StationSoftwareAssignmentService.AssignAsync`/`ActivateAsync`/`PinVersionAsync`/`DeactivateAsync`/`RemoveAssignmentAsync`.
2. Assignment target/version change → `InstallationJobService` enqueue gates and records → Agent `AgentWorker.PollTickAsync` → `AgentController.Poll`/`ResolveManifest` → `InstallationJobService.PollAsync`/`ResolveManifestAsync`.
3. `JobExecutor.ExecuteAsync` / journal `GetOrCreateAsync` → ACK → `ExecuteInstallJobAsync` or `ExecuteUninstallJobAsync` → cache download/deploy/catalog/process supervision → progress/completion; trace restart at each durable transition.
4. Cancellation from assignment changes → `CancelActiveJobsForPackageAsync` → Agent `CancelJob` → `JobCancellationRegistry` and executor process/filesystem boundaries.
5. Agent complete/progress → `InstallationJobService.ProgressAsync`/`CompleteAsync` → `PcInstallationRecord`, Computer operational status, UI job state, `InstallationJobWatchdogService`, and `StationRollbackWatchdogService` → `RollbackToPreviousVersionAsync`.
6. `InventoryReporter` → Agent inventory API → `PcInventoryService.ProcessAsync`, including drift reconciliation and a stale local catalog.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
