# Toàn vẹn & chống sửa tay software/config trên máy trạm

> Ghi chú phân tích + kế hoạch implement. Bối cảnh: software (phần cài & chạy) sau khi
> agent tải về máy trạm, nếu bị **một người không có quyền sửa tay** (VD đổi số liệu trong
> file config) thì đã có cơ chế phát hiện chưa? Và nhà máy thực tế giải bài này thế nào?
>
> Trạng thái tại thời điểm ghi: **đã có cơ chế phát hiện (drift detection), nhưng phát hiện
> chậm + không ngăn + có điểm mù.** Chi tiết + việc cần làm bên dưới.

---

## 1. Cơ chế ĐÃ CÓ trong MProject (drift detection = giám sát toàn vẹn file)

Luồng băm SHA-256 + đối chiếu định kỳ:

1. **Lúc deploy** — agent ghi **SHA-256 của từng file** đã cài vào bảng cục bộ `DeployedFiles`.
   - `MProjectAgent/Services/InstallDirectoryService.cs` → `RecordDeployedAsync` (ghi record kèm SHA).
   - Bảng: `MProjectAgent/Storage/CacheIndex.cs` (`DeployedFiles`, cột `Sha256`, `InstallRoot`, `RelativePath`).

2. **Định kỳ** — `InventoryReporter` băm lại toàn bộ file trên đĩa, so với SHA đã ghi.
   - `MProjectAgent/Services/InventoryReporter.cs` → `CheckFileAsync`:
     - Khác hash → `"hash_mismatch"` (đúng case: sửa số liệu trong config)
     - File biến mất → `"missing"`
     - Không đọc được → `"unreadable"`
   - Lịch chạy: `MProjectAgent/Services/AgentWorker.cs` → `RunInventoryLoopAsync`.
   - Chu kỳ: `Agent:InventoryIntervalHours`, **mặc định 24 giờ** (`Configuration/AgentOptions.cs`;
     `appsettings.json` không override nên đang là 24h).

3. **Server nhận báo cáo** → đặt trạng thái drift cho bản ghi cài đặt.
   - `MProjectBackend/MProject.Application/Services/Software/PcInventoryService.cs` → `ProcessAsync`:
     `record.DriftStatus = pkg.Issues.Count == 0 ? Healthy : Drift;` + `DriftSummary = BuildSummary(pkg)`.

4. **Nổi lên UI** — console `/computers` → tab **Drift / Needs attention** liệt kê trạm lệch + file lệch.
   - FE: `MProjectFrontend/src/pages/Computer/components/DriftTable.tsx`,
     `NeedsAttentionTable.tsx`.
   - API drift: `PcInventoryService.GetDriftedComputersAsync`.

**Kết luận phần này:** sửa tay 1 file config *mà agent đã deploy* → chu kỳ inventory kế tiếp
phát hiện `hash_mismatch`, trạm hiện "Drift".

### Bốn giới hạn phải nắm

| Giới hạn | Hệ quả |
|---|---|
| **Độ trễ 24h** | Không real-time; phát hiện muộn tối đa 24h. Chỉnh `InventoryIntervalHours` nhỏ lại được. |
| **Chỉ giám sát file agent đã ghi nhận** | File `PreserveLocal` (database runtime, config kiểu "giữ tại máy") **cố tình KHÔNG được ghi/giám sát** → sửa loại này drift **không bắt được**. Điểm mù lớn nhất. Xem `InstallDirectoryService.cs` nhánh `if (file.PreserveLocal)` (không `RecordDeployedAsync`). |
| **Chỉ phát hiện, không ngăn & không tự chữa** | Nó báo cáo thôi. Deploy lại **sẽ** khôi phục file đúng (`TargetMatchesAsync` copy đè file lệch) nhưng phải operator bấm, không tự động. |
| **Không phải access control** | Ai có quyền ghi vào `D:\Apps` vẫn sửa được; baseline SHA nằm trong SQLite của agent, bản thân nó không chống admin cục bộ cố tình. |

### Phân biệt với Config Baseline (thứ KHÁC, đừng nhầm)

`Config Baseline` (trang FE `/config-baselines`) kiểm tra **giá trị** config (BOM/SKU/tiêu chí)
so với chuẩn theo model, **enforce lúc pin/release** — là **cổng chặn version sai trước khi
phát hành**, KHÔNG giám sát file cục bộ sau khi bị sửa tay.

---

## 2. Thực tế nhà máy giải quyết thế nào (5 lớp: ngăn → phát hiện → khôi phục)

1. **Ngăn bằng phân quyền — line phòng thủ SỐ 1, quan trọng nhất.**
   Operator chạy bằng **standard user không có quyền ghi** vào thư mục app/config; chỉ service
   account của agent (SYSTEM) ghi được → dùng **NTFS ACL** + optionally **AppLocker/WDAC**
   (application allowlisting). Người "không có quyền" khi đó **không sửa được về mặt vật lý**.
   (UIStore cũ chỉ `attrib +h +s` để ẩn `D:\Apps` — là "che giấu", không phải bảo mật.)

2. **File Integrity Monitoring (FIM).** Chính là cách băm-baseline MProject đang làm — chuẩn
   công nghiệp (Tripwire, Wazuh/OSSEC, AIDE). MProject đã có bản gọn; cần cải thiện độ trễ + auto-heal.

3. **Desired-state / golden image (tự chữa).** Trạm coi app+config là **bất biến**; phát hiện
   lệch thì **tự deploy đè lại** từ nguồn tin cậy (giống SCCM/Ansible/DSC). Thay đổi phải đi qua
   hệ trung tâm.

4. **Ký số manifest/binary.** Agent verify chữ ký trước khi tin. MProject **đã có cho self-update
   agent** (F-08), nhưng **nội dung package app mới verify bằng SHA so với manifest server** (tin
   tưởng server + TLS) — chưa ký nội dung package.

5. **Config là dữ liệu tập trung + audit trail.** Nguồn chân lý config nằm ở server (Override
   Files + Config Baselines + Approvals RBAC + lịch sử audit ai-sửa-gì-khi-nào). Sửa tay ở máy =
   "drift" cần khôi phục. Ngành kiểm định (dược, ô tô) thêm chuẩn audit kiểu 21 CFR Part 11.

---

## 3. Kế hoạch implement (theo thứ tự ưu tiên đáng làm)

### P0 — Khóa ACL `D:\Apps` (ngăn chặn, đòn bẩy cao nhất, ít code)
Đánh trúng "người không có quyền": làm họ **không sửa được** thay vì phát hiện sau.
- Operator chạy non-admin; chỉ agent service (SYSTEM/service account) có quyền Write/Modify vào
  `D:\Apps` (InstallRoot) và thư mục cache.
- Cách làm: bước cấu hình lúc **cài agent** (`MProjectAgent/Commands/ServiceInstaller.cs` hoặc
  script cài) — set ACL bằng `icacls`:
  - Gỡ quyền Write của `Users`/`Authenticated Users` trên `D:\Apps`.
  - Cấp Modify cho tài khoản chạy service của agent.
- Optionally: AppLocker/WDAC policy chỉ cho chạy exe trong `D:\Apps` đã biết.
- **Gần như không đụng code C#**, chủ yếu script + tài liệu vận hành. Rủi ro thấp, giá trị cao.

### P1 — Giảm độ trễ phát hiện + kiểm tra theo yêu cầu
- Hạ `Agent:InventoryIntervalHours` (vd 1h) cho trạm config-critical (chỉ đổi appsettings).
- Thêm **nút "Kiểm tra toàn vẹn ngay"** trên `/computers` → gửi lệnh cho agent chạy
  `InventoryReporter.RunAsync` ngoài lịch. Plumbing đã có (`RunAsync` public); cần:
  - 1 AgentCommand mới (kênh lệnh agent) hoặc endpoint kích hoạt inventory on-demand.
  - Nút FE ở `Computers` gọi API đó.

### P2 — Auto-heal tùy chọn (desired-state)
- Khi server nhận báo cáo có `Issues` (`PcInventoryService.ProcessAsync`), **tự enqueue một job
  deploy lại** version đang pin để khôi phục file đúng (thay vì chờ operator).
- Nên có **cờ bật/tắt** (mặc định tắt) + tránh loop nếu file cứ bị sửa lại; log rõ đã auto-heal.
- Lưu ý: deploy lại chỉ sửa file agent quản lý; file preserve-local vẫn ngoài phạm vi (đúng thiết kế).

### P3 — Lấp điểm mù PreserveLocal
- Rà lại: có file config nào *không nên cho người sửa* mà đang bị đánh dấu `PreserveLocal` không?
- `PreserveLocal` chỉ nên dành cho **state runtime thật sự** (database .mdb…), KHÔNG dành cho
  config chuẩn. Config chuẩn nên deploy thường (được ghi + giám sát) hoặc qua Override Files.

### P4 — Ký số nội dung package (nếu threat model gồm server/transport bị xâm nhập)
- Ký manifest package app như đã làm cho agent release (F-08 `ReleaseSignatureVerifier`), agent
  verify chữ ký trước khi tin nội dung. Chỉ cần khi không còn tin tưởng hoàn toàn server + TLS.

---

## 4. Tóm tắt 1 dòng
Phát hiện sửa tay config **đã có** (drift qua inventory, hiện ở tab Drift `/computers`), nhưng
**phát hiện chậm + không ngăn + mù file preserve-local**. Cách bài bản: **ngăn trước bằng ACL
phân quyền (P0)** → rồi FIM + auto-heal (P1/P2) → config tập trung có audit. Phần lớn khung
MProject đã có nền; chủ yếu thiếu lớp "ngăn bằng quyền" và "tự chữa".

## Con trỏ code nhanh (để bắt đầu)
- Ghi SHA khi deploy: `MProjectAgent/Services/InstallDirectoryService.cs` (`RecordDeployedAsync`,
  nhánh `PreserveLocal`, `TargetMatchesAsync`).
- Băm lại + phát hiện: `MProjectAgent/Services/InventoryReporter.cs` (`CheckFileAsync`).
- Lịch chạy inventory: `MProjectAgent/Services/AgentWorker.cs` (`RunInventoryLoopAsync`).
- Cấu hình chu kỳ: `MProjectAgent/Configuration/AgentOptions.cs` (`InventoryIntervalHours`);
  `MProjectAgent/appsettings.json`.
- Server xử lý drift: `MProjectBackend/.../Services/Software/PcInventoryService.cs`
  (`ProcessAsync`, `GetDriftedComputersAsync`, `BuildSummary`).
- UI drift: `MProjectFrontend/src/pages/Computer/components/DriftTable.tsx`, `NeedsAttentionTable.tsx`.
- Cài đặt agent (chỗ thêm ACL): `MProjectAgent/Commands/ServiceInstaller.cs`.
