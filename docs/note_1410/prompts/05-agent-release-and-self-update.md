# Astra review: Agent release packaging and self-update

Review the complete signed Agent-binary lifecycle from build artifact to a restarted Windows service. Do not modify production code.

## Scope

- Version/package scripts: `MProject.StationBundleVersion.props`, `scripts/{package-agent,sign-agent-release,prepare-deploy,install-agent}.ps1`, `MProjectAgent/scripts/*`, Agent/Launcher/IPC project files, and release bundle manifest models.
- Server: `Controllers/Assets/AgentReleasesController.cs`, `Services/Assets/AgentReleaseService.cs`, Agent heartbeat/update models and `AgentService.ResolveAgentUpdateAsync`.
- Agent: `Services/{AgentUpdater,ReleaseSignatureVerifier,LauncherBootstrapper,ServerClient}.cs`, `Commands/{ApplyUpdateCommand,BootstrapCommand,ScanBootstrapCommand,ServiceInstaller}.cs`, options/state paths, and update result/readiness models.
- Launcher/IPC artifacts only where package exactness and version compatibility depend on them.
- Tests/scripts: Agent release/updater/apply-update/installer/station-bundle tests, API release service tests, OTA bootstrap drill, package/sign smoke scripts, and e2e wire check.

## Exclusions

Do not review normal payload installation (`02`), server-wide IIS/database rollback (`10`), or generic Agent enrollment (`01`) other than the authenticated heartbeat that offers an update. Do not ask for a new signing-key rotation design unless current code demonstrates a concrete defect; report the existing documented OTA-002 gap with evidence if it remains.

## Context to read first

Read `AGENTS.md`, checklist OTA-001 and OTA-002, Agent and server release configuration, and current station-bundle version. Establish which files form a shipping bundle before judging archive validation.

## Review objectives

- Validate that package contents are clean, exact, version-aligned, and cannot silently incorporate stale publish output, unexpected files, or incompatible Launcher/IPC dependencies.
- Validate server publish/activate/deactivate/delete semantics, archive validation, blob ownership, signature verification, force rollback offer, and adoption/status reporting.
- Validate Agent offer selection, authenticated download, hash/signature verification, archive manifest strictness, staged-directory atomicity, retry/jitter state, bootstrap selection, exact install replacement, service/Launcher stop/start, and heartbeat readiness rollback.
- Analyze power loss and process crashes between every meaningful stage, network interruption, repeated offers, downgrade/force updates, partial copies, locked files, missing bootstrap, and stale readiness/result files.
- Preserve safe current controls; do not recommend a fleet-wide redesign without an evidenced failure mode.

## Required tracing

1. `package-agent.ps1` and `sign-agent-release.ps1` → generated zip/manifest/signature → `AgentReleasesController.Publish` → `AgentReleaseService.PublishAsync`/storage/blob reference.
2. Activate/force/deactivate paths → `AgentReleaseService.ActivateAsync` → `AgentService.ResolveAgentUpdateAsync` → heartbeat `AgentUpdateInfo`.
3. `AgentWorker.HeartbeatTickAsync` → `AgentUpdater.MaybeStageUpdateAsync` → download → `StageFromFileAsync` → `ReleaseSignatureVerifier` and `ValidateBundleManifestAsync` → `staged.txt` and retry state.
4. `LaunchApply` → `ApplyUpdateCommand.RunAsync` → backup/replace/exact snapshot → Windows service state and `LauncherBootstrapper` → post-update heartbeat readiness/result consumption.
5. Bundle version projection across `MProjectAgent`, `MProjectLauncher`, and `MProjectAgent.Ipc.Contracts`; trace compatibility/capability assumptions tested by `e2e-wire-check.ps1`.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
