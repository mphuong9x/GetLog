# Backend Review Plan — Fable 5 (2026-07-13)

## Phương án: CHIA PASS, không review 1 lượt

Số liệu thực tế của backend (không tính `obj/bin`):

| Project | Files | Lines | Ghi chú |
|---|---|---|---|
| MProject.Api | 38 | ~4.000 | Controllers + Filters + Infrastructure (Tus, GitBasicAuth) |
| MProject.Application | 140 | ~16.800 | Services 13.200 lines là lõi |
| MProject.Domain | 84 | ~1.500 | Entities, mỏng |
| MProject.Infrastructure | 49 | ~66.400 | **64.200 lines là EF Migrations — KHÔNG review** |
| MProject.Tests | 44 | ~15.000 | Review riêng nếu cần |

→ Code cần review thật sự ≈ **24.000 lines / ~310 files**. Một lượt duy nhất sẽ vượt xa
mức context mà model còn giữ được độ sâu: model buộc phải đọc lướt, bỏ sót bug tinh vi,
và phần findings đầu bị "quên" khi context bị nén. Review theo pass, mỗi pass một
session mới (`/clear`), là cách duy nhất để mỗi file được **đọc trọn vẹn**.

Trừ một ngoại lệ: **tìm code chết / file thừa** thì lại cần nhìn TOÀN BỘ solution
(vì "không ai gọi X" là mệnh đề toàn cục). Nhưng việc đó là search-driven (Grep
references), không cần đọc sâu, nên vẫn làm được 1 lượt → đặt làm Pass 0.

### Thứ tự pass (tuân theo review_rule.md: "remove unnecessary code BEFORE improving existing code")

| Pass | Phạm vi | Lines ước tính |
|---|---|---|
| 0 | Dead code & file thừa — toàn solution (search-driven) | toàn bộ, đọc nông |
| 1 | Software A: Package / Version / File / Assignment | ~4.500 |
| 2 | Software B: InstallationJob / Inventory / Baseline / Override / BlobGc / Watchdogs | ~5.500 |
| 3 | Identity + Authorization (RBAC/ACL/Auth/Token) | ~4.000 |
| 4 | Assets: Agent / Computer / Station / Model / AgentRelease | ~3.500 |
| 5 | Approvals + Organization | ~2.500 |
| 6 | API host: Program, Filters, Middleware, TusUploadHandler, Git (Filter + GitRepositoryService), Storage | ~2.500 |
| 7 | Cross-cutting: trùng lặp pattern giữa các service (đọc kết quả pass 1–6) | tổng hợp |
| 8 | (Tuỳ chọn) Chất lượng test | ~15.000 |

### Cách chạy (quan trọng không kém prompt)

1. Mỗi pass = **1 session mới** (`/clear` trước khi dán prompt).
2. Findings ghi ra file `docs/review/pass{N}_findings.md` — không để trong chat,
   vì context nén sẽ mất. Pass 7 đọc lại các file này.
3. **Review xong hết mới fix.** Fix theo severity: CRITICAL → xoá dead code → HIGH → MEDIUM.
   Fix cũng chia theo module, mỗi đợt fix chạy build + test rồi mới sang đợt sau.
4. Prompt viết tiếng Anh (model review chính xác hơn); findings yêu cầu song ngữ phần Summary.

---

## Pass 0 — Dead code & unnecessary files (toàn solution)

```
Read docs/skills/review_rule.md first and follow it strictly. This is a
dead-code and unnecessary-file sweep of the entire backend under
MProjectBackend/ (exclude obj/, bin/, MProject.Infrastructure/Migrations/).

Goal: find code that should not exist. Do NOT modify anything — report only.

Method (be systematic, evidence-based, no guessing):
1. Build the solution first so you know it compiles (baseline).
2. Enumerate every .cs file. For each public type, Grep the whole solution
   for references. A type referenced only by DI registration + its own
   interface is a deletion candidate.
3. Specifically hunt for:
   - interfaces with a single implementation that add no test-seam value
     (check MProject.Tests for mocks before flagging)
   - services/methods with zero callers (controllers, other services, tests)
   - DTO/model classes in MProject.Application/Models never used in any
     controller signature or service
   - Domain entities not mapped in the DbContext or never queried
   - leftover files: Backup/ folder, commented-out blocks > 10 lines,
     #if DEBUG-only code, TODO stubs never wired up
   - duplicate helpers (same logic implemented twice in different folders)
   - DI registrations in Program.cs for types nothing resolves
   - config options classes whose values are never read
4. For every candidate, verify with a second Grep that removal is safe,
   and note dynamic-usage risks (reflection, DI by convention, EF).

Output to docs/review/pass0_findings.md:
- table: file | symbol | why dead | evidence (grep result summary) | risk
- a "safe to delete now" list vs "needs human confirmation" list
- estimated total LOC removable
End with a short Vietnamese summary (5-10 lines).
```

---

## Prompt template cho các pass module (1–6)

Các pass 1–6 dùng chung khung dưới đây, chỉ thay `<SCOPE>` và `<N>`.
Khung này đã nhúng đúng thứ tự ưu tiên của review_rule.md
(Correctness → Simplicity → Maintainability → Performance).

```
Read docs/skills/review_rule.md first and follow it strictly.

Deep-review the following backend scope. Read EVERY file in scope completely
— do not sample or skim. Do NOT modify code — report only.

Scope:
<SCOPE>

Also read (context only, not in review scope): the interfaces in
MProject.Application/Interface/ and models in MProject.Application/Models/
used by these files, and the relevant tests in MProject.Tests to know what
is already covered.

Review in this order:
1. CORRECTNESS & SECURITY (highest priority)
   - null/empty/boundary inputs; swallowed exceptions; inverted conditions
   - async: missing await, sync-over-async, fire-and-forget losing errors
   - EF: N+1 queries, missing AsNoTracking on reads, global query filter
     surprises (!IsDeleted), race conditions on read-then-write, missing
     transactions across multi-entity writes
   - authz: every controller action and service mutation must enforce
     permission checks consistently (compare against how sibling endpoints
     do it); resource-level ACL bypasses
   - unvalidated external input on agent-facing endpoints (path traversal
     in file paths, unsafe deserialization)
2. UNNECESSARY COMPLEXITY (rule: find code that should not exist)
   - pass-through service methods, wrapper layers with no logic
   - duplicated query/mapping logic within this scope that should be one
     private helper (only flag if truly identical semantics)
   - dead branches, unreachable code, parameters always passed the same value
3. MAINTAINABILITY
   - copy-paste blocks >15 lines repeated 2+ times in this scope
   - methods > ~80 lines doing multiple jobs (flag, don't redesign)
4. PERFORMANCE (only clear wins: query in loop, loading full table to
   filter in memory, missing pagination on unbounded lists)

Rules of engagement:
- Every finding = file:line + evidence + minimal fix. No style/naming/
  formatting findings. No redesigns. Prefer few high-confidence findings.
- Before flagging "duplicate" or "dead", Grep to confirm.
- Severity per review_rule.md: CRITICAL / HIGH / MEDIUM (ignore LOW).

Output to docs/review/pass<N>_findings.md using the Output format defined
in review_rule.md (Summary / Good Decisions / Findings / Unnecessary Code /
Simpler Alternative / Complexity Report / Final Recommendation).
End with a short Vietnamese summary (5-10 lines).
```

### Pass 1 — Software A (`<N>`=1)

```
<SCOPE>:
- MProject.Application/Services/Software/: SoftwarePackageService.cs,
  SoftwareVersionService.cs, SoftwareFileService.cs,
  StationSoftwareAssignmentService.cs
- MProject.Api/Controllers/Software/: SoftwarePackagesController.cs,
  SoftwareVersionsController.cs, SoftwareFilesController.cs,
  SoftwareAssignmentsController.cs
Domain focus: package/version lifecycle (clone, release, pin), assignment
multi-active semantics (IsActive is an independent Enable flag — one station
may have several active assignments; Deactivate must emit an Uninstall job),
soft-delete + global query filter interactions.
```

### Pass 2 — Software B (`<N>`=2)

```
<SCOPE>:
- MProject.Application/Services/Software/: InstallationJobService.cs,
  InstallationJobWatchdogService.cs, PcInstallationService.cs,
  PcInventoryService.cs, BlobGcService.cs, StationRollbackWatchdogService.cs,
  ConfigBaselineService.cs, ConfigBaselineValidator.cs, BaselineEvaluator.cs,
  ConfigBaselinePermissionService.cs, ConfigFileReader.cs,
  OverrideFileService.cs, OverrideResolver.cs, OverrideFilePermissionService.cs
- MProject.Api/Controllers/Software/: PcInstallationsController.cs,
  ConfigBaselinesController.cs, OverrideFilesController.cs
Domain focus: job state machine (enqueue → poll → report), the deliberate
IgnoreQueryFilters usage for Uninstall jobs of deleted versions (must NOT be
"simplified" away — it is a bug fix), watchdog timers and concurrency,
BlobGc reference-guards (SoftwareFile + AgentRelease), override resolution
precedence.
```

### Pass 3 — Identity + Authorization (`<N>`=3)

```
<SCOPE>:
- MProject.Application/Services/Identity/ (all 11 files)
- MProject.Application/Authorization/ (all 9 files: AclQueryService,
  RbacGrantQueryService, SubjectResolver, ...)
- MProject.Api/Controllers/Identity/ (Auth, Authorization, Roles, Users)
Domain focus: permission evaluation path (RBAC grants + resource ACL),
cache invalidation correctness (stale grants after role/ACL mutation),
refresh-token rotation and revocation, seeded roles consistency
(Viewer/Member defaults), privilege-escalation paths (can a user grant
themselves permissions they don't hold?).
```

### Pass 4 — Assets (`<N>`=4)

```
<SCOPE>:
- MProject.Application/Services/Assets/ (all 8 files)
- MProject.Api/Controllers/Assets/ (Agent, AgentReleases, Computers,
  Models, Stations)
Domain focus: agent poll/report protocol (this is the hot path — every
station polls it), liveness watchdog vs status mapper consistency,
agent self-update publish flow (release ref-guard), test-metrics ingestion
(RJ45 counters), input validation on agent-supplied data.
```

### Pass 5 — Approvals + Organization (`<N>`=5)

```
<SCOPE>:
- MProject.Application/Services/Approvals/ (all 5 files)
- MProject.Application/Services/Organization/ (all 4 files)
- MProject.Api/Controllers/Approvals/ + MProject.Api/Controllers/Organization/
Domain focus: approval handler dispatch (OverrideFile,
SoftwareAssignment handlers — is the handler abstraction earning its keep
with only 2 implementations?), approver resolution, department ownership
authorization, NoOp notification service (dead scaffolding?).
```

### Pass 6 — API host & infrastructure (`<N>`=6)

```
<SCOPE>:
- MProject.Api/: Program.cs, Filters/ (incl. GitBasicAuthFilter),
  Infrastructure/ (incl. TusUploadHandler), Middleware if any,
  Controllers/Common/ (Admin, AuditLogs, Base, Health)
- MProject.Application/Services/GitRepositoryService.cs
- MProject.Application/Services/Common/ (AdminService,
  DomainEventDispatcherService)
- MProject.Infrastructure/ EXCLUDING Migrations/: DbContext, Storage/, Options/
Domain focus: DI registrations vs actual usage, middleware order, Tus upload
security (path traversal, size limits, orphaned partial uploads),
GitBasicAuthFilter credential handling (no secrets in logs), blob storage
stream disposal, DbContext configuration vs entity reality.
```

---

## Pass 7 — Cross-cutting duplication (sau khi có pass 0–6)

```
Read docs/skills/review_rule.md first. Then read ALL of
docs/review/pass0_findings.md through pass6_findings.md.

This is the cross-cutting pass: find duplication and inconsistency BETWEEN
modules that per-module passes cannot see. Do NOT modify code — report only.

1. Aggregate: merge duplicate/overlapping findings across the pass files;
   produce one deduplicated master list ranked CRITICAL → HIGH → MEDIUM.
2. Cross-module duplication — Grep-verify each hypothesis:
   - pagination handling (PagedResult construction) — is it hand-rolled
     per service? Count occurrences; propose ONE shared helper only if
     3+ near-identical copies exist.
   - permission-check preamble in services (resolve subject → check grant
     → throw) — same question.
   - entity→DTO mapping duplicated between list and detail endpoints.
   - "get by id or throw NotFound" boilerplate.
   - watchdog/background-service scaffolding repeated across the 3+
     watchdog services.
3. Consistency: same concept handled differently in different modules
   (soft-delete checks, audit logging, error responses) — flag ONLY where
   the inconsistency can cause a real bug, not for uniformity's sake.
4. Per review_rule.md decision rules: prefer removal over cleanup; do not
   propose a new abstraction unless it replaces 3+ real duplications.

Output docs/review/pass7_master_plan.md:
- deduplicated master findings table (severity, file:line, fix, est. effort)
- deletion list (from pass 0, confirmed)
- a phased FIX PLAN: phase 1 CRITICAL bugs, phase 2 deletions,
  phase 3 HIGH dedup/simplification, phase 4 MEDIUM — each phase sized to
  one session, each ending with "build + run full test suite".
End with a Vietnamese summary.
```

---

## Prompt fix (chạy từng phase sau khi bạn duyệt master plan)

```
Read docs/skills/coding_rule.md and docs/review/pass7_master_plan.md.
Apply ONLY the items in Phase <X>. For each item: make the minimal fix
exactly as specified; if reality differs from the finding (code changed,
finding wrong), skip it and note why. After all items:
dotnet build + dotnet test (all projects). Report per-item outcome
(fixed / skipped+reason) and final build/test status. Do not commit.
```

---

## Pass 8 (tuỳ chọn) — Test quality

```
Read docs/skills/review_rule.md first. Review MProject.Tests/ (~15k lines).
Do NOT modify code — report only.
Find: tautological tests (assert what the mock returns), tests that would
still pass if the production code broke, duplicated setup that hides intent,
dead test helpers, and REAL coverage gaps only for CRITICAL paths found in
pass 1-6 findings (authz enforcement, job state machine, agent protocol).
Output docs/review/pass8_findings.md. End with a Vietnamese summary.
```

---

## Vì sao không dùng 1 prompt cho cả backend?

- 24k lines code + interfaces + tests liên quan ≈ vượt ngưỡng model đọc kỹ
  từng dòng trong 1 context; model sẽ tự chuyển sang đọc mẫu (sampling) →
  chính loại bug bạn muốn bắt (trùng lặp tinh vi, authz thiếu 1 endpoint)
  là loại bị sót đầu tiên.
- Findings nằm trong chat sẽ bị mất khi context nén; ghi file + pass 7 tổng
  hợp giải quyết việc này.
- Dead code là bài toán toàn cục nhưng chỉ cần Grep, không cần đọc sâu →
  tách riêng Pass 0 là tối ưu chi phí/chất lượng.
- Lưu ý: `/code-review` của Claude Code chỉ review DIFF của branch hiện tại,
  không review cả codebase — nên bộ prompt này là đúng công cụ cho mục tiêu
  của bạn. (Sau này, mỗi lần code xong 1 feature thì dùng `/code-review`
  cho diff đó là đủ, không cần chạy lại cả bộ này.)
