# MProject Review And Remediation Checklist

Ngày review gần nhất: 2026-08-22

Phạm vi đã đọc: Backend, Agent, Launcher, Frontend, scripts cài đặt/cập nhật,
Claude memory, tài liệu sản phẩm và đặc tả parity của hệ thống cũ.

Đây là backlog kỹ thuật, không phải danh sách lỗi đã được sửa. Mọi mục mặc định
đều đang mở cho đến khi đạt đủ tiêu chí hoàn thành bên dưới.

## Quy ước trạng thái

- `[ ]` Chưa hoàn thành.
- `[x]` Đã hoàn thành và có bằng chứng kiểm thử.
- Không đánh dấu `[x]` chỉ vì code đã được thay đổi.
- Khi bắt đầu một mục, ghi người thực hiện, branch/PR và ngày bắt đầu vào dòng
  `Theo dõi` của mục đó.
- Khi đóng một mục, ghi test đã chạy, môi trường đã xác minh và commit/PR.

## Definition Of Done

Một finding chỉ được đóng khi tất cả điều kiện phù hợp đều đạt:

- [ ] Invariant hoặc quyết định nghiệp vụ đã được chốt rõ.
- [ ] Code fix bao phủ mọi đường gọi liên quan, không chỉ UI hoặc happy path.
- [ ] Có test tự động tái hiện lỗi trước fix và pass sau fix.
- [ ] Với lỗi Npgsql, concurrency, Windows Service, IIS hoặc filesystem, đã có
      integration test trên provider/môi trường thật tương ứng.
- [ ] Security negative test chứng minh actor/resource không hợp lệ bị từ chối.
- [ ] Runbook, UI contract và `PROJECT_MEMORY.md` đã được đồng bộ nếu hành vi đổi.
- [ ] Không đưa secret, private key, token hoặc credential thật vào test/docs.

## Thứ tự triển khai đề xuất

1. P0: chặn đường thực thi/gửi payload trái phép trên station.
2. P1-A: sửa state machine deployment, upload và authentication lifecycle.
3. P1-B: sửa tính toàn vẹn release, log, migration, OTA và blob.
4. P1-C: đồng bộ approval frontend với backend.
5. P2: hardening, khả năng vận hành và build tái lập.

## Bảng tổng quan

| Trạng thái | ID | Mức | Khu vực | Tóm tắt |
| --- | --- | --- | --- | --- |
| [ ] | SEC-001 | P0 | Agent | Manifest/package path có thể thoát install root |
| [ ] | SEC-002 | P0 | Agent/IIS | Agent chấp nhận mọi TLS certificate |
| [ ] | SEC-003 | P0 | RBAC/Approval | Viewer có quyền publish/deploy và activation bypass approval |
| [ ] | SEC-004 | P0 | Enrollment | InstallerToken dùng chung còn plaintext trên station |
| [ ] | UPL-001 | P1 | TUS | `document.own` có thể ghi vào software Draft |
| [ ] | UPL-002 | P1 | TUS/Storage | Blob upload bỏ quota và nằm ngoài GC |
| [ ] | JOB-001 | P1 | Deployment | Remove/Deactivate không dừng install đang chạy |
| [ ] | JOB-002 | P1 | Deployment | Job thiếu concurrency và crash-resume protocol |
| [x] | AUTH-001 | P1 | RBAC | Role manager có thể tự nâng quyền/sửa system role |
| [ ] | AUTH-002 | P1 | Authentication | Disable/reset không vô hiệu access JWT |
| [x] | AUTH-003 | P1 | Team/RBAC | Membership hết hạn nhưng quyền leader/cache còn hiệu lực |
| [x] | REL-001 | P1 | Software | Released version chưa immutable |
| [x] | REL-002 | P1 | Software | `AutoRemoveOnUnassign=false` vẫn uninstall |
| [x] | LOG-001 | P1 | Agent | Test log scanner bị starvation/skipped timestamp |
| [ ] | DEP-001 | P1 | Deploy/DB | Rollback binary không rollback destructive migration |
| [ ] | AGT-001 | P1 | Agent/Legacy | Entry `.bat/.jar/.py` không chạy từ Windows Service |
| [ ] | OTA-001 | P1 | Agent/Launcher | OTA overlay không exact và không crash-atomic |
| [x] | STO-001 | P1 | Blob/Auth | Agent tải được blob không thuộc job/station |
| [x] | STO-002 | P1 | Blob/GC | GC có TOCTOU với writer/cache và xóa DB trước object |
| [x] | CFG-001 | P1 | Config/Approval | Xóa override pending để approval bị kẹt |
| [x] | FE-001 | P1 | Frontend | Pending assignment vẫn hiện Activate và gate sai quyền |
| [x] | FE-002 | P1 | Frontend | Approver không thấy đầy đủ target/change snapshot |
| [x] | FE-003 | P2 | Frontend | Approval pagination bị cắt ở 100 và không polling đúng |
| [x] | FE-004 | P2 | Frontend | Response model cũ có thể ghi đè deployment matrix mới |
| [x] | FE-005 | P2 | Frontend/Build | Hai lockfile lệch manifest, clean build không tái lập |
| [ ] | IPC-001 | P2 | Launcher IPC | Local user điều khiển app hoặc độc chiếm named pipe |
| [ ] | AGT-002 | P2 | Agent Auth | Race rotation có thể phục hồi token cũ đã vô hiệu |
| [ ] | OTA-002 | P2 | OTA Signing | Không có quy trình rotate/revoke signing key từ xa |
| [ ] | CFG-002 | P2 | Agent Config | `ReloadConfig` ACK nhưng không áp cấu hình |
| [ ] | OPS-001 | P2 | Deploy | Watchdog config fresh install lệch app defaults |
| [ ] | AUTH-004 | P2 | Authentication | `SingleDeviceLogin` có race phát nhiều session |
| [ ] | SEC-005 | P2 | Secrets | Development config chứa credential cố định tracked |
| [ ] | ARCH-001 | P2 | Domain Events | Dispatcher hiện không phải transactional outbox |

## P0 - Chặn trước rollout tiếp theo

### SEC-001 - Chặn filesystem path escape trên Agent

Trạng thái: `IMPLEMENTED, LIVE CANARY ACTIVE; MIGRATION/ROLLBACK PENDING`.
Lớp containment phía Agent, validation phía Backend và các consumer đọc cache cũ đã được implement
ngày 2026-08-18. Ngày 2026-08-27, Agent đã chuyển root legacy sang `PackageId`
bằng rename cùng volume, reconcile cache/catalog có crash recovery, giữ nguyên
cả cây `PreserveLocal`, giữ handle thư mục xuyên suốt deploy/cleanup/uninstall
và installer khóa boundary của install root. Junction barrier đã pass dưới
LocalSystem/session 0. Canary thật trên `UTPG3TM0T01 / FT1` đã nâng Agent và
xác minh ACL biên; job migrate payload legacy chưa được server phát tới Agent.

Tác động: manifest hoặc package name không an toàn có thể ghi file ngoài
`D:\Apps` bằng quyền của Windows Service, thường là SYSTEM.

Bằng chứng:

- `MProjectAgent/Services/InstallDirGuard.cs`
- `MProjectAgent/Services/InstallDirectoryService.cs:30-61,122-157`
- `MProjectAgent/Storage/CacheIndex.cs`
- `MProjectAgent/Services/JobExecutor.cs:93-96,475-507`
- `MProjectAgent/Services/InventoryReporter.cs:101-140`
- `MProjectAgent/Commands/HardenCommand.cs:72-105`
- `MProjectBackend/MProject.Application/Utils/WindowsPathRules.cs`
- `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs`
- `MProjectBackend/MProject.Application/Services/Software/SoftwarePackageService.cs`
- `MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:451-511`

Checklist:

- [x] Cài mới dùng immutable `PackageId` làm tên thư mục, không dùng package name.
- [x] Bridge package cũ theo catalog/cache; từ chối dual root và shared root thay vì ghi đè.
- [x] Uninstall tìm root từ cả catalog và cache, bao gồm package không có entry point.
- [x] Migrate vật lý root legacy sang `PackageId` mà vẫn giữ `PreserveLocal` và có crash recovery.
- [x] Backend từ chối rooted path, `.`/`..`, ADS, reserved name và collision ở ingest/release.
- [x] Agent canonicalize bằng `GetFullPath`, preflight cả manifest và kiểm containment.
- [x] Inventory/harden bỏ qua cache record có root/path không an toàn.
- [x] Agent từ chối reparse point/junction đã tồn tại trước thao tác file.
- [x] Uninstall xóa filesystem không-follow-reparse trước khi xóa catalog/cache state.
- [x] Harden ACL boundary thư mục deploy và đóng race thay junction giữa check/write bằng directory handle.
- [x] Chạy Agent unit/regression tests: 244/244 pass ngày 2026-08-27.
- [x] Windows junction barrier test pass dưới LocalSystem (`S-1-5-18`), cùng identity/session 0 với service production.
- [x] Chạy manifest giả end-to-end: Agent từ chối traversal sau resolve độc lập với Backend và không ghi file/root.
- [x] Canary thật: standard user không tạo được top-level root dưới `D:\Apps`; Agent LocalSystem vẫn online/kết nối Backend.
- [ ] Chạy migration/canary/rollback matrix và đối chiếu không còn dual/legacy root.

Tiêu chí đóng: không input nào tạo/sửa/xóa file ngoài root; test chạy dưới cùng
identity/môi trường với service production.

Theo dõi: owner: Codex | branch/PR: local workspace | bắt đầu: 2026-08-27 | đóng: ___

#### Bàn giao canary SEC-001 ngày 2026-08-27

Mục tiêu tiếp theo là hoàn tất đúng **SEC-001 trước**, chưa chuyển sang SEC-002.
Workspace đang có nhiều thay đổi P0/P1 của người dùng và các phiên trước; không
reset/checkout hoặc gom sửa ngoài phạm vi. Các file code chính của SEC-001 là:

- `MProjectAgent/Services/InstallDirGuard.cs`
- `MProjectAgent/Services/InstallDirectoryService.cs`
- `MProjectAgent/Services/JobExecutor.cs`
- `MProjectAgent/Services/BlobCacheService.cs`
- `MProjectAgent/Storage/CacheIndex.cs`
- `scripts/install-agent.ps1`
- `MProjectAgent.Tests/InstallDirGuardTests.cs`
- `MProjectAgent.Tests/JobExecutorInstallRootTests.cs`
- `MProjectAgent.Tests/InstallRootMigrationTests.cs`

Trạng thái code/validation trước canary:

- Agent full suite pass `244/244`; focused SEC-001 pass `67/67`.
- PowerShell 5.1 parse `install-agent.ps1` pass.
- Malicious resolved-manifest E2E từ chối `../outside.txt` độc lập với Backend.
- Directory-handle junction fixture pass dưới LocalSystem/session 0.

Trạng thái trạm canary hiện tại:

- Máy `UTPG3TM0T01`, station `FT1`; service `MProjectAgent` chạy LocalSystem.
- Agent đã được thay thủ công từ `2.0.0.0` sang canary `2.0.0.1`, giữ nguyên
  `appsettings.json`, state và service registration; service đang Running và có
  kết nối tới Backend local (`/health/live` trả 200).
- Không chạy `install-agent.ps1` trên trạm này trong canary SEC-001: config hiện
  còn dùng loopback HTTP/legacy bootstrap, không thỏa strict-TLS rollout của
  SEC-002. Không đọc, in hoặc chép token từ `agent-state.json`.
- Backup rollback: `D:\MProjectCanaryBackup\SEC001_20260827_144653`. ACL của
  thư mục này chỉ cho `SYSTEM` và `Administrators`; nó chứa state/config nhạy
  cảm, binary Agent cũ và bản sao payload trước canary, không được mở quyền hoặc
  đưa vào Git.
- Backup BOM11 đã đối chiếu SHA-256 đủ `2238` file, `715611489` byte; manifest
  nằm trong backup. `D:\Apps` đã có deny ACE trên boundary và thao tác tạo root
  bằng user thường trả `Access is denied`, không để lại fixture.
- Root legacy BOM11 hiện vẫn là
  `D:\Apps\FCD101001_FTU102431816_FW32127_BOM11_MEBOM10_FT1`.
- PackageId BOM11: `418f88ab-5606-4997-939d-f98b34f42518`; root đích mong đợi:
  `D:\Apps\418f88ab-5606-4997-939d-f98b34f42518`.
- BOM19 (`0aa384ff-07a0-46a5-b98a-9a81fa555641`) giữ nguyên làm đối chứng.
- Người dùng đã release một package/version mới lên FT1, nhưng sau hơn hai phút
  Agent chưa nhận job: catalog vẫn trỏ hai root legacy và không có root mới.
  Cần kiểm tra Deployment Matrix xem **version mới của chính package BOM11** đã
  được pin/deploy tới `UTPG3TM0T01 / FT1` hay mới chỉ release/tạo package khác.

Trình tự tiếp tục:

1. Xác nhận Deployment Matrix tạo job cho packageId BOM11 ở trên; không phát
   package không liên quan chỉ để thử migration.
2. Theo dõi tới khi Agent nhận job. Root BOM11 phải được rename cùng volume sang
   root PackageId; legacy root phải biến mất và BOM19 không đổi.
3. Đối chiếu catalog (`InstallRoot`, `ExePath`), cache `DeployedFiles`, file count,
   manifest SHA-256 và `PreserveLocal`; xác nhận job terminal thành công và app
   launch/stop bình thường.
4. Diễn tập rollback bằng backup đã khóa ACL: dừng Agent, giữ lại cây sau migrate
   theo tên quarantine/backup (không xóa), phục hồi binary `2.0.0.0`, catalog,
   cache và ACL gốc, phục hồi root legacy; bật Agent cũ và xác minh healthy.
5. Nâng lại `2.0.0.1`, chạy lại/reconcile migration, xác minh trạng thái cuối là
   root PackageId. Chỉ tick/đóng SEC-001 khi cả migration và rollback matrix pass.

Các script tạm canary nằm dưới
`C:\Users\Administrator\AppData\Local\Temp\sec001-*.ps1`; phải đọc/kiểm tra lại
trước khi dùng, và xóa sau khi SEC-001 đóng. Không coi đường dẫn temp hoặc binary
publish là artifact phát hành lâu dài.

### SEC-002 - Khôi phục TLS trust đúng cho Agent

Trạng thái: `IMPLEMENTED, ROLLOUT PENDING`. Agent đã dùng OS trust mặc định,
scripts dùng canonical hostname và có helper migrate config trước OTA. Chưa đóng
cho đến khi strict TLS được kiểm tra trên station thật và fleet đã migrate URL.

Tác động: MITM trong LAN có thể nhận `X-Agent-Token` và trả manifest/payload giả.
SHA từ cùng server giả không tạo được trust.

Bằng chứng:

- `MProjectAgent/appsettings.json:9-20`
- `MProjectAgent/Program.cs:87-100`
- `scripts/install-server.ps1:123-136,217-226,439-475`
- `scripts/prepare-deploy.ps1:142-145,241`

Checklist:

- [x] Chọn URL canonical theo DNS/hosts alias, mặc định `https://te:8443`.
- [x] Đồng bộ certificate SAN, IIS binding và URL trong scripts/artifact.
- [x] Bỏ `DangerousAcceptAnyServerCertificateValidator` khỏi production path.
- [x] Artifact mới không còn mang `AllowUntrustedCertificate`.
- [ ] Cân nhắc public-key/certificate pinning và quy trình rotate pin.
- [ ] Integration test từ station: cert đúng pass, cert giả/expired/wrong-host fail.
- [ ] Xác minh token không được gửi sau TLS validation failure.

Tiêu chí đóng: fresh factory install handshake được bằng URL được quảng bá và
Agent từ chối certificate giả trước khi lộ credential.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

### SEC-003 - Siết Viewer và đóng approval activation bypass

Trạng thái: `IN PROGRESS`. Viewer read-only reconciliation, forward-only data
migration, activation guard, system-role guard và package policy API đã được
implement ngày 2026-08-18. Policy được xác nhận là optional theo từng package;
audit chỉ đọc trên database được cấu hình hiện tại không tìm thấy Viewer grant
cấm, active+Pending hoặc active assignment thiếu approval đúng policy. Audit
phải được chạy lại trên từng database triển khai khác trước rollout.

Tác động: mọi user thường hiện có thể create, publish và assign software; người
submit assignment có thể tự gọi Activate dù request đang chờ duyệt.

Bằng chứng:

- `MProjectBackend/MProject.Infrastructure/AppDbSeeder.cs:320-414`
- `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:55-101,138-168`
- `MProjectBackend/MProject.Api/Controllers/Software/SoftwareAssignmentsController.cs:42-71`
- `MProjectBackend/MProject.Application/Models/SoftwareModels.cs:8-26,245-260`

Checklist:

- [x] Chốt Viewer production là read-only; bỏ cả quyền tải software đã mở tạm.
- [x] Thu 5 software grants bằng migration forward-only và exact seeder reconciliation.
- [x] Legacy permission fan-out không còn cấp granular grants cho system role.
- [x] Chặn add/remove permission của system role qua `RoleService`.
- [x] Endpoint Activate từ chối Pending và yêu cầu Approved đúng current policy.
- [x] Expose/validate policy nullable trên package API; chỉ nhận active policy đúng target.
- [x] Thêm policy selector optional trên UI quản trị package.
- [x] Báo cáo và xử lý active+Pending/active-without-Approved từ dữ liệu lịch sử trên database được cấu hình hiện tại.
- [ ] Permission riêng cho submit, approve và administrative override nếu cần.
- [x] Backend test suite 908/908 gồm seeder, system-role và direct-activate pass.
- [ ] Chạy HTTP role matrix: Viewer GET được nhưng create/release/assign/activate bị 403.

Tiêu chí đóng: user read-only không publish/deploy; assignment cần duyệt không
thể active nếu chưa có ApprovalAction hợp lệ.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

### SEC-004 - Thay global InstallerToken bằng enrollment một lần

Trạng thái: `IMPLEMENTED, PHYSICAL ACL TEST PENDING`. Luồng enrollment theo máy
dùng token có hạn và xóa hash sau lần dùng đầu; shared Agent artifact bị script
package/prepare-deploy cưỡng chế để trống InstallerToken. Legacy self-announce
vẫn tồn tại như compatibility path khi server operator chủ động cấu hình global
token, nên rollout phải để trống setting này.

Tác động: local user đọc token còn lại trong config có thể tạo station/Agent
credential mới qua anonymous self-announce.

Bằng chứng:

- `scripts/prepare-deploy.ps1:147-168`
- `scripts/install-agent.ps1:31-38,93-96,164-190`
- `MProjectAgent/Services/AgentWorker.cs:371-381,428-457`
- `MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:45-69`

Checklist:

- [x] Phát enrollment material riêng cho từng machine và chỉ dùng một lần.
- [ ] Truyền secret qua installer/protected store, không bake vào shared artifact.
- [x] Xóa enrollment material trong config/backups sau khi cấp Agent token thành công.
- [x] Đặt ACL rõ cho install directory và ProgramData state, có guard chống root/system path.
- [x] Có cơ chế disable self-announce/global bootstrap sau provisioning.
- [ ] Test standard user không đọc secret và token đã dùng không enroll máy thứ hai.

Tiêu chí đóng: artifact dùng chung không chứa reusable enrollment credential.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

## P1-A - Upload, authentication và deployment state

### UPL-001 - Tách authorization TUS theo upload purpose

Trạng thái: `IN PROGRESS`. Client mới dùng capability HMAC ngắn hạn và legacy
completion branch đã bị khóa; còn thiếu HTTP negative matrix để đóng finding.

Bằng chứng: `MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs`;
`MProjectBackend/MProject.Application/Services/Uploads/TusUploadCapabilityService.cs`.

- [x] Parse/validate purpose trước khi authorize completion.
- [x] Software upload bắt buộc permission và target version còn Draft.
- [x] Document upload bắt buộc ownership/quota của đúng user.
- [x] Dùng capability ngắn hạn bind actor, purpose, target, hash, size và expiry.
- [ ] Test `OwnDocuments`-only không thể tạo `SoftwareFile`.
- [x] Bỏ hoặc khóa legacy completion branch khi client đã migrate.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### UPL-002 - Quản lý staging blob, quota và abandoned upload

Bằng chứng: `TusUploadHandler.cs:249-302`; `BlobGcService.cs:86-96`.

- [ ] Tạo upload reservation/staging row trước khi nhận bytes.
- [ ] Reserve quota theo user trước upload, kiểm lại atomically khi finalize.
- [ ] Mọi object hoàn tất phải có owner/state/expiry để GC nhìn thấy.
- [ ] GC được upload bỏ dở và reservation hết hạn.
- [ ] Rate/size/count quota không bị vượt bởi concurrent requests.
- [ ] Integration test abandoned upload và concurrent quota race.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### JOB-001 - Cancel install thật khi assignment bị remove/deactivate

Trạng thái: `IN PROGRESS`. CancelJob đã được persist cho job đang chạy và Agent
dùng linked token để ngắt pipeline; uninstall intent cũng được tạo từ job cài
dở dù chưa có Installed record. Inventory của catalog lạ được đối chiếu lại
package/version, tạo record và enqueue uninstall theo policy thay vì bỏ qua.
Còn thiếu barrier E2E trên filesystem/process thật.

Bằng chứng:

- `StationSoftwareAssignmentService.cs:188-260,494-559`
- `MProjectAgent/Services/JobExecutor.cs:95-138,341-355`
- `InstallationJobService.cs:480-500`
- `PcInventoryService.cs:49-58`

- [x] Server gửi/persist CancelJob cho mọi active/partial install.
- [x] Agent link cancellation vào download, deploy, catalog và launch.
- [x] Terminal callback khác status trả `409/410`, không trả success no-op.
- [x] Persist uninstall intent kể cả chưa có Installed record.
- [x] Reconcile unknown inventory/catalog thay vì bỏ qua.
- [ ] Barrier E2E: pause Deploy, remove assignment, resume, assert không còn file/process/catalog.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### JOB-002 - Làm job state machine atomic và crash-resumable

Trạng thái: `IN PROGRESS`. InstallationJob có optimistic concurrency Version;
mọi tracked transition tăng generation nên stale progress/watchdog không thể
ghi đè terminal transition. Durable Agent journal/resume protocol vẫn chưa có.

Bằng chứng: `InstallationJobService.cs:37-106,441-500,626-641`; `AgentWorker.cs:336-368`.

- [x] Dùng conditional update hoặc concurrency token cho mọi transition.
- [x] Một transition terminal không thể bị progress/watchdog hồi sinh.
- [ ] Agent lưu durable local journal trước ACK/deploy/complete.
- [ ] Resume được Pending, Downloading và Installing theo lease/generation.
- [ ] Complete server và local catalog có protocol idempotent, retry được.
- [x] Npgsql concurrency tests: cancel-vs-progress, watchdog-vs-complete.
- [ ] Restart test sau ACK và test server commit rồi mất response.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AUTH-001 - Áp grantability/no-self-elevation cho RoleService

Bằng chứng: `MProjectBackend/MProject.Application/Services/Identity/RoleService.cs:101-130`.

- [x] Reuse invariant grantability/no-self-impact cho RoleService.
- [x] Actor không được grant permission global mà chính actor không có.
- [x] Chặn mutation ảnh hưởng role actor đang hoặc sẽ giữ.
- [x] Bảo vệ Admin/Viewer/system roles và expose `isSystem` cho UI read-only.
- [x] Backend test delegated manager, self-held role và system-role mutation pass.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AUTH-002 - Vô hiệu access JWT khi user/security state đổi

Bằng chứng: `MProjectBackend/MProject.Api/Program.cs:317-333`; `AuthService.cs:159-175`; `UserService.cs:429-465`.

- [x] Thêm `AuthVersion`/security stamp vào User và JWT claim.
- [x] Tăng version khi disable, reset/change password và logout-all.
- [x] `OnTokenValidated` kiểm user Active và version hiện tại.
- [x] Approval approver resolver bắt buộc user Active.
- [ ] Integration test issue token, disable/reset, rồi approve phải fail.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AUTH-003 - Ràng buộc TeamLeader vào membership còn hiệu lực

Trạng thái: `COMPLETED` ngày 2026-08-27.

Bằng chứng: `TeamService.cs:304-350,402-424`; `AuthorizationService.cs:121-141,201-233`.

- [x] Clip role interval theo membership interval hoặc join membership khi authorize.
- [x] Mọi leader action kiểm Active user và Active UserTeam.
- [x] Cache key/TTL phản ánh team membership generation và next boundary.
- [x] TimeProvider test deny đúng ngay sau membership expiry.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

## P1-B - Artifact, storage và vận hành

### REL-001 - Bảo đảm Released version immutable

Trạng thái: `COMPLETED` ngày 2026-08-27. Relational barrier test dùng hai
`DbContext` trên PostgreSQL thật xác nhận release thắng race thì upload
finalization bị từ chối và không để lại file/blob mới.

Bằng chứng: `SoftwareVersionService.cs:392-412`; `ConfigParameterService.cs:493-720`; `SoftwareFileService.cs:374-393`.

- [x] Entry point/icon/watch/health chỉ sửa được khi Draft.
- [x] File/config mutation lock hoặc recheck status trong cùng transaction.
- [x] Thêm concurrency token/generation vào version artifact.
- [x] PostgreSQL two-context test: mutation pause, release, resume phải conflict.
- [x] Manifest của cùng released version luôn có cùng artifact identity.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### REL-002 - Thực thi hoặc loại bỏ AutoRemoveOnUnassign

Trạng thái: `COMPLETED` ngày 2026-08-27. Khi flag false, payload đã cài được giữ
lại dưới trạng thái unmanaged; job đang chạy vẫn bị cancel để không tiếp tục
một intent assignment đã bị thu hồi.

Bằng chứng: `StationSoftwareAssignmentService.cs:188-231,537-557`; `InstallationJobService.cs:150-199`.

- [x] Chốt behavior khi flag false: giữ installed/unmanaged.
- [x] Deactivate/remove và orphan poll dùng cùng một policy.
- [x] Sửa test đang codify việc false vẫn uninstall.
- [x] N/A — tiếp tục giữ flag trong DB/API/UI vì flag có semantics được thực thi.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### LOG-001 - Sửa TestResultScanner cursor

Trạng thái: `COMPLETED` ngày 2026-08-27.

Bằng chứng: `MProjectAgent/Services/TestResultScanner.cs:82-123`; `AgentService.cs:954-967`.

- [x] Cursor ổn định theo `(LastWriteTime, relativePath)` hoặc immutable event ID.
- [x] Drain dữ liệu mới trước, overlap replay chỉ dùng capacity còn lại.
- [x] Persist cursor atomically sau server acknowledgement.
- [x] Multi-cycle test với hơn batch size trong overlap và timestamp bằng nhau.
- [x] Assert mọi file cuối cùng được accepted đúng một lần về mặt nghiệp vụ.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### DEP-001 - Làm migration/rollback production nhất quán

Bằng chứng:

- `MProjectBackend/MProject.Infrastructure/AppDbSeeder.cs:44-52`
- `scripts/update-server.ps1:154-178,223-241`
- `Migrations/20260810011135_RemoveConfigValueSets.cs:14-44`

- [x] Chọn một migration owner: deploy script trong production; startup chỉ auto-migrate mặc định ở Development.
- [x] `-SkipSchema` thực sự ngăn startup migration trong production.
- [ ] Chuyển destructive change sang expand/contract có compatibility window.
- [ ] Preflight phát hiện migration không rollback được.
- [ ] Có DB backup/restore procedure đã diễn tập.
- [ ] Fault test: health fail sau migrate, old code phải healthy sau rollback.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AGT-001 - Chạy đúng legacy entry `.bat/.jar/.py`

Trạng thái: `IN PROGRESS`. Command mapping đã có và fixture `.bat` thật trên
Windows xác nhận supervisor attach vào configured child watch-process sau khi
wrapper thoát. Còn thiếu lần chạy dưới identity/session của Windows Service.

Bằng chứng: `ProcessSupervisor.cs:198-212`; `InteractiveProcessLauncher.cs:50-83`; `docs/uistore_parity_spec_verified.md:54-58`.

- [x] Chốt danh sách entry type hỗ trợ và runtime prerequisite.
- [x] Build command rõ cho `cmd /c`, `java -jar` và Python interpreter.
- [x] Quote argument/path an toàn, giữ WorkingDirectory đúng entry folder.
- [x] Watch process hoạt động khi wrapper khác process thực tế.
- [ ] Windows Service integration test bằng fixture legacy `.bat` thật.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### OTA-001 - Làm update/rollback exact và crash-atomic

Bằng chứng: `ApplyUpdateCommand.cs:52-93,105-137`; `AgentUpdater.cs:77-95`; `scripts/package-agent.ps1:100-109`.

- [ ] Bundle có signed manifest cho mọi file và version/protocol metadata.
- [ ] Cài vào versioned directory rồi atomic switch active version.
- [ ] Chỉ preserve allowlist station-owned state/config.
- [ ] Rollback tạo exact old snapshot, không giữ new-only/old-only file sai.
- [ ] Recovery marker được xử lý trước version short-circuit khi reboot.
- [ ] Chờ stable readiness/version heartbeat, không chỉ SCM `RUNNING` tức thời.
- [ ] Fault injection sau từng file/switch: chỉ được all-old hoặc all-new.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### STO-001 - Bind agent blob download vào resource/job

Trạng thái: `COMPLETED` ngày 2026-08-27.

Bằng chứng: `MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:121-133`; `LocalStorageService.cs:72-75`.

- [x] Download capability bind agentId, job/releaseId, SHA và expiry.
- [x] Job phải active và thuộc đúng computer/station tại thời điểm tải.
- [x] UserDocument không bao giờ tải được bằng agent credential.
- [x] Test agent A, agent B, cancelled job và private document.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### STO-002 - Loại bỏ Blob GC TOCTOU

Trạng thái: `COMPLETED` ngày 2026-08-27. Barrier tests trên PostgreSQL thật
xác nhận tombstone claim thắng race sẽ chặn cả software upload writer và config
render writer trước khi object deletion được tiếp tục.

Bằng chứng: `BlobGcService.cs:86-134,186-204,251-260`; `ConfigRenderService.cs:191-208`.

- [x] Dùng per-SHA claim/lease hoặc advisory lock với writers.
- [x] Conditional delete kiểm lại reference và cutoff tại commit.
- [x] Dùng tombstone/deletion queue; storage failure phải retry được.
- [x] Không xóa DB row trước khi object deletion có trạng thái durable.
- [x] Relational concurrency test GC-vs-upload và GC-vs-render touch.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### CFG-001 - Giải quyết lifecycle config override pending

Trạng thái: `COMPLETED` ngày 2026-08-27. Active value và pending replacement
được lưu riêng; chỉ approval thành công mới promote pending thành active.

Bằng chứng: `ConfigValueOverrideService.cs:833-889,1106-1134`; `ConfigValueOverrideApprovalHandler.cs:46-83`.

- [x] Delete pending override cancel approval trong cùng transaction.
- [x] Pending revision không làm biến mất Active value hiện tại.
- [x] Tách active revision khỏi draft/pending replacement nếu cần.
- [x] Test delete pending, replace pending và reject/cancel lifecycle.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

## P1-C - Frontend approval contract

### FE-001 - Không cho pending assignment xuất hiện như inactive

Bằng chứng:

- `src/pages/Software/hooks/use-deployment-matrix.ts:260,354`
- `src/pages/Software/components/ListView.tsx:181`
- `src/constants/access-rules.ts:83`
- `src/constants/permissions.ts:28`

- [x] Assignment DTO trả approval state và intended version rõ ràng.
- [x] Pending/rejected/cancelled có UI state riêng, không hiện Activate trái phép.
- [x] Route read gate khớp `software.read` và action gate khớp permission backend.
- [x] Pin/toggle/remove chỉ render và execute khi được phép.
- [x] Test role matrix và pending flow không gọi `/activate`.

Theo dõi: owner: Codex | branch/PR: local workspace | bắt đầu: 2026-08-25 | test/PR: backend 910/910, frontend 194/194, build pass, lint 0 errors

### FE-002 - Hiển thị đầy đủ đối tượng/thay đổi trước khi approve

Bằng chứng: `src/types/approvals.ts:1-23`; `src/components/approvals/ApprovalActionDrawer.tsx:193-211`.

- [x] Type frontend hỗ trợ mọi target backend hiện có.
- [x] API trả immutable target/change snapshot phù hợp từng loại request.
- [x] Drawer hiển thị target, scope, model/station, reason và diff quan trọng.
- [x] Requester nhìn thấy Cancel khi `currentUserCanCancel=true`.
- [x] Test SoftwareVersion, SoftwareAssignment, ConfigValueOverride và OverrideFile.

Theo dõi: owner: Codex | branch/PR: local workspace | bắt đầu: 2026-08-25 | đóng: 2026-08-26 | validation: backend 912/912; frontend 201/201; build pass; lint 0 errors

#### Bàn giao phiên 2026-08-25

- FE-001 đã hoàn thành: DTO có approval state/intended version; UI phân biệt
  Pending/Rejected/Cancelled; route/action gate và direct activate guard đã có
  test tập trung.
- FE-002 đang thực hiện: frontend đã khớp bốn target type backend
  (`SoftwareVersion`, `SoftwareAssignment`, `OverrideFile`,
  `ConfigValueOverride`) và contract `scopeKind`/`scopeId`.
- Drawer hiện đã hiển thị target type/ID, model-station, scope, reason trong
  timeline và cho requester hủy request Pending khi `currentUserCanCancel=true`.
- Backend đã enrich `modelStation` cho cả bốn target type ngày 2026-08-26;
  SoftwareAssignment/ConfigValueOverride không còn trả context rỗng và override
  theo station có thêm model cha. Đây vẫn là live context, chưa phải snapshot.
- Backend đã persist target/change snapshot JSONB tại thời điểm submit và trả
  snapshot typed trong detail API ngày 2026-08-26. Test sửa entity sau submit xác
  nhận snapshot của SoftwareVersion, SoftwareAssignment, ConfigValueOverride và
  OverrideFile không đổi; request lịch sử không có snapshot vẫn đọc được.
- FE-002 đã hoàn thành ngày 2026-08-26: drawer ưu tiên immutable snapshot, hiển
  thị action, target, model/station/computer, scope và bảng `before -> after`;
  reason tiếp tục hiển thị trong approval timeline. Request lịch sử không có
  snapshot fallback về live `modelStation` và không giả lập snapshot bất biến.
- Frontend test matrix bao phủ SoftwareVersion, SoftwareAssignment,
  ConfigValueOverride, OverrideFile và legacy fallback; đồng thời assert live
  context không ghi đè snapshot.
- Validation gần nhất: backend 912/912; frontend 201/201; build pass; lint 0
  errors (19 baseline warnings); `git diff --check` trên local Windows.
- FE-003 đã hoàn thành ngày 2026-08-26: Inbox/My Requests gọi từng trang 12 bản
  ghi với `page`/`pageSize`, dùng `total` server cho tổng số trang và không còn
  tải snapshot 100 bản ghi để slice cục bộ. Hai màn polling nền mỗi 60 giây;
  response của request cũ không được ghi đè trang hiện tại.
- Test frontend đi qua đủ chín trang của tập 101 bản ghi, kiểm tra My Requests
  gửi trang đã chọn lên server và dùng fake timer xác nhận refresh nền.
- Validation FE-003: frontend 204/204; build pass; lint 0 errors (17 baseline
  warnings); `git diff --check` pass trên local Windows.
- FE-005 đã hoàn thành ngày 2026-08-26: Yarn 1.22.22 là package manager duy
  nhất; `package-lock.json` đã xóa và `yarn.lock` đã được Yarn prune từ clean
  install. React Router/DOM được pin cùng 7.9.6 để tránh hai Router runtime.
- Frontend CI dùng Node 20/Corepack, frozen install rồi lint/test/build;
  `prepare-deploy.ps1` luôn xóa `node_modules` và frozen install trước build.
- Validation FE-005: frozen install trong temp sạch pass; frontend 204/204;
  build pass; lint 0 errors (17 baseline warnings); PowerShell 5.1 parse pass;
  chỉ còn một lockfile; `git diff --check` pass.

## P2 - Hardening và khả năng vận hành

### FE-003 - Approval pagination và refresh

- [x] Inbox/MyRequests dùng server-driven page/pageSize/total.
- [x] Không slice cục bộ snapshot tối đa 100.
- [x] Polling nền 60 giây làm request mới xuất hiện không cần reload.
- [x] Test 101 records và fake-timer refresh.

Evidence: `src/pages/Approvals/Inbox.tsx`; `MyRequests.tsx`;
`ApprovalPages.test.tsx`.

Theo dõi: owner: Codex | branch/PR: local workspace | đóng: 2026-08-26 |
validation: frontend 204/204, build pass, lint 0 errors (17 baseline warnings),
`git diff --check` pass

### FE-004 - Chặn stale response trong Deployment Matrix

- [x] Gắn request generation với model hiện tại.
- [x] Chỉ current generation được set assignments/loading/error.
- [x] Test deferred promises trả model A sau model B.

Evidence: `src/pages/Software/hooks/use-deployment-matrix.ts:144-214`.

Theo dõi: owner: Codex | branch/PR: local workspace | đóng: 2026-08-26 |
validation: frontend 200/200, build pass, lint 0 errors

### FE-005 - Chọn một lockfile và clean build tái lập

- [x] Chọn Yarn 1 theo `packageManager`.
- [x] Regenerate đúng một `yarn.lock` từ clean state.
- [x] Xóa `package-lock.json` và dependency cũ không còn dùng.
- [x] CI chạy clean install với frozen lockfile rồi lint/test/build.
- [x] `prepare-deploy.ps1` không reuse `node_modules` stale.

Evidence: `package.json`, `yarn.lock`, `.github/workflows/frontend.yml`,
`scripts/prepare-deploy.ps1`.

Theo dõi: owner: Codex | branch/PR: local workspace | đóng: 2026-08-26 |
validation: clean frozen install pass; frontend 204/204; build pass; lint 0
errors (17 baseline warnings); PowerShell 5.1 parse pass; diff check pass

### IPC-001 - Xác thực và bound named-pipe I/O

- [ ] Chốt operator group/SID/session được phép run/stop/restart.
- [ ] Một request mỗi connection hoặc bounded concurrent server instances.
- [ ] Giới hạn request bytes và read/response deadline.
- [ ] Launcher dùng round-trip timeout và single-flight refresh.
- [ ] Test unauthorized SID, slowloris, oversized JSON và black-hole pipe.

Evidence: `IpcServer.cs:49-80`; `IpcRequestHandler.cs:27-73`; `IpcClient.cs:21-37`.

### AGT-002 - Loại race token rotation

- [ ] Mỗi request mang token generation/snapshot.
- [ ] Bỏ qua 401 của generation cũ sau khi token mới đã promote.
- [ ] Serialize rotation hoặc retry latest trước fallback previous.
- [ ] Barrier test delayed old-token poll không ghi đè current token mới.

Evidence: `AgentWorker.cs:99-104,384-425`; `ServerClient.cs:98-106`.

### OTA-002 - Có quy trình signing-key rotation/revocation

- [ ] Release signature có KeyId và trust set thay vì một public key duy nhất.
- [ ] Transitional bundle/key statement được key hiện tại ký.
- [ ] K2-only hoạt động sau K1/K2 transition; K3 trái phép bị từ chối.
- [ ] Có incident runbook khi private key mất/lộ.

Evidence: `AgentOptions.cs:22-24`; `ReleaseSignatureVerifier.cs:7-35`.

### CFG-002 - Không ACK `ReloadConfig` khi không có hiệu lực

- [ ] Hoặc bỏ command khỏi server/UI contract, hoặc triển khai config generation.
- [ ] Dùng `IOptionsMonitor`/runtime state cho allowlist setting reloadable.
- [ ] Validate và rollback invalid config.
- [ ] Test interval/concurrency đổi thật hoặc command trả Unsupported.

Evidence: `AgentCommandHandler.cs:135-137`; `AgentWorker.cs:48-49`.

### OPS-001 - Đồng bộ watchdog production defaults

- [ ] Chốt inactivity/max-attempt values theo tải factory thật.
- [ ] `install-server.ps1` và backend appsettings sinh cùng giá trị.
- [ ] Fresh artifact/install test đọc đúng production configuration.
- [ ] Test job dài hợp lệ không bị watchdog fail sớm.

Evidence: `MProject.Api/appsettings.json`; `scripts/install-server.ps1:252-257`.

### AUTH-004 - Serialize SingleDeviceLogin

- [ ] Lock/generation theo user bao quanh revoke và issue.
- [ ] Hai login đồng thời chỉ để một refresh family hợp lệ.
- [ ] Concurrent login integration test trên PostgreSQL.

Evidence: `AuthService.cs:98-116`.

### SEC-005 - Loại credential cố định khỏi tracked config

- [ ] Di chuyển dev secrets sang user-secrets hoặc environment.
- [ ] Rotate mọi giá trị đã từng nằm trong Git/history nếu còn hiệu lực.
- [ ] Secret-scan CI và startup guard khi default fingerprint bind non-loopback.
- [ ] Không chép development config vào production artifact.

Evidence: `MProject.Api/appsettings.Development.json`.

### ARCH-001 - Không coi DomainEventDispatcher là outbox

- [ ] Chốt có cần transactional outbox thật hay loại bỏ abstraction hiện tại.
- [ ] Nếu cần, dispatch handler, retry/backoff, poison/dead-letter và idempotency.
- [ ] Chỉ set `ProcessedAt` sau side effect thành công.
- [ ] Integration test crash/retry và duplicate delivery.

Evidence: `DomainEventDispatcherService.cs:35-44`.

## Đối chiếu tài liệu và memory

- Code/tests/scripts hiện tại là nguồn đúng hơn guide và Claude memory.
- Development mặc định tự `MigrateAsync()` khi startup; Production mặc định để
  deploy script sở hữu schema và `-SkipSchema` không bị startup migrate vòng qua.
- Viewer software write permissions từng được mở có chủ ý để thử nghiệm ngày
  2026-08-13, nhưng chưa phải quyết định production an toàn.
- `AutoRemoveOnUnassign=false` hiện giữ payload installed/unmanaged; released
  artifact có generation guard nhưng vẫn chờ PostgreSQL race test để đóng.
- Legacy parity hỗ trợ explicit command cho `.bat/.cmd/.jar/.py`; còn thiếu
  Windows Service fixture thật để xác minh session/runtime prerequisite.
- `docs/`, `.claude/`, `PRODUCT.MD` và `Old_program/` đang bị root `.gitignore`
  bỏ qua; clone khác có thể thiếu nguồn đối chiếu này.

## Nhật ký xác minh

| Ngày | ID | Người thực hiện | Code/PR | Test đã chạy | Môi trường | Kết quả |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-08-17 | REVIEW | Codex | Tạo backlog | Static review | Local workspace | 33 finding mở |
| 2026-08-18 | SEC-001 | Codex | Agent containment patch | Chưa chạy được | Static inspection | IMPLEMENTED, NOT VERIFIED |
| 2026-08-18 | SEC-001 | Codex | Backend validation + cache consumer hardening | Chưa chạy được | Static inspection | IMPLEMENTED, NOT VERIFIED |
| 2026-08-22 | SEC-001 | Codex | PackageId fresh-root + legacy catalog/cache bridge + ACL boundary | `dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore` (221/221) | Local .NET 8/Windows | BRIDGE IMPLEMENTED, READY FOR LEGACY MIGRATION LAB |
| 2026-08-22 | SEC-002 | Codex | Strict TLS client + canonical hostname scripts | Agent tests 221/221; 6 PowerShell scripts parse | Local .NET 8/Windows | IMPLEMENTED, ROLLOUT PENDING |
| 2026-08-22 | SEC-003/AUTH-001 | Codex | Viewer revoke + optional approval/system-role guards | Backend 908/908; Frontend 177/177; build pass | Local .NET 8/Node | IMPLEMENTED, HTTP MATRIX PENDING |
| 2026-08-25 | FE-001 | Codex | Approval-aware assignment DTO, gates and deployment UI states | Backend 910/910; Frontend 194/194; build pass; lint 0 errors | Local .NET 8/Node | COMPLETED |
| 2026-08-25 | FE-002 | Codex | Target/scope contract, drawer context và requester cancel | Full frontend test; build; lint; diff check | Local Node/Windows | IN PROGRESS — immutable snapshot/diff còn mở |
| 2026-08-26 | FE-004 | Codex | Assignment request generation khi đổi model | Frontend 200/200; build; lint 0 errors; diff check | Local Node/Windows | COMPLETED |
| 2026-08-26 | FE-002 | Codex | Backend target context cho bốn approval type | ApprovalService 15/15; Backend 911/911; diff check | Local .NET 8/Windows | LIVE CONTEXT COMPLETED — IMMUTABLE SNAPSHOT CÒN MỞ |
| 2026-08-26 | FE-002 | Codex | Immutable target/change snapshot JSONB trong approval detail API | ApprovalService 16/16; Backend 912/912; diff check | Local .NET 8/Windows | BACKEND SNAPSHOT COMPLETED — DRAWER/FRONTEND TEST CÒN MỞ |
| 2026-08-26 | FE-002 | Codex | Approval drawer render immutable target context và before/after diff | Frontend 201/201; build pass; lint 0 errors; diff check | Local Node/Windows | COMPLETED |
| 2026-08-26 | FE-003 | Codex | Server-driven Approval pagination và polling nền 60 giây | Frontend 204/204; build pass; lint 0 errors; diff check | Local Node/Windows | COMPLETED |
| 2026-08-26 | FE-005 | Codex | Yarn-only clean frozen install cho CI và deployment build | Clean frozen install; frontend 204/204; build; lint; PS 5.1 parse; diff check | Local Node/Windows | COMPLETED |
| 2026-08-27 | AUTH-002/AUTH-003/REL-001/REL-002/LOG-001/JOB-001/AGT-001/STO-001 | Codex | Auth version, membership boundary, release generation, unassign policy, scanner cursor, active cancel, explicit entry command, agent blob capability | Backend và Agent full suites; API/Frontend build | Local .NET/Node/Windows | CODE PATHS VERIFIED; ENVIRONMENT TESTS GIỮ MỞ THEO CHECKLIST |
| 2026-08-27 | UPL-001/STO-002/CFG-001/DEP-001/JOB-001/JOB-002 | Codex | Purpose-bound TUS, blob tombstone claim, pending config revision, production migration owner, job cancellation/reconciliation/concurrency | Backend 933/933; Frontend 204/204; EF model diff sạch; PowerShell parse; production builds | Local .NET/Node/Windows | CFG-001 COMPLETED; CÁC RELATIONAL/HTTP/ROLLBACK/E2E TEST CÒN MỞ |
| 2026-08-27 | REL-001/STO-002/JOB-002 | Codex | PostgreSQL isolated-database barrier/concurrency tests | `PostgresConcurrencyIntegrationTests` 5/5 | Local PostgreSQL/.NET 8 | REL-001 và STO-002 COMPLETED; JOB-002 Npgsql races VERIFIED, durable Agent journal còn mở |
| 2026-08-27 | AGT-001 | Codex | Real `.bat` wrapper attaches to unique child watch executable | Focused Agent test 1/1 | Local interactive Windows/.NET 8 | WATCH PROCESS VERIFIED; Windows Service identity fixture còn mở |
| 2026-08-27 | SEC-003 | Codex | Read-only Viewer grant và historical assignment approval audit | `PostgresProductionDataAuditTests` 1/1 | Database từ current Development config | Không có Viewer forbidden grant, active+Pending hoặc active thiếu current-policy approval |
| 2026-08-27 | SEC-001 | Codex | Directory-handle junction barrier, crash-recoverable legacy-root migration, install-root boundary ACL và malicious-manifest E2E | Agent 244/244; PowerShell 5.1 parse; temporary `icacls` fixture; junction barrier 1/1 under LocalSystem | Local Windows/.NET 8 plus Scheduled Task session 0 (`S-1-5-18`) | CODE/IDENTITY FIXTURE COMPLETE; station legacy-payload canary/rollback còn mở |
| 2026-08-27 | SEC-001 | Codex | Agent 2.0.0.1 live canary, protected rollback snapshot và install-root boundary | Service/health 200; 2238-file SHA-256 backup; standard-user top-level create denied | `UTPG3TM0T01 / FT1`, Agent LocalSystem | AGENT/ACL CANARY PASS; BOM11 job chưa tới, migration/rollback còn mở |
| | | | | | | |

## Giới hạn của lần review này

Đây là review kèm unit/regression validation cục bộ. Các finding phụ thuộc
PostgreSQL thật, Windows Service/IIS, TLS station, migration/canary, HTTP host
hoặc concurrency vẫn cần test runtime theo từng checkbox trước khi đóng.
