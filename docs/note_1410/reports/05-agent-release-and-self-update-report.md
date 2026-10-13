# 05 — Agent release packaging và self-update

Ngày review: **2026-09-08 → 2026-09-09, Asia/Saigon**. Prompt: `docs/code-review/prompts/05-agent-release-and-self-update.md`. Repository revision: **`439ce7035d89c5236c9d468660fe6b21b131ae8d`**, không đổi giữa hai lượt làm việc. SHA-256 của prompt: **`c582ad9e679dea739dc29a3cbba05d28e429510d8cae7dc16c24e2a445df36eb`**.

Review source và kiểm chứng bằng fixture cục bộ; **không sửa production code**, configuration, checklist hoặc project memory. Tuân thủ `AGENTS.md`: giữ workflow dùng chung install token để installer đăng ký các PC và đưa chúng lên `/computer`. Không coi token reuse hoặc thiếu enrollment thủ công là lỗi. Không mở lại payload deployment, generic enrollment, server/database rollback hoặc lifecycle upload thông thường.

## Kết quả

**9 confirmed defects và 2 design risks**. Những điểm cần ưu tiên là bảo toàn ACL khi đổi thư mục cài, tránh rollback từ thư mục đã bị xóa một phần, giữ backup tốt qua crash trước readiness, và sửa vòng đời cờ chống chạy apply trùng. Checklist OTA-001 đã đóng nhưng chưa bao phủ các failure mode này; OTA-002 vẫn là gap đã được ghi nhận.

| ID | Severity | Confidence | Category | Nội dung |
| --- | --- | --- | --- | --- |
| OTA05-01 | High | High | confirmed defect | Thay thư mục cài làm mất ACL đã harden của binaries/config |
| OTA05-02 | High | High | confirmed defect | Lỗi xóa snapshot cũ có thể phá bản cài và rollback dù backup còn nguyên |
| OTA05-03 | High | High | confirmed defect | Crash sau switch, trước readiness: bootstrap chạy bản chưa xác nhận; retry ghi đè backup tốt |
| OTA05-04 | High | High về latch; Medium về tần suất race | confirmed defect | Cờ apply không được giải phóng sau lỗi launch hoặc helper thoát; có thể chặn OTA kế tiếp |
| OTA05-05 | Medium | High | confirmed defect | Publish tự activate archive mà không kiểm tra manifest/version như Agent |
| OTA05-06 | High | High | confirmed defect | Manifest có đúng một Agent exe vẫn được chấp nhận; exact replacement xóa Launcher/dependencies |
| OTA05-07 | Medium | High | confirmed defect | DLL cũ trong output Launcher được copy và đưa vào manifest hợp lệ |
| OTA05-08 | Medium | High | confirmed defect | Download body treo giữ toàn bộ heartbeat tick, không có deadline riêng |
| OTA05-09 | Medium | High | confirmed defect | Update result bị xóa trước khi heartbeat được server nhận |
| OTA05-10 | Medium | High về retention; Medium về thời điểm hết đĩa | design risk | Staged versions và thư mục mồ côi không có retention/cleanup hữu hạn |
| OTA05-11 | Medium | High | design risk | OTA-002: trust chỉ có một public key, chưa có rotate/revoke từ xa |

Severity phản ánh hậu quả khi điều kiện nêu trong finding xảy ra; không khẳng định fleet đang gặp các lỗi đó. Archive/PE giả và giá trị trong diagnostics chỉ là fixture, không phải production payload. Không có credential, private key hay operational log trong báo cáo.

## Phạm vi và kiểm chứng

Đã đọc `AGENTS.md`, `docs/PROJECT_REVIEW_CHECKLIST.md:601-615,816-823`, Agent/server release configuration, station version props, các scripts và source trong scope của prompt. Đã trace controller/service/storage reference/capability, heartbeat offer và telemetry, staging/signature/manifest, bootstrap/apply/exact snapshot/readiness; đọc các tests tương ứng cùng OTA service drill và wire check.

| Kiểm chứng | Kết quả |
| --- | --- |
| Backend: `AgentReleaseServiceTests`, `AgentServiceTests`, `AgentBlobAuthorizationTests` | **74 pass**, 0 fail, 0 skip |
| Agent: `AgentUpdaterTests`, `ApplyUpdateCommandTests`, `AgentInstallerLauncherTests`, `StationBundleVersionContractTests`, `SelfUpdateSupportOutboxTests`, `IpcWireCompatTests`, `IpcProtocolContractTests` | **58 pass**, 0 fail, 0 skip |
| Diagnostics bổ sung gọi production classes/helpers | **8/8 pass** ở lượt kiểm chứng cuối; assertions xác nhận hành vi lỗi hiện tại |
| Signing smoke qua `scripts/sign-agent-release.ps1 -Sign -Quiet` thật | **1/1 pass**; Windows PowerShell/OpenSSL tạo chữ ký được `ReleaseSignatureVerifier` chấp nhận; hash khác bị từ chối |
| Package fragment smoke | **Pass**; thực thi nguyên các statement copy Launcher và tạo manifest lấy từ AST của script thật, với thư mục fixture riêng |
| Full `package-agent.ps1`, output riêng `.tmp/review05-package` | **Chưa build được**: SDK 10.0.400 yêu cầu runtime/ILLink 8.0.30 nhưng cache chỉ có 8.0.28; không tạo được shipping bundle hoàn chỉnh |
| `test-agent-ota-bootstrap.ps1` với Windows Service/LocalSystem | Đã đọc; **không chạy** vì phiên hiện tại không elevated |
| `e2e-wire-check.ps1` trên package vừa build, WPF/net48 thực | Đã đọc; **không chạy** vì chưa có package hoàn chỉnh từ lượt review này |

Tổng bộ test hiện có: **132 pass**. Chúng không chứng minh các findings đã được sửa. Backend diagnostics dùng EF InMemory/fake storage; các kiểm tra filesystem chạy trên Windows thật trong thư mục fixture. Crash diagnostic gọi subprocess `MProjectAgent.UpdateFixture` và cho tiến trình thoát tại `new-active`, sau đó gọi recovery và phần tạo backup của lần apply tiếp theo. Đây không phải full `RunAsync`/SCM end-to-end. Diagnostic cleanup ban đầu dự kiến còn thư mục install; thực nghiệm cho thấy install biến mất, nên assertion cuối kiểm tra hậu quả thực tế này và recovery sau khi bỏ lock vẫn thiếu exe.

Lần chạy Agent ban đầu gặp assets trỏ sang cache NuGet của sandbox, và hai build chạy đồng thời tranh output của shared projects. Đã chuyển sang cache có sẵn `C:/Users/Admin/.nuget/packages`, restore không dùng network/audit và chạy build tuần tự; các test nêu trên pass. Full package bị thiếu runtime package là giới hạn môi trường kiểm chứng, không được tính thành defect của ứng dụng. Không tải dependency mới, đổi runtime mục tiêu trong source, publish release lên server hay chạy installer trên máy thật.

Artifacts kiểm chứng cục bộ:

- `.tmp/review05-results/agent-ota.trx`, `.tmp/review05-results/server-ota.trx`.
- `.tmp/review05/Review05.csproj`, `.tmp/review05/Program.cs`: diagnostics và signing smoke; private key fixture của signing smoke được xóa trong `finally`.
- `.tmp/review05/package-smoke.ps1`: chỉ chạy statement đã chỉ rõ, không đại diện cho toàn bộ publish/version gates.

Lệnh tái chạy diagnostics: `dotnet run --project .tmp/review05/Review05.csproj --no-restore -p:BuildProjectReferences=false`. Có thể truyền `-- signing` để chỉ chạy signing smoke. Các artifact `.tmp` không phải test/production source đã thêm vào repository.

## Shipping bundle và năm luồng bắt buộc

Version hiện tại là **2.0.0.3**. `MProject.StationBundleVersion.props:3-9` chiếu cùng Version/AssemblyVersion/FileVersion/InformationalVersion vào Agent net8, Launcher net48 WPF và IPC contracts netstandard2.0 qua import của ba project. Agent heartbeat dùng assembly version (`MProjectAgent/Services/SystemInfo.cs:9-12`). OTA manifest có **schema=1/protocol=1**, độc lập với Launcher IPC hiện tại **v2**; không được tăng OTA protocol chỉ vì thêm IPC capabilities.

Bundle mà packager định nghĩa gồm Agent win-x64 self-contained/single-file và output cần thiết của publish; Launcher exe/config; IPC contracts, Newtonsoft.Json và QRCoder loose DLL; root `appsettings.json`; `install-agent.ps1`, `install-agent.cmd`, `migrate-agent-tls.ps1`; optional `install-agent.defaults.json`; cuối cùng `agent-release-manifest.json`. Native/debug output của Agent nếu được publish ra cũng được script liệt kê và hash. Đây là inventory suy ra từ project/script, **không phải danh sách artifact đã build thành công trong phiên này**. Do đó “manifest khớp tất cả file có trong zip” chưa đủ chứng minh zip là một station bundle hoàn chỉnh.

| Trace | Hành vi hiện tại và điểm cần lưu ý |
| --- | --- |
| 1. Package/sign → Publish → storage/reference | `package-agent.ps1:179-217` làm sạch package output, publish Agent, build Launcher rồi copy extensions từ output Launcher. Loại development/nested settings; cấu hình station trước khi hash; kiểm tra FileVersion của ba binaries tại `333-339`; tạo manifest mọi payload file tại `364-387`; zip root tại `390-430`; sign exact archive tại `434-443`. `sign-agent-release.ps1:75-108` ký SHA-256 bằng RSA PKCS#1 v1.5, nhánh .NET PEM hoặc OpenSSL. Controller bắt `ManageAgentReleases`, giới hạn multipart 300 MiB. Service kiểm Version/base64/root exe, hash và chữ ký nếu server đã cấu hình key, upload/dedup blob, tăng reference rồi deactivate release cũ và activate release mới trong transaction. Không có bước server validate manifest. |
| 2. Activate/force/deactivate → offer | `ActivateAsync:255-270` có early return khi trạng thái force không đổi; đổi active và force trong transaction. `DeactivateAllAsync:297-302` chỉ là helper khi publish/activate release khác, **không có public endpoint pause/deactivate độc lập**. Delete từ chối active, soft-delete inactive và giảm reference. Partial unique index đảm bảo tối đa một active live release. `ResolveAgentUpdateAsync:489-528` offer khi version mới hơn hoặc force và string version khác; same version không được reinstall. Offer mang Version/SHA/Size/Signature/Force và capability download 15 phút. |
| 3. Heartbeat → download → stage | Worker gửi authenticated heartbeat, ghi readiness sau response, xử lý token transition/commands rồi **await** updater. Updater kiểm enabled; pending staged marker được ưu tiên trước offer/version; claim jitter/backoff cho attempt mới. Download relative capability dùng X-Agent-Token. Sau SHA và RSA verification mới extract vào `.prepare-GUID`; manifest phải khớp version/protocol, đường dẫn/size/hash hợp lệ, file set chính xác; swap vào `update/versions/<version>`, rồi ghi `staged.txt`. Signature là của zip, bao phủ cả manifest và bytes. |
| 4. LaunchApply → exact replacement → service/readiness/result | `LaunchApply` ưu tiên `<install>.bootstrap/v1/MProjectAgent.exe`, fallback staged exe khi chưa có bootstrap; giữ `_applyLaunched` trong process. `RunAsync` lấy file lease, chuẩn bị/reuse bootstrap, đổi service binPath, stop service, stop Launcher, copy backup, tạo sibling candidate từ staged và preserve root appsettings. Ghi `.switch.json` có flush, rename old/candidate, xóa old. Start service và chờ SCM RUNNING cùng một readiness heartbeat đúng version/thời gian. Thất bại thì exact snapshot rollback và kiểm SCM RUNNING; thành công/thất bại ghi result rồi xóa staged marker. Bootstrap recovery xử lý directory switch trước start child nhưng không sở hữu readiness/backup recovery. |
| 5. Bundle/Launcher/IPC compatibility | Ba project import version chung; packager so ba FileVersion với output `Agent.exe version`. `e2e-wire-check.ps1:20-39,132-177` đòi các loose files, chặn Agent khác đang chạy để tránh đọc nhầm named pipe, dùng Newtonsoft/IPC DLL của package đọc response và đòi đủ bốn capabilities `support.status.v1`, `support.preview.v1`, `support.report.v2`, `support.update.v1`. `-LaunchLauncher` còn kiểm cửa sổ WPF thực. Test net8 dùng serializer/contracts và linked Launcher source không thay thế phép chạy net48/package thật này. |

Server download qua `AgentController.DownloadLocalBlob:138-149` yêu cầu Agent authentication, capability đúng Agent và live authorization. `AgentBlobDownloadAuthorizer:18-45` yêu cầu active Agent, blob đúng SHA/path chưa bị claim GC, release vẫn active/live và đúng hash. Giữ các điều kiện này: shared install-token workflow không miễn download authorization. Publish nhận **archive bytes và signature**, không nhận một SHA tùy ý để chiếm blob; việc dedup nội dung đã upload/ký không đủ bằng chứng cho defect ownership trong scope này.

Adoption lấy version/capabilities/last outcome từ Computer; count chỉ Computer có Agent, phân OnActive/Behind/Ahead/Unknown và Offline. `SigningReady` là số máy báo public-key **không rỗng**, không phải bằng chứng tất cả key đang khớp release. `UpdateFailed` chỉ phản ánh last outcome cho active version; OTA05-09 và lỗi download chỉ ghi log làm số này không phải tổng số lần rollout thất bại.

## Findings chi tiết

### OTA05-01 — Exact replacement không bảo toàn ACL của bản cài

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Commands/ApplyUpdateCommand.cs:146-161,200-214,235-257`; `scripts/install-agent.ps1:126-168`.
- **Symbol:** `CopyStagedOverInstall`, `ReplaceWithExactSnapshot`, `CopyDirectory`, `CopyStationOwnedConfig`, `Protect-AgentInstallation`.
- **Evidence:** Installer bảo vệ install root, cấp Users RX binaries và giới hạn root appsettings cho SYSTEM/Administrators. Updater tạo sibling directory mới, copy bytes và rename làm install; không chép/reapply security descriptor của root/config. Diagnostic tạo parent có Users RX, install/config được bảo vệ riêng: sau gọi production helper, cả hai mất `AreAccessRulesProtected`, config có inherited Users allow. Bootstrap chỉ harden `<install>.bootstrap`, không sửa ACL candidate.
- **Finding:** “Preserve station config” hiện chỉ bảo toàn nội dung; quyền truy cập được thay bằng inheritance từ parent của candidate. Cả update và rollback có thể làm yếu ACL đã thiết lập khi install.
- **Realistic failure scenario:** Agent được cài dưới parent cho Users RX; OTA làm appsettings trở nên đọc được. Nếu parent cho Users Modify hoặc thuộc khu vực người dùng có quyền ghi, binaries/config mới có thể nhận quyền ghi đó. Trường hợp thứ hai phụ thuộc ACL nơi triển khai, không được khẳng định cho mọi PC.
- **Operational impact:** Mất kiểm soát truy cập config; với parent ghi được, người dùng thường có thể thay nội dung sẽ được service LocalSystem thực thi hoặc đọc. Đây là vấn đề ACL, không phải phản đối việc dùng chung install token.
- **Root cause:** Exactness được định nghĩa bằng file names/bytes, bỏ security metadata và điều kiện an toàn của sibling candidate.
- **Recommended direction:** Áp ACL tin cậy cho candidate trước khi có thể được sử dụng và trước switch, giữ quyền đọc Launcher/binaries cho operator nhưng bảo vệ config riêng; làm tương tự cho backup/rollback. Kiểm quyền parent và vùng chứa switch metadata theo cùng policy hiện có.
- **Required regression test:** Update và rollback dưới parent Users RX và Users Modify; so effective access của standard user trên root, exe, config và backup trước/sau. Assert candidate được bảo vệ trước rename, không chỉ sau service start.
- **Estimated change scope:** Medium.

### OTA05-02 — Lỗi cleanup có thể rollback từ snapshot đã bị xóa một phần

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Commands/ApplyUpdateCommand.cs:156-185,206-225,114-126`; `MProjectAgent/Commands/BootstrapCommand.cs:61-77`.
- **Symbol:** `CopyStagedOverInstall`, `ReplaceWithExactSnapshot`, `RunAsync`, `BootstrapCommand.Recover`.
- **Evidence:** Sau khi new candidate trở thành install, `Directory.Delete(parkedOld, recursive:true)` vẫn nằm trong transaction bằng `try/catch` của switch. Nếu delete fail, catch xóa install mới và đưa `parkedOld` về; không dùng backup đầy đủ khi parked còn tồn tại. Diagnostic giữ một file cuối của parked bằng handle không cho delete tại `new-active`: các file khác đã bị xóa, rollback move cũng fail, install biến mất; backup vẫn đủ. Sau bỏ lock, `Recover` chuyển phần parked còn lại về rồi báo thiếu Agent exe.
- **Finding:** Cleanup hậu switch có thể biến một bản new hoàn chỉnh thành trạng thái không boot được và coi thư mục đã bị phá một phần là nguồn rollback. `ReplaceWithExactSnapshot` có cùng cấu trúc xóa/catch/restore.
- **Realistic failure scenario:** Antivirus hoặc một handle còn mở làm recursive delete chỉ hoàn tất một phần. Khi rollback sau readiness timeout, Launcher bản mới cũng có thể đã được bootstrapper khởi chạy; nhánh rollback không gọi lại `StopLauncher` trước khi thay/xóa snapshot.
- **Operational impact:** Service không thể khởi động dù backup nguyên vẹn nằm trên đĩa; cần sửa thủ công. Việc “marker còn tồn tại” không đủ phục hồi vì marker chỉ trỏ đến parked, không biết backup tốt.
- **Root cause:** Gộp cleanup không nguyên tử với quyết định commit/rollback; dùng sự tồn tại của thư mục làm bằng chứng nó còn exact.
- **Recommended direction:** Tách commit switch khỏi cleanup có thể retry. Không xóa bản active tốt khi chỉ cleanup old thất bại. Giữ nguồn rollback nguyên vẹn tới khi kết thúc transaction/readiness; khi restore phải có provenance/trạng thái snapshot, không chọn một thư mục đang hoặc đã bị xóa dở.
- **Required regression test:** Lock một file sau khi các file trước đó đã bị xóa; assert vẫn có bootable exact install và intact rollback source. Lặp cho update/rollback và process crash trong cleanup; kiểm tra Launcher đã chạy lại khi rollback.
- **Estimated change scope:** Medium.

### OTA05-03 — Recovery sau switch không giữ transaction cho tới readiness

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Commands/ApplyUpdateCommand.cs:81-92,106-126,156-162`; `MProjectAgent/Commands/BootstrapCommand.cs:61-77,94-108`; `MProjectAgent/Services/AgentWorker.cs:233-283`; `MProjectAgent/Services/AgentUpdater.cs:77-82`.
- **Symbol:** `RunAsync`, `BootstrapCommand.Recover`, `BootstrapService.OnStart`, `HeartbeatTickAsync`, `MaybeStageUpdateAsync`.
- **Evidence:** Switch marker chỉ lưu parked path; recovery giữ install mới nếu nó tồn tại và có exe, rồi xóa marker. Backup được `ResetDir` và chép lại mỗi lần RunAsync, không kiểm transaction trước đã qua readiness chưa. Pending-stage resume chỉ chạy sau heartbeat thành công. Diagnostic crash subprocess tại `new-active`, recovery chọn NEW; chạy đúng phần reset/copy backup của lần apply sau thay backup OLD bằng NEW.
- **Finding:** All-old/all-new về directory không đồng nghĩa có rollback tốt sau crash. Updater chết giữa switch và readiness làm mất người giám sát readiness. Khi new Agent không heartbeat được, nó không gọi resume; khi heartbeat trở lại, resume có thể ghi đè known-good backup bằng bản chưa commit.
- **Realistic failure scenario:** Mất điện sau rename new-active hoặc sau khi xóa switch marker nhưng trước khi xác nhận heartbeat. Reboot chạy Agent mới có lỗi startup/network. Nếu không heartbeat, không còn updater sống để đếm timeout/rollback. Nếu heartbeat phục hồi và resume, lần lỗi sau chỉ còn backup của chính new version.
- **Operational impact:** Máy có thể mắc ở bản lỗi, cần can thiệp tại PC, dù thiết kế/checklist hứa rollback sau readiness failure. Đây là lỗi của transaction recovery, không phải file overlay cũ đã được sửa.
- **Root cause:** `.switch.json` chỉ giải quyết khoảng trống directory rename; `staged.txt` không lưu phase, known-good backup hoặc trạng thái readiness commit. Recovery phụ thuộc vào khả năng hoạt động của chính Agent mới.
- **Recommended direction:** Giữ journal nhỏ cho phases backup/switch/start/readiness/commit, gắn backup bất biến với attempt; bootstrap/updater ngoài install sở hữu recovery đến khi commit. Resume không reset backup của attempt chưa kết thúc. Giữ cơ chế external bootstrap và exact snapshot hiện có.
- **Required regression test:** Kill updater hoặc reboot sau switch, sau start và trước/sau heartbeat đầu tiên; bản mới crash ngay hoặc không thể heartbeat. Assert rollback OLD vẫn xảy ra và backup OLD không bị ghi đè qua nhiều restart, kể cả offer null/same-version/force downgrade.
- **Estimated change scope:** Medium.

### OTA05-04 — `_applyLaunched` có thể chặn các update sau cho tới khi Agent restart

- **Severity:** High.
- **Confidence:** High cho lỗi state; Medium cho tần suất race trên service thật.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/AgentUpdater.cs:77-82,282-318`; `MProjectAgent/Commands/ApplyUpdateCommand.cs:43-56,87-110`; `MProjectAgent/Services/AgentWorker.cs:255-283`.
- **Symbol:** `LaunchApply`, `MaybeStageUpdateAsync`, apply lease trong `RunAsync`.
- **Evidence:** `Interlocked.Exchange(...,1)` xảy ra trước `Process.Start`. Chỉ nhánh thiếu install/staged exe reset; không catch-reset khi launch ném lỗi, không quan sát child exit. Diagnostic staged exe không chạy được làm Process.Start ném Win32Exception; latch vẫn 1 và lần gọi kế tiếp trả ngay. Còn một đường thông thường: Agent mới ghi heartbeat-ready rồi thấy `staged.txt` của updater đang chờ; nó launch helper thứ hai, helper trả 0 khi lease đang bị giữ, nhưng Agent mới giữ latch=1.
- **Finding:** Cờ biểu diễn “đã từng thử launch” thay vì “một attempt còn hoạt động”. Ngay cả helper từ chối lease đúng cách cũng có thể làm process Agent mới mất khả năng áp update sau đó.
- **Realistic failure scenario:** Sau rollout A→B, heartbeat đầu tiên của B chạy trước khi updater A xóa staged marker. Helper cạnh tranh thoát theo lease; đến rollout B→C, B tải/stage được C nhưng LaunchApply bỏ qua do latch cũ. Một lần Process.Start fail tạm thời tạo hậu quả tương tự.
- **Operational impact:** Rollout đứng ở staged cho tới khi service được restart bằng cách khác. Việc lease chống apply trùng vẫn cần giữ; lỗi nằm ở caller quản lý lifecycle không đầy đủ.
- **Root cause:** Không reset/đối soát latch với lỗi launch, exit code, lease contention hoặc completion của transaction.
- **Recommended direction:** Theo dõi tiến trình/attempt và hoàn tất trạng thái trên mọi terminal path; chỉ bỏ retry khi có updater đang sở hữu transaction. Tránh tạo helper cạnh tranh chỉ vì readiness marker chưa kịp được commit, nhưng không bỏ recovery-before-version-check.
- **Required regression test:** Process.Start fail rồi thành công trong cùng Agent process; child thoát do lease; hai release liên tiếp A→B→C không restart thủ công; crash updater trong lúc new Agent đang heartbeat.
- **Estimated change scope:** Medium.

### OTA05-05 — Server activate release trước khi kiểm hợp đồng archive

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:179-200,223-247,321-334`; `MProjectAgent/Services/AgentUpdater.cs:230-278`.
- **Symbol:** `PublishAsync`, `EnsureAgentArchive`, `ValidateBundleManifestAsync`.
- **Evidence:** Server chỉ tìm entry tên root exe trong zip, không đọc release manifest hoặc đối chiếu form Version với manifest. Diagnostic archive có manifest 8.0.0.0 và chữ ký bằng key được server trust được publish/activate dưới 9.0.0.0; Agent từ chối cùng archive/offer này. Test server hiện có còn chủ đích dùng zip chỉ chứa exe để kiểm Publish.
- **Finding:** Chữ ký hợp lệ xác nhận bytes đã được signer ký, không chứng minh version nhập trên form hoặc archive đáp ứng protocol của updater. Publish sai đồng thời deactivate release tốt đang hoạt động.
- **Realistic failure scenario:** Operator chọn đúng zip đã ký nhưng gõ sai version, hoặc ký zip từ publish folder thiếu manifest. UI trả thành công, tất cả station mới tải xong mới phát hiện lỗi rồi backoff/reject.
- **Operational impact:** Rollout không tiến triển, tốn download và thay active version/adoption sang release không thể cài. Agent fail-closed vẫn ngăn áp bytes sai.
- **Root cause:** Archive validator phía server không thực hiện cùng hợp đồng với Agent; metadata ngoài archive không được bind trước active transition.
- **Recommended direction:** Validate manifest/schema/protocol/version, file set, sizes/hashes và station bundle requirements trước storage reference/activation; không chạy executable upload để đọc version. Reuse contract hoặc có parity tests để tránh hai validator lệch nhau.
- **Required regression test:** Signature hợp lệ nhưng thiếu manifest, sai form version, protocol khác, duplicate/path sai, thiếu/thừa payload hoặc hash không khớp phải bị từ chối; active release/reference count trước đó không đổi.
- **Estimated change scope:** Medium.

### OTA05-06 — Agent có thể exact-install một bundle không có Launcher

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/AgentUpdater.cs:243-278`; `MProjectAgent/Commands/ApplyUpdateCommand.cs:150-161,337-368`; `MProjectAgent/Services/LauncherBootstrapper.cs:35-38`; `scripts/package-agent.ps1:219-221,333-339`.
- **Symbol:** `ValidateBundleManifestAsync`, `CopyStagedOverInstall`, `WaitForHeartbeatReadinessAsync`, `LauncherBootstrapper.EnsureRunning`.
- **Evidence:** Validator chỉ bắt buộc manifest khai báo `MProjectAgent.exe`; không bắt Launcher/contracts/dependencies hoặc đối chiếu binary versions. Diagnostic zip ký đúng, manifest đúng, chỉ có exe: stage thành công; helper exact replacement xóa Launcher đang có ở install. Bootstrapper im lặng return khi thiếu Launcher, và readiness chỉ đo heartbeat Agent.
- **Finding:** “Exact file set” đang xác nhận inventory do người đóng gói tự khai báo, chưa xác nhận nội dung bắt buộc của station bundle. Bundle thiếu Launcher có thể vượt security/integrity checks rồi làm mất giao diện operator.
- **Realistic failure scenario:** Signer dùng `sign-agent-release.ps1` với archive từ bước publish Agent riêng và tự tạo manifest, hoặc pipeline bỏ sót Launcher. Nếu Agent exe thật hoạt động, heartbeat vẫn xác nhận readiness trong khi Launcher đã bị xóa. Packager chuẩn hiện tại có kiểm bốn Launcher files, nên finding không khẳng định đường chuẩn luôn tạo bundle thiếu.
- **Operational impact:** Agent vẫn Online/applied nhưng PC mất Launcher hoặc không load được loose dependencies. Signed manifest và rollback không tự phát hiện lỗi chức năng đó.
- **Root cause:** Không có minimum shipping contract được Agent/server enforce; readiness không bao phủ tính khả dụng của Launcher.
- **Recommended direction:** Enforce bộ thành phần bắt buộc phù hợp loại bundle và version compatibility trước stage/activate; giữ OTA protocol tách IPC protocol. Có validation khả dụng Launcher phù hợp máy không có user đăng nhập, không bắt buộc desktop window cho mọi station.
- **Required regression test:** Signed exe-only bundle, thiếu từng Launcher dependency, sai Launcher/IPC binary version và dư dependency ngoài contract; reject trước switch. Test bundle đầy đủ và rollback tương thích vẫn qua được.
- **Estimated change scope:** Medium.

### OTA05-07 — Stale Launcher output được hợp thức hóa bằng manifest mới

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `scripts/package-agent.ps1:179-217,333-339,368-382`; `MProjectLauncher/MProjectLauncher.csproj:27-31`.
- **Symbol:** Launcher MSBuild/bundle step, FileVersion checks, `$releaseFiles`.
- **Evidence:** Package destination được làm sạch, nhưng Launcher được incremental build vào `bin/<Configuration>/net48` không có clean riêng. Script copy mọi top-level `.exe/.dll/.config` từ folder này; version check chỉ ba binaries, manifest hash mọi file đã copy. Smoke thực thi đúng statement source bằng AST: `removed-dependency.dll` có sẵn trong fixture Launcher output được copy và liệt kê vào manifest.
- **Finding:** Manifest exactness không loại stale inputs; nó ký/khai báo cả output thừa. Một DLL bị bỏ khỏi project hoặc được để lại từ build/tooling trước vẫn có thể ship im lặng.
- **Realistic failure scenario:** Build machine bỏ một PackageReference hoặc từng có DLL thử nghiệm trong Launcher bin. MSBuild không xóa file không còn thuộc output hiện tại; lần release sau tiếp tục mang DLL đó dù ba version chính vẫn aligned.
- **Operational impact:** Phân phối dependency lỗi thời/unexpected file, khó tái lập artifact; có thể ảnh hưởng binding hoặc quy trình inventory. Smoke không khẳng định một stale DLL cụ thể hiện đang tồn tại trong bundle production.
- **Root cause:** Copy bằng extension từ mutable incremental output thay vì output sạch/được build khai báo.
- **Recommended direction:** Build Launcher vào output riêng mới hoặc làm sạch đúng output trước build, rồi đóng gói inventory được kiểm soát gồm exe/config và toàn bộ dependencies cần thiết. Giữ version gate và hash manifest.
- **Required regression test:** Seed old DLL/config/extra exe trong output trước packaging; chúng phải không xuất hiện trong package/manifest. Test sau khi remove/upgrade PackageReference và test đủ QRCoder/Newtonsoft/IPC dependencies.
- **Estimated change scope:** Small.

### OTA05-08 — Download body treo ngăn heartbeat tiếp tục

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/ServerClient.cs:113-119`; `MProjectAgent/Services/AgentUpdater.cs:104-126,429-433`; `MProjectAgent/Services/AgentWorker.cs:215-229,283`; `MProjectAgent/Program.cs:117`.
- **Symbol:** `OpenDownloadStreamAsync`, `DownloadToFileAsync`, `MaybeStageUpdateAsync`, `HeartbeatTickAsync`.
- **Evidence:** SendAsync dùng ResponseHeadersRead; body CopyToAsync chỉ dùng cancellation token dừng service. HttpClient timeout một giờ không tạo body-copy deadline tại đây. Worker await updater ngay trong heartbeat tick. Diagnostic HttpClient timeout 100 ms, response headers 200 trả ngay nhưng stream chờ vô hạn: sau 500 ms updater vẫn pending; chỉ dừng khi token ngoài được cancel.
- **Finding:** TCP/HTTP download đã có headers nhưng ngừng trả body có thể treo update vô hạn và ngăn heartbeat kế tiếp, thay vì đi vào backoff. Các poll/inventory loops khác độc lập vẫn có thể chạy.
- **Realistic failure scenario:** Server/storage/proxy ngừng truyền body mà chưa đóng kết nối. Agent tiếp tục một số công việc tại PC nhưng web chuyển Offline và không nhận heartbeat commands/offer mới.
- **Operational impact:** Mất control-plane liveness đúng lúc rollout; phải đợi kết nối được đóng hoặc restart service. Không có giới hạn bytes theo `update.Size` trong copy cũng làm thiếu một guard tài nguyên trước verify.
- **Root cause:** Download gắn trực tiếp với heartbeat và không có deadline/idle timeout/byte bound riêng sau headers.
- **Recommended direction:** Cho download deadline và/hoặc idle timeout hữu hạn, giới hạn byte count phù hợp archive policy, báo lỗi và giữ retry state. Tách thực thi update dài khỏi lịch heartbeat bằng một worker có single-flight nếu cần.
- **Required regression test:** Headers trả nhanh nhưng body không trả thêm bytes, body chậm rồi ngừng, EOF giữa chừng, cancellation và quá size; heartbeat vẫn tiếp tục hoặc tick kết thúc hữu hạn, partial zip được dọn và lần sau retry đúng backoff.
- **Estimated change scope:** Medium.

### OTA05-09 — ConsumeResult xóa kết quả trước khi delivery thành công

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/AgentUpdater.cs:39-66`; `MProjectAgent/Services/AgentWorker.cs:238-251`; `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:651-669`; `MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:105-115`.
- **Symbol:** `WriteResultFile`, `ConsumeResult`, `HeartbeatTickAsync`, `UpdateAgentTelemetryAsync`, `GetAdoptionAsync`.
- **Evidence:** ConsumeResult delete file trong finally ngay khi deserialize; Worker gọi nó khi dựng request trước khi await HTTP. Diagnostic ghi rolled_back result, consume và gọi ServerClient với network failure; consume kế tiếp là null. WriteResultFile ghi đè trực tiếp, trong khi readiness dùng temporary+move; partial JSON cũng bị consume-delete khi parse lỗi.
- **Finding:** Applied/rejected/rolled_back có thể mất vĩnh viễn chỉ vì lần heartbeat mang result thất bại, hoặc file write bị cắt giữa chừng.
- **Realistic failure scenario:** Service rollback khởi động lại đúng lúc mạng gián đoạn; heartbeat đầu đọc result rồi fail. Mạng hồi phục, Agent report version cũ nhưng không còn update outcome để server lưu.
- **Operational impact:** Adoption và last-update status không phản ánh nguyên nhân thất bại, operator khó phân biệt máy chưa thử update với máy đã rollback. Không ảnh hưởng tính đúng của chữ ký hay bytes đã cài.
- **Root cause:** Read được coi là acknowledgement; single mutable file không có atomic write hoặc generation để tránh xóa kết quả mới.
- **Recommended direction:** Peek/persist result cho tới heartbeat accepted, sau đó acknowledge đúng result/attempt ID; ghi file atomically và không xóa một generation mới xuất hiện trong khi request cũ đang bay. Reuse pattern nhỏ tương tự durable state hiện có, không cần fleet redesign.
- **Required regression test:** HTTP timeout/401/500 trước và sau server commit; result tiếp tục được gửi cho tới acknowledgement và không clear nhầm result mới. Test partial JSON/restart và stale last outcome trên adoption.
- **Estimated change scope:** Small.

### OTA05-10 — Không có retention hữu hạn cho staged versions

- **Severity:** Medium.
- **Confidence:** High về source; Medium về dung lượng và tần suất rollout thực tế.
- **Category:** design risk.
- **File:** `MProjectAgent/Services/AgentUpdater.cs:164-201,418-426`; `MProjectAgent/Commands/ApplyUpdateCommand.cs:106-127,146-191`; `MProjectAgent/Commands/BootstrapCommand.cs:19-25,61-77`.
- **Symbol:** `StageFromFileAsync`, `GetVersionedStagingDir`, update completion, bootstrap preparation/recovery.
- **Evidence:** Mỗi version dùng directory riêng; cleanup staging chỉ dọn prepared/previous của cùng lần stage, download.zip và staged marker. Không có xóa version cũ sau apply. Crash bypass finally có thể để `.prepare-*`, `.next-*`, parked snapshots; Recover không sweep các sibling còn lại khi install đã tồn tại. Cache size/LRU của payload CAS không quản lý các thư mục update này.
- **Finding:** Dung lượng update không bị chặn bởi một retention policy. Việc giữ staged source phục vụ bootstrap fallback/recovery có lý do, nhưng hiện không có ranh giới khi nào một version không còn được tham chiếu.
- **Realistic failure scenario:** PC chạy nhiều tháng, nhận nhiều self-contained releases và một vài rollout bị ngắt; mỗi version giữ thêm bản extract trong ProgramData. Sau cùng không đủ chỗ cho backup/candidate hoặc công việc khác.
- **Operational impact:** OTA mới thất bại do thiếu đĩa; có thể tác động runtime state/outbox nếu chung volume. Chưa có dữ liệu dung lượng fleet để dự báo khi nào xảy ra.
- **Root cause:** Các bước tạo artifact có cleanup cục bộ cho happy path, thiếu registry/retention theo active transaction và known-good snapshots.
- **Recommended direction:** Giữ số lượng snapshot hữu hạn và dọn theo reference/phase sau commit, có headroom check trước download/backup. Không xóa staged exe còn đang chạy hoặc backup của attempt chưa kết thúc.
- **Required regression test:** Nhiều version thành công, rollback và crash tại mỗi phase; dung lượng/số directory về giới hạn sau recovery, trong khi active executable và known-good backup luôn còn.
- **Estimated change scope:** Medium.

### OTA05-11 — OTA-002 vẫn mở: một key duy nhất và chưa có rotate/revoke OTA

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** design risk.
- **File:** `docs/PROJECT_REVIEW_CHECKLIST.md:816-823`; `MProjectAgent/Configuration/AgentOptions.cs:22-26`; `MProjectAgent/Services/ReleaseSignatureVerifier.cs:7-35`; `MProjectAgent/Commands/ApplyUpdateCommand.cs:248-257`; `MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:337-356`.
- **Symbol:** `ReleasePublicKeyPem`, `ReleaseSignatureVerifier.Verify`, `EnsureSignatureTrusted`, station config preservation.
- **Evidence:** Agent/server nhận một PEM; release/offer/manifest không có KeyId/trust-set transition. OTA giữ appsettings cũ nên đổi public key trong package mới không cập nhật trust của các PC đang chạy. Checklist vẫn để bốn mục rotation/revocation/runbook chưa hoàn thành.
- **Finding:** Gap OTA-002 đã biết vẫn còn; đây không phải bằng chứng có private key bị lộ hoặc RSA verification hiện tại bị bypass.
- **Realistic failure scenario:** Khi key đang dùng mất hoặc phải ngừng tin cậy, chỉ đổi server key và ký bằng key mới làm Agent cũ reject. Khôi phục trust cần phương thức triển khai/cấu hình khác hoặc một transition đã chuẩn bị trước.
- **Operational impact:** Khả năng duy trì/khôi phục kênh OTA trong incident phụ thuộc can thiệp vận hành ngoài cơ chế release hiện có.
- **Root cause:** Trust được cấu hình tĩnh theo station, chưa có protocol/runbook chuyển trust như checklist mô tả.
- **Recommended direction:** Giữ OTA-002 trong backlog và xác nhận đường vận hành khi mất/thu hồi key. Nếu thực hiện rotation, dùng chính các acceptance criteria đang có; không mở một thiết kế fleet mới chỉ từ review này.
- **Required regression test:** Theo OTA-002: transition K1→K1/K2, K2-only và reject K3; case station offline trong transition, revoke và recovery khi key cũ không dùng được nữa.
- **Estimated change scope:** Medium.

## Ma trận crash, retry và readiness

| Thời điểm/tình huống | Hành vi từ source và kiểm chứng | Kết luận |
| --- | --- | --- |
| Network exception/EOF khi tải | finally dọn download.zip, attempt đã có NotBefore; SHA mismatch không stage | Có retry/backoff; lỗi network chỉ log, không phải mọi lỗi đều thành UpdateStatus |
| Headers đã có, body đứng | Copy chờ token service; heartbeat đang await | OTA05-08 |
| SHA/signature/manifest sai | Reject trước staged marker; prepare được dọn khi exception được xử lý | Giữ fail-closed |
| Crash trong extract/prepare trước marker | Active install chưa thay; lần sau có thể tải/stage lại; prepared directory có thể mồ côi | Exact install an toàn tại bước này; retention thuộc OTA05-10 |
| Crash khi thay staging cùng version | Có two-rename và rollback khi exception; process exit không chạy catch/finally | Chưa có full fault drill cho cặp rename staging/marker; không khẳng định marker write có durability tuyệt đối |
| Candidate đang copy, old chưa park | Install cũ còn nguyên; backup đang được tạo lại có thể chưa đầy đủ | Existing exactness tests hỗ trợ all-old; chưa chứng minh mọi điểm mất điện thực |
| Old parked, candidate chưa active | `.switch.json` cho bootstrap move old về | Existing subprocess tests pass |
| New active, chưa heartbeat readiness | Bootstrap chấp nhận new nếu có exe; mất watchdog nếu updater chết; resume reset backup | OTA05-03 |
| Delete parked cũ bị khóa/xóa dở | Catch có thể xóa new, restore partial old hoặc mất install | OTA05-02, xác nhận trên Windows fixture |
| New service RUNNING nhưng không heartbeat | Updater sống: timeout và rollback; updater đã chết: không còn cùng cơ chế | Phân biệt rõ hai trường hợp, OTA05-03 |
| Stale heartbeat-ready từ lần trước | File bị xóa trước start; check version và timestamp ≥ startedAt | Chặn stale marker thông thường; chỉ một heartbeat được yêu cầu, không phải một khoảng “stable” nhiều heartbeat |
| Heartbeat đầu của Agent mới khi staged marker chưa clear | Có thể launch contender rồi latch giữ mãi | OTA05-04 |
| Rollback new đã khởi chạy Launcher | Service được stop; nhánh rollback không gọi StopLauncher lần nữa; chỉ đợi SCM RUNNING sau restore | Cần drill locks/Launcher và readiness cho rollback; không xem RUNNING là chứng minh Agent cũ hoạt động end-to-end |
| Offer lặp/new/forced downgrade | Jitter persisted 0..900 giây mặc định; backoff 5/20/80/320 phút rồi cap 360; cùng version clear retry; force chỉ bỏ điều kiện newer | Giữ so Version và backoff; pending marker đi trước ClaimAttempt là recovery policy, nhưng cần xử lý terminal apply đúng |
| Stale/corrupt result hoặc heartbeat fail | Read-delete; không có delivery acknowledgement | OTA05-09 |
| Bootstrap chưa có | Fallback staged exe; Prepare copy snapshot ngoài install rồi config service | Đường nâng từ bản thiếu bootstrap tồn tại; chưa kiểm end-to-end downgrade tới binary lịch sử không biết verb/bootstrap |

## Những quyết định tốt cần giữ

- Private signing key nằm ngoài server; Agent luôn đòi SHA và chữ ký hợp lệ, kể cả khi server không cấu hình verification key. Server hiện có `AgentRelease:PublicKeyPem`; Agent source mặc định self-update off/key rỗng là fail-closed, không phải bằng chứng fleet cấu hình sai.
- Manifest nằm trong zip được ký; kiểm exact file set, size, hash, version/protocol và stage vào thư mục riêng trước marker. `ZipFile.ExtractToDirectory` cùng path validation được dùng; không thấy code extract thủ công bỏ containment.
- Packager làm sạch output Agent, exclude `publish/**` khỏi SDK default items, loại environment/nested settings, cấu hình trước hash, đối chiếu ba FileVersion. Không bỏ các guard này khi sửa stale Launcher output.
- Root appsettings được preserve; durable state mặc định ở ProgramData bên ngoài install, được outbox tests kiểm việc update/rollback không đổi pending operations. Giữ sự tách biệt này khi sửa security metadata và recovery.
- External bootstrap, atomic recovery-marker write có flush, file lease chống hai switch cùng lúc và exact directory replacement giải quyết được các lỗi overlay trước đây. Cần mở rộng transaction đến readiness và sửa cleanup/latch, không quay lại overlay.
- Heartbeat readiness kiểm đúng version và timestamp sau start, mạnh hơn chỉ SCM RUNNING. Launcher được stop trước update và bootstrapper tự mở lại trên desktop phù hợp; thiếu logged-on user không nên tự động là lỗi rollout.
- Publish/activate/delete dùng transaction cho active/reference changes; DB có unique live version và unique active index. Blob GC claim được kiểm trước reference, và download capability bị vô hiệu khi release không còn active.
- Contract OTA v1 tách Launcher IPC v2; wire check dùng dependencies của bundle và đòi capabilities cụ thể. Không nâng OTA protocol như một cách đồng bộ tên version với IPC.

## Các nghi vấn đã loại hoặc không tách thành finding

- **Dùng chung install token:** đã được owner chấp thuận, không mở lại và không đề nghị per-machine mandatory enrollment. Những đoạn warning/enrollment prose trong scripts không được dùng để phủ nhận project memory.
- **Mọi concurrent activate đều tạo hai active release:** bị partial unique index `UX_AgentReleases_Active` chặn trong relational DB; chưa có bằng chứng cần flag duplicate-active. InMemory tests không kiểm constraint/concurrency đó.
- **Force cho phép áp cùng version vô hạn:** server và Agent đều bỏ same-version. Force downgrade là chủ ý; không xem version thấp hơn tự thân là lỗi.
- **Jitter 900 giây chắc chắn dùng capability đã hết hạn:** ClaimAttempt lưu version/NotBefore, không giữ URL cũ; khi hết jitter, heartbeat mới cung cấp offer/URL mới. Không xác nhận giả thuyết này.
- **Server có thể thay unsigned manifest sau khi signer ký:** zip hash/RSA bao phủ manifest; sửa bytes làm signature/hash fail. Stage chưa có yêu cầu attacker tự viết marker vì state/install ACL thuộc trust boundary; OTA05-01 là lỗi ACL riêng đã có bằng chứng.
- **Mọi extra file trong manifest là archive-validation defect:** manifest cho phép output phụ hợp lệ của publish. Lỗi được giới hạn vào thiếu thành phần bắt buộc và stale output được copy không kiểm soát, không đề nghị chỉ cho đúng ba binaries.
- **Server dedup blob là chiếm ownership của upload khác:** Publish nhận toàn bộ archive bytes và kiểm signature theo cấu hình, không attach tùy ý bằng SHA request. Không có bằng chứng mới để mở lại generic blob ownership finding của prompt 03.
- **Không có endpoint deactivate riêng tự động là lỗi:** source hiện chỉ hỗ trợ thay active release bằng publish/activate release khác. Đã ghi semantics này; thiếu yêu cầu sản phẩm về pause độc lập nên không nâng thành confirmed defect.
- **ScanBootstrapCommand là bootstrap OTA thứ hai:** đây là bước quét payload/install dir vào CAS, không phải SCM/update recovery. `cleanup-launcher-catalog.ps1` là công cụ catalog/payload thủ công; đã đọc nhưng không review/xử lý payload deletion trong prompt này.

## Phần chưa đủ bằng chứng và kiểm chứng tiếp theo

1. **Windows Service và mất điện thật:** phiên không elevated; chỉ có subprocess crash và filesystem/ACL fixtures. Chưa chạy LocalSystem drill, reboot thật, SCM recovery timing, loadable-exe locks, nhiều user session hoặc shutdown trong từng file copy. Existing drill chỉ cover ba switch points với child fixture, không cover toàn `RunAsync` và accepted network heartbeat.
2. **Shipping bundle mới:** full package không build qua do cache thiếu runtime/ILLink 8.0.30. Chưa có inventory/hash/PE metadata của final artifact tạo từ revision này; chưa chạy actual net48 Launcher/pipe E2E hoặc signer production. Signing smoke dùng key fixture và Windows PowerShell/OpenSSL, chưa chạy nhánh PowerShell 7 PEM API.
3. **Fleet configuration và historical downgrade:** chưa biết bootstrap/v1 cụ thể, ACL parents, dung lượng update, key thực, self-update flags và binary cũ trên các PC. Bootstrap/v1 được reuse khi exe tồn tại; không khẳng định binary bootstrap hiện deploy chứa toàn bộ logic của source mới. Việc nâng bootstrap protocol cần compatibility drill khi triển khai sửa ApplyUpdateCommand.
4. **Database/storage races:** 74 backend tests và diagnostics không thay PostgreSQL transaction/unique-index/GC-race integration trên release publish/delete/activate. Upload-to-storage trước DB insert có crash window cần đối chiếu lifecycle report 03; không tự tạo thêm finding chung ngoài scope.
5. **Full readiness của station:** một heartbeat không kiểm Launcher/window, IPC readiness, poll/job health hoặc stable interval; rollback chỉ kiểm SCM RUNNING. Ngoài lỗi thiếu bundle đã chứng minh, chưa có timing fixture đủ để xác định ngưỡng readiness phù hợp sản xuất, nên không đề nghị thay health policy toàn fleet từ suy đoán.

Ưu tiên regression là OTA05-01 đến OTA05-04, sau đó parity/minimum-bundle validation và packet/telemetry retry. Checklist OTA-001 cần được đối chiếu lại với các findings cụ thể này trước khi kết luận toàn vòng đời signed bundle → restarted service đã crash-safe.
