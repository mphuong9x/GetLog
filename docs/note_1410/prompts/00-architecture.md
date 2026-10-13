# Astra review: architecture, composition, and contract ownership

Review the real MProject runtime boundaries and dependency directions. This is a focused architecture-conformance review, not a request to review every controller or to redesign the system. Do not modify production code.

## Scope

- `AGENTS.md`, `docs/PROJECT_REVIEW_CHECKLIST.md`, `MProject.StationBundleVersion.props`, and relevant root/build scripts.
- `MProjectBackend/MProject.slnx`; all shipping `.csproj` files under `MProjectBackend`, `MProjectAgent`, `MProjectLauncher`, `MProjectAgent.Ipc.Contracts`, `MProject.TestResults`, and `MProject.ProductionSupport.Contracts`.
- `MProjectBackend/MProject.Api/Program.cs`, `MProjectBackend/MProject.Infrastructure/DBContext.cs`, `MProjectAgent/Program.cs`, `MProjectLauncher/App.xaml.cs`, `MProjectLauncher/MainWindow.xaml.cs`, and `MProjectFrontend/src/main.tsx`, `App.tsx`, and `api/services/axios-client.ts`.
- Shared contracts: `MProjectAgent.Ipc.Contracts/IpcContracts.cs`, all source/fixtures in `MProject.TestResults` and `MProject.ProductionSupport.Contracts`.
- `build-local.ps1`, `scripts/prepare-deploy.ps1`, `scripts/package-agent.ps1`, `scripts/install-agent.ps1`, `scripts/install-server.ps1`, and `scripts/update-server.ps1`.

## Exclusions

Do not deep-review business rules inside individual Application services, controllers, React pages, or Agent worker subsystems; record a concrete handoff and defer detailed findings to prompts 01–10. Exclude generated directories and historical/sample payloads described in the review plan.

## Context to read first

Read the repository guidance and the review-checklist overview/documentation section first. Current source and tests are authoritative; the checklist records history and regression hypotheses. The owner-approved shared install-token provisioning workflow must remain accepted and must not be reported as a token-uniqueness or mandatory-manual-enrollment defect.

## Review objectives

- Verify the actual runtime components, entry points, data stores, hosted workers, and package/install/update paths.
- Verify project-reference direction and prevent accidental dependency inversion between Domain, Application, Infrastructure, API, Agent, Launcher, frontend, and shared contracts.
- Verify which component owns each cross-system protocol and version: browser API, Agent API, IPC, test results, production support, and station bundle version.
- Detect documented behavior that is materially inconsistent with current source, deployment scripts, or runtime registration.
- Focus on composition failures that can silently disable watchdogs, point a component at the wrong implementation, or deploy an incompatible artifact.

## Required tracing

1. Trace every `AddHostedService` and major DI registration in API `Program.cs` to the concrete service and its persistence/storage dependency.
2. Trace Agent `RunAgentAsync` registrations to `AgentWorker`, `ProductionSupportDeliveryWorker`, `IpcServer`, and `LauncherBootstrapper`.
3. Trace `MProjectLauncher` to `MProjectAgent.Ipc.Contracts`, then Agent IPC server; trace React API URL conversion to API versioning and SPA hosting in `Program.cs`.
4. Trace each shared-contract project to all consumers and identify version/target-framework compatibility assumptions.
5. Trace build/deployment scripts from source input to the server artifact and station bundle. Confirm the backend solution file does not stand in for the entire shipping system.

## Findings format

For every finding provide:

- Severity: Critical / High / Medium / Low
- Confidence: High / Medium / Low
- Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement
- File
- Symbol
- Evidence
- Finding
- Realistic failure scenario
- Operational impact
- Root cause
- Recommended direction
- Required regression test
- Estimated change scope: Small / Medium / Large

Also report design decisions that are good and should remain unchanged, suspected issues investigated but rejected, and areas where evidence is insufficient. Do not modify production code.
