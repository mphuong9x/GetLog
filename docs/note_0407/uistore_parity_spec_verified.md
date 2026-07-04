# UIStore Parity Spec — KẾT QUẢ KIỂM CHỨNG PHA A (đọc code thật)

> Sinh bởi Pha A theo `docs/uistore_parity_spec.md`. Mọi trích dẫn `file:line` đã đọc trực tiếp từ
> `Old_program/**` (chỉ `.cs/.xaml`, bỏ bin/obj/packages) và 2 fixture `Sample_Software/**`, `Cpp_Software/**`.
> Ngày kiểm chứng: 2026-07-04. KHÔNG chứa đề xuất kiến trúc (để Pha B).
>
> Đường dẫn viết tắt: `US/` = `Old_program/UIStore/UiStore/`, `UP/` = `Old_program/Upload/Upload/`,
> `AU/` = `Old_program/AppUpdater/AppUpdater/`, `FTU/` = `Old_program/FTU Program/CPEI_MFG/`.

---

## 1. Sơ đồ quan hệ `Old_program` + ranh giới distributor ↔ payload (nhiệm vụ 2)

```
                      SFTP 10.72.162.101:4422  (US/Configs/ConfigModel.cs:20-22, user/pass hardcode)
                      user "download"/"download168!!" — mọi file zip có password MD5("@RaspberryPi5@")
                      (US/Common/ConstKey.cs:5)
      ┌────────────────────────────────────────────────────────────────────────────┐
      │  AutoDownload/<RemotePath>/<Product>/<Station>/                            │
      │      Apps.zip                    (danh sách app của trạm)                  │
      │      AccessUserList.zip          (login vào STORE của trạm)                │
      │      Program/<App>_AppModel.zip  + <App>_AccessUserList.zip                │
      │                                  + <App>_AccessPcList.zip                  │
      │      Common/<Md5>.zip            (kho blob content-addressed per station)  │
      │  AutoDownload-Config/<RemotePath>/                                         │
      │      Auth/UserModel.zip          (user toàn cục cho tool Upload)           │
      │      UiStoreModel.zip + Common/  (gói self-update UiStore)                 │
      └────────────────────────────────────────────────────────────────────────────┘
        ▲ đẩy (publish)                      │ kéo (poll ~UpdateTime giây + jitter)
        │                                    ▼
   ┌─────────────┐                    ┌──────────────┐     ghi bin/UiStoreUpdate rồi chạy
   │ Upload      │  UiStore mở được   │ UiStore (WPF │────────────────┐
   │ (WinForms,  │◄───"upload.exe"────│ tại trạm)    │                ▼
   │ admin tool) │  (MainViewModel.cs │  cache MD5   │         ┌────────────┐
   └─────────────┘        :50-57)     │  + launch    │◄──kill/──│ AppUpdater │ (self-update
      layout SFTP: UP/common/         └──────┬───────┘  copy/   └────────────┘  UiStore.exe,
      PathUtil.cs:65-77                      │          restart   AU/Program.cs:19-38 ↔ F17)
                                             │ deploy + launch (KHÔNG biết logic test)
   ═══════════════ RANH GIỚI DISTRIBUTOR ↔ PAYLOAD ═══════════════════════════════════
                                             ▼
   ┌──────────────────────────── NHÓM B — TEST ENGINE / PAYLOAD (không rewrite) ─────┐
   │ FTU Program/CPEI_MFG  — app test chức năng WinForms (= mẫu Sample_Software)     │
   │ FcdDownload/WebControl_WinForm — nạp FCD/firmware, cùng họ CPEI_MFG (có        │
   │   Config/, Common/, communicate/, Program.cs — fork cấu trúc tương tự)          │
   │ UiTest — framework test WPF config-driven (App.xaml, Config/, Functions/…)      │
   └──────────────────────────────────────────────────────────────────────────────────┘
```

**Hợp đồng distributor ↔ payload PHẢI bảo toàn (đã kiểm chứng bằng code):**

| # | Hợp đồng | Bằng chứng |
|---|----------|-----------|
| C1 | **Entry-point do người publish chọn**, chạy với **WorkingDirectory = thư mục chứa file chạy** | `US/Common/ProcessUtil.cs:116` — `WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(process)`; AppUpdater cũng vậy `AU/Program.cs:34` |
| C2 | **Payload đọc đường dẫn tương đối từ thư mục exe** — cây thư mục gói phải giữ NGUYÊN VẸN | `FTU/Config/ConfigLoader.cs:24` — `Path.Combine(AppContext.BaseDirectory, "..\\Config")`; `Sample_Software/Config/ProgramConfig.json` `FtuConfig.DirPath: "../FTU_efbb_..."`; `LimitConfig.json` `LocalFilePath: "../limits.ini"` |
| C3 | **File config overridable** (INI/json) giữ giá trị custom qua update | cơ chế CheckSumCustom — xem F15 |
| C4 | **Payload tự ghi HKCU `Software\CPEI_MFG\Unit{n}`** — distributor không đụng | `FTU/Services/Condition/CheckTestFailed.cs:15`, `GoldenVerify.cs:18`, `UnitCounter.cs:10` (khớp memory `reference_cpei_mfg_registry_state`) |
| C5 | **Entry-point không chỉ .exe** — UiStore theo dõi/chạy được `.exe/.jar/.py/.bat` | `US/Common/ProcessUtil.cs:44-60` (switch extension); fixture `Cpp_Software/CTS_UBNT/Launch/UTPG3T00T01.bat` là file chạy thật |
| C6 | **File theo dõi tiến trình (MainPath) có thể KHÁC file chạy (LaunchFile)** | `US/Models/AppModel.cs:14-19` `FullMainPath` fallback `FullLaunchFile`; `AppAttack.CheckRunning` (`US/Services/AppUnit/AppAttack.cs:328-332`) poll theo `FullMainPath` |

**Vị trí AppUpdater:** helper console riêng, được gói self-update tải xuống `bin/UiStoreUpdate` rồi UiStore chạy nó (`US/Services/AutoUpdate.cs:224-240`); nó kill process UiStore, copy `NewAppDir` đè thư mục đích, chạy lại exe (`AU/Program.cs:19-38`). Hệ mới đã thay bằng GAP-4 AgentRelease → **không port**.

**FcdDownload / UiTest:** chỉ là *payload* được phân phối như mọi app khác (không có liên kết code nào với UiStore/Upload ngoài việc nằm trong kho SFTP). Không cần hiểu sâu hơn cho module phân phối.

---

## 2. Kiểm chứng checklist F1–F20 (nhiệm vụ 3)

Ký hiệu: ✅ = spec đúng, đã có trích dẫn; ✏️ = spec cần SỬA/bổ sung (ghi rõ); phần "Trạng thái ở module mới" của spec KHÔNG kiểm ở pha này (thuộc Pha B).

### F1 — Chọn Product + Station ✅
- `US/Configs/LocationConfig.cs:10` — lưu `location.json` tại `LocalModelPath` (`C:/UiStoreModel`, `US/Configs/ConfigModel.cs:28`).
- `US/ViewModels/SetStationModelView.cs:22-35` — OkCommand ghi Product/Station rồi `LocationConfig.UpdateConfig()` + reload; danh sách Product/Station lấy bằng cách **liệt kê thư mục SFTP** (`Refresh():40-56`, `OnProductItemPropertyChanged():88-103`).
- `US/ViewModels/MainViewModel.cs:107-124` `Reload()` — đổi location → stop toàn bộ → start lại.

### F2 — Danh sách app theo trạm ✅
- `US/Models/AppList.cs:5-8` — `Dictionary<string, ProgramPathModel>`; `US/Models/ProgramPathModel.cs:4-9` — `{AppPath, AccectUserPath, AccectPcPath}` (đúng như spec, kể cả lỗi chính tả "Accect").
- Đồng bộ mỗi vòng poll: `US/ViewModels/MainViewModel.cs:204-225` `SyncAppsConfigAsync` tải `Apps.zip` (path `US/Common/PathUtil.cs:70-73`) → `ProgramManagement.UpdateApps`.
- App biến mất khỏi list → remove: `US/Services/ProgramManagement.cs:127-130` `RemoveAppsNotExists`.

### F3 — App model + metadata phiên bản ✅ (+2 ghi chú)
- `US/Models/AppModel.cs:10-34` — đủ `LaunchFile/MainPath/IconFile`, `FWSersion/FCDVersion/BOMVersion/FTUVersion` (sic "FWSersion"), cờ `Enable/AutoOpen/AutoUpdate/AutoRemove/CloseAndClear`, `FileModels[]`, `CheckSumFileModels{}`, `LastTimeUpdate`.
- Phía Upload thêm 3 field hạ tầng: `RemoteStoreDir/RemoteAppListPath/Path` (`UP/model/Program/AppModel.cs:16-19`) và FileModel thêm `Mb/IsMain/IsLaunch/IsIcon` (`UP/model/Program/FileModel.cs:10-13`).
- ✏️ **Ghi chú 1:** hệ cũ **KHÔNG có lịch sử phiên bản** — mỗi app chỉ có đúng 1 `AppModel.zip` hiện hành, "version" FW/FCD/BOM/FTU chỉ là text metadata; không rollback, không Draft/Released. (Mô hình Package↔Version mới là nâng cấp, không phải parity.)
- ✏️ **Ghi chú 2:** `RegionVersion` KHÔNG tồn tại trong AppModel cũ — chỉ có trong `ProgramConfig.json` của payload (`Sample_Software/Config/ProgramConfig.json` → `VersionConfig.RegionVer`). Spec ghi mới "rộng hơn" là đúng.

### F4 — Phát hiện bản mới ✅ (+1 SỬA quan trọng)
- `US/Services/AppUnit/AppAttack.cs:44-93` `CheckUpdate`; so model `AppModelManagement.IsModelChanged` (`US/Services/AppUnit/AppModelManagement.cs:69-78`) dùng `AppModelComparetor.CompareInfo` (`US/Services/AppUnit/AppModelComparetor.cs:11-42` — so Launch/Main/Icon/FTU/BOM/FCD; **không so FWSersion** — có vẻ bug/chủ ý cũ) + `CompareFiles` trả danh sách file cần xóa.
- Verify MD5 từng file kể cả custom: `AppAttack.HasChangeProgramFiles:258-280` (file custom check qua `FileCustomCenter.IsCheckSumPass`).
- ✏️ **SỬA:** `HasNewVersion` **chỉ được tính khi app ĐANG chạy** (`AppAttack.cs:64-67` — `if (AppStatus.IsRunning …)`); khi app không chạy thì vòng update tải thẳng phần đổi vào cache và lần Open kế tiếp extract đè (không có khái niệm "bản mới chờ xác nhận"). Spec câu "So sánh model và verify MD5 → HasNewVersion" cần thêm điều kiện này.
- Chỉ tải phần đổi: cache-first per file — `US/Services/ProcessService/FileProcess/FileProcessSevice.cs:35-39` (`TryGetCache(model.Md5)` → skip download).

### F5 — Tải nội dung (delta) + tiến độ ✅
- `AppAttack.UpdateWareHouse` (`US/Services/AppUnit/AppAttack.cs:94-158`): retry tối đa **3 vòng** (`while i++ < 3`, dòng 103), báo `Progress = done*100/total` (dòng 121), hủy được (`CancelUpdate:39-42` + CancellationToken).
- Dedup theo MD5 trước khi tải: `FileProcessSevice.DownloadFilesAsync:24-26` (`GroupBy(i => i.Md5)` → chỉ tải bản đầu mỗi nhóm).
- Worker pool song song: `US/Configs/ConfigModel.cs:23-25` (MinWorker=0, MaxWorker=3, QueueCapacity=100), `Services/worker/**`; mỗi file là 1 zip riêng có password tải qua stream (`FileProcessSevice.CreateDownloadJob:28-76`).

### F6 — Extract + chạy app ✅
- `AppAttack.Open` (`US/Services/AppUnit/AppAttack.cs:298-326`): `ExtrackProgramFiles` (164-222, copy từ cache theo MD5, **file custom được thay bằng bản custom** dòng 180-191) → tìm process đang chạy sẵn `FindProcessByFile(FullLaunchFile)` nếu không có thì `RunProcess` (311-314) → `IsRunning = Process != null`.
- WorkingDirectory contract: xem C1 ở mục 1.

### F7 — State machine vòng đời ✅
- Cờ + derived: `US/Services/AppUnit/AppStatusInfo.cs:23-39` — `IsRunnable = IsAppAvailable && IsEnable && !IsRunning && UpdateStatus==SUCCESS && !IsExtracting && !HasNewVersion` (dòng 35), `IsUpdateAble` (36), `IsStandby` (39); enum `UpdateState/ExtractState {SUCCESS,UPDATING/EXTRACTING,FAILED}` (67-78).
- UI phản ứng theo event: 9 nhóm event đăng ký trên `OnValueChange` — `US/Services/AppEvents/AppEvent.cs:8-30`; màu trạng thái `US/ViewModels/AppViewModel.cs:21-29, 253-295`.
- Dọn dẹp khi app dừng/mất: `RunningStatusEvents.cs:41-49` (dừng + standby → xóa file thừa, CloseAndClear → xóa folder), `:57-70` (unavailable → CleanStore + RemoveApp; disable → CleanStore + DisableApp).

### F8 — Cờ `AutoOpen` ✅ (+1 ghi chú)
- `AppModel.AutoOpen` → `AppStatusInfo.IsAutoRun` (`US/Services/AppUnit/AppModelManagement.cs:50`).
- `US/Services/AppEvents/AutoRunActionEvents.cs:14-15`: khi runnable → `AppUnit.LaunchApp(false)`; được kích sau update thành công (`UpdateActionEvents.cs:37`) và khi app dừng (`RunningStatusEvents.cs:54-55`).
- ✏️ **Ghi chú:** `LaunchApp(false)` = **bỏ qua login** (`US/Services/AppUnit/AppUnit.cs:67-113`, nhánh `!isCheckLogin`) — auto-open không hỏi user/password dù app có user list. Mở tay mới phải login.

### F9 — Cờ `AutoUpdate` ✏️ SỬA NGHĨA
- Spec ghi "Bật thì tự tải bản mới; tắt thì chỉ tải khi mở" — **SAI**. Việc TẢI luôn diễn ra theo poll bất kể cờ (`ProgramManagement.UpdateApps:37-40` gọi `appUnit.Update()` vô điều kiện → `AppAttack.CheckUpdate`).
- Nghĩa thật: khi phát hiện `HasNewVersion` **trong lúc app đang chạy**, nếu `IsAutoUpdate` → **tự đóng app** để bản mới được extract ở lần mở sau (`US/Services/AppEvents/HasNewVersionEvents.cs:16-24`). Tắt cờ → app cứ chạy bản cũ tới khi tự đóng.

### F10 — Cờ `AutoRemove` ✏️ SỬA NGHĨA
- Spec ghi "Không còn trong danh sách → xoá program folder + icon + cache ref" — hành vi xóa đó là **VÔ ĐIỀU KIỆN**, không phụ thuộc cờ: app mất khỏi `Apps.zip` → `ActiveStatusEvents.cs:13-16` (`CleanStore()` + `RemoveApp`, chỉ chờ app không chạy) và `ProgramManagement.RemoveApp:52-67` (unsubscribe cache ref).
- Nghĩa thật của cờ `AutoRemove`: sau update thành công, nếu app bị **Enable=false** và `AutoRemove` → **force-close app đang chạy** (`US/Services/AppEvents/UpdateActionEvents.cs:18-22`); sau đó nhánh disable của `RunningStatusEvents.cs:65-69` dọn folder.

### F11 — Cờ `CloseAndClear` ✏️ SỬA NGHĨA
- Spec ghi "Đóng app và xoá file khi cần" — cờ này **KHÔNG đóng app**. Nghĩa thật: khi app **dừng chạy** (hoặc sau chu kỳ update lúc app không chạy) → xóa toàn bộ program folder + icon (`RunningStatusEvents.cs:41-49`, `UpdateActionEvents.cs:24-33` → `AppStoreFileManagement.CleanStore:40-44`). Tức "không lưu app trên đĩa khi không chạy — extract lại từ cache mỗi lần mở".
- Liên quan: **launch file LUÔN bị xóa khi app dừng, bất kể cờ** (`RunningStatusEvents.cs:36-38` → `RemoveLaunchFile`, `US/Services/AppUnit/AppStoreFileManagement.cs:75-88`) — chống chạy tay ngoài store (xem F22 mới).

### F12 — Kill / theo dõi tiến trình ✅
- Poll: `AppUnit.CheckRunning` (`US/Services/AppUnit/AppUnit.cs:208-232`, chu kỳ 1s khi chạy / 5s khi không) → `AppAttack.CheckRunning:328-332` tìm theo `FullMainPath`.
- `FindProcessByFile` hỗ trợ `.exe/.jar/.py/.bat` qua WMI ExecutablePath/CommandLine (`US/Common/ProcessUtil.cs:41-106`); kill: `AppAttack.KillProcess:282-294`; cờ tổng `IsHaveAppRuning` poll 1s (`MainViewModel.cs:46-47`, `ProgramManagement.cs:69`).
- ✏️ **Ghi chú:** với `.bat` chỉ match process `cmd` có command-line chứa path — yếu; và `MainPath` mặc định = LaunchFile nếu bỏ trống (`AppModel.cs:14-18`, Upload tự điền `UP/gui/FormMain.cs:223-233`).

### F13 — Phân quyền theo PC (allow/deny) ✅
- `US/Services/Authorization.cs:80-88` `IsAcceptPc`: list rỗng/không có → cho phép; ngược lại `IsAllow ? isContain : !isContain` (allow-list HOẶC deny-list đúng như spec); `US/Models/pc/AccessPcListModel.cs:5-9`.
- Được nhồi vào `IsEnable` mỗi lần đọc model (`AppModelManagement.cs:46`) và check lại lúc mở app (`AppUnit.LaunchApp:85-88`).
- Phía Upload soạn list per-app: `UP/ModelView/PcListViewModelView.cs:23,93` (checkbox Allow), file `<App>_AccessPcList.zip` (`UP/common/PathUtil.cs:74-77`).

### F14 — Phân quyền theo user + login ✅ (+1 SỬA: 2 CẤP login)
- ✏️ **SỬA/bổ sung:** có **HAI cấp** login, spec chỉ nói 1:
  1. **Login vào STORE khi khởi động** nếu trạm có `AccessUserList.zip` cấp station: `MainViewModel.Start:92-95` (`GetStationAccessUserPath`) → `CheckLogin:161-164` → không login thì không vào vòng poll.
  2. **Login khi mở từng app** nếu app có user list riêng: `AppUnit.LaunchApp:100-111` (`_authentication.Login()`).
- `Authorization.Login` (`US/Services/Authorization.cs:52-79`): list rỗng → pass luôn; password so MD5 (`US/ViewModels/LoginViewModel.cs:50-65`); ghi log `loginLog.txt` (`LoginViewModel.cs:98-109`).
- Đã CHỐT không port (D.1 #5) — chỉ ghi nhận hành vi.

### F15 — File override / custom (giữ qua update) ✅ (chi tiết semantics 4 cờ)
- Model: `US/Models/checksumcustom/CheckSumFileModel.cs:5-21` — `IsCheckSum` (default **true** = file thường, verify MD5), `IsCheckValue`, `JustExist`, `IsPrivate`, `IsEditableKey`, `Keys{ItemKeyModel}`, `FileExtension`.
- Semantics (từ `US/Services/CheckSumCustom/FileCustomCenter.cs:25-96` + `IniExtensionEditer.cs:23-53`):
  - `IsCheckSum=false` mới là "file custom". `IsCheckValue=true` → **merge INI**: key trong `Keys` (theo `IsEditableKey` là whitelist-sửa hay lock-list) giữ giá trị custom của trạm, key còn lại ép theo bản gốc (`IniExtensionEditer.TryCreateCustomFile:23-53` — đây là điểm tinh vi nhất: *một file, một phần key theo server, một phần key theo trạm*).
  - `JustExist=true` → chỉ cần file tồn tại, nội dung trạm giữ nguyên (copy ngược ra data custom, `FileCustomCenter.cs:41-44`).
  - còn lại → so MD5 giữa bản custom (`data/Custom/...`) và bản đang deploy; extract dùng bản custom thay bản gốc (`AppAttack.ExtrackProgramFiles:180-191`).
  - `IsPrivate` → lưu custom theo `data/Custom/<AppName>/<path>` (per-app) vs chia sẻ chung giữa app (`AppModelManagement.cs:114-120`).
- UI trạm: `View/CustomFileForm` + INI editor `View/CustomFileView/EditFileValueView` (mở qua `AppViewModel.ShowSetting:72-80`; edit/replace/delete/open-with: `FileCustomCenter.cs:97-190`).
- UI publish đặt cờ + chọn keys: `UP/gui/FileSettingForm.cs:76-134` (checkbox Checksum/CheckValue/JustExist/Privacy/EditableKey + parse INI chọn từng key).
- Lưu ở `data/Custom`: `US/Common/PathUtil.cs:114-117`.

### F16 — Icon app ✅
- `AppAttack.UpdateIcon:235-246` (copy icon file từ cache) → `AppUnit.ExtractIconFromApp:203-206` → `AppViewModel.ExtractIconFromApp:181-209` (`Icon.ExtractAssociatedIcon`, fallback `SystemIcons.Application`); icon lưu `Icons/<Product>/<Station>/<App>/` (`US/Common/PathUtil.cs:118-125`).

### F17 — Self-update client ✅ (+1 bổ sung)
- `US/Services/AutoUpdate.cs:34-66` `CheckUpdate`: tải `UiStoreModel.zip` (`PathUtil.GetUiStoreRemotePath:35-38`) → tải file vào `bin/UiStoreUpdate` → `Open():224-240` chạy `FullLaunchPath` (= AppUpdater.exe trong gói). AppUpdater kill UiStore → copy đè → restart (`AU/Program.cs:19-38`, so MD5 từng cặp file `ConfigModel.Files`, `AU/Program.cs:50-63`).
- ✏️ **Bổ sung:** gói self-update còn là kênh **server-driven tuning** cho client: `UIStoreModel.MaxSession/SessionLifespan/CycleUpdateTime` ghi đè `ConfigModel.MaxWorker/RemoveWorkerTimeMs/UpdateTime` (`AutoUpdate.cs:50-52`, `US/Models/UIStoreModel.cs:11-25` có clamp min). Hệ mới thay bằng GAP-4 — chỉ ghi nhận.
- Publish gói self-update từ Upload: `UP/gui/UiStoreSetting.cs:41-120`.

### F18 — PUBLISH: tạo/soạn gói & đẩy lên ✅ (+chi tiết)
- Bắt buộc trước khi upload: BOM/FCD/FTU/FW/Launch/Icon không rỗng (`UP/Services/Uploader.cs:57-65` `InitCheckCondition`) **và** Launch/Main/Icon phải trỏ tới file có thật trong cây (`UpdateAppModel:220-241` + `IsAppFileSelectionOk:244-270`). `MainPath` auto = LaunchFile nếu bỏ trống (`UP/gui/FormMain.cs:223-233`).
- Set cờ Enable/AutoOpen/AutoUpdate/AutoRemove/CloseAndClear: `Uploader.cs:213-217`.
- Upload file: MD5 từng file → nén từng file thành `<Md5>.zip` (password) đặt vào `Common/` của station — content-addressed thật sự (`UP/Services/Process/FileProcess/FileProcessSevice.cs:28-39`, dòng 33 `RemotePath = RemoteDir/{Md5}.zip`; dedup GroupBy Md5 dòng 38-39) → rồi upload `AppModel.zip` (`Uploader.cs:88-121`).
- **Dọn file thừa có REF-COUNT giữa các app**: `UP/common/ModelUtil.cs:92-110` `GetCanDeleteFileModelsAsync` — chỉ xóa blob không còn app nào trong `AppList` tham chiếu (tiền thân của BlobGc ref-guard).
- Quản lý user-list/pc-list per app: `UP/gui/Auth/User/AccessUserControl.cs`, `UP/gui/Auth/Pc/AccessPcControl.cs`, `UP/ModelView/PcListViewModelView.cs`, `UserListViewModelView.cs`.
- Tạo app mới = tạo `AppModel.zip` rỗng + 2 access list rỗng + ghi vào `Apps.zip` (`UP/Services/LocationCreater.cs:205-276` `CreateAppModel`).
- Publish self-update: `UP/gui/UiStoreSetting.cs` (xem F17).

### F19 — Logging / trạng thái kết nối ✅
- `US/Services/Logger.cs:24-34` — log dòng theo tên app, **giữ tối đa 10 dòng** (rolling); IP refresh 5s (`MainViewModel.cs:44-45, 192-202`); mất kết nối → "Connect to server failded!" retry 60s (`MainViewModel.CheckConnectServer:131-143`).

### F20 — Đóng gói phi cấu trúc + chọn entry/icon ✅
- Tree picker: context-menu "Set Launch file" / "Set Main file" / "Get icon file" trên node file bất kỳ (`UP/ModelView/MyTreeFolderForApp.cs:21-44`); thao tác cây tự do: Add files/Add folder/Create folder/Rename/Delete/Replace/Download (`:45-76`).
- Không ép cấu trúc: cây được populate từ `FileModels` phẳng (`Uploader.Show:154-180`), mọi path đều hợp lệ.
- Fixture C++ xác nhận: entry thực tế là **file .bat** `Cpp_Software/CTS_UBNT/Launch/UTPG3T00T01.bat` — hệ cũ chọn được vì tree picker + `ProcessUtil` hỗ trợ .bat (xem C5). ⇒ module mới phải cho entry-point non-exe hoặc tuyên bố rõ hạn chế.

---

## 3. Feature PHÁT HIỆN THÊM — đề nghị bổ sung checklist (F21–F26)

| # | Tính năng | Nguồn (file:line) | Hành vi | Ghi chú map mới |
|---|-----------|-------------------|---------|-----------------|
| **F21** | **Single-instance + signal show/close + chạy cùng Windows** | `US/App.xaml.cs:20-95` (EventWaitHandle `SINAL_SHOW/COLSE`), `US/Services/StartupShortcut.cs:8-35`, cờ `IsOpenWithSystem` (`ConfigModel.cs:36`) | Instance 2 cùng path → signal show rồi thoát; khác path → đóng instance cũ (đường update); shortcut Startup folder | Memory `project_gap7_misc_confirmed`: đã map sang LauncherBootstrapper/tray — ghi vào checklist cho đủ, trạng thái DONE |
| **F22** | **Chống chạy tay/chống sửa tại trạm** | Xóa launch file khi app dừng `RunningStatusEvents.cs:36-38` + `AppStoreFileManagement.RemoveLaunchFile:75-88`; ẩn folder Apps/cache `attrib +h +s` (`ProgramManagement.cs:24-25`, `CacheService.Init:31`); zip password mọi file (`ConstKey.cs:5`) | Operator không thể chạy app ngoài store hoặc copy exe | Cần quyết định Pha B: agent mới có cần mức "hardening" này không |
| **F23** | **Duplicate program (clone app trong station)** | `UP/Services/LocationCreater.cs:277-368` `DuplicateProgram` | Copy AppModel + user-list + pc-list sang tên mới, `Enable=false`, file dùng chung blob (không re-upload) | Tiện publish; mới chưa chắc có "clone package/version" |
| **F24** | **Quản trị taxonomy Product/Station từ tool publish** | `UP/Services/LocationCreater.cs:28-95` CreateProduct/DeleteProduct, `:96-180` CreateStation (kèm tạo `Common/` + AccessUserList rỗng)/DeleteStation (chặn xóa khi còn app), DeleteProgram `:369-428` (kèm ref-count blob) | CRUD cây Product/Station/App trên kho | Mới: station/model là resource enroll — cần map đủ thao tác xóa an toàn |
| **F25** | **User toàn cục + role cho tool Upload** | `UP/model/Auth/Role/RoleEnum.cs:3-6` (ROOT/ADMIN/PROGRAM_ADMIN/PROGRAM_MANAGE/ACCOUNT_ADMIN/USER), `UP/model/Auth/User/UserModel.cs:9-11` (Roles), kho `AutoDownload-Config/Auth/UserModel.zip` (`UP/common/PathUtil.cs:22-25`) | Login vào Upload + phân quyền thao tác publish | = RBAC web hiện có; ghi nhận để không tưởng là thiếu |
| **F26** | **Server-driven client tuning qua gói self-update** | `US/Services/AutoUpdate.cs:50-52`, `US/Models/UIStoreModel.cs:11-25` | Chỉnh chu kỳ poll/số worker của MỌI trạm từ server không cần phát hành bản mới | Mới: tương đương = agent config qua heartbeat/`ReloadConfig` — cần xác nhận ở Pha B |

(Đã cân nhắc và KHÔNG tách riêng: nút mở Upload từ UiStore (`MainViewModel.cs:50-57`) — chỉ là launcher tiện tay, gộp vào F18; jitter poll ngẫu nhiên ≤5s (`MainViewModel.cs:185`) — chi tiết hạ tầng.)

---

## 4. Kiểm chứng Part B — 2 fixture gói (nhiệm vụ 4)

### B.1 `Sample_Software/` (có cấu trúc — họ CPEI_MFG) ✅ đúng như spec
- 3 khối xác nhận bằng listing thật: `Config/` (13 file json đúng bảng spec), `Debug/` (CPEI_MFG.exe + DLL BouncyCastle/Newtonsoft/SshNet/NI…), `FTU_efbb_1.0.24_3.18.16_UTP-G3-Touch-Pro/` (python38 runtime, .pyd, PyQt…).
- `ProgramConfig.json` xác nhận: `VersionConfig{FWVer,FCDVer,FTUVer,BOMVer,RegionVer}`, `FtuConfig.DirPath: "../FTU_efbb_..."` (relative-path contract C2).
- `LimitConfig.json` xác nhận **SFTP creds nhúng** (`10.72.162.101:4422`, user `limitUser`/`Foxconn168!!`) + `LocalFilePath: ../limits.ini` — đúng ứng viên overridable như spec B.3.
- ⚠ Cây gói khi publish: entry `Debug/CPEI_MFG.exe` nằm ở **thư mục con cấp 1**, `Config/` và FTU bundle là **anh em cùng cấp** — mọi relative `..\` từ exe đều ra root gói ⇒ ingest phải giữ nguyên cả cây từ root, không được "nhặt" riêng thư mục exe.

### B.2 `Cpp_Software/` (phi cấu trúc — C++) ✅ đúng như spec, +1 chi tiết đắt giá
- Xác nhận 429 file; cấu trúc `CTSLib/`, `CTS_UBNT/{bin,DB,Launch}`, `equipment/{common,EpmControl,include,InstrBaseClss,MessageBox}`, `TestApp/` (source .cpp/.h/.vcproj lẫn lộn); nhiều exe tool phụ (adb, fastboot, iperf, putty, kernel, scp) — không có entry hiển nhiên.
- ✏️ **Chi tiết mới:** entry-point thực tế là **`.bat`**: `CTS_UBNT/Launch/UTPG3T00T01.bat` (+ biến thể `_noSFC.bat`), kèm `DB/*.mdb` (Access DB — state đọc/ghi tại trạm, ứng viên `JustExist`/overridable). ⇒ yêu cầu "chọn entry thủ công" phải chấp nhận **file không phải exe**, và bộ theo dõi tiến trình không thể chỉ dựa tên exe (xem C5/F12).

### B.3 Hệ quả ✅ — mọi khẳng định của spec (structure-agnostic, version-trong-tên-folder chỉ B.1, config chứa creds là ứng viên override, content-addressed bắt buộc, không đụng HKCU) đều khớp bằng chứng ở trên; riêng "verify UI publish mới cho chọn từ cây file" thuộc Pha B.

---

## 5. Điểm nghi ngờ / cần user xác nhận

1. **F9/F10/F11 sửa nghĩa (mục 2)** — tôi đã sửa theo code thật: AutoUpdate = "tự đóng app khi có bản mới", AutoRemove = "force-close khi bị disable" (xóa-khi-mất-gán là vô điều kiện), CloseAndClear = "không giữ file trên đĩa khi app không chạy". Xác nhận đây là hành vi *muốn giữ* khi map sang cờ per-assignment mới (D.1 #6), hay chỉ cần giữ *ý định* (auto-close-on-update / force-close-on-disable / clean-on-stop)?
2. **F22 hardening tại trạm** (xóa launch file khi dừng, ẩn folder, zip password): có phải yêu cầu thật của nhà xưởng (chống operator can thiệp) cần parity, hay là giải pháp tình thế của hệ SFTP cũ có thể bỏ (agent mới đã chạy service + RBAC)?
3. **Entry-point `.bat` (Cpp_Software)**: xác nhận trạm C++ thật đang chạy qua `.bat` → module mới phải hỗ trợ entry non-exe (và cách theo dõi process của nó) ngay từ đầu, đúng không? (ảnh hưởng quyết định D.1 #3 "mở rộng EntryPointMode").
4. **`MainPath` (file theo dõi ≠ file chạy)**: glossary spec đã cảnh báo mới thiếu trường này. Code cũ xác nhận nó tồn tại và Upload bắt buộc (auto-default = LaunchFile). Có case thực tế nào Launch=bat/wrapper còn Main=exe chính không? (quyết định có thêm trường "watch process" ở version mới).
5. **Dead field nghi ngờ ở hệ cũ** (chỉ để khỏi port nhầm): `UP/model/Auth/User/AccessUserListModel.cs:9` có `IsAllow` nhưng UiStore **không đọc** (user list luôn là allow-login); `AppModelComparetor.CompareInfo` **không so FWSersion** (`US/Services/AppUnit/AppModelComparetor.cs:11-42`) — đổi mỗi FW version sẽ không kích "model changed" (bug cũ?). Xác nhận không cần tái hiện.
6. **2 cấp login (F14)**: spec chỉ mô tả login khi mở app; code có thêm login vào store lúc khởi động (station-level `AccessUserList.zip`). Đã CHỐT bỏ login tại trạm — xác nhận bỏ CẢ HAI cấp.
7. **`HasChangeProgramFiles` chỉ chạy khi app đang chạy** (F4): khi app không chạy, mọi chỉnh sửa tay vào program folder bị lặng lẽ ghi đè lúc mở (trừ file custom). Hệ mới có drift-detection (BaselineEvaluator) — xác nhận hành vi "ghi đè im lặng khi deploy" vẫn là mong muốn.
8. **Fixture ≠ nguồn**: `Sample_Software` là bản build/đóng gói của chính `FTU Program/CPEI_MFG` (model UTPG3T00T01) — tôi coi đây là 1 app duy nhất ở 2 dạng (source vs package). Xác nhận đúng.
