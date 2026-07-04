# UIStore → MProject Software Module — Parity Spec & Blast‑Radius Map

> **Mục đích.** Tài liệu này là *nguồn chân lý* cho việc làm lại chức năng **tạo / quản lý / phân phối phần mềm test** trong MProject (thay thế UIStore cũ).
> - **Part A** — checklist tính năng UIStore cũ *phải đạt tối thiểu* (acceptance criteria).
> - **Part B** — 2 phong cách đóng gói thật (`Sample_Software` có cấu trúc + `Cpp_Software` phi cấu trúc).
> - **Part C** — module software mới hiện có trong MProject + **blast‑radius map** = **rào chắn** cho quyết định giữ/sửa/thay (KHÔNG phải kế hoạch phá dỡ).
> - **Part D** — 2 nguyên tắc chỉ đạo + 7 quyết định kiến trúc cần chốt.
> - **Phụ lục** — quy trình 3 pha (spec→plan→code) để giao cho Fable 5.
>
> **Nguồn khảo sát (đọc trực tiếp):** `Old_program/**` — nhóm phân phối `UIStore`/`Upload`/`AppUpdater` đọc kỹ, nhóm test-engine `CPEI_MFG`/`FcdDownload`/`UiTest` đọc **mức quan hệ/ranh giới**; `Sample_Software/**` + `Cpp_Software/**` (2 gói mẫu); `MProjectBackend/**`, `MProjectAgent/**`, `MProjectFrontend/src/**`. Ngày lập/cập nhật: 2026‑07‑04.

---

## TL;DR (đọc trước)

1. **Mục tiêu:** thay lớp phân phối/quản lý phần mềm test (UIStore/Upload/AppUpdater cũ) bằng MProject web, đạt **tối thiểu parity F1–F20**, được phép nâng cấp.
2. **Module software mới KHÔNG phải lá — nó là xương sống pipeline deploy.** 8 seam (đứng đầu là **Agent**, deployable ngoài hiện trường) sẽ gãy nếu thay ẩu → Part C.
3. **Không mặc định xoá:** đánh giá code hiện có, quyết **giữ/sửa/thay từng phần**. **Không port 1:1:** parity = tương đương *chức năng*, ưu tiên pattern web-native → Part D.0.
4. **Ingest phải structure-agnostic:** nhận cây thư mục bất kỳ (C# *và* C++), chọn entry-point/icon/overridable-path thủ công → Part B.
5. **7 quyết định ở Part D.1 ĐÃ CHỐT (2026-07-04):** agent contract *được đổi thoải mái* (hệ mới thử nghiệm, chưa lên sản xuất → không cần rollout an toàn lúc này) · giữ blob SHA-256 · ingest cây-bất-kỳ + C++ hạng nhất · giữ 3 khái niệm override/baseline (đánh giá tinh gọn) · bỏ login tại trạm (chỉ RBAC web) · cờ per-app lên UI · giữ approvals.

## Mục lục

- **Part A** — UIStore cũ: kiến trúc (A.0), checklist F1–F20 (A.1), glossary cũ→mới (A.2)
- **Part B** — 2 phong cách đóng gói: có cấu trúc (B.1), phi cấu trúc (B.2), hệ quả (B.3)
- **Part C** — module mới: kiểm kê (C.1), 8 seam blast-radius (C.2), sơ đồ phụ thuộc (C.3)
- **Part D** — 2 nguyên tắc chỉ đạo (D.0), 7 quyết định cần chốt (D.1)
- **Phụ lục** — quy trình 3 pha cho Fable 5

---

## Part A — UIStore (cũ): kiến trúc & checklist parity

### Bản đồ hệ chương trình cũ (`Old_program`) — 6 chương trình & ranh giới scope

Hệ cũ gồm 6 project .NET, chia 2 nhóm. **MProject CHỈ thay nhóm phân phối/quản lý; nhóm test-engine giữ nguyên làm payload — KHÔNG viết lại.**

**Nhóm A — Phân phối/quản lý (ĐANG THAY):**
- `UIStore/UiStore` — client WPF tại trạm: tải/cập nhật/chạy app + file custom (trọng tâm tài liệu này).
- `Upload/Upload` — công cụ admin đóng gói + đẩy app/version + quản user/pc-list lên SFTP (phía publish, F18).
- `AppUpdater/AppUpdater` — self-update `UiStore.exe` (↔ **F17**; hệ mới thay bằng **GAP‑4 AgentRelease**).
- Kho chung: SFTP `10.72.162.101:4422`.

**Nhóm B — Test engine / payload (CHỈ hiểu ranh giới, KHÔNG rewrite):**
- `FTU Program/CPEI_MFG` — chương trình test chức năng (DHCP+SFIS+golden+limit+errorcode); chính là mẫu `Sample_Software`.
- `FcdDownload/WebControl_WinForm` — nạp FCD/firmware (fork CPEI_MFG; Selenium+RaspberryPi+PoE+UART).
- `UiTest` — framework test config-driven.

**Ranh giới UIStore ↔ payload (hợp đồng PHẢI GIỮ):** UIStore chỉ *deploy + launch* payload, KHÔNG biết logic test. Hợp đồng ngầm gồm: (1) **entry-point** (exe khởi đầu do người publish chọn); (2) **relative-path** payload tự đọc — vd CPEI_MFG đọc `..\Config` từ thư mục exe ⇒ agent phải đặt `WorkingDirectory` = thư mục exe; (3) **file config overridable** (limits.ini, ProgramConfig.json…); (4) payload **tự ghi HKCU** `Software\CPEI_MFG\Unit{n}` — module phân phối KHÔNG đụng, giữ qua update (xem memory `reference_cpei_mfg_registry_state`). ⇒ Bản mới phải **bảo toàn hợp đồng này**, không chạm nội tại payload.

### A.0 Kiến trúc tổng thể (để hiểu bối cảnh)

UIStore cũ là **app WPF chạy trên từng PC trạm test**. Nó *kéo* (pull) phần mềm từ 1 **server SFTP** theo chu kỳ, cache nội dung theo **MD5 (content‑addressed)**, và quản lý vòng đời từng app bằng một **state machine**.

- **Phân phối = SFTP pull.** Layout remote (xem `Common/PathUtil.cs`):
  - `AutoDownload/<RemotePath>/<Product>/<Station>/` chứa: `Apps.zip` (danh sách app của trạm), `AccessUserList.zip`, `Program/`, `Common/`.
  - `AutoDownload-Config/<RemotePath>/Auth/UserModel.zip` (user toàn cục).
  - `UiStoreModel.zip` + `UiStoreUpdate/` (UIStore tự cập nhật chính nó).
- **Content‑addressed + cache dedup.** Mỗi file mô tả bằng `FileModel{ ProgramPath, RemotePath, Md5, StoragePath }` (`Models/FileModel.cs`). File tải về **cache Common theo MD5**, chỉ tải cái *đổi MD5*; khi chạy thì *extract/copy từ cache* ra program folder theo MD5 (`Services/AppUnit/AppAttack.cs`). Zip có mật khẩu (`ConstKey.ZIP_PASSWORD`).
- **Worker pool** tải song song (`Configs/ConfigModel.cs`: MinWorker=0, MaxWorker=3, QueueCapacity; `Services/worker/**`), có TransferProgress.
- **Poll loop** mỗi `UpdateTime` giây + jitter (`ViewModels/MainViewModel.cs:166` `LoopAsync`): (1) tự cập nhật UIStore → (2) đồng bộ `Apps.zip` → (3) mỗi app tự update.

### A.1 Checklist tính năng (mỗi dòng = 1 tiêu chí nghiệm thu)

| # | Tính năng | Nguồn (file cũ) | Hành vi cần giữ | Trạng thái ở module mới |
|---|-----------|-----------------|-----------------|--------------------------|
| **F1** | **Chọn Product + Station** cho trạm | `Configs/LocationConfig.cs`, `View/SetStationView` | Mỗi PC gắn 1 (Product, Station); đổi được, reload lại danh sách app | Có: `StationSoftwareAssignment` gắn theo `StationResourceId`; agent tự biết station/model (enroll) |
| **F2** | **Danh sách app theo trạm** | `Models/AppList.cs` (`Apps.zip`) — `ProgramPaths: name→{AppPath, AccectUserPath, AccectPcPath}` | Trạm chỉ thấy app được gán cho nó; mỗi app trỏ tới config app + (tuỳ chọn) list user/pc riêng | Có (khác mô hình): gán = `StationSoftwareAssignment` (station↔package↔version) |
| **F3** | **App model + metadata phiên bản** | `Models/AppModel.cs` | Mỗi app có: `LaunchFile`, `MainPath`, `IconFile`, **versions FW/FCD/BOM/FTU**, cờ `Enable/AutoOpen/AutoUpdate/AutoRemove/CloseAndClear`, `FileModels[]`, `CheckSumFileModels{}` | Có (rộng hơn): `SoftwareVersion` có Bom/Fcd/Ftu/Fw/**Region**Version, `EntryPointPath`+`EntryPointMode`, `HealthCheckUrl`, `OverridablePaths[]` |
| **F4** | **Phát hiện bản mới** | `AppAttack.CheckUpdate` + `AppModelManagement.IsModelChanged` + `AppAttack.HasChangeProgramFiles` | So sánh model **và** verify MD5 từng file (kể cả file custom) → `HasNewVersion`; chỉ tải phần đổi | Tương đương ở agent: `manifest/resolve` gửi `HaveBlobHashes`, server trả delta (`JobExecutor.ResolveAsync`) |
| **F5** | **Tải nội dung (delta) + tiến độ** | `AppAttack.UpdateWareHouse`, `Services/ProcessService/FileProcess`, worker pool | Tải file thiếu vào cache, retry ≤3, báo `Progress` %, huỷ được | Có: `JobExecutor.DownloadMissingAsync` (parallel, throttle progress) |
| **F6** | **Extract + chạy app** | `AppAttack.Open` | Extract từ cache → program folder (tôn trọng file custom) → tìm/chạy `FullLaunchFile` → theo dõi tiến trình | Có: `InstallDirectoryService.DeployAsync` + `ProcessSupervisor` + `LaunchPolicy` |
| **F7** | **State machine vòng đời** | `Services/AppUnit/AppStatusInfo.cs`, `Services/AppEvents/**` | Cờ: `IsAppAvailable/IsEnable/IsRunnable/IsRunning/IsUpdateAble/HasNewVersion/Progress`; state `Update{SUCCESS/UPDATING/FAILED}`, `Extract{…}`; UI phản ứng theo event | Một phần ở agent/launcher (`CatalogAppDto.Status`: Running/Idle/CrashLoop/Maintenance/Updating) — **cần đối chiếu kỹ** |
| **F8** | **Cờ `AutoOpen`** (tự chạy) | `AppModel.AutoOpen`, `AutoRunActionEvents` | App khả dụng → tự mở không cần thao tác | `LaunchPolicy` quyết định launch; **chưa chắc có cờ per‑package** — cần xác nhận |
| **F9** | **Cờ `AutoUpdate`** | `AppModel.AutoUpdate` | Bật thì tự tải bản mới; tắt thì chỉ tải khi mở | Server đẩy job theo assignment; **mô hình khác** — cần map |
| **F10** | **Cờ `AutoRemove`** (gỡ khi hết gán) | `AppModel.AutoRemove`, `AppStoreFileManagement.CleanStore` | Không còn trong danh sách → xoá program folder + icon + cache ref | Có: `SoftwarePackage.AutoRemoveOnUnassign` + `JobExecutor.ExecuteUninstallJobAsync` (GAP‑6) |
| **F11** | **Cờ `CloseAndClear`** | `AppModel.CloseAndClear` | Đóng app và xoá file khi cần | **Cần xác nhận** có tương đương |
| **F12** | **Kill / theo dõi tiến trình** | `AppAttack.KillProcess/CheckRunning`, `Common/ProcessUtil` | Poll tiến trình theo `FullMainPath`; kill được; cờ `IsHaveAppRuning` | Có: `ProcessSupervisor`; stop qua launcher IPC (`IpcOps.Stop`) hoặc server command `AgentCommandType.StopApp`; ⚠ memory: operator chưa stop được trong vài case (deadlock) |
| **F13** | **Phân quyền theo PC** (allow/deny) | `Services/Authorization.cs` `IsAcceptPc`, `Models/pc/AccessPcListModel.cs` (`PcModels`, `IsAllow`) | Danh sách PcName allow *hoặc* deny; app không mở trên PC ngoài phạm vi | Khác: gán theo station/computer resource + RBAC; **không có "allow/deny theo tên PC" 1‑1** — cần map |
| **F14** | **Phân quyền theo user + login** | `Authorization.Login`, `Models/UserModel.cs`, `AccessUserListModel` | Trước khi mở app phải đăng nhập (Id/Password) nếu trạm có user list | **Không có ở luồng deploy mới** (RBAC là cho web console). Cần quyết định: có cần login tại trạm không? |
| **F15** | **File override / custom (giữ qua update)** | `Models/checksumcustom/CheckSumFileModel.cs`, `Services/CheckSumCustom/**`, `View/CustomFileForm` | Một số file (INI/config) đánh dấu `IsPrivate/IsCheckSum/JustExist/IsEditableKey`; giá trị custom *không bị update ghi đè*; có UI sửa key/value (INI editor); lưu ở `data/Custom` | Có (mô hình khác): `OverrideFile` (Scope Model/Station/Computer) + `SoftwareVersion.OverridablePaths` + `OverrideResolver` + `ConfigBaseline`/`BaselineEvaluator` |
| **F16** | **Icon app** | `AppAttack.UpdateIcon`, `AppUnit.ExtractIconFromApp` | Trích icon từ exe/icon file để hiển thị | Có: agent trích icon → `CatalogAppDto.IconBase64` |
| **F17** | **Self‑update client** | `Services/AutoUpdate.cs`, `UiStoreModel.zip` | Store tự cập nhật chính nó | Có: **GAP‑4 agent self‑update** + `AgentRelease` (đã có, xem memory) |
| **F18** | **PUBLISH: tạo/soạn gói & đẩy lên** | **`Old_program/Upload/**`** (`Services/Uploader.cs`, `SftpFileAction.cs`, `ModelView/MyTreeFolderForApp.cs`, `PcListViewModelView`, `UserListViewModelView`) | Tree-picker chọn **Launch/Main/Icon** từ cây file; **bắt buộc** BOM/FCD/FTU/FW + Launch + Icon (`InitCheckCondition`); set cờ Enable/AutoOpen/AutoUpdate/AutoRemove/CloseAndClear; MD5→zip→đẩy SFTP; **dọn file thừa** so với app list; quản lý user-list + pc-list per app; publish self‑update | Có (web): `NewSoftwareWizard.tsx`, `SoftwarePackages.tsx`, upload file → Blob, publish version, gán station |
| **F19** | **Logging / trạng thái kết nối** | `Services/Logger.cs`, `MainViewModel` (Ips, LogLines) | Log dòng sự kiện; hiển thị IP; báo mất kết nối server | Có ở web + agent logs |
| **F20** | **Đóng gói phi cấu trúc + chọn entry/icon** | `AppModel.LaunchFile/MainPath/IconFile`, Upload tree picker | Đẩy cây thư mục BẤT KỲ (vd `Cpp_Software` C++), người publish chọn file khởi đầu + icon + overridable paths; KHÔNG ép cấu trúc | `SoftwareVersion.EntryPointPath`+`OverridablePaths[]` nhận path tuỳ ý — **cần verify UI publish cho chọn từ cây file** |

> **Ghi chú parity quan trọng:** UIStore cũ **hợp nhất "store + runner"** trên trạm (1 app WPF vừa tải vừa chạy vừa cho login). MProject **tách 3 vai**: *web console* (tạo/quản lý/gán), *backend API* (điều phối), *agent + launcher* (tải/chạy trên trạm). Vì vậy vài tính năng "tại trạm" của UIStore (F13 allow/deny theo PcName, F14 login user tại trạm, F8/F9/F11 cờ per‑app) **không map 1‑1** — đây là các mục *phải quyết định* ở Part D, không được bỏ sót âm thầm.

### A.2 — Bảng ánh xạ thuật ngữ cũ → mới (glossary)

Dùng bảng này để đọc code cả hai bên mà không lạc. ⚠ = cảnh báo parity cần lưu ý.

| UIStore/Upload cũ | MProject mới | Ghi chú |
|---|---|---|
| `AppModel` (1 app) | `SoftwareVersion` (thuộc `SoftwarePackage`) | mới tách Package ↔ Version rõ; Status `Draft/Released/Deprecated` |
| `AppList` / `Apps.zip` (per station) | `StationSoftwareAssignment` | "app nào chạy ở trạm nào" (active/pin) |
| `FileModel.Md5` + cache Common | `SoftwareFile.BlobSha256` + `Blob` store + agent `BlobCache` | content-addressed; đã hiện đại → nên GIỮ |
| `LaunchFile` (chạy) | `SoftwareVersion.EntryPointPath` + `EntryPointMode(LongRunning\|RunOnce)` | ⚠ mới KHÔNG có `MainPath` riêng để *theo dõi tiến trình* khác file chạy (F12) — cân nhắc bổ sung |
| `IconFile` | agent trích icon → `CatalogAppDto.IconBase64` | |
| `CheckSumCustom` / overridable | `OverrideFile` (scope Model/Station/Computer) + `SoftwareVersion.OverridablePaths` + `OverrideResolver` | giữ file custom qua update |
| `LimitFile` / `FtuDataConfigs` (đối chiếu) | `ConfigBaseline` + `ConfigBaselineRule` + `BaselineEvaluator` | validation contract — NGƯỢC hướng override |
| `AccessPcListModel` (allow/deny PcName) | RBAC + assignment theo resource | ⚠ F13 chưa map 1‑1 — cần quyết định |
| `AccessUserListModel` + login tại trạm | RBAC web console | ⚠ F14 KHÔNG có login tại trạm — cần quyết định |
| cờ `AutoOpen/AutoUpdate/AutoRemove/CloseAndClear` | một phần: `AutoRemoveOnUnassign` + `LaunchPolicy` | ⚠ F8–F11 map chưa đủ |
| `AutoUpdate.cs` (self-update store) | GAP‑4 `AgentRelease` + agent self-update | |
| Upload tool (WinForms) | `NewSoftwareWizard.tsx` / `SoftwarePackages.tsx` (web) | phía publish |
| SFTP server + zip password | HTTP API + Blob store (presigned) | phía phân phối |
| poll loop (timer, `MainViewModel`) | server điều phối job + agent poll/heartbeat | |

---

## Part B — Định dạng gói phần mềm (2 phong cách đóng gói)

> **Nguyên tắc cốt lõi:** đơn vị đẩy lên = **một cây thư mục BẤT KỲ**. Người publish tự chỉ định *file khởi đầu (entry-point)*, *icon (tuỳ chọn)* và *các path được override (config)*. Module ingest **KHÔNG được giả định** cấu trúc `configs/bin/FTU` hay có `Config/`. Có (ít nhất) 2 phong cách:

### B.1 — Phong cách CÓ cấu trúc (họ C#/CPEI_MFG) — mẫu `Sample_Software/`

Gồm **3 khối** (mẫu: `Sample_Software/FTU_efbb_1.0.24_3.18.16_UTP-G3-Touch-Pro/`):

1. **Binary chính** — `Debug/` chứa `CPEI_MFG.exe` + hàng loạt DLL (BouncyCastle, Newtonsoft, SshNet, NI Common…). Đây là app test thật.
2. **Sub‑tool bundle** — thư mục kiểu `FTU_efbb_1.0.24_3.18.16_UTP-G3-Touch-Pro/` là 1 app Python đóng gói (python38.dll, PyQt5, PIL…) + exe. Gói lớn (hàng ngàn file runtime).
3. **`Config/*.json`** — cấu hình vận hành, mỗi file 1 mối quan tâm:

| File | Nội dung chính |
|------|----------------|
| `ProgramConfig.json` | Model, Station, versions (`VersionConfig`: FW/FCD/FTU/BOM/Region), `FtuConfig` (DirPath, param, data‑checks, log‑checks), `SfisConfig`, `LoopTestConfig`… |
| `LimitConfig.json` | `RemoteLimitFile`, `LocalFilePath: ../limits.ini`, **SftpConfig nhúng** (host/user/pass) |
| `ErrorCodeConfig.json` | bảng error‑code/keyword, `LocalFilePath: ../Errorcodes.csv`, SftpConfig nhúng |
| `GoldenConfig.json` | Good/Bad golden units, mốc thời gian verify (12h) |
| `CheckListMacConfig.json`, `ShippingConfig.json`, `DhcpConfig.json`, `CcdConfig.json`, `AdbCcdConfig.json`, `SerialControlConfig.json`, `TestConditionConfig.json`, `WorkerCheckerConfig.json`, `LoggerConfig.json` | các cấu hình phụ, nhiều file có `IsEnable`, đường dẫn local, và **SFTP creds nhúng** |

### B.2 — Phong cách PHI cấu trúc (payload native/C++) — mẫu `Cpp_Software/`

Chương trình **C++ Win32** của khách (CTS_UBNT). Cấu trúc HOÀN TOÀN khác họ C#: `CTSLib/`, `CTS_UBNT/{bin,DB,Launch}`, `equipment/{EpmControl,InstrBaseClss,include}`, `TestApp/{Debug,Release}`. 429 file, chủ yếu `.h/.cpp/.obj/.lib/.dll/.bat/.ini/.log`; **nhiều `.exe`** (adb, fastboot, iperf, putty, kernel, scp — phần lớn là tool phụ) ⇒ **không có entry-point hiển nhiên**, không có thư mục `Config/` gọn, file `.ini/.bat` rải rác.

Chương trình cũ xử lý loại này bằng cách: **đẩy cả thư mục lên, người dùng chọn file khởi đầu + icon, rồi chạy như bình thường** — đúng mô hình `AppModel.LaunchFile/MainPath/IconFile` chọn từ cây file (F3/F6). ⇒ Module mới phải cho **chọn entry-point + icon thủ công từ cây file** và **đánh dấu overridable path bất kỳ** (không auto-detect theo `Config/`); metadata version với loại này **nhập tay** (không parse được từ tên folder).

### B.3 — Quy ước & hệ quả cho module mới (áp dụng CẢ hai phong cách)
- **Ingest phải structure-agnostic:** đơn vị = cây thư mục bất kỳ; **chọn entry-point + icon thủ công**, đánh dấu **overridable path bất kỳ**. Mô hình mới đã hợp hướng: `SoftwareVersion.EntryPointPath` + `OverridablePaths[]` (list path tuỳ ý) + `OverrideFile.TargetRelativePath` đều nhận path tự do — **verify UI publish có cho chọn từ cây file không**.
- **Version nằm trong tên thư mục** (`FTU_efbb_1.0.24_3.18.16_...`) — **CHỈ** áp dụng phong cách B.1: nên *parse* metadata (BOM/FCD/FTU/FW/Region) khi ingest; phong cách B.2 thì **nhập tay**.
- **Nhiều file config chứa thông tin đặc thù site** (SFTP creds, đường dẫn local, limits.ini, Errorcodes.csv). Đây **chính là ứng viên "override files / overridable paths"**: phải cho phép ghi đè theo Model/Station/Computer và **giữ qua update** (đúng ý F15). Đừng hard‑code creds vào blob dùng chung.
- **Gói rất lớn + nhiều file trùng** giữa các version (runtime Python/DLL) → **content‑addressed blob + delta download là bắt buộc** để không tải lại toàn bộ. Mô hình `SoftwareFile.BlobSha256` mới đã đúng hướng (giống MD5 cũ).
- **App test tự ghi HKCU** `Software\CPEI_MFG\Unit{n}` (xem memory `reference_cpei_mfg_registry_state`) — module phân phối **không được đụng** registry đó; giữ nguyên qua update.

---

## Part C — Module software mới trong MProject + Blast‑Radius Map

> **Vai trò của Part C:** đây là *rào chắn* cho quyết định **giữ / sửa / thay** ở Part D — Fable phải *đánh giá* code hiện có rồi quyết từng thành phần; blast-radius cho biết đụng vào đâu thì gãy cái gì. **KHÔNG phải kế hoạch phá dỡ mặc định.** Nhiều phần (blob content-addressed, override path tuỳ ý, agent supervisor, RBAC) *đã hiện đại* và có thể giữ nguyên/chỉ sửa.

### C.1 Kiểm kê module hiện tại (phạm vi cân nhắc giữ/sửa/thay)

**Domain entities** (`MProjectBackend/MProject.Domain/Entities/Software/`):
`SoftwarePackage`, `SoftwareVersion`, `SoftwareFile`, `StationSoftwareAssignment`, `OverrideFile`, `ConfigBaseline`, `ConfigBaselineRule`, `InstallationJob`, `PcInstallationRecord`
+ enums: `AssignmentEffect`, `BaselineMatchType`, `ConfigBaselineStatus`, `InstallationJobStatus`, `InstallationJobType`, `InstallationStatus`, `OverrideFileStatus`, `OverrideScope`, `SoftwareVersionStatus`, `PcInstallationDriftStatus`, `EntryPointMode`.

**Application services** (`MProject.Application/Services/Software/`, 18 file):
`SoftwarePackageService`, `SoftwareVersionService`, `SoftwareFileService`, `StationSoftwareAssignmentService`, `StationRollbackWatchdogService`, `OverrideFileService`, `OverrideResolver`, `OverrideFilePermissionService`, `ConfigBaselineService`, `ConfigBaselineValidator`, `ConfigBaselinePermissionService`, `BaselineEvaluator`, `ConfigFileReader`, `InstallationJobService`, `InstallationJobWatchdogService`, `PcInstallationService`, `PcInventoryService`, `BlobGcService`
+ interfaces tương ứng (`Interface/Software/`, 15 file).

**Controllers** (`MProject.Api/Controllers/Software/`, 7): `SoftwarePackagesController`, `SoftwareVersionsController`, `SoftwareFilesController`, `SoftwareAssignmentsController`, `OverrideFilesController`, `ConfigBaselinesController`, `PcInstallationsController`.
Ngoài ra: các endpoint **agent‑facing** `agent/v1/*` (announce/enroll/heartbeat/poll/manifest/jobs/inventory/commands) — xem C.2 seam #1.

**Frontend** (`MProjectFrontend/src/pages/`): `Software/` (`SoftwarePackages.tsx`, `NewSoftwareWizard.tsx`, `DeploymentMatrix.tsx`, `components/*`, `hooks/*`), `OverrideFiles/`, `ConfigBaselines/`, `Installation/` (`InstallationJobs.tsx` + components). Routes trong `src/App.tsx`.

**DB**: bảng `SoftwarePackages/SoftwareVersions/SoftwareFiles/StationSoftwareAssignments/OverrideFiles/ConfigBaselines/ConfigBaselineRules/InstallationJobs/PcInstallationRecords` + `AppDbContextModelSnapshot` + hàng loạt migrations.

### C.2 Các SEAM (khớp nối) — nơi sẽ GÃY nếu thay ẩu

> Đây là phần *quan trọng nhất*. Module software **không phải lá**, nó là **xương sống pipeline deploy**. Với mỗi seam: hợp đồng (contract) + cái gì gãy.

**① AGENT — deployable ngoài, rủi ro cao nhất.**
- Contract = **HTTP API `agent/v1/*`** (xem `MProjectAgent/Services/ServerClient.cs`): `announce`, `enroll`, `heartbeat`, `poll`, `manifest/resolve`, `jobs/{id}/ack|progress|complete`, `inventory`, `commands/{id}/ack`, + tải blob qua URL (presigned hoặc relative + `X-Agent-Token`).
- Agent tiêu thụ **`AgentManifestJob`** (định nghĩa chính xác ở `MProjectAgent/Models/AgentApiModels.cs`): `JobId, PackageId, VersionId, PackageName, VersionNumber, JobType(Install|Uninstall), Status, EntryPointPath, EntryPointMode(LongRunning|RunOnce), HealthCheckUrl, TotalSize/DeltaSize`, `Files[AgentManifestFile{Path, Sha256, Size, ContentType, NeedsDownload, DownloadUrl, ExpiresAt}]`. Heartbeat còn mang lệnh `AgentCommandType`(CancelJob/Restart/KillAndRestart/ReloadConfig/ForceMaintenanceOff/StopApp) + báo health/specs + tín hiệu self-update (GAP‑4).
- Agent làm: **delta download theo Sha256** (gửi `HaveBlobHashes`), deploy vào install base, supervise/launch theo `LaunchPolicy`, uninstall (GAP‑6), báo tiến độ/hoàn tất.
- **Gãy nếu:** đổi shape `SoftwareVersion`/`SoftwareFile`/`InstallationJob` hoặc bất kỳ endpoint `agent/v1/*` → **mọi agent đang chạy ngoài hiện trường hỏng** cho tới khi được cập nhật. Đổi contract ⇒ **bắt buộc phát hành agent mới đồng bộ** (dùng GAP‑4 self‑update; xem memory `project_gap4_self_update`, `project_gap2_launcher`).

**② INSTALLATION pipeline.**
- `InstallationJobService` (server): từ assignment/version → tạo `InstallationJob`, presign manifest, nhận progress/complete, ghi `PcInstallationRecord`, phát **domain event** `InstallationJob.Completed/Failed` (`InstallationJobService.cs:443`).
- `InstallationJobWatchdogService` (job treo), `PcInstallationService` + `PcInstallationsController` (`installations/jobs...`), FE `InstallationJobs.tsx`.
- Phụ thuộc `SoftwareVersion` + `Computer` (Assets).
- **Gãy nếu:** đổi mô hình version/file hoặc bỏ domain event → FE Installation, watchdog, và cơ chế thông báo mất tín hiệu.

**③ ASSIGNMENT + ROLLBACK.**
- `StationSoftwareAssignment` = "software nào chạy ở station nào" (= AppList cũ): `TargetVersionId` (pin), `IsActive`, `PreviousVersionId`, `LastRollbackFromVersionId`, `AutoRemoveOnUnassign` trên package.
- `StationSoftwareAssignmentService`, `StationRollbackWatchdogService` (tự rollback khi fail lặp), `SoftwareAssignmentsController`.
- **Lưu ý deadlock đã biết** (memory `project_agent_deploy_deadlock`): cần version **Released + Pinned + assignment Active**, và app test cũ còn chạy sẽ chặn job mới (`CanEnqueueNewJobsAsync`).
- **Gãy nếu:** bỏ khái niệm assignment/active/pin → agent không biết phải chạy gì; rollback mất.

**④ APPROVALS.**
- `SoftwareAssignmentApprovalHandler`, `OverrideFileApprovalHandler`; `SoftwarePackage.AssignmentApprovalPolicyId → ApprovalPolicy`.
- Hành động gán/override có thể **phải duyệt** trước khi hiệu lực.
- **Gãy nếu:** luồng create/assign mới không raise/consume approval → **âm thầm bỏ cổng duyệt** (rủi ro tuân thủ). Xem FE Approvals đã redesign (memory `project_frontend_redesign_convention`).

**⑤ BLOB STORAGE + GC.**
- `SoftwareFile.BlobSha256 → Blob` (content‑addressed). `BlobGcService` **ref‑guard** Software + OverrideFile + AgentRelease trước khi thu hồi blob.
- **Gãy nếu:** lưu file mới không giữ tham chiếu Blob đúng cách → **GC xoá nhầm blob đang sống** (mất dữ liệu). Nếu bỏ entity cũ, phải cập nhật ref‑guard của `BlobGcService`.

**⑥ RBAC / Permissions.**
- `AppPermissions` (`MProject.Application/Constants/AppPermissions.cs`): `software.manage/read/download`, `software.install.manage`, `software.package.manage`, `software.version.publish`, `software.assignment.manage/approve`, `overridefile.manage/read`, `configbaseline.manage/read`, `assignment.manage`, `approvals.*`.
- Controllers gate theo các quyền này; role seed (Viewer/Member — memory `reference_rbac_roles_permissions`).
- **Gãy nếu:** xoá/đổi tên quyền → authorization + seed vỡ; user mất quyền hoặc lộ quyền.

**⑦ DOMAIN EVENTS / OUTBOX.**
- `InstallationJob.*` (và có thể assignment/override) chảy qua dispatcher → approvals/notifications. Giữ hoặc thiết kế lại có chủ đích.

**⑧ OVERRIDE RESOLVER / BASELINE EVALUATOR / INVENTORY.**
- `OverrideResolver` tính "file hiệu lực" cho từng computer (file package + override theo scope). `BaselineEvaluator` + `ConfigBaseline` đánh giá **drift** cấu hình. `PcInventoryService` + agent `inventory` báo trạng thái cài đặt/drift (`PcInstallationRecord.DriftStatus`).
- **Gãy nếu:** đổi mô hình override/version → manifest deploy và drift‑detection sai.

### C.3 Sơ đồ phụ thuộc (rút gọn)

```
Web console (FE) ──HTTP──> Backend API ──┬── domain events ──> Approvals / Notifications
   Software/Override/                     │
   Baseline/Installation/                 ├── StationSoftwareAssignment (station↔pkg↔version, active/pin)
   DeploymentMatrix                       │        └── StationRollbackWatchdog
                                          ├── InstallationJobService ── PcInstallationRecord/Inventory/Drift
                                          ├── OverrideResolver + BaselineEvaluator ─> effective files
                                          └── SoftwareFile ─> Blob <─ BlobGcService (ref-guard)
                                                   ▲
                       agent/v1/* (announce/enroll/heartbeat/poll/                │ presigned download
                       manifest/resolve/jobs/ack|progress|complete/inventory)     │
   Agent (trạm) ──────────────────────────────────────────────────────────────────┘
      └── BlobCache(delta by Sha256) → InstallDirectory → ProcessSupervisor/LaunchPolicy → Launcher (IPC)
```

---

## Part D — Nguyên tắc chỉ đạo + quyết định kiến trúc

### D.0 Hai nguyên tắc chỉ đạo (user chốt 2026-07-04)

- **ĐÁNH GIÁ TRƯỚC, không mặc định xoá.** Fable phải *review* code software mới hiện có (Part C), chấm sức khoẻ, rồi quyết **GIỮ / SỬA / THAY theo TỪNG thành phần**, kèm lý do. Code dùng được thì chỉ sửa; chỉ thay khi thay **tối ưu hơn hẳn**. Blast-radius map là rào chắn cho quyết định này.
- **NÂNG CẤP, không port 1:1.** Parity = **tương đương chức năng/năng lực** (checklist F1–F20), KHÔNG phải sao chép cơ chế/UI của WinForms/WPF. Ưu tiên pattern web/hiện đại khi tốt hơn, ví dụ: server điều phối (pull job) thay vì desktop poll-SFTP; RBAC web + approval thay vì login dialog tại trạm; agent supervisor/health-check thay vì tray-runner; blob content-addressed (Sha256) thay vì zip-password; event-driven/outbox thay vì timer-loop. Mục tiêu là *khai thác nền tảng mới tốt hơn*, không phải dựng lại UIStore trên web.

### D.1 — 7 quyết định (ĐÃ CHỐT 2026-07-04)

| # | Quyết định | Chốt | Hệ quả bắt buộc cho plan |
|---|---|---|---|
| 1 | **Contract Agent** | **ĐƯỢC đổi thoải mái** | Tự do thiết kế lại `agent/v1/*` + `AgentManifestJob`. **Bối cảnh: mới thử nghiệm, CHƯA lên dây chuyền sản xuất thật** → chỉ cần rebuild + redeploy vài agent test; **KHÔNG cần** version song song/back-compat lúc này. Plan vẫn nên gói phần agent thành lát cắt riêng cho dễ phát hành lại. |
| 2 | **Lưu trữ file** | **Giữ SHA‑256 blob + delta** | Giữ `Blob`/`BlobCache`/`BlobGcService`; không đụng thiết kế content-addressed. |
| 3 | **Ingest & payload** | **Cây bất kỳ + C++ hạng nhất** | Ingest structure-agnostic (`Sample_Software` *và* `Cpp_Software`), chọn entry/icon/overridable **thủ công**; **mở rộng `EntryPointMode`** cho exe opaque (không health-endpoint). Không auto-ép cấu trúc. |
| 4 | **Override/Baseline** | **Giữ 3 khái niệm, đánh giá tinh gọn** | Giữ `OverrideFile` + `OverridablePaths` + `ConfigBaseline` (giữ validation/drift). Bước B0: Fable review độ phức tạp, đề xuất tinh gọn nếu thừa — **KHÔNG bỏ năng lực**. |
| 5 | **Auth tại trạm** | **Chỉ RBAC web, bỏ login tại trạm** | Agent headless theo assignment; F13/F14 **KHÔNG port**; thay allow/deny PcName bằng gán theo Station/Computer resource. |
| 6 | **Cờ per-app** | **Map vào assignment/policy + lên UI** | AutoOpen/AutoUpdate/AutoRemove/CloseAndClear → cấu hình per-package/assignment chỉnh trên web (mở rộng `LaunchPolicy` + `AutoRemoveOnUnassign` hiện có). |
| 7 | **Approvals** | **Giữ** | Gán/override đi qua approval policy (giữ handler + policy hiện có). |

> **Đọc lại seam ① sau quyết định #1:** contract agent ĐƯỢC đổi thoải mái. Vì hệ **mới thử nghiệm, CHƯA có trạm sản xuất thật**, rủi ro rollout gần như bằng 0 — cứ đổi contract rồi rebuild + redeploy agent test. Chỉ khi **lên dây chuyền thật sau này** mới cần chiến lược nâng cấp an toàn (v1/v2 song song hoặc cờ tương thích); ghi nhận để đó, KHÔNG phải lo bây giờ.

---

## Phụ lục — dùng tài liệu này với Fable 5 (3 pha)

- **Pha A (spec):** input = Part A + Part B. Yêu cầu Fable 5 đọc `Old_program/UIStore/UiStore/**` (bỏ bin/obj/packages/DLL) và *trích dẫn file\:line* cho từng feature để buộc đọc thật; bổ sung/hiệu chỉnh checklist F1–F20 nếu phát hiện thiếu.
- **Pha B (plan, bật plan mode):** input = Part C + Part D. **Bước B0 — ĐÁNH GIÁ:** Fable review code software mới hiện có, chấm sức khoẻ, ra bảng **GIỮ / SỬA / THAY** cho từng thành phần (entities, 18 service, controllers, FE, agent seam) kèm lý do — đối chiếu blast-radius ①–⑧. **Bước B1 — THIẾT KẾ:** từ bảng đó, plan theo **lát cắt dọc** (BE→Agent→FE), mỗi lát nói rõ giữ contract hay redesign có chủ đích, và *nâng cấp chỗ nào sang pattern web-native* (nguyên tắc D.0). Trả lời 7 câu ở D.1 trước.
- **Pha C (code):** làm từng lát; verify bằng gói thật `Sample_Software`; nếu lát chạm seam ① thì verify cả phía Agent.

**Ràng buộc xuyên suốt:** đọc `docs/skills/coding_rule.md` trước khi code, `docs/skills/review_rule.md` trước khi review; không phá agent deploy / installation / approvals / rollback đang chạy trừ khi được duyệt.
