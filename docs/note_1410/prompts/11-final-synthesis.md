# Astra review synthesis: MProject remediation backlog

You are given the completed reports listed in `../reports/README.md`. Produce a single decision-ready synthesis. Do not modify production code.

## Scope

Consume the completed Astra reports named in `../reports/README.md`, the plan, and the repository guidance. This prompt synthesizes review evidence; it does not start a new deep source review.

## Exclusions

Do not modify production code, repeat every source inspection, or turn unverified historical checklist notes into current findings. Do not use the synthesis to propose speculative platform redesign.

## Context

1. Read `AGENTS.md`, `../00-REVIEW-PLAN.md`, and `docs/PROJECT_REVIEW_CHECKLIST.md`.
2. Read every available report named in `../reports/README.md` and record any missing reports before drawing conclusions about their scopes.
3. Treat current review evidence as authoritative. Use the historical checklist only to identify known prior fixes, accepted decisions, and unresolved follow-up; do not resurrect a closed finding without current evidence.
4. Preserve the owner-approved shared installer-token provisioning workflow. Do not recommend unique tokens per PC or mandatory manual enrollment merely because of token reuse.

## Review objectives

- Normalize findings that describe the same root cause, even when the reports use different symptoms or severities. Keep all evidence links under one canonical issue.
- Resolve overlaps using the plan's ownership rules. Explain where one root cause spans workflow boundaries rather than counting it several times.
- Reassess severity by the stated production priorities: correctness, production safety, recoverability, security, state/data consistency, observability, maintainability, performance, then elegance.
- Assign P0/P1/P2/P3 based on realistic production impact and preconditions, separately from individual report severity.
- Distinguish confirmed defects from likely defects, design risks, maintainability issues, and optional improvements. Include low-confidence items only when their required evidence is clear.
- Identify quick wins, high-risk fixes, structural refactoring candidates, optional improvements, findings that should not be acted on, and items requiring a human/product decision.
- Define regression tests at the actual boundary that failed: PostgreSQL concurrency, Windows service/LocalSystem, IIS/TLS, filesystem/reparse-point, TUS HTTP, named pipe, Agent restart, network interruption, or browser/API contract as appropriate.
- Propose an incremental remediation sequence that avoids destabilizing production stations and states dependencies/rollback considerations.

## Required tracing

For every merged issue, trace its report evidence back to the exact source file/symbol named by the originating report. When reports disagree, trace the disputed handoff across the relevant report boundaries before choosing a canonical conclusion.

## Required output

Start with a short scope/completeness statement that lists supplied and missing reports. Then provide:

1. A deduplicated issue table with canonical ID, P0/P1/P2/P3, report severity/confidence, category, affected workflow, evidence sources, realistic station/server impact, and disposition.
2. One detailed section per canonical issue: root cause, exact files/symbols, merged evidence, scenario, impact, recommended direction, change-risk/rollback notes, required regression test, and estimated change scope.
3. Separate lists for quick wins, high-risk fixes, structural refactoring, optional improvements, do-not-act items, and human/product decisions.
4. A staged remediation order with prerequisites and validation gates. Station-affecting changes must include a practical recovery/rollback check.
5. A section of good decisions to preserve, rejected suspicions, and insufficient-evidence questions.

Do not inflate generic code-quality concerns into production work. Do not propose microservices, CQRS, event buses, generic repository layers, or large rewrites unless the reports demonstrate a concrete failure that the smaller remediation sequence cannot address.
