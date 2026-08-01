# Compare 01 — Hạng mục MProject cần bổ sung để thay thế chương trình cũ

> Mục tiêu tài liệu: tổng kết **những phần MProject (web) cần xây dựng thêm** để thay thế hoàn chỉnh hệ thống cũ trong `Old_program/`, ưu tiên **Phase 1 = thay `UIStore`**.
> Ngày lập: 2026-06-16.
>
> **Changelog**
> - v1 (2026-06-16): bản đầu, 7 GAP.
> - v2 (2026-06-16): bổ sung mục **1.1 Giải phẫu một gói app thật** (`Sample_Software/`); refine **GAP-1** (composite + override 2 tầng/2 định dạng/2 phạm vi) và **GAP-5** (ví dụ `VersionConfig` thật).
> - v3 (2026-06-16): **GAP-1 có thiết kế** (`docs/gap1_config_override_design.md`) + **HỢP NHẤT** với LimitFile → **GAP-3 gộp vào GAP-1** (1 entity `OverrideFile` có `Kind`, 3 tầng scope, approval Station/Computer). Pilot tracer-bullet **PASS** phần lõi.
> - v4 (2026-06-20): **GAP-1/GAP-2/GAP-3 DONE**; **GAP-4 (agent self-update) DONE** end-to-end (3 slice A/B/C). ⚠️ **Còn nợ bảo mật trước production: ký số bản phát hành (F-08)** — xem GAP-4. Self-update đang gate `Agent:SelfUpdateEnabled=false` mặc định.
> - v5 (2026-06-20): **GAP-7 ĐÃ XÁC NHẬN** (audit code thật, 0 dòng code): startup → `LauncherBootstrapper`; show/shutdown.signal → `AgentCommand` + tray; icon DONE; "đóng & xóa cache" để GAP-6 phụ trách. Còn lại: GAP-6 (kiểm thử Uninstall), GAP-5 (metadata version), F-08 (ký số).
> - v8 (2026-06-21): **GAP-6 (Uninstall/cleanup) CODE DONE** — hướng A (Uninstall job thật) + cờ opt-in `AutoRemoveOnUnassign` mặc định OFF; agent xóa install dir có guardrail + giải ref cache; agent 113 + BE 441 pass, FE typecheck OK. **Chờ nghiệm thu PC thật** (destructive). ⇒ **Toàn bộ GAP trong tài liệu đã code xong**, chỉ còn các hạng mục nghiệm thu E2E trên trạm thật.
> - v7 (2026-06-21): **GAP-5 (metadata version có cấu trúc) DONE** — thêm 5 cột `BomVersion/FcdVersion/FtuVersion/FwVersion/RegionVersion` trên `SoftwareVersion` (migration `AddSoftwareVersionMetadata`), nhập khi tạo version + hiển thị chip trên FE; BE 439/439 + FE typecheck OK. **Còn lại: chỉ GAP-6** (Uninstall/cleanup — hoãn, cần PC thật + destructive; chủ dự án chọn làm GAP-5 trước).
> - v6 (2026-06-20): **F-08 (ký số bản phát hành) DONE** — chữ ký RSA/SHA-256 tạo offline (private key ngoài server), server lưu opaque, agent verify **fail-closed** trước khi apply; tool `scripts/sign-agent-release.ps1`; doc `docs/note_2006/F08_release_signing.md`. BE 49 + agent 99 tests pass. Còn lại: **GAP-6** (kiểm thử Uninstall), **GAP-5** (metadata version) — đều P2, không chặn.

---

## 1. Bối cảnh

`Old_program/` gồm 2 nhóm (sản phẩm UniFi Access — Ubiquiti, gia công Foxconn):

- **Nhóm Auto-download** (kho chung SFTP `10.72.162.101:4422`):
  - `Upload` — công cụ admin: đóng gói app, gán version (BOM/FCD/FTU/FW), phân quyền User/PC, khai báo tùy biến config.
  - `UIStore` — client WPF chạy ở khay hệ thống trên mỗi PC: tải/cập nhật/auto-run app, **CheckSumCustom** (vá config theo từng máy), login operator.
  - `AppUpdater` — console tự cập nhật `UiStore.exe`.
- **Nhóm Test programs**: `FTU/CPEI_MFG`, `FcdDownload`, `UiTest` (chưa thuộc phạm vi thay thế phase này).

**MProject** thay thế dần nhóm Auto-download trước, bắt đầu từ `UIStore`. Hiện đã phủ ~85% và phần lớn vượt trội về kiến trúc.

### 1.1 Giải phẫu một gói app thật — `Sample_Software/`

`Sample_Software/` là **một "app"** mà hệ cũ đẩy lên server cho **mỗi trạm của mỗi model** rồi hiển thị trên UIStore. Nó **KHÔNG phải 1 chương trình đơn — mà là composite 3 phần**:

```
Sample_Software/                     ← 1 gói = 1 app (1 model + 1 trạm)
├── Debug/CPEI_MFG.exe               ← LAUNCHER của ta (C#/.NET; SSH.NET, NI instruments) = entry point
├── Config/*.json  (13 file)         ← config của CPEI_MFG (giá trị theo trạm + model)
└── FTU_efbb_1.0.24_3.18.16_UTP-G3-Touch-Pro/   ← chương trình test CỦA KHÁCH (Ubiquiti)
        (Python/PyInstaller: PyQt5, cv2, scipy… + jlinkarm_nrf_worker.exe nạp FW)
        └── data/config_files/        (template)  ↔  data/custom_config_files/ (override theo trạm)
```

**Quan hệ:** `CPEI_MFG.exe` là vỏ điều phối; nó **gọi** chương trình khách qua `Config/ProgramConfig.json > FtuConfig`:
- `DirPath` = `../FTU_efbb_1.0.24_3.18.16_UTP-G3-Touch-Pro` (đường dẫn — **đổi theo version/model**)
- `CustomConfigFileName` = `ctr_04247_efbb.ini` (chọn file custom của khách)
- `FTUParam` = `-p=UniFiTalk --mvc` (tham số chạy — khớp `run_windows_mvc.bat`)
- `FtuDataConfigs` còn **đọc ngược** ini của khách để verify BOM (`General/top_level_bom == 000-08323-01`…).

**Hệ quả quan trọng:** mỗi model có **bộ chương trình khách riêng** (tên folder mã hóa cả version), và toàn bộ composite này phải đi cùng nhau như **một package-version duy nhất**. Đây là input trực tiếp định hình GAP-1 và GAP-5 bên dưới.

---

## 2. Bản đồ phủ — MProject ĐÃ CÓ

| Chức năng hệ cũ | Thành phần MProject | Trạng thái |
|---|---|---|
| Upload: đóng gói app, version, entry point | `SoftwarePackage`/`SoftwareVersion`/`SoftwareFile`; FE `pages/Software` | ✅ Mạnh hơn |
| Zip + mã hóa + MD5 | `Blob` content-addressed SHA-256, dedup, delta, tải song song | ✅ Vượt trội |
| Phân quyền User + PC (file INI) | RBAC + ACL + Resource hierarchy + Department/Team/ProductGroup + Approval workflow | ✅ Vượt xa |
| UIStore client tải/cập nhật | `MProjectAgent` (Windows service): enroll, heartbeat, poll, inventory | ✅ Có |
| Cache cục bộ | `BlobCacheService` (SHA-256, ref-count, LRU) | ✅ Vượt trội |
| AutoOpen (tự chạy app) | `ProcessSupervisor` (launch + restart + crash-loop protection + health probe) | ✅ Vượt trội |
| "App nào cho station/model nào" | `Model`, `Station`, `Computer`↔`ComputerStationHistory`, `StationSoftwareAssignment` (pin version) | ✅ Đúng nhu cầu |
| Điều khiển từ xa | `AgentCommand` queue (Restart/Stop/Cancel/ReloadConfig) | ✅ Vượt trội |
| AutoRemove (gỡ khi bỏ gán) | `InstallationJobType.Uninstall` | ⚠️ Có enum, cần kiểm flow |

**Điểm cộng MProject có mà hệ cũ KHÔNG có** (nên giữ): maintenance window theo trạm, auto-rollback watchdog, drift detection, liveness watchdog, approval + audit log, không hard-code mật khẩu SFTP.

---

## 3. HẠNG MỤC CẦN BỔ SUNG (trọng tâm)

### 🔴 GAP-1 — Tùy biến config theo từng máy/trạm khi deploy (thay `CheckSumCustom`) — BLOCKER

**Hệ cũ làm gì:** Trên cùng một gói app, mỗi *file* có một chính sách (`CheckSumFileModel`) đi kèm gói; với file INI thì khai báo **whitelist key** được phép mang giá trị riêng theo máy (`DUT_IP`, tên trạm/PC, `COM port`, slot, region…). Client trộn **template publish + giá trị riêng lưu cục bộ trên PC** → ghi ra file thật. 4 chế độ:

- `IsCheckSum` (mặc định): khóa cứng theo MD5 — chống sửa.
- `JustExist`: chỉ cần tồn tại, update không đè.
- `IsCheckValue` (INI): tùy biến theo key (whitelist + cờ `IsEditableKey` để đảo nghĩa "được sửa"/"bị khóa"). Key bị khóa luôn ép về giá trị publish; key editable giữ giá trị máy.
- (không cờ): file quản lý nhưng thay thế thủ công được.

Giá trị riêng lưu tại `DataCustomFilePath` (cục bộ per-PC); cờ `IsPrivate=false` cho phép một config máy-cấp dùng chung nhiều app trên cùng PC.

**Bằng chứng từ `Sample_Software/` — bài toán thực tế lớn hơn dự kiến: tùy biến trải trên 2 TẦNG, 2 ĐỊNH DẠNG, 2 PHẠM VI.**

| Tầng | File | Định dạng | Ví dụ giá trị riêng thật |
|---|---|---|---|
| CPEI_MFG | `Config/ProgramConfig.json` | **JSON** | `Model=UTPG3T00T01`, `Station=FT2`, `DUT_IP=192.168.1.20`, `SfisConfig.Com=COM8` |
| CPEI_MFG | `Config/DhcpConfig.json` | JSON | `ServerIp=192.168.1.254`, `Start/EndIp=192.168.1.20` |
| CPEI_MFG | `Config/SerialControlConfig.json` / `ShippingConfig.json` | JSON | `Com=COM3`; SFTP `192.168.240.20/user/ubnt` |
| Khách | `data/config_files/*.ini` ↔ `data/custom_config_files/*.ini` | **INI** | `common.ini`: `fail_stop=True` (override) vs `False` (template) |
| Khách | `custom_config_files/ctr_04247_efbb.ini` | INI | `IP/dut=192.168.1.20`, `func_power/switch_ip=192.168.1.10` + port PoE, criteria `emmc/ddr/iperf/power`, `General/sku`, `BOM`, firmware files |

- **2 phạm vi giá trị** (trộn lẫn trong cùng file custom của khách):
  - *Model-scope* (chung mọi trạm cùng model): SKU, BOM, product_name, firmware, criteria test.
  - *Station/PC-scope* (riêng từng fixture/máy): các IP (DUT/host/PoE switch), COM, port PoE, switch credential.
- **Tin tốt:** chương trình khách **tự có sẵn** cơ chế template/override (`config_files` ↔ `custom_config_files`). ⇒ Với phía khách, MProject **không cần parse từng key**, chỉ cần **đặt đúng file override theo Model/Station** vào đúng chỗ (gần với `LimitFile` đang có).

**MProject hiện trạng:** Blob **content-addressed** (cùng SHA → file y hệt mọi PC); supervisor deploy nguyên file. **Không có** lớp template + override theo máy. `LimitFile` (per Model+Station) là file toàn vẹn và **chưa nối tới agent**.

**Cần xây dựng (2 cơ chế, có thể làm song song):**
1. **Per-file override toàn-file theo Model/Station** (đơn giản, đủ cho phần lớn ca — nhất là INI của khách): cho phép gắn vào `SoftwareVersion` một danh sách "file có thể override", rồi nạp bản thay thế theo `Model`/`Station`/`Computer`. Tái dùng pattern `LimitFile`.
2. **Per-key templating** (cho file cần khóa phần lớn, chỉ cho sửa vài key — vd `ProgramConfig.json`): schema chính sách per-file (mode: Locked / JustExist / KeyTemplate / Replaceable) + **whitelist key**; phải hỗ trợ **cả JSON lẫn INI** (cơ chế editer mở rộng như `IniExtensionEditer` cũ).
- Nơi lưu **giá trị override phân tầng**: Model-default → Station-override → (tùy chọn) PC-override; quản lý qua FE.
- **Điểm chèn trong agent** lúc deploy: lấy file template từ blob → áp override theo Model/Station/PC → ghi ra install root; bước verify/drift **bỏ qua** các key/file được phép khác nhau (không báo drift oan).
- FE: màn admin khai báo file/whitelist key + màn engineer nhập giá trị override theo model/trạm/máy.

**Ưu tiên:** P0 (chặn việc thay UIStore cho trạm có config riêng theo máy). **Effort:** L. *Khuyến nghị làm cơ chế (1) trước để chạy được sớm, (2) bổ sung sau cho file JSON khóa-cứng-một-phần.*

---

### 🟠 GAP-2 — Trải nghiệm tại trạm cho operator (tray / launcher / login) — CẦN QUYẾT ĐỊNH

**Hệ cũ làm gì:** `UIStore` là app **system-tray** có **login operator** và **danh sách app cho công nhân tự chọn/mở lại**.

**MProject hiện trạng:** `MProjectAgent` **headless** (Windows service, không tray, không login operator, không UI chọn app). App test vẫn hiện GUI qua `InteractiveProcessLauncher`, nhưng việc khởi chạy do server điều khiển.

**Cần quyết định + xây dựng (nếu cần):**
- Nếu chấp nhận **tự động hoàn toàn** (server đẩy + supervisor tự mở) → không cần làm gì, thậm chí tốt hơn về governance.
- Nếu cần operator **tự chọn/khởi động lại app** hoặc **login theo ca/người ngay tại trạm** → làm **thin launcher** (tray nhỏ gọi agent cục bộ): liệt kê app được gán cho máy, nút mở/đóng/restart, (tuỳ chọn) login.

**Ưu tiên:** P0 (phải chốt trước khi bỏ UIStore). **Effort:** S (nếu bỏ) → M/L (nếu làm launcher).

---

### 🟡 GAP-3 — ~~Đẩy `LimitFile` xuống agent~~ → **ĐÃ GỘP VÀO GAP-1**

**Quyết định (2026-06-16):** "limit file" thực chất là một config override (`Config/LimitConfig.json`). LimitFile thử nghiệm bị **đạp bỏ**, thay bằng entity hợp nhất `OverrideFile` có `Kind` (Config/Limit). Cơ chế GAP-1 (server-side blob-substitution tại manifest) **tự wire LimitFile tới agent** → không còn là hạng mục riêng. Xem `docs/gap1_config_override_design.md`.

**Ưu tiên:** — (gộp vào GAP-1).

---

### ✅ GAP-4 — Agent self-update (thay `AppUpdater`) — **DONE end-to-end (2026-06-20)**

**Hệ cũ:** `AppUpdater` tự cập nhật `UiStore.exe` (so MD5 → kill → copy → restart).

**Đã xây dựng (3 slice):**
- **Backend offer:** entity `AgentRelease` (Version, BlobSha256, MinServerVersion, IsActive — 1 active/lúc); `AgentService.ResolveAgentUpdateAsync` trả `AgentHeartbeatResponse.AgentUpdate` cho agent cũ hơn (guard `IsNewerVersion`).
- **Slice A — download/verify/stage:** agent tải release (qua endpoint `blobs/local`) → **verify SHA256** → giải nén vào `{StateDirectory}\update\staging`. Gate cờ `Agent:SelfUpdateEnabled` (mặc định **off**).
- **Slice B — apply:** verb `apply-update` (exe MỚI ở staging chạy detached) → `sc stop` → backup → swap install dir → `sc start`; **rollback** nếu bản mới không lên. Guard spawn 1 lần/process.
- **Slice C — quản lý:** RBAC `agentrelease.manage`/`.read`; `AgentReleaseService` + controller `api/v1/agent-releases` (publish multipart/list/activate/delete); FE trang **Agent releases**. Đã thêm AgentRelease vào ref-guard của `BlobGcService` (tránh GC xoá blob release đang dùng).
- Tài liệu chi tiết: memory `project_gap4_self_update.md`.

> ### ✅ NỢ BẢO MẬT — Ký số bản phát hành (F-08) — **DONE (2026-06-20)**
> **Trước đây chỉ verify SHA256** (chỉ chứng minh "bytes khớp hash **server báo**", không chứng minh "đúng bản build hợp lệ"). Self-update **thay exe agent và chạy như LocalSystem** → kẻ xấu chiếm server/DB, tráo blob, hoặc MITM (⚠️ `AllowUntrustedCertificate=true` trên LAN) có thể đẩy bản độc.
> **Đã làm (F-08):** chữ ký **RSA-3072 PKCS#1 / SHA-256** tạo **offline** (private key **giữ ngoài server**, ở pipeline); server **chỉ lưu opaque** (`AgentRelease.Signature`) và trả trong `AgentUpdateInfo`; agent **nhúng public key** (`Agent:ReleasePublicKeyPem`) và **verify sau SHA-256, trước khi stage/apply**, **fail-closed** (thiếu/sai chữ ký hoặc chưa cấu hình key ⇒ từ chối). Chiếm server/DB/TLS **không giả được** chữ ký vì không có private key.
> **Tool + doc:** `scripts/sign-agent-release.ps1` (tạo khóa + ký, không cần openssl) ↔ tương thích `openssl dgst -sha256 -sign`; chi tiết `docs/note_2006/F08_release_signing.md`.
> **Quy tắc vẫn giữ:** bật `SelfUpdateEnabled=true` ở production **phải** kèm cấu hình `ReleasePublicKeyPem` (nếu trống, agent từ chối mọi update).

**Ưu tiên:** GAP-4 lõi DONE; **F-08 DONE.** Tùy chọn tăng cường về sau: Authenticode-sign exe, server pre-verify, compiled-in public key (xem doc F-08).

---

### ✅ GAP-5 — Metadata version có cấu trúc (BOM / FCD / FTU / FW / Region) — **DONE (2026-06-21)**

**Hệ cũ:** `Upload` tách 4–5 trường version riêng; các program test **báo lên SFIS** theo các trường này. **Xác nhận từ `Sample_Software/`** — `Config/ProgramConfig.json > VersionConfig` chứa thật:
```json
"VersionConfig": { "FWVer":"32127", "FCDVer":"101001", "FTUVer":"102431816", "BOMVer":"113-04247-11", "RegionVer":"WORLD" }
```

**Đã làm:** thêm **5 cột tường minh** trên `SoftwareVersion` — `BomVersion/FcdVersion/FtuVersion/FwVersion/RegionVersion` (nullable; migration `AddSoftwareVersionMetadata`) thay vì nhồi vào `Label`. DTO gói gọn trong `VersionMetadata` (request `Create/Update` + response `detail/summary/latest`); service `ApplyMetadata` (full-replace, trim). FE: ô nhập khi tạo version (modal "New version") + hiển thị **chip BOM/FW/FCD/FTU/Region** dưới mã version. BE 439/439 + FE typecheck OK.

**Còn dư địa (không chặn):** lọc theo metadata trên FE; nhập metadata trong **NewSoftwareWizard** (hiện chỉ ở modal "New version"); cân nhắc đồng bộ/sinh metadata từ giá trị override config (GAP-1) để tránh lệch.

**Ưu tiên:** P2 — **DONE**. **Effort:** S–M.

---

### ✅ GAP-6 — Luồng Uninstall/cleanup (thay `AutoRemove`/`CloseAndClear`) — **CODE DONE (2026-06-21), chờ nghiệm thu PC thật**

**Hệ cũ:** cờ `AutoRemove` (gỡ khi không còn gán), `CloseAndClear` (đóng & dọn khi thoát).

**Audit ban đầu:** `InstallationJobType.Uninstall` có enum nhưng **chưa bao giờ được tạo**; `JobExecutor` không đọc `JobType`; bỏ gán chỉ mark record + (không) StopApp → **file không bị xóa, cache không thu**. Shortcut = N/A (tray launcher).

**Đã làm (hướng A, cờ opt-in mặc định OFF — chủ dự án chốt):**
- **Cờ** `SoftwarePackage.AutoRemoveOnUnassign` (default false; migration `AddAutoRemoveOnUnassign`) + toggle FE ở modal "New package".
- **Server:** bỏ gán (`RemoveAssignmentAsync`) khi cờ ON → tạo **Uninstall job** cho mỗi PC còn cài (hủy job cũ + persist trước để không vướng unique index); `PollAsync` trả Uninstall job kể cả khi không còn assignment; manifest Uninstall không mang file; complete Uninstall → mark `PcInstallationRecord=Uninstalled`. Cờ OFF → giữ hành vi cũ (mark uninstalled, giữ file).
- **Agent:** `JobExecutor` nhánh Uninstall (Ack→Installing→Complete): dừng app nếu đang supervise → gỡ catalog (`AppCatalogStore.RemoveAsync`) → giải ref cache (`CacheIndex.RemoveDeployedFilesForPackageAsync` + decrement → GC thu blob) → **xóa install dir qua `InstallDirGuard`** (chỉ trong `InstallRoot.Base`, không == base, chống traversal, idempotent, lỗi→Failed).
- **Tests:** agent 113 (guardrail + cleanup) , BE 441 (flag tạo job + poll + complete→Uninstalled), FE typecheck OK.

**Còn lại:** **nghiệm thu trên PC thật** (thao tác destructive): cờ ON → bỏ gán → app dừng, `D:\Apps\<pkg>` bị xóa, cache GC thu, record Uninstalled; cờ OFF → giữ file như cũ.

**Ưu tiên:** P2 — **code DONE**, chờ E2E PC thật. **Effort:** đã làm (M).

---

### ✅ GAP-7 — Hành vi phụ (nhỏ) — **ĐÃ XÁC NHẬN (2026-06-20)**

Audit 3 mục con đối chiếu code thật → không phát sinh việc code mới:

- `IsOpenWithSystem` / startup shortcut: **đã phủ, còn chắc hơn**. Hệ cũ tạo `.lnk` trong Startup folder (`StartupShortcut.cs`); MProject agent là Windows service (auto-start) và `LauncherBootstrapper` tự mở `MProjectLauncher.exe` vào console session đang active, mở lại mỗi 30s nếu operator đóng → không cần shortcut per-user.
- `show.signal` / `shutdown.signal` (IPC file): **đã thay hoàn toàn**. Điều khiển app = `AgentCommand` (Restart/KillAndRestart/StopApp/Cancel…) + named-pipe IPC launcher (status/run/stop/restart); hiện window = tray double-click/"Mở"; thoát = tray "Thoát"; stop khi self-update = `sc stop` (không còn file-poll 1s).
- Hiển thị icon app: **DONE** (`AppRowViewModel` decode `IconBase64`). "Đóng & xóa cache" theo app: **không làm riêng** — trùng GAP-6 (Uninstall/cleanup) và cache content-addressed đã ref-count/GC tự dọn → để GAP-6 phụ trách.

**Ưu tiên:** P3 — **DONE (xác nhận)**. **Effort:** S (0 dòng code).

---

## 4. Lộ trình tối thiểu để "bật công tắc" thay UIStore (Phase 1)

0. **(Nền tảng)** Xác nhận đóng gói **composite** (CPEI_MFG.exe + Config JSON + folder khách) thành **một** `SoftwarePackage`/`SoftwareVersion`, entry point = `CPEI_MFG.exe` — vốn `SoftwareFile` (cây file) đã đáp ứng, chỉ cần kiểm thử với gói `Sample_Software/` thật.
1. ✅ **(Bắt buộc)** GAP-1 — cơ chế tùy biến config theo máy/trạm (`OverrideFile`, gộp LimitFile). **DONE.**
2. ✅ **(Bắt buộc)** GAP-2 — thin launcher cho operator (`MProjectLauncher`). **DONE** (verify trạm thật PASS).
3. ✅ **(Nên có)** GAP-3 (gộp vào GAP-1) + GAP-4 (agent self-update). **DONE.**
4. ✅ **(Code)** GAP-6 — Uninstall/cleanup (hướng A, cờ opt-in OFF). **CODE DONE (2026-06-21)** — chờ nghiệm thu PC thật (destructive).
5. ✅ GAP-7 — hành vi phụ: **đã xác nhận (2026-06-20)**, 0 code. ✅ **GAP-5 — metadata version: DONE (2026-06-21).**
6. ✅ **(Bảo mật)** **F-08 — ký số bản phát hành agent. DONE (2026-06-20)** — điều kiện bật self-update production đã đủ (cấu hình `ReleasePublicKeyPem` khi bật).

> **Tất cả GAP đã code xong.** Việc còn lại để "bật công tắc" là **nghiệm thu E2E trên trạm thật** theo
> checklist: **[phase1_acceptance_runbook.md](phase1_acceptance_runbook.md)** (đóng gói composite + GAP-1/2/4/5/6 + F-08, mục A→H).

---

## 5. Bảng tổng hợp ưu tiên

| # | Hạng mục | Thay cho | Ưu tiên | Effort | Chặn thay UIStore? |
|---|---|---|---|---|---|
| GAP-1 | Tùy biến config per-station/PC (`OverrideFile`, gộp cả LimitFile) | CheckSumCustom | P0 | L | ✅ **DONE** |
| GAP-2 | UX tại trạm (tray/launcher) | UIStore tray | P0 | S→L | ✅ **DONE** (verify trạm thật PASS) |
| ~~GAP-3~~ | **Gộp vào GAP-1** (OverrideFile Kind=Limit) | limit file theo model | — | — | — |
| GAP-4 | Agent self-update | AppUpdater | P1 | L | ✅ **DONE** |
| **F-08** | **Ký số bản phát hành agent** (điều kiện bật self-update production) | — | **P1** | M + key ops | ✅ **DONE** (2026-06-20) — cấu hình `ReleasePublicKeyPem` khi bật |
| GAP-5 | Metadata version BOM/FCD/FTU/FW | version fields của Upload | P2 | S–M | ✅ **DONE** (2026-06-21) |
| GAP-6 | Uninstall/cleanup (Uninstall job, cờ opt-in OFF) | AutoRemove/CloseAndClear | P2 | M | ✅ **CODE DONE** (2026-06-21) — chờ E2E PC thật |
| GAP-7 | Hành vi phụ (startup/signal/icon) | misc UIStore | P3 | S | ✅ **DONE** (xác nhận, 0 code) |

---

## 6. Ghi chú

- Phần **phân phối/quản lý theo trạm & model, đẩy/tải chương trình** (mục tiêu cốt lõi) — MProject **đã làm tốt và đầy đủ hơn** hệ cũ.
- Hai khoảng trống quyết định là **GAP-1 (config riêng theo máy)** và **GAP-2 (UX tại trạm)**.
- Một "app" thực tế là **composite** (launcher của ta + config + chương trình test của khách) — xem mục 1.1; cần coi là **một package-version**.
- Tham chiếu mẫu gói thật: `Sample_Software/` (`Debug/CPEI_MFG.exe`, `Config/*.json`, `FTU_efbb_..._UTP-G3-Touch-Pro/data/{config_files,custom_config_files}`).
- Tham chiếu code hệ cũ: `Old_program/UIStore/UiStore/Services/CheckSumCustom/`, `Old_program/Upload/Upload/`.
- Tham chiếu MProject: `MProjectBackend/MProject.Domain/Entities/Software/`, `MProjectAgent/Services/`, roadmap `docs/refactor_plan_01.md`.
