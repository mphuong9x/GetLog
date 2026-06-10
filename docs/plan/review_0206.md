# Review Backend MProject — 02/06/2026

Review tập trung phần agent, software, installation flow. Đã đọc: `Program.cs`, agent auth (announce/enroll/heartbeat), `AgentService`, `SoftwareFileService`, `SoftwareVersionService`, `StationSoftwareAssignmentService`, `InstallationJobService`, `BlobGcService`, hai watchdog, `PcInstallationService`, `GlobalExceptionHandler`, `AuthService`, hai storage backend (Local/Minio), Dockerfile và docker-compose.

---

## 1) Lỗi & rủi ro cao (cần xử lý sớm)

### H1. `/agent/v1/announce` cho phép MAC spoofing → chiếm token máy khác
`MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:107-211`

> **✅ ĐÃ FIX (2026-06-02)** — Không làm chức năng admin pre-register PC. Thay vào đó dùng **Trust-On-First-Use (TOFU): self-announce chỉ first-contact**.
> - Backend `AnnounceAsync`: nếu MAC đã tồn tại (đã announce hoặc pre-registered) → **từ chối**, không bao giờ cấp lại token. Mỗi MAC chỉ self-announce **đúng 1 lần**. Re-provision máy đã biết (cài lại / mất token / bị revoke) đi qua **per-machine enrollment-token flow** đã có sẵn (`GenerateEnrollmentTokenAsync` + lệnh agent `enroll`) — không cần tính năng admin thêm PC mới.
> - Agent `AgentWorker`: **bỏ auto re-announce khi gặp 401**. Chỉ announce ở first-run (chưa có token lưu local). Khi 401 mà không có token mới trong store → log hướng dẫn re-enroll thủ công, không tự announce (đây chính là vòng lặp hijack mà H1 mô tả).
> - DB đã có unique index trên `MacAddress` → race 2 first-announce song song: 1 thắng, 1 dính unique-constraint (an toàn).
> - Tests cập nhật: `Announce_RepeatForSameMac_IsRefusedAndKeepsOriginalToken`, `Announce_SecondAttemptAfterAssignment_IsRefused`, `Announce_RevokedAgent_IsRefused`. 27/27 AgentServiceTests pass.
> - **Còn lại để cứng hơn nữa (tùy chọn, tương lai)**: bind machine GUID/TPM hoặc mTLS client cert + bảo vệ token local bằng DPAPI machine-scope (xem H4).

Self-announce chỉ check `InstallerToken` (1 shared secret duy nhất). Ai có installer token + biết MAC của 1 PC production có thể gọi `/announce` với MAC đó → code rơi vào nhánh `existing != null` → cấp **TokenHash mới**, ghi đè TokenHash cũ. Hậu quả:

- Máy production thật mất quyền (token cũ invalid).
- Attacker được cấp `AgentToken` mới, đọc manifest, lấy presigned download URL, ack/complete job.
- Installer token là shared → 1 lần leak (từ 1 file installer bất kỳ) là cả fleet bị ảnh hưởng.

**Cách fix tối thiểu**:
- Pre-register MAC ở backend trước (admin tạo Computer trước, set `AnnouncedAt = null`), self-announce chỉ accept khi máy đã pre-registered (đúng ra code đã có nhánh này nhưng đang **throw chứ không phải nhánh chính**). Đề xuất: bỏ luôn nhánh "tự tạo Computer mới" trong announce.
- Hoặc dùng **per-machine enrollment token** (đã có flow `GenerateEnrollmentTokenAsync` rồi — chỉ cần force dùng nó, disable installer-token self-announce trong prod).
- Mạnh hơn nữa: bind agent với TPM / machine GUID / mTLS client cert. Trên Windows có thể dùng `DPAPI machine-scope` để bảo vệ token lưu local.

### H2. Heartbeat update không filter `IsDeleted`
`MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:408-419`

```csharp
var rowsAffected = await _context.Computers
    .Where(c => c.Id == computerId)
    .ExecuteUpdateAsync(...)
```

Computer đã bị soft-delete vẫn nhận heartbeat → `LastSeenAt`, `LiveStatus = Online` tiếp tục update → liveness watchdog không thấy stale → máy "zombie" tồn tại mãi. Thêm `&& !c.IsDeleted` và return 401 nếu agent không match.

> **✅ ĐÃ FIX (2026-06-02)** — Đính chính + sửa thực chất:
> - **Đính chính review**: DBContext có global query filter `HasQueryFilter(e => !e.IsDeleted)` áp dụng cho mọi `ISoftDeletable` (DBContext.cs:572-579). EF Core 8 áp filter này vào cả `ExecuteUpdateAsync`, nên dòng `_context.Computers.Where(c => c.Id == computerId).ExecuteUpdateAsync(...)` thực ra **đã** không ghi vào row soft-deleted (0 rows affected → throw KeyNotFoundException). Phần "viết LastSeenAt vào máy đã xóa" trong review là sai.
> - **Vẫn còn lỗ thật đã fix**:
>   1. `AuthenticateAsync` chỉ filter trên `Agents` (không động tới `Computer`) → token của agent có máy đã soft-delete **vẫn auth thành công** → middleware cho qua, request mới fail muộn ở service. Đã sửa: predicate giờ tham chiếu `x.Computer.IsDeleted` → global filter trên `Computer` được áp khi join → row biến mất → trả 401 sớm tại auth layer.
>   2. `RecordHeartbeatAsync` lookup `Agent` (không có IsDeleted) trước khi gọi update Computer. Trước fix: ghi `agent.LastHeartbeatAt`/`AgentVersion` xong rồi mới fail ở step Computer update → **leak `LastHeartbeatAt` cho agent của máy đã xóa** (watchdog của agent vẫn thấy nó "tươi"). Đã sửa: lookup `Agent` filter thêm `!x.Computer.IsDeleted` → không có row → throw `KeyNotFoundException` **trước khi** ghi gì.
>   3. `UpdateComputerFromHeartbeatAsync` (cả nhánh in-memory lẫn SQL): thêm `!c.IsDeleted` filter cho rõ ràng (defense-in-depth, không phụ thuộc behavior global filter), in-memory branch nay throw `KeyNotFoundException` thay vì `FirstAsync` ném `InvalidOperationException` lạc loài.
> - Tests mới: `Heartbeat_WhenComputerSoftDeleted_ThrowsAndDoesNotResurrect` (verify cả Computer LẪN Agent đều không bị bump), `Authenticate_WhenComputerSoftDeleted_ReturnsNull`. **296/296 tests** pass.

### H3. Race condition khi 2 PC clone trùng MAC announce song song
`MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:107-195`

Không có lock/serialization, 2 `SaveChangesAsync` đè TokenHash của nhau → một bên client cầm token đã thành "ghost". Phải dùng unique constraint `(MacAddress) WHERE IsDeleted = false` (chắc đã có?) + xử lý `DbUpdateException`. Đồng thời nên `SELECT ... FOR UPDATE` (PG advisory lock theo MAC) để tránh race.

### H4. Agent bearer token = HMAC-SHA256(pepper, secret), **không salt per-record**
`MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:479-503`

Token entropy 32 byte → brute không khả thi. Nhưng pepper là **1 secret toàn cục, dùng cho toàn bộ fleet**, lưu trong process memory. Nếu DB dump + pepper leak → tất cả token reusable. Đề xuất:
- Hoặc thêm per-agent salt vào `Agent` (cột `TokenSalt`) → HMAC(pepper, salt || secret).
- Hoặc **versioned pepper**: lưu `TokenKeyVersion` để rotate pepper không invalidate toàn bộ.
- Bắt buộc rotate token định kỳ (xem M2 bên dưới).

### H5. `BlobGcService` race với upload chậm
`MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs:86-92`

GC quét blob `UploadedAt < cutoff` (mặc định 7 ngày) và không có `SoftwareFiles`/`LimitFiles` reference. Nhưng `CompleteUploadAsync` mới insert reference *sau khi* upload xong. Nếu user upload 1 file lớn, để dở > 7 ngày rồi quay lại finalize → blob có thể đã bị GC xóa, finalize fail bí ẩn. Hiếm xảy ra, nhưng nên có cờ `UploadInProgress` hoặc bảo vệ bằng `IncrementBlobReferenceAsync` ngay khi gen presigned URL (count = 0 placeholder, expired tự dọn).

### H6. `appsettings.json` connection string có literal `${ubntubnt}`
`MProjectBackend/MProject.Api/appsettings.json:3`

ASP.NET Core configuration **không expand** `${...}` mặc định. Đó là literal string. Nếu prod đang trông đợi nó được sub → sai. Override bằng env `ConnectionStrings__Default` (docker-compose đã làm), nhưng nên xóa khỏi appsettings để không gây nhầm.

> **✅ ĐÃ FIX (2026-06-02)**:
> - `appsettings.json` → `ConnectionStrings:Default` đặt thành chuỗi rỗng, kèm `_comment` giải thích vì sao và nguồn cấu hình mong đợi cho mỗi môi trường.
> - `Program.cs` → trước `AddDbContext`, kiểm tra `connectionString` không rỗng/whitespace. Nếu rỗng → `throw InvalidOperationException` với hướng dẫn rõ ràng (set qua `appsettings.Development.json` cho dev hoặc env `ConnectionStrings__Default` cho staging/prod, kèm nhắc "ASP.NET Core không expand `${...}`"). Đặt cạnh các guard có sẵn (`JwtKey`, `RefreshTokenPepper`) theo cùng pattern.
> - Build sạch, **296/296 tests** pass.
> - **Tác động khi chạy dev**: KHÔNG đổi. `appsettings.Development.json` đã có `ConnectionStrings:Default = "Host=localhost;...;Password=ubntubnt"` → override file base → guard pass. Test dùng in-memory provider, không động connection string.
> - **Tác động khi chạy prod/staging**: nếu trước đây nào lỡ chạy không set env var → app sẽ "khởi động được" rồi mới fail mơ hồ ở lần đầu query DB (password sai literal). Sau fix → **fail ngay tại startup** với message rõ ràng. Đây là cải thiện chủ ý (fail loud, fail early).

### H7. Storage hash verify mặc định ON, nhưng nếu admin tắt thì là cửa hậu
`MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:67-71, 441-477`

`VerifyUploadHash = false` → upload trustfully theo client-claimed hash. Content-addressable storage tự bảo vệ (nếu hash trùng = content trùng), nhưng **chỉ đúng nếu hash do server tính, không phải client gửi**. Khi flag off, attacker upload blob X với hash giả "Y" → server lưu key=Y, content=X → ai khác request Y nhận X. Đề xuất:
- Cảnh báo loud ở log startup nếu flag tắt.
- Trong production, **bắt buộc enforce true** (kiểm tra `IsProduction()` → throw nếu false).

### H8. JWT key dev hardcode trong appsettings.Development.json
`MProjectBackend/MProject.Api/appsettings.Development.json:5`

OK cho dev nhưng đã commit Git rồi. Hợp lý, nhưng:
- Đảm bảo `.gitignore` không bỏ qua, để pre-commit hook scan secret.
- Thêm check ở Program.cs: nếu `Environment != Development` mà `JwtKey == dev_only_secret_key...` → **throw** (đã check Length>=32, nhưng chưa block giá trị dev).

> **✅ ĐÃ FIX (2026-06-02)**:
> - `Program.cs` sau guard length: thêm guard "non-Development + JwtKey bắt đầu bằng `dev_` (case-insensitive)" → `throw InvalidOperationException` với env name + hướng dẫn override qua env var `JwtKey`. Dùng prefix `dev_` thay vì so sánh literal vì đây là convention của repo cho mọi placeholder dev (xem `dev_installer_token_...`) → bắt được cả trường hợp clone giá trị dev khác.
> - Build sạch, **296/296 tests** pass.
> - **Tác động dev**: không. `builder.Environment.IsDevelopment()` → guard bỏ qua.
> - **Tác động prod/staging**: nếu deploy mà lỡ không override env `JwtKey` → trước: app chạy thầm với key dev đã public trên Git, mọi JWT bị forge được; sau: **fail ngay startup** với message rõ ràng.
> - **Còn lại tương lai (chưa làm)**: pattern này nên áp cho `Agent:InstallerToken` (đã có prefix `dev_`), `Agent:TokenPepper` và `AuthTokens:RefreshTokenPepper` (hai cái cuối là base64 nên không có prefix `dev_` — cần helper khác, ví dụ list "known weak values" hoặc bắt buộc thay đổi giữa env). Để tách thành 1 item nhỏ riêng sau.

### H9. `ResolveManifestAsync` tin tuyệt đối `HaveBlobHashes` từ agent
`MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:185-215`

Agent claim "đã có hash X" → server không issue download URL. Agent độc hại tự gán hash → install fail (self-DoS, không phải critical) **nhưng** nếu sau đó server tin agent đã có file → mark "installed" sai. Đây là rủi ro với scenario H1 (agent giả). Bù lại: agent phải `complete` job để được mark installed, và complete chỉ set Status (không tự tạo PcInstallationRecord với metadata sai). Tạm OK, nhưng nên log warning nếu agent claim quá nhiều hash không nằm trong manifest hiện hành.

---

## 2) Chưa hợp lý / nên cải tiến

### M1. `/poll` regen presigned URL cho mọi file mỗi lần gọi
`MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:420-475`

Mỗi /poll → loop file → presign → tốn MinIO API call. Với 100 agent poll mỗi 30s, 1 version 50 file → 10k presign call/phút. Đề xuất:
- Cache presigned URL theo `(BlobSha256, expiry_bucket)` trong `IMemoryCache` 10–15 phút.
- Hoặc lazy: `/poll` chỉ trả manifest **không có URL**; agent gọi `/agent/v1/manifest/resolve` riêng để lấy URL khi thực sự sắp download.
- Đã có `ResolveManifestAsync` rồi → cân nhắc chuyển logic gen URL hoàn toàn sang `/manifest/resolve`, `/poll` trả lightweight.

### M2. Thiếu token rotation cho agent
TokenHash của agent giữ nguyên từ enroll → vô thời hạn. Đề xuất: heartbeat response **đính kèm token mới** khi token quá `RotateAfter` (ví dụ 30 ngày). Agent verify, lưu, dùng token mới từ request sau. Pattern này gọi là rolling credentials.

### M3. `InstallationJobWatchdog` set Status=Error nhưng không flip lại Idle khi không còn active job
`MProjectBackend/MProject.Application/Services/Software/InstallationJobWatchdogService.cs:131-152`

Sau khi watchdog fail job → `OperationalStatus = Error`, `LastError = ...`. Sau đó user cancel/delete job, hoặc retry thành công → `OperationalStatus` vẫn Error trong UI cho đến khi có job mới complete. UI sẽ hiển thị "Error" mặc dù máy đang chạy bình thường. Đề xuất: tách "transient error" (LastError) ra column riêng, `OperationalStatus` chỉ tính từ active jobs + heartbeat.

### M4. CORS `AllowCredentials` cùng `AllowAnyHeader` cho mọi origin trong allowed list
`MProjectBackend/MProject.Api/Program.cs:148-162`

Nếu auth là Bearer JWT (header), không cần `AllowCredentials`. Loại bỏ để giảm bề mặt CSRF nếu sau này có ai vô tình thêm cookie auth.

### M5. Hostname update theo heartbeat → user có thể bị nhầm máy
`MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:413`

Heartbeat từ agent update Hostname/IpAddress vô điều kiện. Trên nhà máy thường rename PC → mất tracking. Đề xuất: lưu cả `HostnameClaimed` từ agent vs `HostnameLabel` do admin gán; UI hiển thị label, hostname chỉ informational.

### M6. `BackgroundService` chạy trên mỗi instance — sai khi scale ra
`MProjectBackend/MProject.Api/Program.cs:111-114`

`BlobGcService`, `InstallationJobWatchdogService`, `ComputerLivenessWatchdogService` đều là singleton-per-process. Nếu chạy 2 backend instance → mỗi tác vụ chạy 2 lần đồng thời → double sweep, có thể tranh chấp. Đề xuất:
- Dùng **PostgreSQL advisory lock** (`pg_try_advisory_lock(key)`) đầu mỗi sweep — chỉ 1 instance lấy được lock thực hiện.
- Hoặc tách job runner ra service riêng (Hangfire / Quartz / 1 sidecar) chỉ chạy 1 replica.

### M7. Migration chạy trong Program.cs khi app start
`MProjectBackend/MProject.Api/Program.cs:236-239`

`AppDbSeeder.SeedAsync` chạy mỗi app start, mọi instance. Multi-instance → race trong seeder. Production nên:
- Init container chạy `dotnet ef database update` 1 lần.
- API instance chỉ verify schema version, không tự seed.

### M8. Không thấy `Idempotency-Key` cho POST từ agent
Agent retry `/heartbeat`, `/jobs/{id}/complete` khi network flap → có thể double-complete (đang OK vì transition guard, nhưng `PcInstallationRecord` insert có thể `DbUpdateException` xử lý không sạch). Đề xuất header `Idempotency-Key`, server lưu key → response trong 24h.

### M9. Rate limit
`MProjectBackend/MProject.Api/Program.cs:119-146`

Có "auth" và "general" nhưng chưa thấy attach `[EnableRateLimiting]` cho hầu hết endpoint agent. `/poll` nếu agent spam (bug ở agent) → đè DB. Đề xuất:
- Limiter `agent-poll`: PartitionedRateLimiter theo `AgentId` claim, 1 request/30s.
- Limiter `agent-download`: theo `ComputerId`, vài request/giây.

### M10. Concurrency: `ExecuteInTransactionAsync` lồng `SaveChangesAsync` nhiều lần
`MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:85-114`

Nhiều `SaveChanges` trong 1 transaction → nếu retry policy ở DBContext (chưa thấy) → có thể retry partial. Cần đảm bảo `EnableRetryOnFailure` (Npgsql) **không** bật khi dùng explicit transaction, hoặc dùng `IExecutionStrategy.ExecuteAsync`.

### M11. `PostInstallationJobs` Unique Constraint check trong GlobalExceptionHandler bằng string constraint name
`MProjectBackend/MProject.Api/Middleware/GlobalExceptionHandler.cs:72-83`

Fragile — rename migration đổi constraint là vỡ thầm lặng. Đề xuất: tạo extension method `IsViolationOf(constraintName)` + integration test verify constraint name thực tế (chạy migration → query `pg_constraint`).

### M12. Liveness sweep cutoff không atomic với "sau đó agent gọi heartbeat"
`MProjectBackend/MProject.Application/Services/Assets/ComputerLivenessWatchdogService.cs:101-106`

Watchdog `ExecuteUpdateAsync` set Offline trên list ID. Trong khoảng giữa SELECT → UPDATE, agent có thể heartbeat → Online. Update đè lại thành Offline. Vô hại nhưng sau đó liveness flip qua lại. Fix: thêm `&& c.LastSeenAt < cutoff` trong ExecuteUpdate predicate, không dựa vào list từ SELECT cũ.

---

## 3) Kiểm tra file software bị sửa trên máy local

**Đây là vấn đề rất quan trọng cho nhà máy** (tester đôi khi sửa exe/dll/config để bypass test, hoặc lab IT thử "patch" cho dễ vận hành). 3 lớp phòng vệ, làm theo thứ tự tăng dần:

### Lớp 1 — Hash check trước khi launch (BẮT BUỘC)

Server đã có `SoftwareVersionManifestResponse` với SHA-256 từng file. Bổ sung:

**Server-side**:
1. Thêm 1 field `ManifestDigest` = SHA-256 của manifest canonicalized (sort theo path, concat `path|sha256|size`).
2. Endpoint mới `GET /agent/v1/jobs/{installRecordId}/manifest` trả manifest đầy đủ + `ManifestDigest`. Agent cache local.
3. Endpoint `GET /agent/v1/computers/me/installed-manifests` để agent đối chiếu khi mất file cache.

**Agent-side, mỗi lần launch software**:
```
on launch_request(packageId):
    install = local_state.get(packageId)        // versionId + path
    manifest = local_cache.read(install.versionId)
    if manifest == null:
        manifest = fetch_from_server(install.versionId)
        local_cache.write(manifest)
    
    server_digest = HEAD /agent/v1/manifests/{versionId}/digest
    if server_digest != manifest.digest:
        manifest = fetch_from_server(install.versionId)   // có thể version đã bị deprecate
    
    drift = []
    for f in manifest.files:
        path = install.root + f.relative_path
        if not exists(path) or size(path) != f.size:
            drift.append((f, "missing_or_wrong_size"))
            continue
        # fast path: cache (path, mtime) -> hash trong RAM
        h = hash_cache.get_or_compute(path)
        if h != f.sha256:
            drift.append((f, "hash_mismatch"))
    
    if drift:
        POST /agent/v1/integrity-violation { versionId, drift }
        for f, _ in drift:
            re-download from presigned URL (request /agent/v1/files/{f.sha256}/download-url)
            verify hash after download
        re-run check
    
    launch(install.root + manifest.entry_point)
```

**Cache hash trong RAM** để tránh re-hash toàn bộ tree (vài GB) mỗi lần launch:
- Key: `(absolute_path, length, mtime_utc)`.
- Persist lên đĩa khi agent shutdown để khởi động lại không hash từ đầu.
- Nếu mtime thay đổi → bắt buộc rehash.

**Endpoint mới đề xuất ở backend**:
- `POST /agent/v1/integrity-violation` — payload `{versionId, violations: [{path, expectedSha, actualSha, reason}], detectedAt}` → lưu bảng `IntegrityViolations` (audit + alert). Tự tạo InstallationJob loại `Repair` để chỉ re-download file lệch.
- `GET /agent/v1/files/{sha256}/download-url` — agent xin presigned URL cho 1 blob cụ thể, server check blob đó thuộc 1 version assigned cho station.

### Lớp 2 — Filesystem hardening (chống tay-trắng)

Không cho user thường sửa file:

- Cài software vào `C:\ProgramData\MProject\packages\{packageId}\versions\{versionId}\` (không phải `Program Files` để dễ chmod).
- Agent service chạy với account riêng (LocalSystem hoặc `NT SERVICE\MProjectAgent`). Khi extract files, set ACL:
  - `SYSTEM`: Full Control
  - `MProjectAgent service account`: Full Control
  - `Users`: Read & Execute (deny Write/Delete)
  - `Administrators`: Read & Execute + Take Ownership (nếu cần debug)
- Block `icacls` từ user thường (chỉ admin reset được ACL).

Layout đề xuất:
```
C:\ProgramData\MProject\
├── agent\
│   ├── MProjectAgent.exe         (signed, self-verify)
│   └── config.dat                (encrypted DPAPI machine-scope)
├── packages\
│   └── {packageId}\
│       ├── current\              (junction → versions\{activeVersionId})
│       └── versions\
│           ├── {versionId-A}\    (ACL: read-only cho Users)
│           │   ├── manifest.json (signed)
│           │   └── files\...
│           └── {versionId-B}\    (giữ lại để rollback nhanh)
└── state\
    ├── installed.json            (versionId active của mỗi package)
    ├── hash-cache.bin            (path → hash cache)
    └── violations.log
```

Lợi ích:
- Rollback = đổi junction `current` trỏ version cũ, không cần redownload.
- Side-by-side install cho A/B test.

### Lớp 3 — Code signing manifest (mạnh nhất, nếu cần)

Khi release 1 version:
- Backend ký manifest bằng private key (RSA/ECDSA hoặc cosign).
- Trả về `manifest.json` + `manifest.sig`.
- Agent verify chữ ký với public key embed sẵn (hoặc rotate qua heartbeat).
- Tampered manifest dù sửa cả hash list cũng bị detect (không có private key thì không ký lại được).

Bắt buộc nếu có yêu cầu compliance / audit nghiêm (IATF 16949, FDA 21 CFR Part 11 cho dược/y tế).

**Định kỳ verify thêm**: ngoài on-launch, agent quét tất cả package mỗi 15-30 phút trong background. Nếu phát hiện drift trong khi software đang chạy → cảnh báo + chặn launch lần sau (không kill process đang chạy).

---

## 4) Update fleet hàng loạt (rolling deployment)

Hiện tại code gen InstallationJob ngay khi `/poll` và assignment có `TargetVersionId` → tất cả agent có station đó nhận job đồng thời → bão download + bão install. Cần thêm **Rollout** layer.

### Khái niệm cần bổ sung

#### Entity mới
```
Rollout
├── Id
├── SoftwarePackageId
├── TargetVersionId
├── Strategy          (Immediate | Phased | Manual)
├── Status            (Draft | Active | Paused | Completed | Aborted)
├── CreatedBy, CreatedAt
├── StartedAt, CompletedAt
└── PauseReason

RolloutRing                       (1 rollout có N ring)
├── Id, RolloutId
├── Order             (0=canary, 1, 2, …)
├── Name              ("Pilot line 3", "Day 1 morning", …)
├── SelectorJson      (filter computer: by station/dept/tag/percentage)
├── MaxConcurrent     (số job đồng thời trong ring)
├── MaxFailureRate    (% job fail tối đa, vượt → auto-pause)
├── MinSoakHours      (ring trước phải ổn định N giờ rồi mới promote ring sau)
├── Status            (Pending | Active | Completed | Paused | Skipped)
├── StartedAt, CompletedAt
└── Stats             (succeed/failed/inflight counters, cập nhật bởi watchdog)

MaintenanceWindow                 (per-station hoặc per-ring)
├── Id, ScopeType, ScopeId
├── Cron              ("0 19 * * 1-5"  – 19h thứ 2-6)
├── DurationHours     (8)
└── TimeZone          ("Asia/Ho_Chi_Minh")
```

Thêm `Computer.RolloutTags` (jsonb: `{"line": "L3", "shift": "night", "criticality": "low"}`) để selector dùng.

#### Logic thay đổi ở `PollAsync`

```
foreach assignment có TargetVersion:
    // 1. Đã installed/đã có active job → skip (đã có).
    
    // 2. Hỏi rollout
    rollout = active rollout matching (packageId, versionId)
    if rollout == null: skip   // chưa được rollout, đợi
    
    ring = first ring matching (computer, status=Active)
    if ring == null: skip      // chưa tới lượt ring của máy này
    
    // 3. Window
    if not within_maintenance_window(computer, ring): skip
    
    // 4. Concurrency cap (atomic counter trong Postgres)
    inflight = count InstallationJob (ring=ring, status IN (Pending,Downloading,Installing))
    if inflight >= ring.MaxConcurrent: skip
    
    // 5. Failure-rate guard (do background job set)
    if ring.Status == Paused: skip
    
    // 6. Agent busy? (xem AgentBusy bên dưới)
    if computer.IsRunningSoftware(packageId) and ring.Strategy == WaitForIdle: skip
    
    create InstallationJob with RingId = ring.Id
```

### Báo "máy đang chạy" — case "chạy xong mới update"

Agent gửi thêm trong heartbeat:
```json
{
  "runningEntryPoints": [
    { "packageId": "...", "versionId": "...", "pid": 12345, "since": "..." }
  ]
}
```

Server tổng hợp `Computer.BusyPackages` (set). `PollAsync` skip job cho package đang Busy. Khi user đóng app → next heartbeat report empty → next poll nhận job.

Bổ sung tùy chọn `ring.MaxWaitForIdleHours = 8` → nếu chờ quá thì force install (kèm flag agent cảnh báo user "sẽ tắt sau 5 phút").

### Auto-promote / auto-pause

Background job `RolloutSupervisor` chạy mỗi 1-5 phút:
- Với mỗi ring Active, đếm jobs trong N giờ qua: completed, failed, inflight.
- Tính `failure_rate = failed / (failed + completed)`.
- Nếu `failure_rate > ring.MaxFailureRate` → set ring.Status = Paused, gửi alert.
- Nếu ring complete (không còn computer chưa install) + `soak_hours >= MinSoakHours` + `failure_rate <= threshold` → set ring.Status = Completed, promote ring tiếp theo (set Pending → Active, StartedAt = now).

### Selector chia nhóm

Selector tùy nghiệp vụ. Ví dụ:
```json
{
  "include": [
    { "type": "tag", "key": "line", "value": "L3" }
  ],
  "exclude": [
    { "type": "tag", "key": "criticality", "value": "high" }
  ],
  "percentage": 25,
  "stations": ["..."]
}
```

Pattern thực tế hay dùng cho nhà máy:
- **Ring 0 (Canary)**: 1 PC trên line pilot, MinSoak 24h.
- **Ring 1 (Pilot line)**: cả line pilot (~5 máy), MaxConcurrent 2, MinSoak 24h.
- **Ring 2 (Group A)**: 30% các line A, MaxConcurrent 10, MinSoak 4h.
- **Ring 3 (Group B)**: phần còn lại, MaxConcurrent 50.

### Case cụ thể

- "Máy đang chạy → chờ xong" = `Strategy.WaitForIdle` + busy-tracking trong heartbeat.
- "Máy không chạy → update trước" = đó là behavior mặc định khi guard busy-check kết hợp với MaxConcurrent — idle máy tự nhiên được pick trước. Có thể priority-sort theo `(isBusy, lastInstallSucceeded, computerCreatedAt)` để deterministic.
- "Chia từng nhóm" = `RolloutRing` với selector.

### UI cần

- Wizard tạo Rollout: chọn version → chọn template (Immediate / Canary 1%+25%+100% / Custom).
- Dashboard rollout: pie chart progress per ring, fail rate, estimated time to complete, nút Pause/Resume/Abort.
- Per-computer view: trạng thái rollout, ring nào, nguyên nhân nếu skip ("waiting for idle", "outside window", "ring paused").

---

## 5) Các case thực tế khác nên cân nhắc

Tổng hợp các pattern thường gặp trong hệ thống agent quản lý PC trong nhà máy:

| Nhóm | Case | Ghi chú |
|---|---|---|
| **Offline-first** | Agent cache job + manifest local, install được khi mất mạng nếu blob đã có sẵn | Nhà máy WAN không ổn định; tránh agent retry vô tận làm log ngập. Dùng exponential backoff capped 5 phút |
| | Phân biệt `LiveStatus.Offline` (5 phút) vs `Stale` (24h) vs `Decommissioned` (90 ngày) | Hiện chỉ có Online/Offline/Unknown |
| **Inventory drift** | Agent quét Add/Remove Programs, services, processes, registry → so manifest | Phase 2 #18 trong code đã ghi. Phát hiện ai cài lậu, AV gỡ file, vv |
| **Remote command** | UI ra lệnh "restart service", "reboot PC", "collect logs" qua queue | Dùng whitelist command, không exec arbitrary. Lưu audit ai ra lệnh |
| **Log/crash shipping** | Khi software crash → agent zip log + Windows Event Viewer + minidump → upload qua presigned URL | Hữu ích vì người vận hành không gửi log được |
| **Config management** | Push config riêng cho từng máy (đường dẫn PLC, recipe, calibration) | Tách khỏi software package, versioning riêng |
| **License/activation** | Software có license per-PC → backend cấp slot khi assign station, thu hồi khi remove | Tránh vượt số license |
| **Approval workflow** | Release SoftwareVersion, Pin version, Rollout phải qua approval (đã có `ApprovalService`) | Mở rộng `IApprovalTargetHandler` cho 3 target này |
| **Audit log** | Mọi mutation (assign, pin, revoke, release, rollout) → append-only, immutable | Yêu cầu compliance |
| **Bandwidth** | 500 PC × 2GB = 1TB → chậm. Đặt 1-2 máy làm "Local Cache Node" (squid/nginx caching proxy), agent ưu tiên cache local | Hoặc P2P (BITS/BitTorrent style) — phức tạp |
| **Real-time UI** | Thay polling bằng SignalR push (job state, online/offline, rollout progress) | Backend đã có Prometheus, thêm SignalR dễ |
| **Time sync** | Agent + server phải đồng bộ NTP, log warning nếu skew > 5s | Job timeout sai nếu clock lệch |
| **Health & metrics** | Phân biệt liveness vs readiness; emit metric `agents_online`, `jobs_in_flight`, `blob_storage_bytes`, `install_duration_ms` | Đã có Prometheus, chỉ thiếu metric domain |
| **DR & backup** | DB backup hourly + offsite; blob storage snapshot (rclone/restic); recovery procedure documented + drill quarterly | Critical |
| **Rollback nhanh** | Khi pin version mới fail nhiều → 1-click revert pin về version trước | Lưu `PreviousTargetVersionId` |
| **Pre-requisite** | Software cần .NET runtime, VC++ redist → khai báo trong package metadata, agent tự install nếu thiếu | Tránh fail vì thiếu runtime |
| **Multi-factory / multi-tenant** | Nếu sau này nhiều nhà máy → thêm `TenantId` xuyên suốt, scope mọi query | Khó retrofit sau, suy nghĩ sớm |
| **Distributed lock cho background services** | Chuyển BlobGc/Watchdog sang PG advisory lock hoặc 1 leader-elected process | Bắt buộc khi scale ra >1 instance |
| **Idempotency** | Header `Idempotency-Key` cho POST từ agent | Tránh duplicate job khi network flap |
| **PLC/MES integration** | Software trên PC test có thể cần đẩy kết quả test lên MES/SCADA | Nếu agent cũng làm cầu nối, thêm forwarder module |

---

## 6) Tổng kết ưu tiên

### Đã hoàn thành (2026-06-02)
- ✅ **H1** — MAC spoofing trong `/agent/v1/announce`. TOFU first-contact-only ở backend; agent worker bỏ auto re-announce khi 401. Không cần admin pre-register PC.
- ✅ **H2** — Heartbeat cho máy soft-deleted. `AuthenticateAsync` join Computer → 401 sớm; `RecordHeartbeatAsync` filter `!Computer.IsDeleted` → không leak `LastHeartbeatAt`. Đính chính: SQL update đã được global query filter bảo vệ sẵn.
- ✅ **H6** — Literal `${ubntubnt}` trong `appsettings.json`. Đặt rỗng + startup guard fail-loud với hướng dẫn cụ thể.
- ✅ **H8** — Dev JwtKey dùng ngoài Development. Guard prefix `dev_` chạy khi `!IsDevelopment()`.

296/296 backend tests pass sau cả 4 fix.

### Đánh giá rủi ro cho phần CÒN LẠI
**Không item nào còn lại sẽ làm hệ thống chạy sai ở case bình thường.** Anh có thể yên tâm chuyển sang xây/cải tiến tính năng khác, rồi quay lại theo shortlist sau trước khi deploy.

### Shortlist BẮT BUỘC trước deploy production (security hardening)
Theo thứ tự nên làm:

| # | Item | Estimate | Tóm tắt |
|---|---|---|---|
| 1 | **H3** | 30' | Catch `DbUpdateException` unique-violation cho `IX_Computers_MacAddress` trong `AnnounceAsync` → translate thành 409 + log structured warning. Sau H1 + unique index thì race không còn nguy hiểm, chỉ là UX/observability. |
| 2 | **H7** | 5' | Force `Storage:VerifyUploadHash = true` trong production (throw nếu `IsProduction()` && flag false). Default đang ON nhưng cần khoá lại. |
| 3 | **H5** | 1–2h | Khi gen presigned upload URL → insert placeholder reference (count = 0, `UploadInProgress = true`) để `BlobGcService` không ăn dở. Hoặc reset thời điểm `UploadedAt` khi finalize. |
| 4 | **H9** | 30' | Log structured warning trong `ResolveManifestAsync` khi agent claim hash không nằm trong manifest hiện hành — defense + observability cho compromised-agent scenario. |
| 5 | **H4** | 2–3h (+ migration nhỏ) | Per-agent `TokenSalt` + `TokenKeyVersion` → HMAC(pepper[version], salt ‖ secret). Cho phép rotate pepper không invalidate toàn bộ fleet. |
| 6 | **M2** | 2–3h (đổi protocol nhẹ) | Rolling token: heartbeat response đính kèm token mới khi token đã quá `RotateAfter`. Agent verify + lưu + dùng request kế. |
| 7 | **M11** | 30' | Integration test verify tên constraint thực tế trong `pg_constraint` khớp với chuỗi đang hard-code trong `GlobalExceptionHandler` (`UX_ApprovalRequests_Target_Pending`, `IX_Teams_DepartmentId_Name`, `UX_InstallationJobs_Computer_Version_Active`). |

### Shortlist trước khi SCALE ra >1 backend instance
1 instance thì OK; 2+ instance không có những cái này sẽ chạy double / race.

| # | Item | Tóm tắt |
|---|---|---|
| 8 | **M6** | PG advisory lock (`pg_try_advisory_lock`) đầu mỗi sweep của `BlobGcService`, `InstallationJobWatchdogService`, `ComputerLivenessWatchdogService`. |
| 9 | **M7** | Tách migration runner khỏi API startup (init container hoặc bước CI/CD chạy `dotnet ef database update` riêng). API instance chỉ verify schema version. |
| 10 | **M10** | Audit: nếu sau này bật `EnableRetryOnFailure` ở Npgsql → các `ExecuteInTransactionAsync` lồng nhiều `SaveChangesAsync` phải dùng `IExecutionStrategy.ExecuteAsync`. |

### Có thể skip cho MVP đầu, fix theo phản hồi vận hành thực tế
| Item | Lý do hoãn được |
|---|---|
| M1 | Tốn MinIO API call, không sai dữ liệu. Đo lại khi có >50 agent rồi quyết. |
| **M3** | Cái duy nhất "nhìn thấy được" UI: `OperationalStatus = Error` dính cứng sau khi watchdog timeout 1 job, kể cả khi user retry / xoá job sau đó thành công. Tách `LastError` ra column riêng để fix sạch. **Nên fix sớm nếu operator hay complaint**. |
| M4 | Loại bỏ `AllowCredentials` khỏi CORS. Chỉ cần khi nào lỡ thêm cookie auth. |
| M5 | Hostname auto-update theo heartbeat → mất tracking khi rename PC. Thêm `HostnameLabel` admin gán khi cần. |
| M8 | Header `Idempotency-Key` cho POST agent. Hiện transition guard đã chặn phần lớn double-effect. |
| M9 | Attach rate limit policy `auth`/`general` cho agent endpoints (`/poll`, `/heartbeat`, …). Hiện chưa bị spam. |
| M12 | Liveness flip Online↔Offline khi heartbeat đến đúng lúc sweep — review chính tự nói "vô hại". |

### Roadmap dài (không phải fix, là tính năng)
1. **Lớp 1 file-integrity** (manifest digest + endpoint verify + agent verify on launch). Đã design trong mục 3 của review.
2. **Rollout entity + ring + maintenance window**. Đã design trong mục 4.
3. **Inventory drift Phase 2** (agent quét Add/Remove Programs + so manifest).
4. **SignalR cho UI realtime** thay polling (job state, online/offline, rollout progress).
5. **Code signing + lớp 3 hardening** (filesystem ACL + DPAPI machine-scope cho token local).
6. **Local cache node cho LAN** (giảm bandwidth khi fleet lớn).
