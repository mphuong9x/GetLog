# Astra review: production-support durable workflow

Review the production-support workflow from a station operator's local request through durable offline delivery and server-side recovery UI. Do not modify production code.

## Scope

- Shared contract: every source, fixture, and shared compatibility test under `MProject.ProductionSupport.Contracts`.
- Agent: `Configuration/AgentOptions.cs`; `Support/{ProductionSupportAgentService,ProductionSupportApiClient,ProductionSupportDeliveryService,ProductionSupportDeliveryWorker,ProductionSupportPreviewScanner,ProductionSupportPreviewService,SupportOutboxStore,SupportOutboxContract,SupportOutboxRuntime}.cs`; IPC support branches in `IpcRequestHandler`; Agent support telemetry in `AgentWorker`.
- Server: `Controllers/{Assets/AgentProductionSupportController,SupportRequestsController}.cs`, `Observability/ProductionSupportMetrics*`, `Services/Support/{ProductionSupportService,ProductionSupportQueryService,ProductionSupportRecoveryService}.cs`, support entities/migrations/models/options.
- Browser: production-support API/types/hooks/page, `SupportQueryIsolation`, i18n strings and tests.
- Relevant tests: Agent support service/API/delivery/outbox/compatibility/self-update tests; server contract/API/create/update/recovery/query/PostgreSQL/migration tests; frontend support tests.

## Exclusions

Do not re-review named-pipe transport security (`06`), generic RBAC implementation (`09`), or raw test-result parser/scanner correctness (`08`). Read those dependencies only where they determine a production-support behavior.

## Context to read first

Read `AGENTS.md`, production-support contracts and fixture compatibility matrix, `AgentOptions.AgentMSystemAddress`, support retention/metrics configuration, and relevant checklist material. The workflow uses a durable Agent SQLite outbox; do not assume a synchronous web request is authoritative.

## Review objectives

- Validate preview, report, update, and status IPC contracts including capability negotiation and safe operator errors.
- Validate SQLite outbox initialization, schema/version recovery, bounded capacity, local request graph, claim/lease, operation dependency/supersession, retry/backoff, authentication pause, protocol mismatch, terminal retention, and crash recovery.
- Validate server idempotency and concurrency for create/update/current, Agent/station hierarchy context, employee identity/routing, log/test-result linkage, and database locks/retries.
- Validate recovery reroute/force-resolve authority, query visibility, metric collection, browser query isolation/refetch/mutation updates, and no misleading local-success claim before durable enqueue.
- Analyze lost responses, duplicate sends, offline/restart, changed Agent token, server version mismatch, concurrent local report/update, concurrent recovery action, malformed payload, and current request state divergence.

## Required tracing

1. Launcher support action → `IpcRequestHandler.SupportPreviewAsync`/`SupportReportAsync`/`SupportUpdateAsync` → `ProductionSupportAgentService` → durable `SupportOutboxStore` enqueue result.
2. `ProductionSupportDeliveryWorker` → `ProductionSupportDeliveryService.InitializeAsync`/`RunCycleAsync`/`DeliverAsync` → `ProductionSupportApiClient` → authenticated `ProductionSupportAgentRoutes`.
3. `AgentProductionSupportController` → `ProductionSupportService.GetCurrentForAgentAsync`/`CreateAsync`/`UpdateAsync` → support records, receipts, updates/logs, advisory lock, and response fingerprint/idempotency path.
4. Browser support API/hooks/page → `SupportRequestsController` → query visibility and `ProductionSupportRecoveryService.RerouteAsync`/`ForceResolveAsync`.
5. Agent heartbeat support telemetry → server metric middleware/collector and browser/operator visibility where relevant.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
