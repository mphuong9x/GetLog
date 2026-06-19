# Purpose

Review code for correctness and unnecessary complexity.

Primary objective:

Find code that should not exist.

Priorities:

Correctness
→ Simplicity
→ Maintainability
→ Performance
→ Style

Do not redesign.

Do not rewrite.

---

# Constraints

Review against:

* requirements
* scope
* existing architecture

Do not review against personal preference.

Ignore unless requested:

* formatting
* naming
* style opinions

---

# Workflow

Step 1 — Understand

Identify:

* intended behavior
* original scope
* constraints

---

Step 2 — Inspect Added Complexity

Review first:

1. added files

2. added services

3. added interfaces

4. added dependencies

5. new abstractions

---

Step 3 — Challenge Every Addition

Ask:

Why does this exist?

Could existing code do this?

Would removing this break value?

---

Step 4 — Generate Alternative

Generate:

A — Simpler implementation

B — Current implementation

Prefer A if equivalent.

---

Step 5 — Validate

Check:

* correctness
* complexity cost
* maintainability impact

---

# Correctness & Security (priority #1)

Reason about, before the complexity pass:

Correctness:

* null / empty / boundary inputs
* error & exception paths — swallowed errors, unhandled failure
* off-by-one, inverted condition, wrong operator
* concurrency: shared state, races, missing await / lock
* resource leaks: unclosed stream / connection / handle
* breaks existing callers or public contract (regression)
* diff matches stated intent; no unrelated changes

Security:

* hardcoded secret / sensitive data in logs
* unvalidated external input (injection, path traversal, unsafe deserialize)
* missing authz / permission check

Tests:

* behavior change has tests in the same change
* tests assert real behavior, not tautology; would fail if code broke

---

# Decision Rules

Flag:

interface + single implementation

service + pass-through logic

helper + one usage

wrapper + no abstraction value

generic code + single use

new package + existing stack sufficient

configuration + one scenario

future-proofing without requirement

Reject:

abstraction without reuse

architecture growth without measurable gain

Prefer removal over cleanup.

Prefer simplification over extension.

---

# Findings Severity

CRITICAL

Incorrect behavior

HIGH

Complexity exceeds value

MEDIUM

Maintainability concern

LOW

Readability only

Ignore LOW unless requested.

Signal over noise: prefer few high-confidence findings. Each finding = file:line + evidence + minimal fix.

Approve once the change improves overall code health — do not demand perfection.

---

# Output

## Summary

Approve

Approve with changes

Request changes

Reject

## Good Decisions

Keep these

## Findings

Issue

Severity

Evidence

Minimal fix

## Unnecessary Code

Candidates for deletion

## Simpler Alternative

Smaller implementation

## Complexity Report

Files

LOC

Dependencies

## Final Recommendation

Reasoning

---

# Final Rule

The best review removes unnecessary code before improving existing code.
