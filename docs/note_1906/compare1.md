# Compare 01 — Hạng mục MProject cần bổ sung để thay thế chương trình cũ

> Mục tiêu tài liệu: tổng kết **những phần MProject (web) cần xây dựng thêm** để thay thế hoàn chỉnh hệ thống cũ trong `Old_program/`, ưu tiên **Phase 1 = thay `UIStore`**.
> Ngày lập: 2026-06-16.
>
> **Changelog**
> - v1 (2026-06-16): bản đầu, 7 GAP.
> - v2 (2026-06-16): bổ sung mục **1.1 Giải phẫu một gói app thật** (`Sample_Software/`); refine **GAP-1** (composite + override 2 tầng/2 định dạng/2 phạm vi) và **GAP-5** (ví dụ `VersionConfig` thật).
> - v3 (2026-06-16): **GAP-1 có thiết kế** (`docs/gap1_config_override_design.md`) + **HỢP NHẤT** với LimitFile → **GAP-3 gộp vào GAP-1** (1 entity `OverrideFile` có `Kind`, 3 tầng scope, approval Station/Computer). Pilot tracer-bullet **PASS** phần lõi.
> - v4 (2026-06-19): **2 blocker P0 ĐÃ XONG → Phase 1 (thay UIStore) hoàn tất về code.** GAP-1 ✅ (OverrideFile + ConfigBaseline committed; BE-0 đạp-bỏ-LimitFile landed — migration `DropLimitFile`). GAP-2 ✅ code xong (launcher WPF net48 + IPC named-pipe; L-0→L-7 + slice 2a/2b/2c; 2d icon chủ động bỏ; agent test 86/86) — chỉ còn nghiệm thu thủ công trạm thật. **Kế tiếp theo plan: GAP-4 (agent self-update, P1).** Chi tiết GAP-2: `docs/note_1906/gap2_launcher_design.md`.

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

### 🔴 GAP-1 — Tùy biến config theo từng máy/trạm khi deploy (thay `CheckSumCustom`) — BLOCKER — ✅ DONE (2026-06-19)

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

> **Trạng thái (2026-06-19): ✅ DONE.** Cả 2 cơ chế đã có: (1) **OverrideFile** toàn-file theo Model/Station/Computer (resolver + blob-swap ở manifest + authoring API + approval) committed; (2) **ConfigBaseline** (khóa giá trị + whitelist, INI/JSON) committed. **BE-0** đạp-bỏ-LimitFile đã landed (migration `DropLimitFile`). Còn vài enhancement nhỏ ở validator (không chặn). Xem `gap1_config_override_design.md` + `gap_configbaseline_design.md`.

---

### 🟠 GAP-2 — Trải nghiệm tại trạm cho operator (tray / launcher / login) — ✅ DONE code (2026-06-19), chờ nghiệm thu trạm thật

**Hệ cũ làm gì:** `UIStore` là app **system-tray** có **login operator** và **danh sách app cho công nhân tự chọn/mở lại**.

**MProject hiện trạng:** `MProjectAgent` **headless** (Windows service, không tray, không login operator, không UI chọn app). App test vẫn hiện GUI qua `InteractiveProcessLauncher`, nhưng việc khởi chạy do server điều khiển.

**Cần quyết định + xây dựng (nếu cần):**
- Nếu chấp nhận **tự động hoàn toàn** (server đẩy + supervisor tự mở) → không cần làm gì, thậm chí tốt hơn về governance.
- Nếu cần operator **tự chọn/khởi động lại app** hoặc **login theo ca/người ngay tại trạm** → làm **thin launcher** (tray nhỏ gọi agent cục bộ): liệt kê app được gán cho máy, nút mở/đóng/restart, (tuỳ chọn) login.

**Ưu tiên:** P0 (phải chốt trước khi bỏ UIStore). **Effort:** S (nếu bỏ) → M/L (nếu làm launcher).

> **Quyết định + trạng thái (2026-06-19): ĐÃ CHỐT làm thin launcher — ✅ DONE code.** `MProjectLauncher` (WPF net48, tray) ↔ agent qua named pipe cục bộ; list app + đèn trạng thái + Run/Stop/Restart + gate Run khi đang update + badge "vừa cập nhật" + pane sự kiện gần đây. KHÔNG login/PII operator. L-0→L-7 + slice 2a/2b/2c xong (2d icon chủ động bỏ — tránh dep `System.Drawing.Common` cosmetic); agent test 86/86, launcher build OK; đóng gói qua `scripts/package-agent.ps1`. **Còn lại duy nhất: nghiệm thu thủ công trên 1 trạm thật** (GUI/reboot/đổi ca). Thiết kế đầy đủ: `gap2_launcher_design.md`.

---

### 🟡 GAP-3 — ~~Đẩy `LimitFile` xuống agent~~ → **ĐÃ GỘP VÀO GAP-1**

**Quyết định (2026-06-16):** "limit file" thực chất là một config override (`Config/LimitConfig.json`). LimitFile thử nghiệm bị **đạp bỏ**, thay bằng entity hợp nhất `OverrideFile` có `Kind` (Config/Limit). Cơ chế GAP-1 (server-side blob-substitution tại manifest) **tự wire LimitFile tới agent** → không còn là hạng mục riêng. Xem `docs/gap1_config_override_design.md`.

**Ưu tiên:** — (gộp vào GAP-1).

---

### 🟡 GAP-4 — Agent self-update (thay `AppUpdater`) — F-17

**Hệ cũ:** `AppUpdater` tự cập nhật `UiStore.exe` (so MD5 → kill → copy → restart).

**MProject hiện trạng:** agent chưa tự cập nhật; F-17 mới ở roadmap.

**Cần xây dựng:** `AgentRelease` (Version, Sha256, MinServerVersion); server trả `AgentHeartbeatResponse.AgentUpdate`; agent tải + thay exe + restart service. **Khuyến nghị kèm ký số manifest (F-08)** để an toàn.

**Ưu tiên:** P1. **Effort:** L.

> **Trạng thái (2026-06-20): 🚧 ĐANG LÀM — có design doc + slice backend đầu (G4-1) đã land.** Chốt phạm vi: **agent trước** (launcher tái dùng kênh sau), **hoãn F-08** (dùng SHA-256 + endpoint agent đã xác thực + TLS). G4-1: entity `AgentRelease` + migration `AddAgentRelease` + DTO `AgentUpdateInfo` + quyết định offer trong `RecordHeartbeatAsync` (offer khi release active mới hơn nghiêm ngặt; parse `System.Version`, fail-safe) — **additive thuần, backend 427/427 pass**. Còn lại: G4-2 publish (admin upload), G4-3 agent tải+stage+verify, G4-4 swap+restart, G4-5 đóng gói+nghiệm thu. Thiết kế đầy đủ: `gap4_agent_selfupdate_design.md`.

---

### 🟢 GAP-5 — Metadata version có cấu trúc (BOM / FCD / FTU / FW / Region)

**Hệ cũ:** `Upload` tách 4–5 trường version riêng; các program test **báo lên SFIS** theo các trường này. **Xác nhận từ `Sample_Software/`** — `Config/ProgramConfig.json > VersionConfig` chứa thật:
```json
"VersionConfig": { "FWVer":"32127", "FCDVer":"101001", "FTUVer":"102431816", "BOMVer":"113-04247-11", "RegionVer":"WORLD" }
```

**MProject hiện trạng:** `SoftwareVersion` chỉ có 1 `VersionNumber` + `Label` + `Changelog`.

**Cần xây dựng (nếu nhà máy còn cần tra cứu/đối soát/báo SFIS):** thêm **metadata key-value có cấu trúc** cho version (`{FW, FCD, FTU, BOM, Region}`) thay vì nhồi vào `Label`; hiển thị/lọc trên FE. *Lưu ý:* các trị này hiện nằm trong file config (GAP-1) → cân nhắc cho metadata version **sinh ra/đồng bộ** với giá trị override để tránh lệch.

**Ưu tiên:** P2. **Effort:** S–M.

---

### 🟢 GAP-6 — Kiểm chứng & hoàn thiện luồng Uninstall/cleanup (thay `AutoRemove`/`CloseAndClear`)

**Hệ cũ:** cờ `AutoRemove` (gỡ khi không còn gán), `CloseAndClear` (đóng & dọn khi thoát).

**MProject hiện trạng:** có `InstallationJobType.Uninstall` nhưng cần xác minh end-to-end: xóa file + gỡ shortcut + dừng process + cập nhật `PcInstallationRecord`; và drift báo đúng khi PC lệch assignment.

**Ưu tiên:** P2. **Effort:** S (kiểm thử) → M (nếu thiếu).

---

### ⚪ GAP-7 — Hành vi phụ (nhỏ)

- `IsOpenWithSystem` / startup shortcut: agent đã là service nên thường không cần — xác nhận.
- `show.signal` / `shutdown.signal` (IPC file): đã thay bằng `AgentCommand` — xác nhận đủ.
- Hiển thị icon app, "đóng & xóa cache" theo app: cân nhắc nếu giữ launcher (GAP-2).

**Ưu tiên:** P3. **Effort:** S.

---

## 4. Lộ trình tối thiểu để "bật công tắc" thay UIStore (Phase 1)

0. **(Nền tảng)** Xác nhận đóng gói **composite** (CPEI_MFG.exe + Config JSON + folder khách) thành **một** `SoftwarePackage`/`SoftwareVersion`, entry point = `CPEI_MFG.exe` — vốn `SoftwareFile` (cây file) đã đáp ứng, chỉ cần kiểm thử với gói `Sample_Software/` thật.
1. **(Bắt buộc)** GAP-1 — cơ chế tùy biến config theo máy/trạm. ✅ **DONE**: (1) OverrideFile toàn-file Model/Station/Computer + (2) ConfigBaseline khóa-giá-trị (INI/JSON), đều committed.
2. **(Bắt buộc)** GAP-2 — thin launcher cho operator. ✅ **DONE code** (chốt làm launcher; chỉ còn nghiệm thu trạm thật).
3. **(Nên có)** ~~GAP-3 (LimitFile → agent)~~ đã gộp & xong trong GAP-1 + **GAP-4 (agent self-update) ← 🚧 ĐANG LÀM, P1** (G4-1 backend land; design `gap4_agent_selfupdate_design.md`).
4. **(Kiểm thử)** GAP-6 — Uninstall/cleanup + drift trên PC thật.
5. GAP-5, GAP-7 làm sau, không chặn.

> **Mốc 2026-06-19:** hai blocker P0 (GAP-1, GAP-2) đã xong về code ⇒ **Phase 1 thay UIStore hoàn tất** (chỉ chờ nghiệm thu trạm thật cho GAP-2). Hạng mục kế tiếp theo plan = **GAP-4**.

---

## 5. Bảng tổng hợp ưu tiên

| # | Hạng mục | Thay cho | Ưu tiên | Effort | Chặn thay UIStore? |
|---|---|---|---|---|---|
| GAP-1 | Tùy biến config per-station/PC (`OverrideFile`, gộp cả LimitFile) | CheckSumCustom | P0 | L | **✅ DONE** (committed) |
| GAP-2 | UX tại trạm (tray/launcher/login) | UIStore tray | P0 | S→L | **✅ DONE code** (chờ nghiệm thu trạm) |
| ~~GAP-3~~ | **Gộp vào GAP-1** (OverrideFile Kind=Limit) | limit file theo model | — | — | ✅ xong trong GAP-1 |
| GAP-4 | Agent self-update | AppUpdater | P1 | L | 🚧 **ĐANG LÀM** (G4-1 backend land; không chặn) |
| GAP-5 | Metadata version BOM/FCD/FTU/FW | version fields của Upload | P2 | S–M | ❌ |
| GAP-6 | Uninstall/cleanup | AutoRemove/CloseAndClear | P2 | S–M | ❌ |
| GAP-7 | Hành vi phụ (startup/signal/icon) | misc UIStore | P3 | S | ❌ |

---

## 6. Ghi chú

- Phần **phân phối/quản lý theo trạm & model, đẩy/tải chương trình** (mục tiêu cốt lõi) — MProject **đã làm tốt và đầy đủ hơn** hệ cũ.
- ~~Hai khoảng trống quyết định là **GAP-1 (config riêng theo máy)** và **GAP-2 (UX tại trạm)**.~~ → **Cả hai đã xong (2026-06-19)**; còn lại là các mục không-chặn: **GAP-4** (self-update, kế tiếp), GAP-6 (uninstall/cleanup), GAP-5 (metadata version), GAP-7 (phụ).
- Một "app" thực tế là **composite** (launcher của ta + config + chương trình test của khách) — xem mục 1.1; cần coi là **một package-version**.
- Tham chiếu mẫu gói thật: `Sample_Software/` (`Debug/CPEI_MFG.exe`, `Config/*.json`, `FTU_efbb_..._UTP-G3-Touch-Pro/data/{config_files,custom_config_files}`).
- Tham chiếu code hệ cũ: `Old_program/UIStore/UiStore/Services/CheckSumCustom/`, `Old_program/Upload/Upload/`.
- Tham chiếu MProject: `MProjectBackend/MProject.Domain/Entities/Software/`, `MProjectAgent/Services/`, roadmap `docs/refactor_plan_01.md`.
