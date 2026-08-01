# Tracer-Bullet Runbook — Đẩy `Sample_Software` qua MProject xuống 1 PC

> Mục tiêu: chứng minh **end-to-end** rằng MProject thay được luồng phân phối của UIStore với gói thật, **trước khi** đầu tư xây GAP-1.
> Phạm vi: 1 PC, 1 agent, 1 model, 1 station. Ngày lập: 2026-06-16.

---

## 0. Mục tiêu nghiệm thu

Chứng minh chuỗi: **đóng gói composite → upload (dedup SHA-256) → assign theo Station → agent enroll → poll → delta-download → deploy → supervisor chạy `CPEI_MFG.exe`**, rồi thử **đẩy 1 update code-only** để thấy chỉ file đổi mới truyền.

**KHÔNG** đụng tới GAP-1 (tùy biến config theo trạm): đây là pilot 1 máy nên dùng nguyên giá trị có sẵn trong `Sample_Software/` (`Station=FT2`, các IP cố định). Mục tiêu là kiểm cơ chế phân phối + chạy + phần cứng, không phải multi-station.

## 1. Vì sao gói chạy được mà KHÔNG cần sửa (quan trọng)

CPEI_MFG dùng đường dẫn tương đối, **neo theo thư mục exe**:

| Thành phần | Cách CPEI_MFG resolve | Kết quả khi deploy |
|---|---|---|
| Config JSON | `Path.Combine(AppContext.BaseDirectory, "..\Config")` (xem `ConfigLoader.cs`) | từ `…\Debug\` → `…\Config\` |
| Chương trình khách | `ProgramConfig.json > FtuConfig.DirPath = "../FTU_efbb_..."` | từ `…\Debug\` → `…\FTU_efbb_...\` |

Agent deploy giữ **nguyên cây** vào `C:\MProjectApps\<PackageName>\` và đặt `WorkingDirectory = thư mục chứa exe` (xác nhận trong `JobExecutor.cs` + `ProcessSupervisor.cs`). Nên với layout:

```
C:\MProjectApps\<pkg>\
├── Debug\CPEI_MFG.exe        ← entry point
├── Config\*.json             ← ..\Config  ✓
└── FTU_efbb_...\             ← ..\FTU...  ✓
```

⇒ **Upload nguyên `Sample_Software/`, entry point = `Debug/CPEI_MFG.exe`.** Mọi đường tương đối khớp out-of-the-box.

## 2. Điều kiện tiên quyết

- [ ] Server MProject đã chạy theo `docs/deploy_guideline.md` (FE + API + Postgres + storage local), có tài khoản admin. Ghi lại: **API URL** (vd `https://tess:8443` hoặc `http://<LAN-IP>:8081`) và **Agent:InstallerToken** trong `appsettings.json` của API.
- [ ] 1 PC test (thật hoặc VM) **reach được** API URL qua mạng; có .NET runtime cho agent.
- [ ] Bản build `MProjectAgent.exe` (+ `appsettings.json` cạnh nó).
- [ ] Có sẵn folder `Sample_Software/` để upload.

---

## 3. Phần A — Tạo Model / Station / Computer (web)

1. Đăng nhập FE.
2. **Product Group** (nếu chưa có) → **Model**: tạo model, vd Code `UTP-G3-Touch-Pro`.
3. **Station**: tạo station, vd `FT2` (cùng product group/model theo cấu trúc tổ chức của anh).
4. **Computer**: trang `Computers` → tạo computer cho PC test → **assign vào Station `FT2`**.
5. **Enrollment token**: tại computer vừa tạo → *Generate enrollment token*. **Ghi lại `ComputerId` + token** (token dùng 1 lần / có hạn).

> API tương ứng (nếu thích dùng script thay vì FE):
> `POST /api/v1/models`, `POST /api/v1/stations`, `POST /api/v1/computers`,
> `POST /api/v1/computers/{id}/assign-station`, `POST /api/v1/computers/{id}/agent/enrollment-token`.

## 4. Phần B — Upload gói & release (web wizard)

Trang **Software → New Software Wizard** (FE đã hỗ trợ chọn **nguyên folder** qua `webkitdirectory` và **băm SHA-256 phía client** bằng web worker):

1. Tạo **Package**, vd `FTU-UTP-G3-Touch-Pro`.
2. Tạo **Version**, vd `1.0.24` → chọn **folder `Sample_Software/`**. FE băm SHA-256 từng file → gọi `upload-init` (server trả về blob nào còn thiếu) → **chỉ upload blob thiếu** → `upload-complete`.
3. **Set Entry Point** = `Debug/CPEI_MFG.exe`, **EntryPointMode** = `LongRunning`.
4. (tuỳ chọn) mở **Manifest** để soát cây file + SHA-256.
5. **Release** version.

> API tương ứng: `POST /api/v1/software-packages` → `POST /api/v1/software-packages/{packageId}/versions` → `POST /api/v1/software-versions/{id}/upload-init` → `…/upload-complete` → `PUT /api/v1/software-versions/{id}/entry-point` → `POST /api/v1/software-versions/{id}/release`. (Lấy shape body chính xác qua tab Network khi bấm trên FE.)

## 5. Phần C — Gán version cho Station

1. Software → **Assign package** (hoặc **Deployment Matrix**) → chọn Package `FTU-UTP-G3-Touch-Pro`, **Station `FT2`**, **TargetVersion `1.0.24`**.
2. **Activate** assignment.

> API: `POST /api/v1/software-assignments` → `PUT /api/v1/software-assignments/{id}/pin` → `POST /api/v1/software-assignments/{id}/activate`.

## 6. Phần D — Cài & enroll agent trên PC test

1. Copy agent sang PC. Sửa `appsettings.json` cạnh `MProjectAgent.exe`:
   ```json
   "Agent": {
     "ServerUrl": "https://tess:8443",
     "InstallerToken": "<đúng Agent:InstallerToken của API>",
     "AllowUntrustedCertificate": true,           // cert self-signed
     "StateDirectory": "C:\\ProgramData\\MProjectAgent"
   },
   "Cache":      { "Root": "C:\\ProgramData\\MProjectAgent\\cache" },
   "InstallRoot":{ "Base": "C:\\MProjectApps" }
   ```
2. **Enroll**:
   ```powershell
   .\MProjectAgent.exe enroll --server https://tess:8443 --computer-id <ComputerId> --token <enrollment-token>
   ```
3. Chạy agent — chọn 1:
   - **Foreground (khuyến nghị cho pilot, dễ xem log):** `.\MProjectAgent.exe run`
   - **Windows Service:** `.\MProjectAgent.exe install` (chạy PowerShell Administrator).

## 7. Phần E — Nghiệm thu vòng 1 (deploy sạch + chạy)

Agent sẽ: `poll` → `manifest/resolve` → tải blob → deploy (hardlink từ cache) vào `C:\MProjectApps\<pkg>\` → supervisor launch.

Checklist:
- [ ] Cây file đúng tại `C:\MProjectApps\<pkg>\` (`Debug\`, `Config\`, `FTU_efbb_...\`).
- [ ] `CPEI_MFG.exe` chạy; đọc đúng `..\Config` (UI hiện `Station=FT2`, `Model=...`); gọi được chương trình khách (`..\FTU...`).
- [ ] **Phần cứng** (điểm rủi ro chính): COM port / DHCP trên LAN fixture / thiết bị NI nếu PC có — chạy được dưới account mà agent dùng.
- [ ] Nếu agent chạy **dạng service**: GUI của CPEI_MFG hiện trong session người dùng (qua `InteractiveProcessLauncher`).
- [ ] Server: Computer **online**; `PcInstallationRecord` = installed; **không drift**.

Điểm cần soi khi có lỗi:
- WorkingDirectory / đường tương đối (mục 1).
- Quyền & account chạy service (truy cập COM/USB/HW, ghi `D:\UBNT_Test_Logs`).
- Tường lửa giữa PC ↔ API.

## 8. Phần F — Nghiệm thu vòng 2 (đúng kịch bản "đẩy update code-only")

1. Sửa code CPEI_MFG (vd đổi 1 nhãn/feature nhỏ) → build ra `CPEI_MFG.exe` mới.
2. FE: tạo **Version `1.0.25`** → upload lại folder. **Chỉ `CPEI_MFG.exe` đổi SHA-256 → chỉ blob đó được upload**; toàn bộ Python/dll/khách **dedup, bỏ qua**.
3. Set entry point → **Release** → **re-pin** Station `FT2` sang `1.0.25`.
4. Quan sát agent ở lần poll kế: `resolve` trả về **chỉ 1 blob thiếu** → tải **đúng 1 file** → supervisor **relaunch** bản mới.

Checklist:
- [ ] Chỉ **1 file** được truyền (xem log download + byte count).
- [ ] App relaunch sang `1.0.25`; downtime ngắn.
- [ ] (tuỳ chọn) thử **pin lại `1.0.24`** để kiểm rollback thủ công.

## 9. Phần G — Kết luận (điền sau khi chạy)

| Hạng mục | Kết quả | Ghi chú / rủi ro |
|---|---|---|
| Upload composite + dedup | ☐ Pass ☐ Fail | |
| Deploy đúng cây + relative path | ☐ Pass ☐ Fail | |
| CPEI_MFG chạy + gọi FTU khách | ☐ Pass ☐ Fail | |
| Truy cập phần cứng (COM/DHCP/NI) | ☐ Pass ☐ Fail | |
| GUI trong session (nếu service) | ☐ Pass ☐ Fail | |
| Update code-only chỉ truyền 1 file | ☐ Pass ☐ Fail | |
| Online/Inventory/Drift đúng | ☐ Pass ☐ Fail | |

→ Kết quả vòng này là **input trực tiếp** cho: thiết kế **GAP-1** (tùy biến config theo trạm) và quyết **GAP-2** (UX tại trạm). Xem `compare1.md`.

---

## Phụ lục — Bản đồ endpoint agent (tham khảo)

Agent gọi dưới prefix `/agent/v1`: `announce`, `enroll`, `heartbeat`, `poll`, `manifest/resolve`, `blobs/local`, `jobs/{id}/ack|progress|complete`, `inventory`, `commands/{id}/ack`. Đây là luồng tự động — không cần thao tác tay, liệt kê để debug.
