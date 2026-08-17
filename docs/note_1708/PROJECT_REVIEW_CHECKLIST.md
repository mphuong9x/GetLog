# MProject Review And Remediation Checklist

Ngày review: 2026-08-17

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
| [ ] | AUTH-001 | P1 | RBAC | Role manager có thể tự nâng quyền/sửa system role |
| [ ] | AUTH-002 | P1 | Authentication | Disable/reset không vô hiệu access JWT |
| [ ] | AUTH-003 | P1 | Team/RBAC | Membership hết hạn nhưng quyền leader/cache còn hiệu lực |
| [ ] | REL-001 | P1 | Software | Released version chưa immutable |
| [ ] | REL-002 | P1 | Software | `AutoRemoveOnUnassign=false` vẫn uninstall |
| [ ] | LOG-001 | P1 | Agent | Test log scanner bị starvation/skipped timestamp |
| [ ] | DEP-001 | P1 | Deploy/DB | Rollback binary không rollback destructive migration |
| [ ] | AGT-001 | P1 | Agent/Legacy | Entry `.bat/.jar/.py` không chạy từ Windows Service |
| [ ] | OTA-001 | P1 | Agent/Launcher | OTA overlay không exact và không crash-atomic |
| [ ] | STO-001 | P1 | Blob/Auth | Agent tải được blob không thuộc job/station |
| [ ] | STO-002 | P1 | Blob/GC | GC có TOCTOU với writer/cache và xóa DB trước object |
| [ ] | CFG-001 | P1 | Config/Approval | Xóa override pending để approval bị kẹt |
| [ ] | FE-001 | P1 | Frontend | Pending assignment vẫn hiện Activate và gate sai quyền |
| [ ] | FE-002 | P1 | Frontend | Approver không thấy đầy đủ target/change snapshot |
| [ ] | FE-003 | P2 | Frontend | Approval pagination bị cắt ở 100 và không polling đúng |
| [ ] | FE-004 | P2 | Frontend | Response model cũ có thể ghi đè deployment matrix mới |
| [ ] | FE-005 | P2 | Frontend/Build | Hai lockfile lệch manifest, clean build không tái lập |
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

Tác động: manifest hoặc package name không an toàn có thể ghi file ngoài
`D:\Apps` bằng quyền của Windows Service, thường là SYSTEM.

Bằng chứng:

- `MProjectAgent/Services/InstallDirectoryService.cs:27-41,94-120`
- `MProjectAgent/Services/JobExecutor.cs:359-364,477-480`
- `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:889-902`
- `MProjectBackend/MProject.Application/Utils/ResourceNameNormalizer.cs:5-12`

Checklist:

- [ ] Chuyển tên thư mục local sang immutable `PackageId`, không dùng package name.
- [ ] Backend từ chối rooted path, `.`/`..`, ADS, reserved name và collision.
- [ ] Agent canonicalize bằng `GetFullPath` và kiểm target nằm dưới install root.
- [ ] Chặn reparse point/junction escape và harden ACL thư mục deploy.
- [ ] Test Windows cho `C:\...`, UNC, `../`, `A:B`, `CON`, case collision và junction.
- [ ] Test manifest giả từ server vẫn bị Agent từ chối độc lập.

Tiêu chí đóng: không input nào tạo/sửa/xóa file ngoài root; test chạy dưới cùng
identity/môi trường với service production.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

### SEC-002 - Khôi phục TLS trust đúng cho Agent

Tác động: MITM trong LAN có thể nhận `X-Agent-Token` và trả manifest/payload giả.
SHA từ cùng server giả không tạo được trust.

Bằng chứng:

- `MProjectAgent/appsettings.json:9-20`
- `MProjectAgent/Program.cs:87-100`
- `scripts/install-server.ps1:123-136,217-226,439-475`
- `scripts/prepare-deploy.ps1:142-145,241`

Checklist:

- [ ] Chọn URL canonical: DNS alias có certificate hợp lệ hoặc IP có IP SAN.
- [ ] Đồng bộ certificate SAN, IIS binding, URL trong artifact và runbook.
- [ ] Bỏ `DangerousAcceptAnyServerCertificateValidator` khỏi production path.
- [ ] Bake `AllowUntrustedCertificate=false` cho mọi artifact production.
- [ ] Cân nhắc public-key/certificate pinning và quy trình rotate pin.
- [ ] Integration test từ station: cert đúng pass, cert giả/expired/wrong-host fail.
- [ ] Xác minh token không được gửi sau TLS validation failure.

Tiêu chí đóng: fresh factory install handshake được bằng URL được quảng bá và
Agent từ chối certificate giả trước khi lộ credential.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

### SEC-003 - Siết Viewer và đóng approval activation bypass

Tác động: mọi user thường hiện có thể create, publish và assign software; người
submit assignment có thể tự gọi Activate dù request đang chờ duyệt.

Bằng chứng:

- `MProjectBackend/MProject.Infrastructure/AppDbSeeder.cs:320-414`
- `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:55-101,138-168`
- `MProjectBackend/MProject.Api/Controllers/Software/SoftwareAssignmentsController.cs:42-71`
- `MProjectBackend/MProject.Application/Models/SoftwareModels.cs:8-26,245-260`

Checklist:

- [ ] Chốt Viewer production là read-only hay vẫn giữ quyền thử nghiệm.
- [ ] Nếu read-only, xóa 5 software write grants bằng migration/reconciliation DB.
- [ ] Không dựa vào seeder để revoke vì seeder hiện chỉ add grants.
- [ ] Expose và validate assignment approval policy, hoặc gắn default bắt buộc.
- [ ] Chỉ approval handler được chuyển Pending sang Active trong transaction.
- [ ] Endpoint Activate từ chối pending/rejected/cancelled request.
- [ ] Permission riêng cho submit, approve và administrative override nếu cần.
- [ ] Test role matrix và test requester không tự activate assignment của mình.

Tiêu chí đóng: user read-only không publish/deploy; assignment cần duyệt không
thể active nếu chưa có ApprovalAction hợp lệ.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

### SEC-004 - Thay global InstallerToken bằng enrollment một lần

Tác động: local user đọc token còn lại trong config có thể tạo station/Agent
credential mới qua anonymous self-announce.

Bằng chứng:

- `scripts/prepare-deploy.ps1:147-168`
- `scripts/install-agent.ps1:31-38,93-96,164-190`
- `MProjectAgent/Services/AgentWorker.cs:371-381,428-457`
- `MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:45-69`

Checklist:

- [ ] Phát enrollment material riêng cho từng machine và chỉ dùng một lần.
- [ ] Truyền secret qua installer/protected store, không bake vào shared artifact.
- [ ] Xóa enrollment material ngay sau khi cấp Agent token thành công.
- [ ] Đặt ACL rõ cho install directory và ProgramData state.
- [ ] Có cơ chế disable self-announce/global bootstrap sau provisioning.
- [ ] Test standard user không đọc secret và token đã dùng không enroll máy thứ hai.

Tiêu chí đóng: artifact dùng chung không chứa reusable enrollment credential.

Theo dõi: owner: ___ | branch/PR: ___ | bắt đầu: ___ | đóng: ___

## P1-A - Upload, authentication và deployment state

### UPL-001 - Tách authorization TUS theo upload purpose

Bằng chứng: `MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:59-91,93-210`.

- [ ] Parse/validate purpose trước khi authorize completion.
- [ ] Software upload bắt buộc permission trên đúng package/version target.
- [ ] Document upload bắt buộc ownership/quota của đúng user.
- [ ] Dùng capability ngắn hạn bind actor, purpose, target, hash, size và expiry.
- [ ] Test `OwnDocuments`-only không thể tạo `SoftwareFile`.
- [ ] Bỏ hoặc khóa legacy completion branch khi client đã migrate.

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

Bằng chứng:

- `StationSoftwareAssignmentService.cs:188-260,494-559`
- `MProjectAgent/Services/JobExecutor.cs:95-138,341-355`
- `InstallationJobService.cs:480-500`
- `PcInventoryService.cs:49-58`

- [ ] Server gửi/persist CancelJob cho mọi active/partial install.
- [ ] Agent link cancellation vào download, deploy, catalog và launch.
- [ ] Terminal callback khác status trả `409/410`, không trả success no-op.
- [ ] Persist uninstall intent kể cả chưa có Installed record.
- [ ] Reconcile unknown inventory/catalog thay vì bỏ qua.
- [ ] Barrier E2E: pause Deploy, remove assignment, resume, assert không còn file/process/catalog.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### JOB-002 - Làm job state machine atomic và crash-resumable

Bằng chứng: `InstallationJobService.cs:37-106,441-500,626-641`; `AgentWorker.cs:336-368`.

- [ ] Dùng conditional update hoặc concurrency token cho mọi transition.
- [ ] Một transition terminal không thể bị progress/watchdog hồi sinh.
- [ ] Agent lưu durable local journal trước ACK/deploy/complete.
- [ ] Resume được Pending, Downloading và Installing theo lease/generation.
- [ ] Complete server và local catalog có protocol idempotent, retry được.
- [ ] Npgsql concurrency tests: cancel-vs-progress, watchdog-vs-complete.
- [ ] Restart test sau ACK và test server commit rồi mất response.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AUTH-001 - Áp grantability/no-self-elevation cho RoleService

Bằng chứng: `MProjectBackend/MProject.Application/Services/Identity/RoleService.cs:101-130`.

- [ ] Reuse invariant từ `AuthorizationMutationService`.
- [ ] Actor không được grant permission/scope mà chính actor không có.
- [ ] Chặn mutation ảnh hưởng role actor đang giữ nếu tạo self-elevation.
- [ ] Bảo vệ Admin/Viewer/system roles bằng policy rõ ràng.
- [ ] Test delegated role manager, self-held role và system-role mutation.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AUTH-002 - Vô hiệu access JWT khi user/security state đổi

Bằng chứng: `MProjectBackend/MProject.Api/Program.cs:317-333`; `AuthService.cs:159-175`; `UserService.cs:429-465`.

- [ ] Thêm `AuthVersion`/security stamp vào User và JWT claim.
- [ ] Tăng version khi disable, reset/change password và logout-all.
- [ ] `OnTokenValidated` kiểm user Active và version hiện tại.
- [ ] Approval approver resolver bắt buộc user Active.
- [ ] Integration test issue token, disable/reset, rồi approve phải fail.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AUTH-003 - Ràng buộc TeamLeader vào membership còn hiệu lực

Bằng chứng: `TeamService.cs:304-350,402-424`; `AuthorizationService.cs:121-141,201-233`.

- [ ] Clip role interval theo membership interval hoặc join membership khi authorize.
- [ ] Mọi leader action kiểm Active user và Active UserTeam.
- [ ] Cache key/TTL phản ánh team membership generation và next boundary.
- [ ] TimeProvider test deny đúng ngay sau membership expiry.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

## P1-B - Artifact, storage và vận hành

### REL-001 - Bảo đảm Released version immutable

Bằng chứng: `SoftwareVersionService.cs:392-412`; `ConfigParameterService.cs:493-720`; `SoftwareFileService.cs:374-393`.

- [ ] Entry point/icon/watch/health chỉ sửa được khi Draft.
- [ ] File/config mutation lock hoặc recheck status trong cùng transaction.
- [ ] Thêm concurrency token/generation vào version artifact.
- [ ] PostgreSQL two-context test: mutation pause, release, resume phải conflict.
- [ ] Manifest của cùng released version luôn có cùng artifact identity.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### REL-002 - Thực thi hoặc loại bỏ AutoRemoveOnUnassign

Bằng chứng: `StationSoftwareAssignmentService.cs:188-231,537-557`; `InstallationJobService.cs:150-199`.

- [ ] Chốt behavior khi flag false: giữ installed/unmanaged hay hành vi khác.
- [ ] Deactivate/remove và orphan poll dùng cùng một policy.
- [ ] Sửa test đang codify việc false vẫn uninstall.
- [ ] Nếu không còn dùng flag, xóa khỏi DB/API/UI/docs bằng migration rõ ràng.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### LOG-001 - Sửa TestResultScanner cursor

Bằng chứng: `MProjectAgent/Services/TestResultScanner.cs:82-123`; `AgentService.cs:954-967`.

- [ ] Cursor ổn định theo `(LastWriteTime, relativePath)` hoặc immutable event ID.
- [ ] Drain dữ liệu mới trước, overlap replay chỉ dùng capacity còn lại.
- [ ] Persist cursor atomically sau server acknowledgement.
- [ ] Multi-cycle test với hơn batch size trong overlap và timestamp bằng nhau.
- [ ] Assert mọi file cuối cùng được accepted đúng một lần về mặt nghiệp vụ.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### DEP-001 - Làm migration/rollback production nhất quán

Bằng chứng:

- `MProjectBackend/MProject.Infrastructure/AppDbSeeder.cs:44-52`
- `scripts/update-server.ps1:154-178,223-241`
- `Migrations/20260810011135_RemoveConfigValueSets.cs:14-44`

- [ ] Chọn một migration owner: startup hoặc deploy script.
- [ ] `-SkipSchema` thực sự ngăn startup migration nếu còn option này.
- [ ] Chuyển destructive change sang expand/contract có compatibility window.
- [ ] Preflight phát hiện migration không rollback được.
- [ ] Có DB backup/restore procedure đã diễn tập.
- [ ] Fault test: health fail sau migrate, old code phải healthy sau rollback.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### AGT-001 - Chạy đúng legacy entry `.bat/.jar/.py`

Bằng chứng: `ProcessSupervisor.cs:198-212`; `InteractiveProcessLauncher.cs:50-83`; `docs/uistore_parity_spec_verified.md:54-58`.

- [ ] Chốt danh sách entry type hỗ trợ và runtime prerequisite.
- [ ] Build command rõ cho `cmd /c`, `java -jar` và Python interpreter.
- [ ] Quote argument/path an toàn, giữ WorkingDirectory đúng entry folder.
- [ ] Watch process hoạt động khi wrapper khác process thực tế.
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

Bằng chứng: `MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:121-133`; `LocalStorageService.cs:72-75`.

- [ ] Download capability bind agentId, job/releaseId, SHA và expiry.
- [ ] Job phải active và thuộc đúng computer/station tại thời điểm tải.
- [ ] UserDocument không bao giờ tải được bằng agent credential.
- [ ] Test agent A, agent B, cancelled job và private document.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### STO-002 - Loại bỏ Blob GC TOCTOU

Bằng chứng: `BlobGcService.cs:86-134,186-204,251-260`; `ConfigRenderService.cs:191-208`.

- [ ] Dùng per-SHA claim/lease hoặc advisory lock với writers.
- [ ] Conditional delete kiểm lại reference và cutoff tại commit.
- [ ] Dùng tombstone/deletion queue; storage failure phải retry được.
- [ ] Không xóa DB row trước khi object deletion có trạng thái durable.
- [ ] Relational concurrency test GC-vs-upload và GC-vs-render touch.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### CFG-001 - Giải quyết lifecycle config override pending

Bằng chứng: `ConfigValueOverrideService.cs:833-889,1106-1134`; `ConfigValueOverrideApprovalHandler.cs:46-83`.

- [ ] Delete pending override cancel approval trong cùng transaction.
- [ ] Pending revision không làm biến mất Active value hiện tại.
- [ ] Tách active revision khỏi draft/pending replacement nếu cần.
- [ ] Test delete pending, replace pending và reject/cancel lifecycle.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

## P1-C - Frontend approval contract

### FE-001 - Không cho pending assignment xuất hiện như inactive

Bằng chứng:

- `src/pages/Software/hooks/use-deployment-matrix.ts:260,354`
- `src/pages/Software/components/ListView.tsx:181`
- `src/constants/access-rules.ts:83`
- `src/constants/permissions.ts:28`

- [ ] Assignment DTO trả approval state và intended version rõ ràng.
- [ ] Pending/rejected/cancelled có UI state riêng, không hiện Activate trái phép.
- [ ] Route read gate khớp `software.read` và action gate khớp permission backend.
- [ ] Pin/toggle/remove chỉ render và execute khi được phép.
- [ ] Test role matrix và pending flow không gọi `/activate`.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

### FE-002 - Hiển thị đầy đủ đối tượng/thay đổi trước khi approve

Bằng chứng: `src/types/approvals.ts:1-23`; `src/components/approvals/ApprovalActionDrawer.tsx:193-211`.

- [ ] Type frontend hỗ trợ mọi target backend hiện có.
- [ ] API trả immutable target/change snapshot phù hợp từng loại request.
- [ ] Drawer hiển thị target, scope, model/station, reason và diff quan trọng.
- [ ] Requester nhìn thấy Cancel khi `currentUserCanCancel=true`.
- [ ] Test SoftwareVersion, SoftwareAssignment, ConfigValueOverride và OverrideFile.

Theo dõi: owner: ___ | branch/PR: ___ | test/PR: ___

## P2 - Hardening và khả năng vận hành

### FE-003 - Approval pagination và refresh

- [ ] Inbox/MyRequests dùng server-driven page/pageSize/total.
- [ ] Không slice cục bộ snapshot tối đa 100.
- [ ] Query invalidation/polling làm request mới xuất hiện không cần reload.
- [ ] Test 101 records và fake-timer refresh.

Evidence: `src/pages/Approvals/Inbox.tsx:52-116`; `MyRequests.tsx:58-115`.

### FE-004 - Chặn stale response trong Deployment Matrix

- [ ] Gắn AbortController hoặc request generation với model hiện tại.
- [ ] Chỉ current generation được set assignments/loading/error.
- [ ] Test deferred promises trả model A sau model B.

Evidence: `src/pages/Software/hooks/use-deployment-matrix.ts:144-214`.

### FE-005 - Chọn một lockfile và clean build tái lập

- [ ] Chọn Yarn 1 theo `packageManager` hoặc quyết định package manager mới.
- [ ] Regenerate đúng một lockfile từ clean state.
- [ ] Xóa `package-lock.json` và dependency cũ không còn dùng nếu chọn Yarn.
- [ ] CI chạy clean install với frozen/immutable lockfile rồi lint/test/build.
- [ ] `prepare-deploy.ps1` không reuse `node_modules` stale.

Evidence: `package.json`, `package-lock.json`, `yarn.lock`, `scripts/prepare-deploy.ps1:198-205`.

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
- Backend hiện tự `MigrateAsync()` khi startup; ghi chú cũ "không auto-migrate"
  đã lỗi thời.
- Viewer software write permissions từng được mở có chủ ý để thử nghiệm ngày
  2026-08-13, nhưng chưa phải quyết định production an toàn.
- `AutoRemoveOnUnassign` và released-version immutability hiện là intent, chưa
  phải invariant được code thực thi đầy đủ.
- Legacy parity yêu cầu payload không chỉ `.exe`; đường Windows Service hiện chưa
  đáp ứng `.bat/.jar/.py`.
- `docs/`, `.claude/`, `PRODUCT.MD` và `Old_program/` đang bị root `.gitignore`
  bỏ qua; clone khác có thể thiếu nguồn đối chiếu này.

## Nhật ký xác minh

| Ngày | ID | Người thực hiện | Code/PR | Test đã chạy | Môi trường | Kết quả |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-08-17 | REVIEW | Codex | Tạo backlog | Static review | Local workspace | 33 finding mở |
| | | | | | | |

## Giới hạn của lần review này

Đây là static review. Chưa chạy build/test vì shell runner trên máy lỗi Microsoft
Store `pwsh` / `CreateProcessAsUser 1920`. Mọi finding đã được trace qua code,
test hiện có, scripts, current docs và legacy parity spec; vẫn cần test runtime
theo checklist trước khi đóng.
