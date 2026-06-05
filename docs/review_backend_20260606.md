# Báo cáo Review MProjectBackend — 2026-06-06

> Reviewer: senior dev / solution architect / senior tester / senior UI-UX
> Scope: review toàn bộ MProjectBackend, đi sâu vào phân quyền & logic core
> Method: đọc code tĩnh (không chạy backend). Các nhận định về race condition cần test reproduce trước khi fix.

---

## 1. Đánh giá tổng quan

**Tổng thể: chất lượng tốt cho 1 hệ thống nội bộ giai đoạn đầu, đang tiệm cận "prod-ready" nhưng còn 1 số lỗ hổng nguy hiểm cần xử lý trước khi đưa ra nhà máy thật.**

### Điểm mạnh đáng khen
- **Kiến trúc DDD rõ ràng**: Domain / Application / Infrastructure / Api tách bạch. `IAppDbContext` trong Application để service không phụ thuộc thẳng vào EF Infrastructure — chuẩn.
- **Authorization model rất khá**: ACL + RBAC + resource hierarchy + time range + InheritToChildren + Owner shortcut + audit log + decision trace. Đây là dạng kiến trúc kiểu OPA/Cedar mini, hiếm thấy ở dự án in-house.
- **Atomic deployment design**: Blob có ReferenceCount + CHECK constraint, SHA-256 content-addressed, GC service có grace period, có pre-verify hash để chống tampering.
- **Token security đúng chuẩn**: refresh token có pepper (HMAC), family rotation chống replay, fixed-time compare. Agent token cũng vậy. JWT có guard chống dev key leak vào prod.
- **State machine có kỷ luật**: `InstallationJob` có allowed transitions, watchdog timeout, partial unique index `UX_InstallationJobs_Computer_Version_Active` để chặn race trùng job.
- **Concurrency model**: `IVersionedEntity` với optimistic lock (Version + IsConcurrencyToken), soft-delete global filter, `ExecuteUpdateAsync` cho atomic update (tránh race).
- **DB constraints có chiều sâu**: nhiều check constraint (time range, non-negative, sha256 length, agent active token), filtered unique indexes — không phụ thuộc app logic.

---

## 2. Vấn đề tồn đọng NGUY HIỂM (cần xử lý sớm)

### [P0] Critical

**(1) `SystemUsers.SelfAnnounce = Guid.Empty` gây lẫn audit & quyền** — `MProjectBackend/MProject.Application/Constants/SystemUsers.cs:7`
- Computer/Resource tự announce có `CreatedBy = Guid.Empty`. Nếu sau này có user nào đó vô tình có Id `Empty` (rất khó nhưng có thể qua bug), hoặc nếu có chỗ check `CreatedBy == userId` để cấp quyền — sẽ thủng. Quan trọng hơn: audit log không trace được nguồn gốc thật.
- **Giải pháp**: tạo 1 row `User` thật tên `__system_selfannounce__` với `Status = System`, hoặc đổi `CreatedBy` thành `Guid?`. Bổ sung 1 `UserStatus.System` để chặn user này login.

**(2) `AuthService.GetMeAsync` thiếu logic ACL Deny** — `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:201-216`
- `permissions` trong `/me` chỉ gộp `rolePermissions + aclAllowPermissions`, KHÔNG trừ `aclDenyPermissions`. Nếu admin tạo ACL Deny cho 1 permission cụ thể, UI vẫn nghĩ user có permission → user thấy menu, click thì 403 → UX confusing, có thể leak resource name qua menu/route guard.
- **Giải pháp**: tính `permissions` theo đúng logic `PermissionEvaluator` (deny override allow). Hoặc — tốt hơn — `/me` chỉ trả về **role-based permissions** (coarse), việc check thật vẫn đi qua `IPermissionService` ở backend cho mỗi action.

**(3) Decision cache key thiếu Resource hierarchy version** — `MProjectBackend/MProject.Application/Services/Identity/AuthorizationService.cs:80`
- Cache key = `authz_decision_{userId}_{userVersion}_{permVersion}_{permission}_{resourceId}`. Khi parent của resource đổi (move resource sang station khác) hoặc Owner đổi → quyết định cũ vẫn được cached cho TTL 3 phút.
- `InvalidateResourceScopeAsync` chỉ xoá `authz_scope_*` (cache hierarchy), KHÔNG bump `userVersion` của user bị ảnh hưởng → decision cache vẫn stale.
- **Giải pháp**: khi resource bị move hoặc OwnerId đổi, gọi luôn `cacheInvalidator.InvalidateResourceScope(...)` + bump `permVersion` (đơn giản). Hoặc tốt hơn: thêm `resource_version_{resourceId}` vào cache key.

**(4) Race condition giữa `Poll` và `Pin` đổi version** — `MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:117-122`, `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:202-222`
- `PinVersionAsync` chỉ cancel jobs `Status == Pending` của version cũ. Nhưng nếu 1 job đang `Downloading` (agent đã ack), pin đổi sang version mới → job cũ tiếp tục chạy đến hết, version mới cũng được sinh job → computer có thể cài cả 2 version, hoặc cài version cũ rồi cài đè version mới.
- **Giải pháp**: cancel cả `Downloading`/`Installing` (đã làm trong `RemoveAssignmentAsync` qua `CancelActiveJobsForPackageAsync` nhưng `PinVersionAsync` thì chưa). Và agent client cần check `job.Status == Cancelled` ở mỗi heartbeat để abort sớm.

**(5) Approval policy có thể bị bypass khi không có policy active** — `MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:57-66`
- Khi `SubmitAsync`, nếu không có ApprovalPolicy active cho `TargetType` → throws InvalidOperationException → 409 Conflict.
- Nhưng nếu admin vô tình tắt policy `IsActive=false` thì target (vd: LimitFile mới upload) sẽ kẹt vĩnh viễn ở `Draft`. Không có fallback "auto-approve" hay "approval bypass with audit".
- **Giải pháp**: thêm setting `ApprovalRequired: false` cho từng `TargetType` ở DB, hoặc tạo fallback "no-op approval" được audit kỹ.

**(6) `GitBasicAuthFilter` swallow exception, có thể bị brute force qua side channel** — `MProjectBackend/MProject.Api/Filters/GitBasicAuthFilter.cs:21-52`
- Catch-all `try/catch {}` — base64 lỗi, DB lỗi đều silent. Filter không trả 401 — user-id chỉ KHÔNG được set, tức là controller phải tự check `HttpContext.Items["CurrentUserId"]`. Nếu lập trình viên controller quên check → endpoint mặc định không-auth.
- **Giải pháp**: làm filter strict — auth fail thì gọi `ReturnUnauthorized()` ngay. Hoặc dùng AuthenticationHandler đầy đủ thay vì authorization filter để tận dụng `[Authorize]` standard. Đồng thời rate-limit Basic auth endpoint.

### [P1] High

**(7) Đăng ký user `Pending` mà vẫn assign Viewer role ngay** — `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:67`
- Khi user `Register` → Status `Pending` + được assign Viewer role ngay. User chưa được duyệt nhưng đã có RBAC grant. Tốt là `SubjectResolver.IsUserActive` check `Status == Active` nên không cho phép action → ok về authorization, nhưng `_authorizationService.EvaluateAsync` sớm return "user_inactive" mà vẫn lưu RoleAssignment với startTime = NOW → 1 lúc nào đó admin approve thì user lập tức có role mà có thể admin không biết.
- **Giải pháp**: chỉ assign Viewer khi admin chuyển từ `Pending → Active`. Hoặc set RoleAssignment với `StartTime = null` + ghi rõ "assigned at registration, gated by user status".

**(8) `RegisterAsync` cho phép registration không có email** — `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:60`
- `Email = string.Empty`. Mật khẩu user quên không có cách reset (không email). Trong factory environment đây có thể là design intentional, nhưng cần xác nhận và document.

**(9) `EnsureRolePermissionsGrantableAsync` rất nặng** — `MProjectBackend/MProject.Application/Services/Identity/AuthorizationMutationService.cs:265-282`
- Khi admin assign role cho user, từng permission của role được check bằng `_authorizationService.IsAllowedAsync(...)` — N+1 DB calls (mỗi permission 1 query ACL + 1 query RBAC + scope resolver).
- 1 role có 20 permissions → tối thiểu 60+ queries cho 1 action assign.
- **Giải pháp**: viết 1 query gộp lấy tất cả permission codes mà actor có cho scope, rồi `HashSet.Contains` check. Hoặc cache aggressively per request.

**(10) `RbacGrantQueryService` không phân biệt RoleAssignment có scope vs global đúng cách** — `MProjectBackend/MProject.Application/Authorization/RbacGrantQueryService.cs:38`
- `Where(a => !a.ScopeResourceId.HasValue || scopeList.Contains(a.ScopeResourceId.Value))` — đúng. Nhưng `ResourceDistance = a.ScopeResourceId.HasValue ? distanceByResourceId[...] : int.MaxValue`. Global grant lấy distance = MAX → trong `PickRbac` sort `OrderBy(ResourceDistance)` thì global luôn bị chọn cuối. Điều này có nghĩa là nếu user có cả role global + role scope, evaluator chọn role scope. Đúng business: scope-specific overrides global. **OK**.
- Nhưng có 1 bug nhỏ: trong `PickAcl`, `OrderByDescending(Priority).ThenBy(ResourceDistance)` — priority cao hơn thắng kể cả nếu nó global. Có nên ưu tiên distance nhỏ trước (resource-specific)? Tùy business model. **Cần xác nhận**: 1 ACL Deny global priority 100 có nên thắng 1 ACL Allow scope priority 50? Hiện code: có (priority thắng). Document rõ vào.

**(11) `ResourceLookupService` cache 30s nhưng không invalidate khi resource bị xóa** — `MProjectBackend/MProject.Application/Services/Identity/ResourceLookupService.cs:12`
- Nếu computer bị delete trong vòng 30s, filter vẫn dùng `resourceId` cũ → có thể authorize nhầm (đặc biệt với ACL `InheritToChildren`).
- **Giải pháp**: hoặc giảm TTL xuống 5s, hoặc invalidate khi entity bị soft-delete (gọi `_cache.Remove`).

**(12) `AnnounceAsync` tạo Resource và Computer trong cùng `SaveChangesAsync` không transaction** — `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:182`
- EF SaveChanges sẽ chạy trong 1 transaction implicit ở PG → OK. Nhưng test InMemory provider thì không có transaction → có thể partial state. `IsInMemoryProvider()` được dùng nhiều chỗ; xem xét chuẩn hóa: dùng `ExecuteInTransactionAsync` cho cả announce flow để code path đồng nhất.

**(13) `OnFileCompleteAsync` (Tus) không transaction quanh storage + DB** — `MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:198-238`
- Có rollback storage trong catch, nhưng nếu app crash giữa `UploadAsync` và `RegisterUploadedFileAsync` → blob đã ở MinIO nhưng không có ai reference → BlobGc sẽ dọn (đúng grace period). OK nhưng cần ghi rõ.
- Riêng `weUploaded = false` khi blob đã tồn tại (dedup) — nếu `RegisterUploadedFileAsync` fail, KHÔNG xoá blob (đúng, vì còn ref khác). **OK**.

### [P2] Medium

**(14) `GetMeAsync` rất tốn DB** — `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:166-229`
- 5 query: user + teams + role assignments (lặp lại 2 lần qua materialize) + acl. Endpoint này client gọi rất thường xuyên — nên cache theo `(userId, userVersion)` 30-60s.

**(15) `BlobGcService` chạy theo "hour UTC" không có jitter** — `MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs:174-181`
- Multiple instance backend → cùng giờ cùng chạy. Có advisory lock kiểu Seeder không? Không thấy. Khi scale ra HA, 2 instance cùng sweep → race delete blob (dù có WHERE check exact, vẫn waste resource và có nguy cơ race với uploader).
- **Giải pháp**: dùng `pg_try_advisory_lock` để chỉ 1 instance chạy.

**(16) Token format `agt_{id:N}.{secret}` lộ AgentId dài hạn**
- `AgentId` là Guid không nhạy cảm nhưng nếu agent token leak (log, ps aux, fiddler) → kẻ tấn công biết AgentId đối tượng. Trong môi trường nhà máy có máy chung không quá nghiêm trọng. Nếu muốn chặt hơn: chỉ encode secret, server lookup `TokenHash IN (...)` (chậm). Trade-off OK.

**(17) Không có index trên `RefreshToken.RevokedAt` / `RotatedAt`** — `_context.RefreshTokens.Where(t => t.UserId == userId && t.RotatedAt == null && t.RevokedAt == null)` — đã có index `(UserId, RotatedAt, RevokedAt)` ✓. **Đúng rồi**, ok.

**(18) `appsettings.json` lộ MinIO credentials default `minioadmin/minioadmin`** — `MProjectBackend/MProject.Api/appsettings.json:16-17`
- Dù là default dev. Cần đảm bảo deployment script luôn override env var, và thêm guard `if (production && credentials == "minioadmin") throw`.

**(19) `RegisterUploadedFileAsync` không kiểm tra version `Status == Draft` trong cùng transaction với việc thêm SoftwareFile** — file có thể được upload sau khi version đã `Released` nếu race.
- **Giải pháp**: lock `SoftwareVersion` row với `SELECT ... FOR UPDATE` (qua transaction), hoặc check `version.Version` (concurrency token).

**(20) Một số quyền permission code rất "coarse"** — `software.manage` cho cả CRUD package + version + assign + pin + activate.
- Trong nhà máy có thể muốn tách: `software.package.manage` (1 nhóm), `software.version.release` (yêu cầu approval), `software.assign.manage` (rollout team). Hiện tất cả đều `software.manage` → khó audit ai làm cái gì.

---

## 3. Cấu trúc & khả năng mở rộng

### Vấn đề tiềm ẩn ở scale

**(A) `AuthorizationCacheInvalidator` dùng `IMemoryCache` — KHÔNG dùng được khi scale horizontal.**
Khi có 2+ backend instance, invalidate ở instance A không lan tới B → user A bị revoke role nhưng B vẫn cho phép trong 3 phút TTL.
**Giải pháp**: Redis (pub/sub) cho bumped version, hoặc cache hybrid: IMemoryCache local + Redis distributed bump signal. Đây là chuẩn industry (Auth0, Permify dùng pattern này).

**(B) `AuthorizedResourceQueryService` walk hierarchy bằng query tuần tự** — `MProjectBackend/MProject.Application/Authorization/AuthorizedResourceQueryService.cs:107-120`
- Lặp `for depth < 30`, mỗi depth 1 query. Nếu cây resource 5 cấp → 5 round trip. Khi danh sách Computer trong 1 ProductGroup lớn (hàng nghìn) sẽ chậm.
- **Giải pháp**: dùng Postgres `WITH RECURSIVE`. Có thể viết raw SQL trong infrastructure layer:
```sql
WITH RECURSIVE descendant AS (
  SELECT id FROM "Resources" WHERE id = ANY(@roots)
  UNION ALL
  SELECT r.id FROM "Resources" r JOIN descendant d ON r."ParentResourceId" = d.id
  WHERE NOT r."IsDeleted"
) SELECT id FROM descendant;
```

**(C) Không có `Outbox` cho events**
- Khi job `Completed` → set Computer Idle. Khi assignment đổi → cancel jobs + flip computer. Logic nằm trong cùng DbContext transaction (tốt) nhưng nếu sau này muốn:
  - Push notification real-time đến UI (SignalR)
  - Trigger CI/CD pipeline khi version Released
  - Audit log đầy đủ

  thì không có outbox sẽ khó. **Đề xuất**: thêm bảng `DomainEvents` với schema `(Id, AggregateType, AggregateId, EventType, PayloadJson, OccurredAt, ProcessedAt)` + 1 BackgroundService publish.

**(D) `Approval` flow tốt nhưng chưa hỗ trợ parallel approval & delegation**
- Multi-step sequential ✓. Nhưng nhà máy thường có "cần 2/3 manager đồng ý" hoặc "nếu primary approver nghỉ → backup". Hiện không có.

**(E) `RoleAssignment` không có `IsActive` flag — phải dùng `IsDeleted` + thời gian**
- Tạm pause role mà không xóa → khó. Soft-delete-then-recreate sẽ mất history. Có thể thêm `IsSuspended bool`.

**(F) Không có versioning cho API**
- Tất cả route `/api/...` không có `/v1/`. Khi cần break compat → đau. Agent có `agent/v1/...` ✓. Admin API nên cũng vậy.

**(G) Không thấy migration "data" — chỉ schema migrations**
- Khi thêm permission mới hay đổi role, dependency là chạy lại seeder. Seeder không dùng version tracking → nếu xóa permission cũ ở code, DB vẫn còn. Cần dọn thủ công.
- **Giải pháp**: thêm 1 cơ chế "applied seed version" để có thể remove permission khi không còn dùng (cẩn thận tránh xóa permission có ACL/Role grant).

**(H) Single DBContext lifetime "Scoped" + nhiều `ExecuteUpdateAsync`**
- `ExecuteUpdateAsync` bypass change tracker → mọi entity tracked sẽ stale. Nếu service nào gọi `ExecuteUpdateAsync` rồi lại đọc entity → có thể đọc cache cũ. Cần guard hoặc `ChangeTracker.Clear()`.

---

## 4. Gợi ý tính năng tiếp theo (từ dự án thực tế)

Dựa trên kinh nghiệm với MDM / SCCM / Ansible Tower / Octopus / Airbyte cho factory:

### Đợt 1 — Stability & ops (ưu tiên cao)
1. **Rollback / hold for re-validation**: khi 1 version mới release fail trên 30% PC → tự động pause rollout, notify admin. Cần thêm `Rollout.MaxFailureRate`, `Rollout.MinDwellTime`.
2. **Canary / Ring deployment**: pin version mới cho 1-2 station nhỏ trước, theo dõi 24h rồi mới rollout toàn line. Hiện có thể làm thủ công nhưng không có entity `RolloutWave`.
3. **Maintenance window per Station**: hiện chỉ có maintenance per-Computer. Trong nhà máy thường "weekend window" cho cả 1 line.
4. **Health check / synthetic test sau install**: agent chạy 1 lệnh ngắn (smoke test) sau khi `Installed`, fail → tự rollback. Cần `SoftwareVersion.PostInstallHealthCheckCommand` + `HealthCheckTimeoutSec`.
5. **Bandwidth-aware downloads**: Agent + presigned URL — nếu nhà máy có 100 PC cùng download 500MB version mới → bão băng thông. Cần `Rollout.MaxConcurrentDownloads`, hoặc thêm tầng peer-to-peer hash share.

### Đợt 2 — Compliance & audit
6. **End-to-end audit trail UI**: hiện có `AuthorizationAuditLog` cho ACL/Role thay đổi, nhưng install/uninstall/assignment thay đổi chưa được audit centralized. Thêm 1 bảng `ActivityLog` cho tất cả mutation.
7. **Operator electronic signature (FDA 21 CFR Part 11 / GMP)**: khi approve release version, manager phải nhập password lại (re-auth) + reason — hiện chỉ cần JWT.
8. **Cryptographic version signing**: sign manifest bằng key của server, agent verify trước khi cài. Hiện chỉ dựa vào HTTPS + SHA-256 file content.
9. **Compliance reports**: "list of all PC running version < X.Y", "PC chưa heartbeat > 7d", export CSV/PDF.

### Đợt 3 — Observability
10. **Real-time dashboard via SignalR**: hiện admin phải refresh để xem status. Push update cho rollout progress, agent online.
11. **Metric labels chuẩn**: hiện `UseHttpMetrics()` + `MapMetrics()` ✓ nhưng chưa expose custom: `installation_job_duration_seconds`, `agent_heartbeat_lag`, `blob_storage_bytes`. Grafana dashboard đã có thì map những metric này vào.
12. **Distributed tracing**: thêm `Activity` + OpenTelemetry, propagate trace từ agent → backend → DB. Hữu ích khi debug "tại sao version mới không tới PC này".

### Đợt 4 — Advanced
13. **Inventory tracking** (đã có placeholder 501): agent gửi list file + hash thực tế trên đĩa → so với expected → drift detection (file bị user xóa, antivirus quarantine).
14. **Remote command execution (RCE)**: gửi 1 command 1-shot xuống agent (restart test app, clear cache, collect log). Cần authorization rất chặt, nên dùng approval flow.
15. **Multi-tenant**: nếu công ty có nhiều nhà máy / khu vực → cần `Tenant` ở root resource hierarchy.
16. **Backup/restore + DR procedures**: chưa thấy. Postgres dump + MinIO snapshot. Document "RPO 1h, RTO 4h" hay tương tự.
17. **Agent self-update**: agent tự nó là 1 EXE — làm sao update? Hiện chưa thấy. Cần `AgentVersion.MinAllowed`, server từ chối agent quá cũ.

---

## 5. Đề xuất quy trình hành động

**Phase 1 (1-2 sprint, P0)**: Fix #1, #2, #3, #4, #5, #6 — đều là gốc rễ logic core/authorization.

**Phase 2 (3-4 sprint, P1)**: Fix #7-13. Thêm `Outbox` (mục C) + WITH RECURSIVE (mục B). Tách permission `software.manage` (#20).

**Phase 3 (continuous)**: scale-out (Redis cache invalidation — mục A), API versioning (mục F), canary deployment (#2).

**Phase 4 (medium-term)**: compliance features (#7, #8), inventory drift (#13).

---

## 6. Việc cần làm trước khi fix

Khuyên viết integration test reproduce trước khi fix:
- `PollAsync` × `PinVersionAsync` chạy concurrent (#4).
- ACL Deny override Allow với `InheritToChildren` (#2, #10).
- Cache invalidation khi resource bị move/owner đổi (#3, #11).

---

*File này được tạo từ session review ngày 2026-06-06. Sử dụng làm tài liệu tham khảo cho các sprint tiếp theo.*
