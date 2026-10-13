# Astra review: configuration, overrides, baselines, and config approval

Review configuration state that changes what a deployed station executes. Do not modify production code.

## Scope

- API: `Controllers/Software/{ConfigParametersController,ConfigValueOverridesController,ConfigBaselinesController,OverrideFilesController}.cs`.
- Application: `Services/Software/{ConfigParameterService,ConfigBaselineService,ConfigBaselineValidator,ConfigBaselinePermissionService,ConfigValueOverrideService,ConfigValueLayering,ConfigValueValidator,ConfigValueRedeploy,ConfigRenderService,ConfigFileReader,ConfigFileWriter,ConfigTargetPaths,OverrideFileService,OverrideFilePermissionService,OverrideResolver,OverrideFileMigrationService}.cs` and configuration models/entities/enums.
- Approval target behavior: `Services/Approvals/{ConfigValueOverrideApprovalHandler,OverrideFileApprovalHandler}.cs`; read `ApprovalService` only where it invokes these handlers.
- Manifest/station integration: `InstallationJobService.ResolveOverridesAsync` and resolved-file models; Agent manifest consumption only to check target/path/ordering handoff.
- Frontend: Config Baselines, Override Files, Computer drift, config modals, relevant API/types/hooks/tests.
- Tests: config service/validator/render/file reader-writer/parameter-target/approval/override migration tests and related frontend tests.

## Exclusions

Do not re-review generic approval authorization/snapshots (`09`), ordinary artifact upload/blob ownership (`03`), or deployment-state execution after the Agent receives a resolved configuration (`02`). Agent `ReloadConfig` command behavior is in scope only to determine whether the server promises an effect that it cannot provide; do not duplicate remote-command security review from `01`.

## Context to read first

Read `AGENTS.md`, checklist CFG-001 and CFG-002, `ConfigValueLayering`, `ConfigTargetPaths`, current configuration models, and the package/version manifest code. Current source decides behavior.

## Review objectives

- Validate configuration ownership and scope resolution from model/station/computer values through the effective runtime file.
- Validate baseline validation, value typing, file parsing/writing, path normalization, and file overlap/migration rules.
- Validate draft/pending/active/stale/pending-revision transitions, approval results, edits/deletes during approval, and stale-impact acknowledgement on pinning.
- Validate rendered config blobs, caching, redeploy/drift flagging, manifest integration, and failure/retry behavior when render or persistence fails.
- Analyze duplicate bulk/update submissions, concurrent approvals, stale views, assignment/pin changes during approval, partial rerendering, and an Agent that acknowledges a config reload without applying it.

## Required tracing

1. `ConfigValuesModal` / configuration API → `ConfigValueOverrideService.BulkSetAsync` and `SetAsync` → target/scope resolution → `ConfigValueLayering.Fold` → validation and persistence.
2. Approval-required configuration → `SubmitApprovalAsync` / `ApprovalService` → `ConfigValueOverrideApprovalHandler.OnSubmittedAsync` / `OnApprovedAsync` / `OnRejectedAsync` → `ConfigValueRedeploy.FlagForOverrideAsync`.
3. Version pin/assignment → `StationSoftwareAssignmentService.EnforceConfigBaselineAsync` and stale impact preview → baseline validators and effective config sources.
4. `InstallationJobService.ResolveOverridesAsync` → `OverrideResolver` / `ConfigRenderService.RenderAsync` → rendered blob/cache → Agent manifest file target; trace a cache miss, stale source, and render failure.
5. Override file create/edit/migrate/delete → `OverrideFileService` / `OverrideFileMigrationService` → approval handler → resolved override precedence.
6. Agent `ReloadConfig` command path → `AgentCommandHandler`, with the server/UI contract that causes it to be sent, if any.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
