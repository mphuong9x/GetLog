# Astra review: test-result ingestion and monitoring

Review the pathway that turns station log files into production test-monitor information. Do not modify production code.

## Scope

- Shared parser: all source, fixtures, and shared tests in `MProject.TestResults`.
- Agent: `Configuration/AgentOptions.cs`, `Services/{TestResultScanner,TestMetricsReporter,AgentWorker,ServerClient}.cs`, test-result/metric models, and relevant cursor/runtime persistence.
- Server: Agent test endpoints in `Controllers/Assets/AgentController.cs`, `Services/Assets/{AgentService,TestResultQueryService,TestShiftCalculator}.cs`, `Controllers/Assets/TestResultsController.cs`, test entities/enums/options/migrations.
- Browser: `src/api/testResults.ts`, test-result types, `pages/TestMonitor/TestMonitor.tsx`, and tests.
- Tests: test parser corpus tests, `TestResultScannerTests`, `TestMetricsReporterTests`, backend `TestResultParserTests`, `TestResultQueryServiceTests`, Agent service test result/metric coverage, and frontend Test Monitor coverage.

## Exclusions

Do not review production-support request semantics (`07`), generic heartbeat authentication (`01`), deployment jobs (`02`), or browser authorization framework (`09`) beyond the endpoint permission needed to validate the monitor boundary.

## Context to read first

Read `AGENTS.md`, checklist LOG-001, Agent `TestLogs`/`TestMetricsIntervalSeconds` configuration, API `TestMonitor` configuration, and parser fixtures. Source files can be large and mutable; preserve production observability over a desire to parse every possible format.

## Review objectives

- Validate scanner root/path handling, file selection, cursor persistence, overlap/tail windows, starvation avoidance, batching, clock/timezone assumptions, and crash/restart behavior.
- Validate parser compatibility and the meaning of fully/partially/unparsed records without masking failures or turning ordinary format drift into a destructive failure.
- Validate authenticated server ingestion ownership, duplicate/idempotency keys, ordering, storage constraints, and Agent/server retry response semantics.
- Validate metrics aggregation, shifts/day/timezone boundaries, fail-rate/streak calculations, query filters/pagination, and UI polling/rendering so operators see timely accurate failures.
- Analyze copied/rotated logs, delayed writes, duplicate batches, network loss after server commit, Agent restart, DST/timezone configuration changes, out-of-order records, and a high-volume station.

## Required tracing

1. Configured `TestLogs.Roots` → `TestResultScanner` parse/cursor/batch → `AgentWorker.RunTestResultsLoopAsync` → `ServerClient.ReportTestResultsAsync`.
2. `AgentController.ReportTestResults` → `AgentService.ReportTestResultsAsync` → `TestResultRecord` persistence/deduplication and its response to the Agent scanner.
3. Test metrics scan/aggregation → `TestMetricsReporter` → `AgentController.ReportTestMetrics` → `AgentService.ReportTestMetricsAsync` and computer/station counters.
4. `TestResultsController` → `TestResultQueryService` / `TestShiftCalculator` → frontend `testResults.ts` → `TestMonitor` refresh/filter/data presentation.
5. Test-result links used by production support only far enough to verify that a missing/duplicate result does not corrupt an unrelated support record.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
