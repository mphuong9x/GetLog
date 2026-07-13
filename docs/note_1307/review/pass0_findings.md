# Pass 0 — Dead Code & Unnecessary File Sweep (Backend)

- **Scope:** `MProjectBackend/` — all 312 `.cs` files (excluding `obj/`, `bin/`, `MProject.Infrastructure/Migrations/`), 516 declared types, all interfaces/methods/options/enums/constants, plus non-`.cs` leftovers.
- **Baseline:** `dotnet build MProject.slnx` → **0 errors** (23 pre-existing nullability warnings in `RepositoryModels.cs` / `GitRepositoryService.cs`).
- **Method:** scripted whole-word reference count per declared type across the solution; per-method call-site analysis for every interface method; per-property read analysis for all 11 options classes; enum-member usage checked repo-wide (FE + agent included, since `JsonStringEnumConverter` at `Program.cs:334` makes member names a wire contract); dynamic-usage risks (reflection, DI, EF, ASP.NET routing, JSON serialization) checked individually for every candidate. Every candidate verified with a second independent grep.
- **Verdict (review_rule.md):** *Approve with changes* — codebase is unusually clean; a bounded set of dead symbols should be removed.

---

## Findings table

Severity per `docs/skills/review_rule.md` (HIGH = complexity/surface exceeds value, MEDIUM = maintainability).

| # | File | Symbol | Why dead | Evidence (grep summary) | Risk | Sev | ~LOC |
|---|------|--------|----------|--------------------------|------|-----|-----|
| 1 | `MProject.Application/Constants/RepositoryAction.cs` | `RepositoryAction` (whole file) | Constants class; zero references anywhere (backend, FE, agent) | Whole-repo grep: only the declaration | None — constants are only reachable via class name | HIGH | 10 |
| 2 | `MProject.Domain/Enums/AssignmentEffect.cs` | `AssignmentEffect` (whole file) | Enum never used: no entity property, no code, no DbContext mapping, no wire usage | Whole-repo grep: only the declaration | None | HIGH | 15 |
| 3 | `MProject.Application/Models/SoftwareModels.cs:71` | `CreateSoftwarePackageResponse` | DTO never used in any controller signature or service | Backend grep: declaration only. (FE declares an independent same-name type in `src/types/software.ts:120` — likely stale there too, FE out of scope) | Swagger schema disappears; nothing consumes it | HIGH | 6 |
| 4 | `MProject.Application/Models/ResourceModels.cs:57` | `UpdateFileContentRequest` | DTO never used | Backend grep: declaration only | None | HIGH | 4 |
| 5 | `MProject.Application/Interface/Common/IStorageService.cs:17` + `LocalStorageService.cs:125` + `MinioStorageService.cs:202` | `DeleteFolderAsync` (+ private cascade `RemoveBatchAsync` + `BatchDeleteSize` in Minio impl, only called from it) + 8 test-fake impls | Interface method with zero call sites | Grep `DeleteFolderAsync`: interface, 2 prod impls, 8 test fakes — no caller anywhere | None; not resolved reflectively. Touches 8 test files (delete one-line fakes) | HIGH | ~60 |
| 6 | `MProject.Application/Interface/Software/ISoftwareVersionService.cs:23` + `SoftwareVersionService.cs:723` | `GetLatestReleasedVersionAsync` | Zero callers (no controller endpoint, no service, no test) | Grep: interface + impl only | None | HIGH | 38 |
| 7 | `MProject.Application/Interface/Identity/IPermissionService.cs:8` + `PermissionService.cs:18` + **whole file** `Models/PermissionResult.cs` (`PermissionResult`, `AssignmentMatch`) + 5 test-fake impls | `CheckPermissionDetailedAsync` | Zero callers. Redundant wrapper: `AuthorizationController.cs:65` already calls `IAuthorizationService.EvaluateAsync` directly; the `PermissionResult` mapping is never exposed. Removing the method makes `PermissionResult.cs` fully dead (only other refs are the test fakes' return values) | Grep: interface + impl + 5 fake declarations, no invocation | None. Touches 5 test files (fake methods) | HIGH | ~75 |
| 8 | `MProject.Application/Interface/Identity/IAuthorizationCacheInvalidator.cs:12` + `AuthorizationCacheInvalidator.cs:32` + 5 test-fake impls | `InvalidatePermissionMap` | Zero callers. The `"authz_perm_version"` cache key it bumps *is* read (`AuthorizationService.cs:132`) but `GetOrCreateVersion` creates it on demand — nothing ever needs the bump. Permissions only change via seeder at startup | Grep: interface + impl + 5 empty fakes, no invocation | None; cache version self-initializes | MEDIUM | ~12 |
| 9 | `MProject.Application/Interface/Software/ISoftwareVersionService.cs:13` | `GenerateNextVersionCodeAsync` — unnecessary public surface | Only called by `SoftwareVersionService` itself (lines 39, 253). Belongs private, not on the interface | Grep: interface + 2 self-calls in impl | None | MEDIUM | 1 |
| 10 | `MProject.Api/MProject.Api.http` | whole file | VS template scratch file: targets `GET /weatherforecast`, an endpoint that does not exist | File content is the untouched project template | None (git-tracked, delete + commit) | MEDIUM | 6 |
| 11 | `MProject.Application/Services/Assets/AgentReleaseService.cs:177` | private `ComputeSha256Async(string path)` | Duplicate helper: reimplements `MProject.Domain/Utils/HashUtils.ComputeSha256Async(Stream)` (same algorithm, same lowercase-hex output) with a `FileStream` wrapper | Both produce identical hex; HashUtils already streams with buffer | Consolidation, not deletion — open the `FileStream` at call site and pass to `HashUtils` | MEDIUM | ~7 net |

### Not code, but leftover artifacts

| Item | State | Action |
|------|-------|--------|
| `MProjectBackend/Backup/MProject.slnx` | Untracked stale solution-file backup (only file in the folder) | Delete folder from disk |
| `MProjectBackend/**/.verify-build/`, `MProjectBackend/.vs/` | Untracked build/IDE output (~full publish tree under `MProject.Api/.verify-build/`) | Delete from disk; confirm `.gitignore` (currently modified, uncommitted) covers `.verify-build/` |
| Git index casing | Git tracks `Mproject.Domain/MProject.Domain.csproj` (lowercase *p*) while the folder on disk is `MProject.Domain/` | Cosmetic; fix with `git mv` when convenient |
| `AgentModels.cs:208-213` | 6 consecutive blank lines where code was removed | Cosmetic (LOW — ignore per review rule, listed for completeness) |

---

## Safe to delete now

All verified with two independent greps; no reflection/DI/EF/wire exposure:

1. `RepositoryAction.cs` — whole file (#1)
2. `AssignmentEffect.cs` — whole file (#2)
3. `CreateSoftwarePackageResponse` class (#3)
4. `UpdateFileContentRequest` class (#4)
5. `DeleteFolderAsync` — interface line, both impls (+ `RemoveBatchAsync`/`BatchDeleteSize` cascade), 8 test fakes (#5)
6. `GetLatestReleasedVersionAsync` — interface line + impl (#6)
7. `CheckPermissionDetailedAsync` — interface line + impl + `PermissionResult.cs` whole file + 5 test fakes (#7)
8. `InvalidatePermissionMap` — interface line + impl + 5 test fakes (#8)
9. `GenerateNextVersionCodeAsync` — remove from interface, make method private (#9)
10. `MProject.Api.http` (#10)

After deletion: rebuild solution + run the 500+ tests (deleting fake members in test files is mechanical but must compile).

## Needs human confirmation

| Item | Why confirmation is needed |
|------|---------------------------|
| `AppPermissions.ApproveSoftwareAssignment` (`AppPermissions.cs:39`, `"software.assignment.approve"`) | **Dynamic usage:** `AppDbSeeder.cs:54` reflects over all `AppPermissions` fields and seeds each as a DB `Permission` row — but no code, FE, or approval handler ever *checks* this code (approval flow uses `ActOnApproval`). Removing it triggers the seeder's stale-permission cleanup (warns at `AppDbSeeder.cs:254` if rows still referenced). Confirm the permission isn't reserved for a planned per-target approval check, then remove constant + let seeder clean the row. |
| `InstallationJobType.Rollback` (enum member, value 3) | Backend never creates Rollback jobs (rollback watchdog re-pins + enqueues Install). FE has a badge mapping for it; the agent reads `JobType` as a string and only special-cases `"Uninstall"`, so it is tolerant. Contract-reserved value — remove only together with the FE mapping, or keep deliberately. |
| `ComputerStatus.Registered` (enum member, value 1) | Never produced by current code (`ComputerStatusMapper.DeriveLegacy` emits Unknown/Online/Offline/Updating/Error only), but old DB rows may still hold value 1 and FE renders it in 3 components. Data-dependent — check prod DB before removal. |
| `ResourceStatus.Archived` (enum member, value 3) | Never read or written anywhere; trailing explicit value so removal doesn't renumber. Wire-visible enum → confirm no external consumer, then safe. |
| `IApprovalNotificationService` + `NoOpApprovalNotificationService` | Working seam, not dead: called by `ApprovalService` on submit/decision, and tests substitute `CountingNotificationService`. It is future-proofing (real notifications not implemented). Keep if email/chat notification is on the roadmap; otherwise it's 35 LOC + an interface for a no-op. |
| Untracked artifacts (`Backup/`, `.vs/`, `.verify-build/`) | Local-disk deletions — not visible in git; do when convenient. |

## Explicitly checked and NOT flagged (clean)

- **No commented-out blocks > 10 lines, no `/* */` blocks > 10 lines, no `#if`/`#pragma` directives, no TODO/FIXME/HACK anywhere** in the backend.
- **All 11 options classes** (`Domain/Options/*`, `Infrastructure/Options/*`) are bound in `Program.cs` and every property is read. `TusUpload`/`Metrics` config sections verified read (`TusTempCleanupService.cs:34`, `Program.cs:402`).
- **All 41 entities** are mapped as DbSets; `ApprovalStepSnapshot` and `StationUpdateWindow` are JSON-column value objects with `ValueComparer`s (`DBContext.cs:591,724`) — alive.
- **All extension methods** (`BlobReferenceExtensions`, `DbContextTransactionExtensions`, `DbExceptionExtensions`, `StorageServiceExtensions`) have call sites.
- **All DI registrations resolve** — the dual `IStorageService` registration is conditional on `Storage:Provider` (`Program.cs:103-120`), both branches reachable.
- **Controllers, attributes, seeders, hosted services** — zero-ref hits from the type scan are all dynamic-usage (ASP.NET routing, `[RequirePermission]` without suffix, xUnit discovery); each was individually excluded with evidence, not assumed.
- **Single-implementation interfaces** (`IAdminService`, `ITeamService`, `IUserService`, …): this is the consistent controller→interface→service DI convention across the entire codebase; per review rule ("review against existing architecture"), not flagged. Tests exercise concrete classes directly, and several interfaces (IStorageService, IPermissionService, IApprovalNotificationService, IAuthorizedResourceQueryService, …) do carry test fakes.

## Estimated total LOC removable

- **Safe-now list:** ≈ **225 LOC** (~165 production + ~60 test-fake/lines across 10 test files), including 4 whole files (`RepositoryAction.cs`, `AssignmentEffect.cs`, `PermissionResult.cs`, `MProject.Api.http`).
- **If human-confirm items approved:** + ~50 LOC (enum members, permission constant, no-op notification service) → ≈ **275 LOC** total.
- Plus ~200 MB of untracked local build/IDE artifacts (`.verify-build/`, `.vs/`, `Backup/`).

---

## Tóm tắt (tiếng Việt)

- Đã quét toàn bộ 312 file `.cs` backend (516 type), build baseline PASS 0 lỗi; mọi ứng viên đều verify 2 lần bằng grep độc lập + kiểm tra rủi ro dynamic (reflection, DI, EF, JSON wire).
- Codebase rất sạch: **không có** code comment-out, `#if`, TODO bỏ hoang; options/config đều được đọc; entity đều được map.
- Tìm thấy **~225 LOC chết xóa được ngay**: 2 file constants/enum không ai dùng (`RepositoryAction`, `AssignmentEffect`), 2 DTO thừa (`CreateSoftwarePackageResponse`, `UpdateFileContentRequest`), 4 method không ai gọi (`DeleteFolderAsync`, `GetLatestReleasedVersionAsync`, `CheckPermissionDetailedAsync` kéo theo cả file `PermissionResult.cs`, `InvalidatePermissionMap`), 1 method nên chuyển private (`GenerateNextVersionCodeAsync`), file scratch `MProject.Api.http` trỏ endpoint không tồn tại, 1 helper SHA-256 trùng lặp trong `AgentReleaseService`.
- **Cần anh xác nhận trước khi xóa**: permission `software.assignment.approve` (được seed vào DB qua reflection nhưng không nơi nào check), 3 enum member không bao giờ được sinh ra (`Rollback`, `Registered`, `Archived` — dính wire contract với FE/DB), và cặp `IApprovalNotificationService`/NoOp (seam chờ tính năng notification).
- Rác ngoài git: folder `Backup/`, `.vs/`, `.verify-build/` (untracked, xóa tay); git index đang track `Mproject.Domain/` sai hoa-thường so với thư mục trên đĩa.
- **CHƯA sửa gì** — report-only đúng yêu cầu; bước tiếp theo đề xuất: xóa nhóm "safe now", rebuild + chạy full test suite.
