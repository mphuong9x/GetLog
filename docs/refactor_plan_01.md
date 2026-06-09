# Refactor Plan 01 — MProject Backend & Agent

> **Ngày tạo:** 2026-06-07
> **Tổng hợp từ:**
> - Senior code review session 2026-06-07 (Agent + Backend, focus: auth, supervisor, cache, job lifecycle).
> - `docs/review_backend_20260606.md` (Backend deep review: authz, scaling, compliance).
> **Đối tượng thực thi:** AI agent (Claude Code) hoặc dev human. Mọi task đều phải self-contained — đường dẫn file + dòng đầy đủ.

---

## 0.1 Changelog

- **2026-06-08 rev2** (senior review trước khi implement):
  - `S1-04` → chỉnh scope: code hiện **đã có** `[EnableRateLimiting("auth")]` cho announce/enroll; việc cần làm là audit/tune policy, không phải thêm từ số 0.
  - `S1-16` → thêm caveat nghiệp vụ: Git public repository hiện có anonymous read path; không được strict 401 blanket nếu vẫn muốn hỗ trợ public clone/pull.
  - `S1-17` (mới): `GitAPIRepositoriesController` thiếu `[Authorize]`, riêng endpoint search user theo repo có nguy cơ leak user/email vì không check `CurrentUserId`/access.
  - `S2-05` → chỉnh fix concurrency: không dựa vào read-only concurrency token của `SoftwareVersion`; phải lock/touch row hoặc dùng conditional update trong transaction.
  - Làm rõ guardrail cho `S0-01`, `S1-02`, `S1-10`, `S1-13`, `S2-08`, `S2-09`, `S2-11` để tránh fix đúng kỹ thuật nhưng lệch nghiệp vụ vận hành.

- **2026-06-07 rev1** (từ review feedback của owner):
  - `S0-02` (SystemUser Guid.Empty) → hạ severity **S0 → S1**, đổi ID thành `S1-15`. Lý do: chỉ là audit smell + theoretical risk, chưa chứng minh được auth bypass.
  - `S0-03` (GitBasicAuthFilter) → hạ severity **S0 → S1**, đổi ID thành `S1-16`. Lý do: controllers hiện tại đều check `CurrentUserId` → 401. Rủi ro là fail-open cho endpoint tương lai, không phải exploit hiện hữu.
  - `S1-04` (MAC reuse) → **viết lại**. Phân tích sai trước đây: DB đã có global unique index trên `Computer.MacAddress` (DBContext.cs:144) → reuse đã bị chặn. Threat hijack không tồn tại. Tách thành:
    - `S1-04` (giữ): chỉ còn rate-limit `/agent/v1/announce` (defense in depth).
    - `S2-19` (mới): xử lý lỗi UX khi DB unique index chặn + document reuse policy.
  - `S1-11` (cache version `"v1"`) → **làm rõ wording**. "Bypass mãi mãi" là sai; stale window bounded 3 phút (decision TTL). Vấn đề thực: invalidate mất hiệu lực sau 1h khi version-cache reset về `"v1"`. Fix vẫn giữ, severity vẫn S1.

---

## 0. Tinh thần & Nguyên tắc

1. **Severity > Impact > Effort.** S0/P0 phải làm trước, không có ngoại lệ. S3/P2/P3 gom vào sprint "cleanup", không xen vào sprint chức năng.
2. **Test reproduce trước khi fix** cho mọi finding liên quan đến race/transaction/cache. Nếu không reproduce được → ghi vào *Open Questions* thay vì fix mù.
3. **Đừng over-engineer khi fix bug** — bug fix giữ scope nhỏ, refactor lớn tách thành PR riêng.
4. **Mỗi PR tương đương 1 ô trong checklist.** Khi mở PR phải link tới task ID (ví dụ `S0-01`).
5. **Phải hỏi trước khi implement** với các finding có dấu `[NEED INPUT]` — câu hỏi tập trung ở mục [§7](#7-câu-hỏi-cần-làm-rõ-trước-khi-implement).
6. **Migration & data backfill** đi cùng PR, không được tách (tránh DB lệch state với code).
7. **Cập nhật `docs/agent_note.md`** khi hoàn thành tasks liên quan tới Agent supervisor / lifecycle (M4 đã DONE — cần sync ngay).

---

## 1. Phân loại Severity & Phase

| Severity | Mô tả | Phase |
|---|---|---|
| **S0** | Critical: exploitable, data loss, broken core workflow | Phase 1 |
| **S1** | High: incorrect authorization on key endpoint, race condition with real impact, hidden contract break | Phase 1 → 2 |
| **S2** | Medium: correctness edge case, perf hotpath, maintenance debt | Phase 2 → 3 |
| **S3** | Low: cleanup, doc lag, minor UX | Phase 3 (gom cleanup) |
| **F**  | Feature: tính năng/roadmap | Phase 3 → 4 |
| **A**  | Architecture/scale: cần làm trước khi HA/multi-tenant | Phase 2 → 3 |

**Roadmap tổng:**
- **Phase 1 (1–2 sprint):** S0 + S1 critical/auth/supervisor. Mục tiêu: prod-readiness baseline.
- **Phase 2 (3–4 sprint):** S1 còn lại + S2 hot + A nền tảng (outbox, recursive hierarchy, Redis cache invalidation).
- **Phase 3 (continuous):** S2 còn lại, S3 cleanup, F-ngắn-hạn (canary, maintenance window).
- **Phase 4 (medium-term):** F compliance, multi-tenant, observability.

---

## 2. Phase 1 — Critical & Auth/Supervisor (S0 + S1 priority)

### 2.1 Authentication & Token

#### `S0-01` Refresh-token rotation race
- **File:** [MProjectBackend/MProject.Application/Services/Identity/RefreshTokenService.cs:73](MProjectBackend/MProject.Application/Services/Identity/RefreshTokenService.cs:73)
- **Vấn đề:** 2 request đồng thời với cùng refresh token đều pass `RotatedAt == null` check → mint 2 child token. Token family integrity vỡ; chỉ detect ở lần rotate kế.
- **Fix:**
  - Đổi `existing.RotatedAt = now` thành conditional UPDATE: `WHERE Id = @id AND RotatedAt IS NULL` (qua `ExecuteUpdateAsync` hoặc raw SQL).
  - Nếu rows-affected = 0 → throw `InvalidCredentialsException("Refresh token race detected")` + revoke family.
  - Hoặc thêm `[ConcurrencyCheck]` trên `RefreshToken.RotatedAt` (EF tự gắn vào WHERE).
- **Lưu ý vận hành:** revoke cả token family là posture bảo mật tốt, nhưng sẽ logout user thật nếu frontend/API gateway gửi song song 2 refresh request. FE nên single-flight refresh + retry rõ ràng để tránh UX "tự logout" khi race hợp lệ.
- **Test:** integration test với 2 task song song cùng gọi `RotateAsync(sameToken)` → chỉ 1 task trả token mới, task kia throw + family revoked.
- **Effort:** S (1 ngày).
- **PR:** `fix(auth): atomic refresh-token rotation [S0-01]`

> **Lưu ý revision:** `S0-02` và `S0-03` ban đầu được phân loại S0 — đã hạ xuống **S1** theo feedback (xem [§0.1 Changelog](#01-changelog)). Định nghĩa task hiện ở [§2.3 — `S1-15`](#s1-15-systemusersselfannounce--guidempty-gây-nhiễu-audit) và [`S1-16`](#s1-16-gitbasicauthfilter-swallow-exception--không-401).

---

### 2.2 Job Lifecycle & Heartbeat

#### `S1-01` Heartbeat upsert + ComputerRuntimeStatus không nằm trong transaction
- **File:** [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:316-405](MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:316)
- **Vấn đề:** Agent `LastHeartbeatAt`, `Computer.LastSeenAt`, `ComputerRuntimeStatus` được persist qua 3 SaveChanges/ExecuteUpdate riêng. DB transient lỗi giữa các bước → partial state. Liveness watchdog có thể flip Offline sai trong cửa sổ đó.
- **Fix:** Bọc toàn bộ `RecordHeartbeatAsync` trong `_context.ExecuteInTransactionAsync(...)`. Tuân thủ pattern đã dùng ở `InstallationJobService.CompleteAsync`.
- **Test:** integration test inject transient DB error giữa Step 1 và Step 2 → kiểm 0 record committed.
- **Effort:** S.

#### `S1-02` `EnsureTransition` không cho phép → Cancelled từ Downloading/Installing
- **File:** [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:571-586](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:571), [CancelJobAsync:420-434](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:420)
- **Vấn đề:** `CancelJobAsync` bypass `EnsureTransition`. Agent vừa download xong (10 phút) → `CompleteAsync(Completed)` → throw vì hiện status đã là `Cancelled`. Cũng race với `PinVersionAsync` đổi version đang Downloading (P0-04 cũ).
- **Fix:**
  1. `EnsureTransition`: cho phép `Pending|Downloading|Installing → Cancelled`.
  2. `CompleteAsync`: idempotent — nếu job đã terminal (`Completed|Failed|Cancelled`), log info + return success, **không đổi status, không gọi `MarkInstalledAsync`, không flip `Computer.OperationalStatus`**. Nếu payload terminal mới khác status hiện tại, log warning/audit để trace stale agent callback.
  3. Server-side push "your job was cancelled" qua `AgentHeartbeatResponse.Commands` (mới — xem F-03).
- **Test:** unit test cho mọi transition; integration test cancel-during-download.
- **Effort:** M.

#### `S1-03` `PinVersionAsync` chỉ cancel Pending — Downloading/Installing tiếp tục → cài cả 2 version
- **File:** [MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:202-222](MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:202)
- **Vấn đề:** Pin đổi từ v1 sang v2; agent đang Download v1 → tiếp tục. v2 sinh job mới → có thể cài cả 2, hoặc cài v1 đè v2.
- **Fix:** Dùng `CancelActiveJobsForPackageAsync` (đã có sẵn ở `RemoveAssignmentAsync`) cho cả `Downloading|Installing`. Agent (sau khi merge `F-03` server-pushed commands) sẽ abort sớm; nếu không, watchdog timeout.
- **Test:** integration test "pin v1 → start download → pin v2 → agent must end up with v2 only".
- **Effort:** S.

#### `S1-04` Tune rate-limit `/agent/v1/announce` — defense in depth cho installer-token
- **File:** [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:86-198](MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:86)
- **Vấn đề:** Installer-token là single shared secret distributed với installer image — risk leak cao. Code hiện đã có `[EnableRateLimiting("auth")]` ở [AgentController.cs:37](MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:37) và policy `auth` 5 req/phút ở [Program.cs:130-136](MProjectBackend/MProject.Api/Program.cs:130). Tuy nhiên policy này đang dùng chung announce/enroll và cần audit partition key; nếu không partition per-IP/per-token đúng, spam/leak token vẫn khó kiểm soát, còn batch enroll hợp lệ có thể bị throttle sai.
- **Lưu ý revision:** Threat "hijack qua MAC reuse sau soft-delete" mình nêu ban đầu **không tồn tại** — DB index `HasIndex(x => x.MacAddress).IsUnique()` ở [DBContext.cs:144](MProjectBackend/MProject.Infrastructure/DBContext.cs:144) là global, chặn mọi re-insert. Phần xử lý UX khi DB chặn được tách ra task riêng [`S2-19`](#42-short-term-features) (Phase 3).
- **Fix:**
  - Review policy hiện tại, tách policy cho `/announce` và `/enroll` nếu cần; partition theo IP + installer/enrollment token hash. Production có thể chuyển sang Redis token bucket khi có A-01.
  - Định nghĩa ngưỡng vận hành rõ: announce có thể thấp hơn enroll; enroll phải tính tới batch rollout nhiều PC cùng NAT.
  - Audit log mỗi announce thành công (`Computer.AnnouncedAt`, `CreatedBy = SystemUser`).
  - Alert admin (qua outbox khi có A-03) khi có announce bất thường (vd > 3/giờ).
- **Effort:** S.
- **PR:** `fix(agent): tune announce rate-limit policy [S1-04]`

#### `S1-05` `ProcessSupervisor.HandleDeadOnRecoveryAsync` mặc định `HostShuttingDown` → không restart sau reboot
- **File:** [MProjectAgent/Services/ProcessSupervisor.cs:118-140](MProjectAgent/Services/ProcessSupervisor.cs:118), [ShouldRestart:249-250](MProjectAgent/Services/ProcessSupervisor.cs:249)
- **Vấn đề:** Sau reboot PC, recovery thấy PID chết → mark `HostShuttingDown` → `ShouldRestart` false → test app không bật lại. Đây là use case chính của factory PC.
- **Fix:**
  - Khi recovery ở startup, nếu Mode = LongRunning và không có dấu hiệu maintenance/crash-loop → **luôn relaunch**. Pattern "desired-state convergence".
  - Thêm field `SupervisedProcessState.LastAgentShutdownAt` set khi agent stop cleanly. Khi recovery, nếu `LastAgentShutdownAt + 60s > ExitedAt` → trust HostShuttingDown; ngược lại → relaunch.
- **Test:** kill -9 agent → reboot mô phỏng → kiểm test app được supervisor restart.
- **Effort:** M.

#### `S1-06` `ExitClassifier` map exit 0 → `WindowClosed` luôn — dashboard hiện nhầm cho RunOnce job
- **File:** [MProjectAgent/Models/ProcessSupervisorModels.cs:53-58](MProjectAgent/Models/ProcessSupervisorModels.cs:53)
- **Fix:** `Classify` nhận thêm `EntryPointMode`. RunOnce + exit 0 → `NormalExit`. LongRunning + exit 0 → `WindowClosed`.
- **Test:** mở rộng `ExitClassifierTests` (đã có) thêm 2 case theo mode.
- **Effort:** S.

#### `S1-07` `RuntimeStateStore` corrupted file swallow → mất state sau crash
- **File:** [MProjectAgent/Storage/RuntimeStateStore.cs:27-41](MProjectAgent/Storage/RuntimeStateStore.cs:27)
- **Fix:**
  - Log error + đếm metric.
  - Giữ backup `supervised.json.bak` (rename trước khi overwrite).
  - Khi load chính fail, thử load backup.
  - `FileStream` thêm `FileOptions.WriteThrough` để flush thật trước rename.
- **Test:** `RuntimeStateStoreTests` (đã có) thêm case backup-recovery.
- **Effort:** S.

#### `S1-08` `BlobCacheService` eviction race với `IncrementRefAsync`
- **File:** [MProjectAgent/Services/BlobCacheService.cs:175-190](MProjectAgent/Services/BlobCacheService.cs:175), [CacheIndex.cs:156-163](MProjectAgent/Storage/CacheIndex.cs:156)
- **Fix:** Trong `CacheIndex.DeleteBlobAsync`, dùng conditional: `DELETE FROM BlobEntries WHERE Sha256 = $sha AND RefCount = 0`. Trả về rows-affected. `TryEvictAsync` chỉ delete file nếu rows-affected > 0.
- **Test:** unit test parallel — 1 task evict, 1 task increment cùng hash → expect chỉ 1 thắng (hoặc both succeed nhưng file/row consistent).
- **Effort:** S.

#### `S1-09` Backend `BuildManifestJobsAsync` N+1 presign — manifest lớn ≥ 5s
- **File:** [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:476-529](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:476)
- **Fix:**
  - Gom `files.Where(NeedsDownload).Select(f => f.Blob.StoragePath)` → `Task.WhenAll` với SemaphoreSlim limit 8-16.
  - Cache presign URL theo storagePath (IMemoryCache, TTL = expiry/2).
- **Test:** load test 50 PC poll cùng lúc, manifest 100 file → p95 < 1s.
- **Effort:** M.

---

### 2.3 Authorization & Cache

#### `S1-10` `AuthService.GetMeAsync` thiếu logic ACL Deny
- **File:** [MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:201-216](MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:201)
- **Vấn đề:** `/me` trả `permissions = rolePerms + aclAllowPerms`. Không trừ ACL Deny → UI hiển thị menu nhưng click 403.
- **Fix (preferred):** `/me` chỉ trả **role-based coarse permissions** (không có effect-level). Mọi check thực vẫn đi qua `IPermissionService`. UI chỉ dùng `/me` cho navigation hint, render fail-soft.
- **Lưu ý frontend contract:** FE hiện dùng `me.permissions` cho navigation/route guard. Nếu đổi sang role-only coarse permissions, phải cập nhật access-control/frontend wording hoặc thêm field riêng kiểu `navigationPermissions`; không được để UI hiểu nhầm đây là authorization truth.
- **Fix (alternative):** tính chính xác giống `PermissionEvaluator` (deny override allow per-resource). Phức tạp & ít người dùng tận dụng.
- **[NEED INPUT]:** chọn approach nào? Mặc định khuyến nghị (preferred) — đơn giản, đúng nhất.
- **Effort:** M.

#### `S1-11` Decision cache key thiếu `resourceHierarchyVersion` + version `"v1"` hardcode
- **File:** [MProjectBackend/MProject.Application/Services/Identity/AuthorizationService.cs:58-67](MProjectBackend/MProject.Application/Services/Identity/AuthorizationService.cs:58), [AuthorizationCacheInvalidator.cs:26-35](MProjectBackend/MProject.Application/Services/Identity/AuthorizationCacheInvalidator.cs:26)
- **Vấn đề (đã làm rõ rev1):**
  1. `GetOrCreate("authz_user_version_*", ...)` luôn return literal `"v1"`. Sau `InvalidateUser` set GUID mới với TTL 1h → trong 1h decision keys isolated. Khi GUID expire, factory recreate `"v1"` → invalidation **mất hiệu lực sau 1h** (không phải "bypass mãi mãi" — decision cache vẫn bounded 3 phút theo `MaxDecisionCacheTtl`).
  2. Move resource → invalidate scope cache, nhưng decision cache vẫn dùng key cũ vì `userVersion`/`permVersion` không thay đổi → stale tối đa 3 phút sau move.
- **Impact thực tế:**
  - Worst case: 3 phút stale decision (bounded bởi TTL).
  - Worst case: `InvalidateUser` chỉ đảm bảo 1h refresh đầu; sau đó user phải đợi hết TTL 3 phút mỗi lần.
  - Không phải critical, nhưng làm vô hiệu hóa toàn bộ ý nghĩa của Invalidate*.
- **Fix:**
  - `GetOrCreate` factory trả `Guid.NewGuid().ToString("N")` thay vì `"v1"`. Lý do: mỗi lần factory chạy đều tạo version mới, kết hợp với `InvalidateUser` set GUID mới → cache key luôn fresh sau invalidate.
  - `ComputerService.MoveToStationAsync` gọi `InvalidatePermissionMap()` (đơn giản, hơi nặng tay nhưng đúng).
  - Hoặc thêm `resource_version_{resourceId}` vào cache key (tinh hơn).
- **Test:** test invalidation hit/miss, test move + access.
- **Effort:** S.

#### `S1-12` `ResourceLookupService` cache 30s không invalidate khi entity soft-delete
- **File:** [MProjectBackend/MProject.Application/Services/Identity/ResourceLookupService.cs:12](MProjectBackend/MProject.Application/Services/Identity/ResourceLookupService.cs:12)
- **Fix:** giảm TTL xuống 5s, hoặc inject `IAuthorizationCacheInvalidator` để xóa key trong các service delete.
- **Effort:** S.

#### `S1-13` Approval policy bypass khi admin tắt `IsActive` → target kẹt `Draft` vĩnh viễn
- **File:** [MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:57-66](MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:57)
- **[NEED INPUT]:** muốn fallback "auto-approve with audit warning" hay "block & alert admin"?
- **Khuyến nghị:** mặc định **block & alert admin**. Auto-approve là compliance-risk, chỉ nên bật bằng config rõ ràng trong môi trường không yêu cầu GMP/IATF/FDA hoặc flow nội bộ đã chấp nhận rủi ro.
- **Fix (auto-approve path):** thêm `ApprovalSettings.AutoApproveWhenNoActivePolicy` (default false). True → tạo `ApprovalRequest.Status = AutoApproved` + ghi audit `ApprovalAction.Auto`.
- **Fix (block path):** giữ throw, nhưng thêm health-check endpoint `/admin/approvals/policy-coverage` báo target nào không có policy active.
- **Effort:** M.

#### `S1-15` `SystemUsers.SelfAnnounce = Guid.Empty` gây nhiễu audit
- **File:** [MProjectBackend/MProject.Application/Constants/SystemUsers.cs:7](MProjectBackend/MProject.Application/Constants/SystemUsers.cs:7)
- **Vấn đề (đã hạ severity rev1):** Mọi Computer/Resource từ self-announce có `CreatedBy = Guid.Empty`. Audit không trace được nguồn gốc thật. Có thêm theoretical risk: nếu bất cứ chỗ nào có check `CreatedBy == userId` để ủy quyền và có user nào đó (qua bug) có Id Empty, sẽ thủng — hiện chưa chứng minh được path cụ thể.
- **Fix:**
  - Thêm `UserStatus.System` enum value mới.
  - DB seeder insert user `__system_selfannounce__` với status System.
  - `SystemUsers.SelfAnnounce = <stable Guid>` (hardcode).
  - `SubjectResolver.IsUserActive` filter `Status != System` để user này không bao giờ resolve làm subject.
  - Migration data backfill: record có `CreatedBy = Guid.Empty` → SystemUser Id.
- **[NEED INPUT]:** ID system user nên là `00000000-0000-0000-0000-000000000001` cố định, hay GUID random ghi vào file `.env` lúc deploy?
- **Effort:** M (2 ngày, có migration).
- **PR:** `feat(auth): introduce SystemUser for self-announce audit trail [S1-15]`

#### `S1-16` `GitBasicAuthFilter` swallow exception — fail-open trap cho endpoint tương lai
- **File:** [MProjectBackend/MProject.Api/Filters/GitBasicAuthFilter.cs:21-52](MProjectBackend/MProject.Api/Filters/GitBasicAuthFilter.cs:21)
- **Vấn đề (đã hạ severity rev1):** Filter catch-all + không trả 401, chỉ skip set `CurrentUserId`. Controllers Git hiện tại đều check `CurrentUserId` và trả 401 → **không có exploit hiện hữu**. Rủi ro là fail-open trap: bất kỳ endpoint Git mới nào quên check sẽ không-auth-by-default.
- **Fix:**
  - Không strict 401 blanket cho mọi request nếu vẫn giữ tính năng clone/pull public repository. `GitRepositoryService.HasAccessAsync(..., requireWrite: false)` hiện cho phép `RepoVisibility.Public` anonymous read ở [GitRepositoryService.cs:330](MProjectBackend/MProject.Application/Services/GitRepositoryService.cs:330) và [GitRepositoryService.cs:475](MProjectBackend/MProject.Application/Services/GitRepositoryService.cs:475).
  - Tách rõ contract: endpoint Git read public được phép anonymous; Git write/private read bắt buộc Basic Auth hợp lệ và trả 401 ngay khi auth fail.
  - Lý tưởng: chuyển sang `AuthenticationHandler` đầy đủ + `[Authorize]` standard, đồng nhất với `AgentAuthenticationHandler`.
  - Audit endpoint Git hiện có (chạy grep tìm tất cả `[GitBasicAuth]`) để xác nhận không có endpoint cố ý fail-open.
- **Effort:** M (2 ngày, có audit endpoints).
- **PR:** `fix(auth): harden git basic-auth without breaking public read [S1-16]`

#### `S1-17` `GitAPIRepositoriesController` thiếu `[Authorize]` + search user không check access
- **File:** [MProjectBackend/MProject.Api/Controllers/GitAPIRepositoriesController.cs:7](MProjectBackend/MProject.Api/Controllers/GitAPIRepositoriesController.cs:7), [GitAPIRepositoriesController.cs:56-58](MProjectBackend/MProject.Api/Controllers/GitAPIRepositoriesController.cs:56), [GitRepositoryService.cs:153-174](MProjectBackend/MProject.Application/Services/GitRepositoryService.cs:153)
- **Vấn đề:** Controller route `/api/repository` không có `[Authorize]`. Nhiều action gọi `CurrentUserId`; request anonymous sẽ throw `UnauthorizedAccessException`, bị `GlobalExceptionHandler` map thành 403 ở [GlobalExceptionHandler.cs:102](MProjectBackend/MProject.Api/Middleware/GlobalExceptionHandler.cs:102) thay vì 401 chuẩn. Riêng `GetUsersForRepo(Guid id, query)` gọi service bằng `repoId + query` mà không truyền `CurrentUserId` và không check `HasAccessAsync`, có nguy cơ leak danh sách user/email cho anonymous hoặc user không có quyền repo.
- **Fix:**
  - Thêm `[Authorize]` ở controller hoặc action-level cho toàn bộ `/api/repository` admin/API surface.
  - `GetUsersForRepo` phải đọc `CurrentUserId`, check `HasAccessAsync(id, CurrentUserId, requireWrite: true)` hoặc permission repo-member-manage trước khi trả user suggestions.
  - Service `GetUsersForRepoAsync` nên nhận `currentUserId`/authorization context, không expose query helper bỏ qua auth ở layer controller.
- **Test:** integration test anonymous gọi `/api/repository` → 401; user không có quyền gọi search member → 403; owner/admin repo gọi search → 200 và không trả owner/member đã có.
- **Effort:** S.
- **PR:** `fix(auth): require authorization on repository admin API [S1-17]`

---

## 3. Phase 2 — High/Medium Auth, Persistence, Foundations (S1 còn + S2 hot + A foundations)

### 3.1 Authorization debt

#### `S1-14` `EnsureRolePermissionsGrantableAsync` N+1 query
- **File:** [MProjectBackend/MProject.Application/Services/Identity/AuthorizationMutationService.cs:265-282](MProjectBackend/MProject.Application/Services/Identity/AuthorizationMutationService.cs:265)
- **Fix:** viết 1 query gộp lấy all perms actor có cho scope; HashSet.Contains kiểm tra. Hoặc cache per-request `IAuthorizationService` (Scoped service đã có lifetime đúng — dùng `ConditionalWeakTable` hoặc dictionary trong service).
- **Effort:** M.

#### `S2-01` `RbacGrantQueryService` priority vs distance semantics
- **File:** [MProjectBackend/MProject.Application/Authorization/RbacGrantQueryService.cs:38](MProjectBackend/MProject.Application/Authorization/RbacGrantQueryService.cs:38), [PermissionEvaluator.cs:70-77](MProjectBackend/MProject.Application/Authorization/PermissionEvaluator.cs:70)
- **[NEED INPUT]:** ACL Deny global priority 100 có nên thắng ACL Allow scope priority 50? Hiện code: có (priority thắng).
- **Action:**
  - Document chính thức trong `docs/authorization-semantics.md`.
  - Thêm 5-7 integration tests phủ matrix `(allow/deny) × (user/team) × (global/scope) × (priority high/low)`.

#### `S2-02` `software.manage` permission quá coarse
- **File:** [AppPermissions.cs](MProjectBackend/MProject.Application/Constants/AppPermissions.cs)
- **Fix:** tách thành:
  - `software.package.manage` (CRUD package)
  - `software.version.draft` (tạo/sửa draft)
  - `software.version.release` (cần approval — gắn `ApprovalPolicy` mặc định)
  - `software.assignment.manage` (assign + activate + pin)
- **Migration:** seed thêm permission mới, mapping role hiện có với `software.manage` → gán cả 4 permission mới.
- **Effort:** M.

#### `S2-03` User `Pending` được assign Viewer ngay khi register
- **File:** [AuthService.cs:67](MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:67)
- **[NEED INPUT]:** approach? (a) chỉ assign khi admin approve `Pending → Active`; (b) RoleAssignment với `StartTime = null` + admin set khi approve.
- **Effort:** S.

#### `S2-04` `RegisterAsync` không yêu cầu email — không có reset password
- **File:** [AuthService.cs:60](MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:60)
- **Decision:** không thêm email reset/self-service trong scope hiện tại. Factory dùng reset password manual bởi admin.
- **Implementation:** giữ public `RegisterAsync` không yêu cầu email; admin có thể reset qua `PUT /api/users/{id}/password` và cập nhật email qua user profile endpoint.

---

### 3.2 Persistence / Concurrency

#### `S2-05` `RegisterUploadedFileAsync` không lock version status trong transaction
- **File:** [SoftwareFileService.cs:47-89](MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:47)
- **Fix:** bọc trong `ExecuteInTransactionAsync`, nhưng **không dựa vào việc read `SoftwareVersion.Version` đơn thuần** vì nếu không update/touch row thì concurrency token không tham gia bảo vệ. Chọn một trong các hướng:
  - Postgres row lock (`SELECT ... FOR UPDATE`) trên `SoftwareVersion` trước khi check status rồi insert file.
  - Conditional update/touch trong transaction (`WHERE Id = @id AND Status = Draft AND Version = @version`) để bump concurrency token trước khi insert.
  - Hoặc chuyển invariant sang DB constraint/trigger nếu muốn chặn file attach sau release ở tầng dữ liệu.
- **Effort:** S.

#### `S2-06` `AnnounceAsync` cho admin pre-register dùng InMemory provider không có transaction
- **File:** [AgentService.cs:182](MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:182)
- **Action:** chuẩn hóa bằng `ExecuteInTransactionAsync` ngay cả khi PG implicit transaction đã đủ — đồng nhất code path với InMemory test.
- **Effort:** S.

#### `S2-07` `TusUploadHandler.OnFileCompleteAsync` ghi rõ semantics rollback
- **File:** [MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:198-238](MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:198)
- **Implementation:** comment chính thức "best-effort rollback; BlobGc cleanup orphan trong grace period" + metrics `mproject_tus_blob_rollback_attempts` / `mproject_tus_blob_rollback_orphans` để theo dõi orphan rate.

#### `S2-08` `ExecuteUpdateAsync` bypass change tracker → entity stale
- **File:** nhiều chỗ trong `InstallationJobService`, `ComputerLivenessWatchdogService`, `AgentService`.
- **Action:** mỗi block `ExecuteUpdateAsync` xong, nếu service đó còn đọc cùng entity → reload entity hoặc gọi `_context.ChangeTracker.Clear()` có chủ đích. Không blanket clear trong transaction khi context đang có pending tracked changes, vì có thể drop/sai lệch thay đổi chưa lưu. Audit & document quy ước.

#### `S2-09` Agent heartbeat DTO thiếu Hostname/LastError so với backend contract
- **File:** Agent [AgentApiModels.cs:39-44](MProjectAgent/Models/AgentApiModels.cs:39), Backend [AgentModels.cs:59-66](MProjectBackend/MProject.Application/Models/AgentModels.cs:59)
- **Vấn đề:** Backend DTO đã có `Hostname` và `LastError`; mismatch nằm ở agent DTO/worker chưa gửi 2 field này, làm backend fields thành gần như dead-code.
- **Fix:** agent gửi `Hostname` và `LastError` (lấy từ `Environment.MachineName` + `_supervisor.GetLastError()`).
- **Effort:** S.

---

### 3.3 Scaling Foundations (A)

#### `A-01` Distributed cache invalidation (Redis pub/sub) cho `AuthorizationCacheInvalidator`
- **Hiện trạng:** `IMemoryCache` local → scale-out 2+ backend instance, invalidate A không lan B.
- **Action:**
  - Abstract `IAuthorizationCacheBus` với 2 impl: `InProcess` (default) + `RedisPubSub`.
  - Bump version qua Redis channel `authz:invalidate:{type}:{id}`.
  - Mỗi backend subscribe + cập nhật IMemoryCache local.
- **[NEED INPUT]:** đã có Redis trong infra chưa? Nếu chưa, có ưu tiên triển khai Redis trong sprint này không?
- **Effort:** L.

#### `A-02` `AuthorizedResourceQueryService` dùng WITH RECURSIVE
- **File:** [AuthorizedResourceQueryService.cs:107-120](MProjectBackend/MProject.Application/Authorization/AuthorizedResourceQueryService.cs:107)
- **Fix:** raw SQL với CTE recursive Postgres. Đo p95 trước/sau.
- **Effort:** M.

#### `A-03` Domain Events Outbox
- **Mục tiêu:** push notification real-time, audit log uniform, future webhook.
- **Schema:** `DomainEvents (Id, AggregateType, AggregateId, EventType, PayloadJson, OccurredAt, ProcessedAt, RetryCount)`.
- **Producer:** `_context.AddDomainEvent(...)` + flush trong `SaveChangesAsync` cùng transaction.
- **Consumer:** BackgroundService scan `ProcessedAt IS NULL`, dispatch tới registered handlers, mark processed.
- **Effort:** L.

#### `A-04` API versioning `/api/v1/...`
- **Action:** introduce `[ApiVersion("1.0")]` qua `Asp.Versioning.Mvc`. Migrate route generator. Agent đã có `agent/v1/` — admin theo sau.
- **Effort:** M.

#### `A-05` BlobGc advisory lock cho HA
- **File:** [BlobGcService.cs](MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs)
- **Fix:** trước khi sweep, `SELECT pg_try_advisory_lock(@lockId)`. Lock fail → skip cycle.
- **Effort:** S.

#### `A-06` Seeder data migration version tracking
- **Hiện trạng:** `AppDbSeeder` chạy idempotent qua check exist; không thể remove permission cũ.
- **Fix:** thêm bảng `AppliedSeeds (Name, AppliedAt, Hash)`. Mỗi seed có `Name + Version`. Cho phép "delete seed" với guard "permission có grant active → block".
- **Effort:** M.

---

## 4. Phase 3 — Medium fixes + Short-term features (S2 còn + F ngắn hạn + S3 cleanup)

### 4.1 S2 cleanup

| Task ID | Mô tả | File | Effort |
|---|---|---|---|
| `S2-10` | `appsettings.json` minioadmin default + production guard | [appsettings.json:16-17](MProjectBackend/MProject.Api/appsettings.json:16) | S |
| `S2-11` | Station-package unique constraint relax `(Station, Package)` | [DBContext.cs:283-291](MProjectBackend/MProject.Infrastructure/DBContext.cs:283), [StationSoftwareAssignmentService.cs:48-51](MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:48) | M, **[NEED INPUT]** |
| `S2-12` | `PollAsync` catch hẹp `PostgresException 23505` | [InstallationJobService.cs:167-173](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:167) | S |
| `S2-13` | Agent token lifetime + admin audit (`Agent.TokenIssuedAt`) | [Agent.cs](MProjectBackend/MProject.Domain/Entities/Assets/Agent.cs), [AgentService.cs](MProjectBackend/MProject.Application/Services/Assets/AgentService.cs) | M, **[NEED INPUT]** |
| `S2-14` | Agent `CacheIndex.GetExistingHashesAsync(IN ...)` thay `GetAllHashesAsync` | [CacheIndex.cs:93-104](MProjectAgent/Storage/CacheIndex.cs:93) | S |
| `S2-15` | `CancelJobAsync` không lật `OperationalStatus` đè Maintenance/Running | [InstallationJobService.cs:420-434](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:420) | S |
| `S2-16` | `AnnounceAsync` error message kèm hint enrollment-token | [AgentService.cs:124-137](MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:124) | S |
| `S2-17` | `ResolveManifestAsync` không throw `KeyNotFoundException` cho job đã terminal — trả `missing[]` | [InstallationJobService.cs:205-206](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:205) | S |
| `S2-18` | `GetMeAsync` cache 30-60s per `(userId, userVersion)` | [AuthService.cs:166-229](MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:166) | S |
| `S2-19` | `AnnounceAsync` xử lý clear error khi DB unique-index chặn MAC + document reuse policy | [AgentService.cs:139-167](MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:139), [DBContext.cs:144](MProjectBackend/MProject.Infrastructure/DBContext.cs:144) | S, **[NEED INPUT]** |

> **Lưu ý `S2-11`:** constraint hiện tại có ý nghĩa nghiệp vụ "một package chỉ được assign một nơi" và service cũng enforce global assignment. Relax thành `(Station, Package)` sẽ cho cùng package đi nhiều station; vẫn cần quyết định riêng có giữ rule "mỗi station chỉ một active package" hay cho multi-package active trên cùng station. Không implement migration trước khi chốt rule này.

### 4.2 Short-term features

#### `F-01` Wait-for-idle: PollAsync skip enqueue khi test app đang chạy
- **File:** [InstallationJobService.PollAsync](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:68)
- **Logic:** join `ComputerRuntimeStatus.IsTestAppRunning` → nếu true và job không có flag `HotfixOverride` → skip.
- **Effort:** M.

#### `F-02` Maintenance window per Station
- **Schema:** `Station.UpdateWindow JSONB { dailyStartUtc, dailyEndUtc, daysOfWeek }`.
- **PollAsync filter:** `if (now ∉ station.UpdateWindow) skip`.
- **UI:** admin set window per station.
- **Effort:** M.

#### `F-03` Server-pushed commands trong heartbeat response
- **Schema:** `AgentHeartbeatResponse.Commands: [Command { Id, Type, Payload }]`, types: `Restart`, `KillAndRestart`, `CancelJob`, `ReloadConfig`, `ForceMaintenanceOff`.
- **Server side:** bảng `AgentCommandQueue (Id, ComputerId, Type, Payload, EnqueuedAt, AckedAt, AckedJobId)`.
- **Agent:** xử lý + ack qua endpoint `/agent/v1/commands/{id}/ack`.
- **Effort:** L. **Prerequisite:** S1-02 (idempotent CompleteAsync).

#### `F-04` Inventory + drift detection
- **Schema:** Agent endpoint `/agent/v1/inventory` (đã có DTO `AgentInventoryRequest`).
- **Logic:** scan `InstallRoot` 1 lần/ngày (background trong agent), so với `PcInstallationRecord`.
- **Dashboard:** "PC có drift" báo cho admin.
- **Effort:** M.

#### `F-05` Rollback automatic khi version mới gây crash-loop
- **Schema:** thêm `SoftwareVersion.PreviousVersionId` (auto-fill khi release).
- **Logic:** server thấy `RuntimeStatus.RestartCountInWindow >= MaxRestarts` cho version mới (< 24h) → tự sinh job rollback về `PreviousVersionId`.
- **Effort:** M. **Prerequisite:** F-03 (push command).

#### `F-06` Test app health probe (named pipe / local HTTP)
- **Hợp đồng:** test app expose `GET http://127.0.0.1:{port}/health` → 200 nếu healthy.
- **Supervisor:** poll mỗi 30s. Fail 3 lần liên tiếp → kill + restart (đi qua restart policy).
- **Effort:** M.

### 4.3 S3 cleanup (gom 1 PR)

- `S3-01` Comment Session-0 outdated ở [JobExecutor.cs:174-175](MProjectAgent/Services/JobExecutor.cs:174).
- `S3-02` Update `docs/agent_note.md` phần A.15 — đánh dấu M4 DONE.
- `S3-03` Extract `Win32ErrorCodes.SharingViolation` thay 2 chỗ hardcode 32.
- `S3-04` `ProcessSupervisor.StartWatch` lưu reference + await trong `DisposeAsync`.
- `S3-05` `BlobCacheService.PutAsync` compute hash inline với write (giảm I/O 2x).
- `S3-06` `MaintenanceCommand` hỗ trợ `--path=value` + validate absolute.
- `S3-07` `ComputerService.GetUnassignedComputersAsync` audit permission ở controller.
- `S3-08` Cache key escape ở `AuthorizationService:80`.
- `S3-09` `SoftwareVersionService.GenerateNextVersionCodeAsync` chuyển sang DB sequence.
- `S3-10` Exhaustive `switch` audit cho `ComputerOperationalStatus` enum (`_ => throw`).
- `S3-11` EnrollCommand `--token` chấp nhận value bắt đầu bằng `-`.

---

## 5. Phase 4 — Compliance, Observability, Multi-tenant (F-medium + advanced)

### 5.1 Compliance

#### `F-07` Operator electronic signature (FDA 21 CFR Part 11)
- **Use case:** khi release version hoặc approve rollout → manager phải re-auth (password) + nhập reason.
- **Schema:** `ApprovalAction` thêm `Signature { ReAuthAt, ReasonText, PasswordHash (snapshot or zero-knowledge proof) }`.
- **Effort:** L. **Decision input cần.**

#### `F-08` Cryptographic version signing
- **Logic:** server ký manifest với private key. Agent có public key đã pin → verify trước khi cài.
- **Threat:** chống "ai đó replace MinIO blob bằng binary malicious".
- **Effort:** L.

#### `F-09` End-to-end audit trail UI
- **Phụ thuộc:** `A-03` Outbox.
- **Bảng mới:** `ActivityLog` (đã có `AuthorizationAuditLog` cho ACL/Role; mở rộng để cover install/uninstall/assignment/maintenance).

#### `F-10` Compliance reports
- "List PC running version < X.Y", "PC chưa heartbeat > 7d", "Drift detected last 30d" → CSV/PDF export.
- **Effort:** M (sau khi có F-04, F-09).

### 5.2 Observability

#### `F-11` Real-time dashboard via SignalR
- Push: rollout progress, agent online/offline, crash-loop, drift detected.
- **Phụ thuộc:** A-03 Outbox.
- **Effort:** M.

#### `F-12` Custom metrics (đã có `UseHttpMetrics`)
- `installation_job_duration_seconds{packageId,versionId,status}`
- `agent_heartbeat_lag_seconds`
- `blob_storage_bytes_total`
- `unexpected_exits_total{exitReason}`
- `pcs_in_maintenance_total{stationId}`
- `version_drift_count{packageName}`
- **Effort:** S.

#### `F-13` Distributed tracing OpenTelemetry
- Trace từ agent → backend → DB. Propagate `traceparent` header.
- **Effort:** M.

### 5.3 Advanced

#### `F-14` Bandwidth scheduler / local mirror peer-to-peer
- **Đơn giản:** server-side rate-limit token bucket per Station.
- **Phức tạp:** 1 PC trong line làm edge cache, các PC khác tải qua LAN. Nhiều OSS có sẵn (Apt-Cacher-NG pattern).
- **Effort:** M (rate-limit) → L (peer).

#### `F-15` Multi-tenant / multi-site
- Thêm `Site` ở root resource hierarchy. Schema đã sẵn sàng (`Resource.ParentResourceId`).
- Kết hợp `A-01` (Redis), `A-04` (API versioning).
- **Effort:** L.

#### `F-16` Backup/restore + DR procedures
- Postgres dump + MinIO snapshot. Document RPO/RTO (đề xuất RPO 1h, RTO 4h).
- **Effort:** M (script + doc).

#### `F-17` Agent self-update
- Schema: `AgentRelease (Version, ChecksumSha256, ReleasedAt, MinSupportedServerVersion)`.
- Logic: server return `AgentHeartbeatResponse.AgentUpdate: { Version, Url }`. Agent download + replace exe + restart service.
- **Threat:** signing required (xem F-08).
- **Effort:** L.

#### `F-18` Test result aggregation (mục tiêu cuối)
- Agent collect test report (CSV/JUnit/custom) từ install root → upload server.
- Schema: `TestRunRecord { ComputerId, VersionId, SerialNumber, Result, StartedAt, FinishedAt }`.
- MES integration optional.
- **Effort:** L.

#### `F-19` Hot-config push cho test app
- Khi config-only thay đổi (limit, threshold), push qua heartbeat thay vì redeploy full package.
- Phụ thuộc: `LimitFile` flow đã có sẵn.
- **Effort:** M.

#### `F-20` Approval parallel + delegation
- Schema: `ApprovalStep` thêm `RequiredApprovals` (M-of-N) + `Backups`.
- **Effort:** M.

#### `F-21` `RoleAssignment.IsSuspended` flag
- Tạm pause role mà không xóa, không vỡ history.
- Migration: thêm cột; service check `!IsSuspended` cùng `!IsDeleted`.
- **Effort:** S.

---

## 6. Checklist tracking

> Đánh dấu `[x]` khi PR đã merge + test pass + doc cập nhật. AI agent **không tự đánh dấu**; đợi human approve.

### Phase 1 — Critical & Auth/Supervisor

- [x] `S0-01` Atomic refresh-token rotation
- [x] `S1-01` Heartbeat upsert transaction
- [x] `S1-02` EnsureTransition allow Cancelled + CompleteAsync idempotent
- [x] `S1-03` PinVersionAsync cancel Downloading/Installing
- [x] `S1-04` Tune `/agent/v1/announce` rate-limit policy (rev2: existing limiter audit)
- [x] `S1-05` ProcessSupervisor recovery default = relaunch
- [x] `S1-06` ExitClassifier per-mode
- [x] `S1-07` RuntimeStateStore backup + logging
- [x] `S1-08` BlobCache eviction conditional DELETE
- [x] `S1-09` BuildManifestJobsAsync batch presign
- [x] `S1-10` GetMeAsync coarse permissions
- [x] `S1-11` Cache version GUID + decision invalidation (rev1: wording clarified)
- [x] `S1-12` ResourceLookupService invalidate
- [x] `S1-13` Approval policy fallback
- [x] `S1-15` SystemUser cho self-announce (rev1: was `S0-02`)
- [x] `S1-16` GitBasicAuthFilter harden without breaking public read (rev1: was `S0-03`)
- [x] `S1-17` Git repository API authorization

### Phase 2 — High/Medium + Foundations

- [x] `S1-14` EnsureRolePermissionsGrantableAsync bulk query
- [ ] `S2-01` Document authz semantics + matrix tests
- [ ] `S2-02` Split `software.manage` permission
- [x] `S2-03` Pending user not assigned Viewer
- [x] `S2-04` Email/reset password decision + endpoint
- [x] `S2-05` RegisterUploadedFileAsync transaction + version lock/touch
- [x] `S2-06` AnnounceAsync transaction unified
- [x] `S2-07` Tus rollback semantics doc
- [x] `S2-08` ExecuteUpdateAsync reload/ChangeTracker clear audit
- [x] `S2-09` Heartbeat DTO contract align
- [ ] `A-01` Distributed cache bus (Redis)
- [ ] `A-02` WITH RECURSIVE hierarchy query
- [ ] `A-03` Domain Events Outbox
- [ ] `A-04` API versioning
- [x] `A-05` BlobGc advisory lock
- [ ] `A-06` Seeder version tracking

### Phase 3 — S2 cleanup + Short-term features + S3

- [ ] `S2-10` Production guard minio default
- [ ] `S2-11` Station-package unique relax + active-package rule decision
- [ ] `S2-12` PollAsync catch narrow
- [ ] `S2-13` Agent token lifetime
- [ ] `S2-14` CacheIndex GetExistingHashesAsync
- [ ] `S2-15` CancelJobAsync preserve operational
- [ ] `S2-16` Announce error hint
- [ ] `S2-17` ResolveManifestAsync soft-skip terminal
- [ ] `S2-18` GetMeAsync cache
- [ ] `S2-19` AnnounceAsync clear error khi DB chặn MAC (rev1: tách từ `S1-04`)
- [ ] `F-01` Wait-for-idle
- [ ] `F-02` Maintenance window per Station
- [ ] `F-03` Server-pushed commands
- [ ] `F-04` Inventory + drift detection
- [ ] `F-05` Rollback automatic
- [ ] `F-06` Test app health probe
- [ ] `S3-XX` All S3 cleanup (1 PR gom)

### Phase 4 — Compliance + Observability + Advanced

- [ ] `F-07` Electronic signature
- [ ] `F-08` Cryptographic manifest signing
- [ ] `F-09` Activity log + audit UI
- [ ] `F-10` Compliance reports
- [ ] `F-11` SignalR real-time dashboard
- [ ] `F-12` Custom Prometheus metrics
- [ ] `F-13` OpenTelemetry tracing
- [ ] `F-14` Bandwidth scheduler / peer mirror
- [ ] `F-15` Multi-tenant
- [ ] `F-16` Backup/DR procedures
- [ ] `F-17` Agent self-update
- [ ] `F-18` Test result aggregation
- [ ] `F-19` Hot-config push
- [ ] `F-20` Approval parallel/delegation
- [ ] `F-21` RoleAssignment IsSuspended

---

## 7. Câu hỏi cần làm rõ trước khi implement

> AI agent **PHẢI dừng và hỏi** ở các quyết định sau. Mỗi câu trả lời sẽ ảnh hưởng tới task scope/effort.

### 7.1 Auth & Compliance

1. **[S1-15]** *(was S0-02)* ID cho `SystemUser.SelfAnnounce` nên cố định Guid `00000000-0000-0000-0000-000000000001` hay random? *(cố định để seed idempotent)*
2. **[S2-19]** *(was S1-04)* Có cho phép re-use MAC sau khi admin "purge" (entity mới `ComputerPurge`), hay cấm vĩnh viễn (bắt admin đổi MAC card cũ trước khi tái sử dụng PC)?
3. **[S1-10]** `/me` chỉ trả coarse role-based permissions (preferred) hay tính full ACL Deny logic?
4. **[S1-13]** Approval policy không-active → "auto-approve with audit warning" hay "block & alert admin"?
5. **[S1-16]** Git public repository có cần hỗ trợ anonymous clone/pull không? Nếu có, S1-16 phải harden theo route/operation, không strict 401 blanket.
6. **[S2-01]** ACL Deny global priority cao có thắng ACL Allow scope priority thấp không? Document chính thức.
7. **[S2-03]** User `Pending` register: (a) không assign role, đợi admin approve mới gán; (b) tạo RoleAssignment với `StartTime = null`?
8. **[S2-04]** Resolved: dùng admin reset manual qua `PUT /api/users/{id}/password`; chưa thêm email reset/self-service.
9. **[F-07]** Compliance level cần đạt: GMP/IATF/FDA 21 CFR Part 11? Ảnh hưởng tới signature scope.

### 7.2 Scaling & Infrastructure

10. **[A-01]** Đã có Redis trong infra chưa? Ưu tiên Phase 2 hay Phase 3?
11. **[A-03]** Outbox dispatcher dùng poll-loop in-proc đơn giản, hay tích hợp message broker (RabbitMQ/Kafka)?
12. **[F-15]** Multi-tenant: roadmap năm nào? Ảnh hưởng tới việc thiết kế lại `Resource` hierarchy có cần `Tenant` ở root.
13. **[F-17]** Agent self-update có ưu tiên không? Hiện tại có cơ chế triển khai agent qua MSI/GPO sẵn không?

### 7.3 Product/Operations

14. **[S2-02]** Tách `software.manage` thành 4 permission: có team nào hiện đang dùng role gắn `software.manage` mà chỉ cần 1 phần? *(cần migration mapping)*
15. **[S2-11]** Station-package unique constraint: hiện có line nào cần share package qua nhiều station không? Nếu có, relax thành `(Station, Package)`. Có giữ rule "mỗi station chỉ một active package" không?
16. **[S2-13]** Agent token lifetime 30/60/90 ngày? Có chấp nhận admin phải re-enroll PC định kỳ không?
17. **[F-01]** "Wait-for-idle" có cần flag `HotfixOverride` cho admin force-install giữa lúc test đang chạy không?
18. **[F-02]** Maintenance window per Station: timezone — UTC hay local time của factory? Daylight saving?
19. **[F-05]** Auto-rollback threshold: bao nhiêu PC crash-loop thì rollback toàn bộ station? (5%? 10%?)
20. **[F-06]** Test app health probe: bắt buộc cho mọi LongRunning app hay opt-in qua `SoftwareVersion.HealthCheckUrl`?
21. **[F-18]** Test result aggregation: format report do test app team định nghĩa (CSV/JUnit), hay schema chuẩn server-defined? MES integration urgency?

### 7.4 Quy trình thực thi

22. **PR size:** prefer 1 task = 1 PR hay gom S3 thành 1 PR cleanup duy nhất? *(đang đề xuất gom S3, tách S0/S1)*
23. **Test gate:** có yêu cầu integration test pass trong CI cho mọi PR S0/S1 không?
24. **Migration apply timing:** PR merge auto apply migration trên staging, hay manual apply có window?
25. **Branch strategy:** một `refactor/phase-1` branch dài lâu, hay từng task 1 branch ngắn `fix/s0-01-...`?

---

## 8. Phụ lục — Risk Matrix

| Phase | Risk chính | Mitigation |
|---|---|---|
| 1 | Refactor auth/heartbeat break production agent đang chạy | Feature flag `Auth:LegacyHeartbeatCompat = true`; rollout incremental; bash test trên staging trước |
| 2 | Outbox migration to Redis bị partial → cache lệch giữa instance | Triển khai `Redis.Required = false` config; fallback InProcess + alert |
| 2 | API versioning break frontend | FE rebuild đồng thời; redirect `/api/*` → `/api/v1/*` 1 sprint deprecation |
| 3 | Server-pushed commands làm agent crash khi schema không khớp | Versioning trong `Command.SchemaVersion`; agent skip unknown |
| 4 | Multi-tenant introduce hierarchy changes affect ACL semantics | Migration dry-run + restore plan; freeze auth changes trong tuần go-live |

---

## 9. Done definition

Mỗi task hoàn thành phải đảm bảo:

1. **Code merge** vào master qua PR có review.
2. **Tests pass** — unit + integration mới thêm cho task đó.
3. **Migration apply** trên staging + smoke test 24h.
4. **Doc updated** — nếu task chạm Agent → cập nhật `docs/agent_note.md`. Nếu chạm semantic → `docs/authorization-semantics.md`.
5. **Checklist tick** trong file này (human only).
6. **Metric/log baseline** — nếu task có perf claim, đính kèm before/after chart.

---

*File này tổng hợp từ 2 review session. Khi implement task, dùng task ID (`S0-01`, `S1-02`, ...) trong commit message và PR title.*
