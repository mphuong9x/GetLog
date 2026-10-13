# Astra review: Agent enrollment, authentication, and remote control plane

Review the trust boundary between the MProject server and Windows station Agents. Do not modify production code.

## Scope

- Server: `MProjectBackend/MProject.Api/Authentication/AgentAuthentication*`, `Controllers/Assets/AgentController.cs`, `RateLimiting/AgentProvisioningRateLimiter.cs`, and `MProjectBackend/MProject.Application/Services/Assets/{AgentService,AgentCommandService,AgentBlobCapabilityService,AgentBlobDownloadAuthorizer,ComputerLivenessWatchdogService}.cs` plus their interfaces/models/entities.
- Agent: `MProjectAgent/Configuration/AgentOptions.cs`, `Program.cs`, `Services/{AgentWorker,ServerClient,AgentTokenTransitionManager,ServerReachabilitySignal,AgentCommandHandler,InventoryReporter,AgentStatusProvider}.cs`, `Storage/{TokenStore,InstallerTokenScrubber,RuntimeStateStore}.cs`, and `Commands/{EnrollCommand,BootstrapCommand,ServiceInstaller}.cs`.
- Contracts/tests: Agent request/response models, relevant Agent/API tests (`AgentServiceTests`, `AgentCommandServiceTests`, `AgentBlobAuthorizationTests`, `AgentTokenRotationRaceTests`, `EnrollCommandOptionsTests`, `InstallerTokenScrubberTests`, `AgentTlsTrustTests`) and `scripts/install-agent.ps1` only for provisioning/TLS/token handoff.

## Exclusions

Do not review installation-job execution or Agent filesystem deployment (`02`), TUS/software content ownership (`03`), Agent binary self-update (`05`), or generic user JWT/RBAC (`09`). Do not report the owner-accepted shared installer token for a managed provisioning batch as a defect.

## Context to read first

Read `AGENTS.md`, checklist SEC-002, SEC-004, AGT-002, current Agent/API appsettings, and composition in both `Program.cs` files. Treat historical remediation as evidence to verify, not as proof.

## Review objectives

- Confirm announce and enroll bind identities to the intended computer and are resistant to replay, concurrent calls, stale state, and rate-limit bypass.
- Confirm Agent credentials are stored, rotated, revoked, recovered after network races, and attached to requests without resurrecting an invalid token.
- Confirm TLS/server-origin validation prevents credential transmission to an untrusted host.
- Confirm heartbeat/poll/command delivery and acknowledgement are authenticated, agent-owned, bounded, idempotent where required, and observable under retries.
- Confirm a station can download only a blob currently authorized for its Agent/job context.
- Identify only real access-control or station-liveness defects; preserve the accepted provisioning workflow.

## Required tracing

1. `scripts/install-agent.ps1` / `EnrollCommand.RunAsync` / `AgentWorker.AnnounceFirstContactAsync` → `ServerClient` → `AgentController.Announce` or `Enroll` → `AgentService.AnnounceAsync` or `EnrollAsync` → Agent/Computer/token persistence.
2. `AgentWorker.HeartbeatTickAsync` → `ServerClient.HeartbeatWithCredentialAsync` → `AgentController.Heartbeat` → `AgentService.RecordHeartbeatAsync`, including `ResolveAgentUpdateAsync` only as an authenticated response handoff.
3. `ServerClient` credential generation snapshots → `AgentTokenTransitionManager` → `TokenStore`, including delayed 401, rotation response, crash between persistence and in-memory change, and restart.
4. Server-side user revocation/computer Agent revocation → `AgentService.AuthenticateAsync` → `AgentAuthenticationHandler` → all `[Authorize(AuthenticationSchemes = AgentAuthenticationDefaults.Scheme)]` routes.
5. `AgentCommandService.EnqueueAsync`/`ClaimPendingForDeliveryAsync` → heartbeat commands → `AgentCommandHandler` → acknowledgement; trace duplicate delivery, TTL, cancellation command, and an Agent from another computer attempting acknowledgement.
6. `InstallationJobService` download capability output → `AgentBlobCapabilityService` → `AgentController.DownloadLocalBlob` → `AgentBlobDownloadAuthorizer` → storage stream.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also list good decisions that should remain unchanged, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
