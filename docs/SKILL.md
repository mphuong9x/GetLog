---
name: senior-code-review
description: Professional code and solution review workflow for evaluating existing implementation choices, business logic correctness, authentication, authorization, data integrity, clean code, performance, and over-engineering risk. Use when asked to review a codebase, pull request, feature implementation, architecture decision, selected solution, security-sensitive flow, core business workflow, API/backend change, permission model, or production-readiness of code.
---

# Senior Code Review

## Overview

Use this skill to review code like a senior developer, tech lead, or solution architect. Prioritize correctness, security, business invariants, maintainability, and practical implementation over stylistic preferences.

Review evidence from the repository first. Do not assume the design is wrong because it is unfamiliar, and do not approve code because it looks clean while core behavior is unverified.

## Review Workflow

1. Identify the review scope: changed files, requested feature, affected domain, user roles, data boundaries, APIs, jobs, migrations, and external integrations.
2. Reconstruct the intended business flow from code, tests, docs, route names, domain models, comments, and existing patterns.
3. Trace entry points to persistence: UI/API/controller/handler -> validation -> auth/authz -> domain logic -> transaction -> database/external side effects -> response/event/log.
4. Review highest-risk paths first: authentication, authorization, tenant/user ownership, core state transitions, money/quota/permission changes, destructive actions, background retries, and admin flows.
5. Compare the chosen solution against simpler local patterns. Prefer the smallest change that satisfies the requirement without weakening boundaries.
6. Validate claims with tests, commands, static analysis, or direct code references when available. If verification is not possible, label the gap clearly.
7. Report findings first, ordered by severity and likelihood. Keep summaries secondary.

## Severity Model

Use these levels consistently:

- `S0 Critical`: exploitable auth bypass, privilege escalation, cross-tenant data exposure, data loss/corruption, broken core workflow in production, secrets exposure, or a change that can take down the system.
- `S1 High`: incorrect authorization on important endpoints, inconsistent business state, transaction/concurrency bug with real impact, unsafe migration/deployment path, serious validation gap, or production incident likely under normal usage.
- `S2 Medium`: correctness edge case, maintainability issue likely to create bugs, performance problem on a realistic hot path, duplicated logic that can diverge, incomplete test coverage for important behavior.
- `S3 Low`: small cleanup, naming, style, minor refactor, local readability issue, non-blocking optimization.

Within the same severity, sort by blast radius, exploitability, likelihood, and cost of fixing later.

## Core Review Areas

### Authentication

Verify the system proves identity correctly before trusting requests.

- Check token/session validation, issuer, audience, expiry, signing keys, key rotation, revocation, and clock skew handling.
- Check password handling, hashing, reset flows, session fixation, remember-me behavior, MFA assumptions, and account lifecycle states.
- Check cookie flags, CSRF protection, CORS, redirect handling, API key storage, secret management, and logging of sensitive values.
- Confirm tests cover expired, malformed, missing, replayed, and wrong-issuer credentials where relevant.

### Authorization

Verify every sensitive action is allowed by server-side policy, not by UI visibility or client-provided fields.

- Check role, permission, ownership, object-level, tenant-level, and admin/superuser boundaries.
- Confirm default-deny behavior for new endpoints, handlers, background jobs, and real-time channels.
- Trace query filters and repository methods for tenant/user scoping; look for IDs accepted from clients without ownership checks.
- Check bulk operations, exports, imports, search, file download, update/delete, invitation, membership, and delegation flows.
- Ensure authorization decisions are centralized enough to be consistent but not abstracted so far that behavior becomes opaque.

### Core Business Logic

Verify domain invariants survive normal, invalid, concurrent, and repeated requests.

- Identify state machines and confirm invalid transitions are rejected.
- Check validation at trust boundaries and domain boundaries; do not rely only on UI validation.
- Check idempotency for retries, webhooks, payment-like flows, background workers, and user double-submits.
- Check transaction scope, rollback behavior, optimistic/pessimistic concurrency, unique constraints, and race conditions.
- Verify time handling, timezone boundaries, soft delete behavior, audit trails, and event ordering.

### Data And Persistence

- Check schema constraints, indexes, migrations, nullability, default values, cascade behavior, and backward compatibility.
- Verify ORM loading patterns, N+1 risks, tracking/no-tracking behavior, pagination, sorting stability, and filtering at database level.
- Confirm serialization/deserialization does not expose internal fields or accept fields that clients should not control.
- Check data retention, PII handling, auditability, and destructive changes.

### Architecture And Implementation Choice

Evaluate whether the selected solution fits the existing project and actual business need.

- Prefer existing project patterns unless they are the source of the problem.
- Flag over-engineering when abstractions, generic frameworks, queues, caches, reflection, or complex patterns do not reduce real risk or complexity.
- Flag under-engineering when core rules are duplicated, hidden in controllers, missing tests, or impossible to reason about.
- Check dependency direction, module boundaries, error handling, observability, deployment impact, and rollback strategy.
- Recommend practical fixes: narrow scope, clear ownership, explicit contracts, and tests around behavior.

### Clean Code, Performance, And Maintainability

- Focus on readability that protects correctness: clear names, local reasoning, small functions with meaningful boundaries, and low surprise.
- Flag dead code, redundant layers, speculative generalization, inconsistent conventions, and copy-paste business rules.
- Review performance only against realistic data volume and hot paths. Distinguish measurable bottlenecks from premature optimization.
- Verify caching invalidation, retry policies, timeout handling, cancellation, resource disposal, and backpressure where applicable.

## Finding Requirements

Only report a finding when there is concrete evidence or a clearly stated assumption. Avoid generic best-practice complaints.

Each finding should include:

- Severity and concise title.
- File and line reference when possible.
- What is wrong and why it matters.
- A realistic failure, exploit, or maintenance scenario.
- Minimal practical fix.
- Tests or validation that should prove the fix.

If a suspected issue cannot be confirmed, put it under `Open Questions` or `Needs Verification`, not as a definite finding.

## Output Format

Match the user's language. If the user's prompt is Vietnamese, respond in Vietnamese.

Use this structure unless the user asks for another format:

```markdown
**Findings**
- `S0 Critical` [Title] - [file:line]
  [Impact and evidence. Explain the broken behavior, realistic scenario, minimal fix, and tests.]

- `S1 High` [Title] - [file:line]
  [Impact and evidence...]

**Open Questions**
- [Question or assumption that affects review confidence.]

**Summary**
[One short paragraph about overall implementation quality, biggest risk area, and whether the solution direction is acceptable.]

**Suggested Fix Order**
1. [Most urgent fix]
2. [Next fix]
3. [Lower-risk cleanup]
```

If there are no findings, say that explicitly and mention remaining test gaps or residual risk.

## Review Discipline

- Lead with issues, not praise.
- Do not rewrite large sections of code unless the user asks for implementation.
- Do not suggest enterprise patterns by default. Explain why any added abstraction is necessary.
- Do not bury security or core correctness issues under style feedback.
- Do not require perfect code; require code that is correct, clear, testable, and appropriate for the project's scale.
- Prefer one precise high-impact finding over many vague low-value comments.
- When reviewing a diff, avoid flagging unrelated legacy code unless it directly affects the changed behavior.
