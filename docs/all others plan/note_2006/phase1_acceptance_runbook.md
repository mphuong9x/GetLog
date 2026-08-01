# Phase-1 Acceptance Runbook — Nghiệm thu trên trạm thật để thay UIStore

> Mục tiêu: validate end-to-end toàn bộ chức năng đã code (GAP-1…GAP-7 + F-08) trên **một trạm thật**, rồi
> "bật công tắc" thay `UIStore` cho trạm đó. Mọi GAP trong [compare1.md](compare1.md) đã code xong; tài liệu này
> là bước **nghiệm thu** (không phải code). Liên quan: [F08_release_signing.md](F08_release_signing.md),
> [agent_deploy_signing_guide.md](agent_deploy_signing_guide.md).
> Ngày lập: 2026-06-21.

## 0. Tiền đề & môi trường

| Hạng mục | Giá trị / kiểm tra |
|---|---|
| Server API chạy, FE truy cập được | đăng nhập được trang quản trị |
| 1 **Model** + 1 **Station** + ≥1 **PC** (Computer) đã khai báo, PC gán vào Station | xem trang Computers/Resources |
| Agent đã cài trên PC nghiệm thu | `MProjectAgent.exe install` (Run as Administrator); service `MProjectAgent` = Running |
| `appsettings.json` của agent | `Agent:ServerUrl`, `Agent:InstallerToken` đúng; biết `InstallRoot:Base` (mặc định `D:\Apps`, sample dùng `C:\MProjectApps`), `Cache:Root`, `StateDirectory` (mặc định `C:\ProgramData\MProjectAgent`) |
| Có gói mẫu thật | `Sample_Software/` (CPEI_MFG.exe + Config/*.json + folder khách FTU_…) |

Đường dẫn hữu ích trên PC (theo `StateDirectory` mặc định):
- Catalog app đã deploy: `C:\ProgramData\MProjectAgent\runtime\catalog.json`
- Log apply self-update: `C:\ProgramData\MProjectAgent\update\apply-update.log`
- Cache: `Cache:Root` (mặc định `D:\MProjectAgent\cache`, sample `C:\ProgramData\MProjectAgent\cache`)

> **Quy tắc an toàn:** nghiệm thu trên trạm **không phải dây chuyền đang chạy production**. Mục G (Uninstall) là
> thao tác **xóa thư mục** — chỉ làm với package nghiệm thu, cờ AutoRemove bật có chủ đích.

---

## A. Đóng gói composite & phát hành (nền tảng — mục 4.0)

**Mục tiêu:** xác nhận 1 "app" composite (vỏ CPEI_MFG + Config + chương trình khách) đóng thành **một** package-version, entry point = `CPEI_MFG.exe`.

1. Tạo **Software Package** mới (để **AutoRemove = OFF** ở bước này).
2. Tạo **Version**, **Upload** nguyên cây thư mục `Sample_Software/` (giữ cấu trúc: `Debug/CPEI_MFG.exe`, `Config/*.json`, `FTU_…/…`).
3. Đặt **Entry point** = đường dẫn tương đối tới `CPEI_MFG.exe` (vd `Debug/CPEI_MFG.exe`).
4. (GAP-5) Nhập **metadata** BOM/FCD/FTU/FW/Region (đối chiếu `Config/ProgramConfig.json > VersionConfig`).
5. **Release** version.

✅ **Pass khi:** version Released; manifest hiển thị đủ cây file; chip metadata BOM/FW/FCD/FTU/Region hiện ở danh sách version.

---

## B. Tùy biến config theo trạm/máy (GAP-1)

**Mục tiêu:** file config riêng theo Model/Station/PC được áp đúng lúc deploy; drift không báo oan.

1. Khai báo **OverrideFile** (hoặc giá trị override) cho file config theo Model/Station/PC (vd `Config/ProgramConfig.json` các trường `Station/DUT_IP/COM…`, hoặc file custom của khách).
2. (Nếu dùng ConfigBaseline) đảm bảo version không vi phạm baseline khi pin.

✅ **Pass khi:** sau khi deploy (mục C), file trên PC mang **giá trị override đúng theo trạm**; chạy inventory → **không** báo drift cho các file/khóa được phép khác biệt.

---

## C. Gán → deploy → chạy tại trạm (GAP-2 launcher)

1. **Assign** package vào Station; **Pin** version vừa Released.
2. Chờ agent `poll` (mặc định 60s) → job Install xuất hiện, tải + deploy.
3. Kiểm tra trên PC: thư mục `<InstallRoot.Base>\<TênPackage>` có đủ file; `CPEI_MFG.exe` đúng entry point.
4. App được khởi chạy (supervisor) **hiện GUI** ở session người dùng.
5. **Operator tại trạm**: mở **tray launcher** (`MProjectLauncher`) → thấy app trong danh sách → thử **Mở / Đóng / Restart**; (tùy chọn) đăng nhập operator nếu có.

✅ **Pass khi:** job → Completed; `PcInstallationRecord` = Installed; app chạy; launcher liệt kê + điều khiển được app; trạng thái/health hiển thị đúng trên FE.

---

## D. Điều khiển từ xa & maintenance (điểm cộng MProject)

1. Từ FE gửi lệnh **Restart / StopApp** tới PC → quan sát app dừng/chạy lại đúng.
2. Bật **maintenance window** cho trạm → xác nhận agent không tự deploy/restart trong cửa sổ.

✅ **Pass khi:** lệnh có hiệu lực; maintenance chặn thay đổi đúng như cấu hình.

---

## E. Inventory & drift (GAP-6 phần báo cáo)

1. Để agent chạy 1 chu kỳ inventory (`Agent:InventoryIntervalHours`, có thể tạm chỉnh ngắn để test).
2. Sửa/xóa thử 1 file **không thuộc** danh sách override trong install dir → chạy inventory.

✅ **Pass khi:** drift **báo đúng** file bị lệch (missing/hash mismatch); file override hợp lệ **không** bị báo oan.

---

## F. Agent self-update có ký số (GAP-4 + F-08)

> Chi tiết thao tác: [agent_deploy_signing_guide.md](agent_deploy_signing_guide.md). Tóm tắt nghiệm thu:

1. Đảm bảo agent có `Agent:ReleasePublicKeyPem` (đã nhúng) và `Agent:SelfUpdateEnabled=true`.
2. Bump `<Version>` agent → `package-agent.ps1` → nén → **ký** (`sign-agent-release.ps1 -Sign`) → **Publish** kèm chữ ký → Activate.
3. Quan sát `apply-update.log` → `Service running on the new version. Done.`; heartbeat báo version mới.
4. **Test fail-closed:** publish 1 bản **UNSIGNED** rồi Activate → agent **từ chối** (log `signature verification failed`); sau đó Activate lại bản đã ký.

✅ **Pass khi:** bản đã ký cập nhật thành công + rollback an toàn nếu bản mới lỗi; bản chưa ký **bị từ chối**.

---

## G. Uninstall/cleanup (GAP-6 — DESTRUCTIVE)

> ⚠️ Thao tác xóa thư mục dưới LocalSystem. Chỉ làm với **package nghiệm thu**.

### G1. AutoRemove = OFF (mặc định, giữ file)
1. Với 1 package **cờ OFF** đã deploy → **Remove assignment** khỏi station.
2. Kiểm tra PC.

✅ **Pass khi:** `PcInstallationRecord` = Uninstalled (server); **file vẫn còn** ở `<InstallRoot.Base>\<pkg>` (hành vi bảo toàn).

### G2. AutoRemove = ON (xóa thật)
1. Tạo/đặt 1 package **cờ AutoRemove = ON** (toggle ở modal "New package"), deploy lên trạm.
2. **Remove assignment**.
3. Chờ agent poll → job **Uninstall** (Pending) → chạy.

✅ **Pass khi:**
- App đang chạy **dừng** (nếu nó là app đang supervise).
- Thư mục `<InstallRoot.Base>\<pkg>` **bị xóa**.
- Entry trong `catalog.json` **biến mất**; launcher không còn liệt kê.
- Cache GC **thu hồi** blob không còn tham chiếu (dung lượng `Cache:Root` giảm sau chu kỳ GC).
- Job → Completed; `PcInstallationRecord` → Uninstalled.
- **Guardrail:** thử cấu hình lệch (vd InstallRoot bất thường) → agent **từ chối xóa** (job Failed, log "not a subdirectory of install base"), **không** xóa nhầm ngoài base.

---

## H. "Bật công tắc" thay UIStore (Phase-1 done)

Chỉ thực hiện khi A→G đều **Pass** trên trạm nghiệm thu:

1. Dừng & gỡ `UIStore` trên trạm đó.
2. Để `MProjectAgent` + `MProjectLauncher` đảm nhận tải/cập nhật/chạy/điều khiển.
3. Theo dõi 1 ca sản xuất; nếu ổn → nhân rộng ra các trạm cùng model.

> Lưu ý production: chỉ bật `SelfUpdateEnabled=true` **kèm** `ReleasePublicKeyPem`; mặc định `AutoRemoveOnUnassign=false`
> cho package production trừ khi chủ đích muốn xóa khi bỏ gán.

---

## Bảng tổng hợp nghiệm thu

| # | Hạng mục | GAP | Kết quả |
|---|---|---|---|
| A | Đóng gói composite + metadata + release | 4.0, GAP-5 | ☐ |
| B | Config override theo trạm/máy | GAP-1 | ☐ |
| C | Gán → deploy → chạy + launcher | GAP-2 | ☐ |
| D | Lệnh từ xa + maintenance | (cộng) | ☐ |
| E | Inventory/drift báo đúng | GAP-6 | ☐ |
| F | Self-update có ký + fail-closed | GAP-4/F-08 | ☐ |
| G1 | Unassign cờ OFF → giữ file | GAP-6 | ☐ |
| G2 | Unassign cờ ON → xóa sạch + guardrail | GAP-6 | ☐ |
| H | Thay UIStore trên trạm | Phase-1 | ☐ |
