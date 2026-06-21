# Agent Refactor — Review Note (GAP-4/5/6 + Launcher + IPC)

> **Ngày:** 2026-06-22 · **Repo:** `C:\dev\MProject` (master) · **Phạm vi diff:** `169f63c..HEAD`
> (MProjectAgent + MProjectAgent.Ipc.Contracts + MProjectLauncher + phần agent của MProjectBackend — 78 file, ~16k dòng; ~3.5k dòng logic).
> Review xhigh: 10 finder-angle + sweep, các finding nặng đã đọc source xác nhận.
> `✅` = đã đọc source xác nhận · `⚠️` = hợp lý nhưng tuỳ môi trường/cần kiểm thêm.

## Bối cảnh khôi phục Git
`.git` trong OneDrive (`...\OneDrive\Desktop\TESS\MProject`) bị hỏng (mất `HEAD`/`config`/`index`/refs; objects là placeholder online-only). Đã clone lại sạch về **`C:\dev\MProject`** (ngoài OneDrive) + overlay working tree. Remote `origin = https://github.com/mphuong9x/MProject.git`, nhánh mặc định **`master`**. Toàn bộ GAP-1/2/4/5/6 **đã commit & push** (ghi chú "uncommitted" cũ đã sai). **Không** đặt repo trong OneDrive nữa.

Phần đúng/khen: `ReleaseSignatureVerifier` fail-closed chuẩn; EF migration `Up/Down` + query-filter soft-delete được kiểm thấy ổn.

---

## 🔴 Nghiêm trọng — vá trước khi self-update trên PC thật

### [ ] 1. ✅ Agent tự báo version cố định `1.0.0.0` → self-update kẹt vòng lặp / không bao giờ chạy
- **File:** `MProjectAgent/Services/SystemInfo.cs:10` + `MProjectAgent/MProjectAgent.csproj`
- **Vấn đề:** `AgentInfo.Version = Assembly...GetName().Version` nhưng `.csproj` không set `<Version>`/`<AssemblyVersion>` và không có `Directory.Build.props` → luôn `1.0.0.0`. Cả `AgentUpdater.IsNewer` lẫn server `IsNewerVersion` so với hằng số này.
- **Hậu quả:** release `1.0.0.0` → không bao giờ update (strictly-newer fail); release `>1.0.0.0` → agent update xong vẫn báo `1.0.0.0` → **mỗi heartbeat tải/đổi/restart service vô tận** (fleet-wide).
- **Fix:** stamp version lúc build/publish (`-p:Version=`), hoặc đọc `InformationalVersion`; đảm bảo agent đã cài báo đúng version thật.

### [ ] 2. ✅ `apply-update` báo THÀNH CÔNG khi copy thất bại
- **File:** `MProjectAgent/Commands/ApplyUpdateCommand.cs:65`
- **Vấn đề:** copy staged→install ném exception → `catch` khôi phục backup nhưng **rơi xuống** bước start service (72-77); bản cũ chạy `RUNNING` thì `return 0` "Done" + xoá marker.
- **Hậu quả:** update thất bại bị ghi nhận như thành công (chỉ tự sửa nhờ heartbeat sau).
- **Fix:** trong `catch`, log + `return` mã lỗi (đừng rơi xuống start như đường thành công).

### [ ] 3. ✅ Kiểm trạng thái service phụ thuộc ngôn ngữ → hỏng trên Windows không-tiếng-Anh
- **File:** `MProjectAgent/Commands/ApplyUpdateCommand.cs:139`
- **Vấn đề:** `WaitForStateAsync` so `sc.exe` output bằng `Contains("RUNNING"/"STOPPED")`. Windows tiếng Việt bản địa hoá tên trạng thái → luôn timeout → stop "abort", start coi là fail → rollback thừa.
- **Fix:** parse dòng `STATE : <code>` theo **số** (1=STOPPED, 4=RUNNING) thay vì chữ tiếng Anh.

### [ ] 4. ✅ `CopyDirectory` chỉ ghi đè, không xoá file cũ
- **File:** `MProjectAgent/Commands/ApplyUpdateCommand.cs:102`
- **Vấn đề:** overlay staged lên install (và rollback overlay backup) không xoá file bị bỏ giữa các version → DLL/asset cũ sót lại, bản mới nạp nhầm; rollback "bẩn".
- **Fix:** swap thư mục nguyên tử (đổi tên install↔staging) thay vì copy chồng.

### [ ] 5. ✅ Xoá thư mục uninstall đi theo junction/symlink (guard chỉ kiểm chuỗi)
- **File:** `MProjectAgent/Services/JobExecutor.cs:204` + `MProjectAgent/Services/InstallDirGuard.cs:36`
- **Vấn đề:** `InstallDirGuard` resolve `..` nhưng không resolve reparse-point; `Directory.Delete(recursive:true)` đi theo junction. Junction bên trong base trỏ ra ngoài (vd `C:\Windows`) → xoá đích đó với quyền **LocalSystem**.
- **Fix:** kiểm `FileAttributes.ReparsePoint`; không đi qua link khi xoá đệ quy.

---

## 🟠 Trung bình (đúng đắn)

### [ ] 6. ✅ `MinServerVersion` là "cổng chết"
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:475`
- **Vấn đề:** trường được validate/lưu/đưa vào DTO nhưng `ResolveAgentUpdateAsync` không hề so sánh với version server → release "yêu cầu server ≥ X" vẫn được chào cho agent trên server cũ.
- **Fix:** enforce trong `ResolveAgentUpdateAsync`, hoặc bỏ trường để khỏi tạo an toàn giả.

### [ ] 7. ✅ Job báo `Completed` TRƯỚC khi ghi catalog; lỗi ghi bị nuốt → server/agent lệch
- **File:** `MProjectAgent/Services/JobExecutor.cs:104` (catch ở `:389`)
- **Vấn đề:** `CompleteAsync` (104) trước `RecordCatalogAndLaunchAsync` (115); `UpsertAsync` lỗi (đĩa đầy/SQLite khoá) bị log-and-swallow. Server tưởng đã cài, catalog local trống → launcher không hiện app, IPC `run` báo "not deployed", uninstall sau không thấy InstallRoot → file mồ côi.
- **Fix:** ghi catalog **trước** khi báo Completed (hoặc báo Failed nếu ghi hỏng).

### [ ] 8. ⚠️ Uninstall xoá catalog entry TRƯỚC khi xoá thư mục → mồ côi khi retry
- **File:** `MProjectAgent/Services/JobExecutor.cs:197`
- **Vấn đề:** gỡ entry + cache ref rồi mới `RemoveInstallDir`; nếu `Directory.Delete` ném (file khoá/AV) → job Failed → retry → `RemoveAsync` trả null → bỏ qua xoá thư mục → thư mục kẹt trên đĩa mãi (server hiển thị Uninstalled).
- **Fix:** xoá thư mục trước rồi mới gỡ catalog; hoặc làm idempotent (retry vẫn xoá được).

### [ ] 9. ✅ AutoRemove: tranh chấp unassign + re-assign / kẹt `Installed`
- **File:** `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:238`
- **Vấn đề:** khi `AutoRemoveOnUnassign`, record giữ `Installed` tới khi agent xong Uninstall job. Re-assign trong cửa sổ đó → `PollAsync` thấy `Installed` → không sinh job cài, còn Uninstall pending lại xoá đúng app vừa gán. PC offline → kẹt `Installed`, chặn cài lại.
- **Fix:** khi re-assign, huỷ các Uninstall job pending tương ứng (hoặc reset record về cài lại).

### [ ] 10. ✅ IPC server 1-instance + không timeout đọc → DoS local treo launcher
- **File:** `MProjectAgent/Services/IpcServer.cs:69`
- **Vấn đề:** `maxNumberOfServerInstances:1`, vòng lặp tuần tự, `ReadLineAsync` chỉ bị chặn bởi `stoppingToken`. Client kết nối rồi không gửi gì → chặn vô hạn; pipe cho `BUILTIN\Users` → user local nào cũng làm launcher "Agent offline".
- **Fix:** read-timeout per-connection và/hoặc xử lý connection bất đồng bộ (≥1 instance).

### [ ] 11. ⚠️ Launcher `DispatcherTimer` `async void` không chặn reentrancy
- **File:** `MProjectLauncher/MainWindow.xaml.cs`
- **Vấn đề:** poll 1.5s nhưng pipe `Connect(2000)`; vòng `RefreshAsync` >1.5s → Tick kế chồng lên → hai `SyncApps` cùng sửa `ObservableCollection Apps` → hàng trùng/mất hoặc `InvalidOperationException`.
- **Fix:** cờ `_busy` bỏ qua tick khi đang chạy.

### [ ] 12. ✅ IPC `stop`/`restart` luôn báo thành công kể cả no-op
- **File:** `MProjectAgent/Services/IpcRequestHandler.cs:68`
- **Vấn đề:** bỏ giá trị `bool` của `RequestStopAsync`/`RequestRestartAsync`; supervisor tắt hoặc app đã dừng (trả `false`) → launcher vẫn nhận "stopped"/"restarted".
- **Fix:** phản ánh kết quả thật trong `OpResult`.

---

## 🟡 Gia cố / tiềm ẩn

### [ ] 13. ⚠️ Tải bản update: URL tuyệt đối do server cấp không ép HTTPS, bỏ header auth, không cap kích thước
- **File:** `MProjectAgent/Services/AgentUpdater.cs` (`OpenDownloadStreamAsync`)
- **Vấn đề:** server bị chiếm/MITM trỏ `DownloadUrl` sang `http://` tuỳ ý + stream khối lượng lớn lấp đầy đĩa trước khi kiểm SHA. (Chữ ký F-08 vẫn chặn cài sai.)
- **Fix:** ép HTTPS + cap theo `update.Size`.

### [ ] 14. ✅ Launcher dùng `Newtonsoft.Json`, agent dùng `System.Text.Json` + `JsonStringEnumConverter`
- **File:** `MProjectLauncher/IpcClient.cs:25` vs `MProjectAgent/Services/IpcServer.cs:20`
- **Vấn đề:** hiện ẩn (mọi field wire là `string`). Thêm bất kỳ field **enum** nào vào IPC contract → agent ghi `"Running"`, Newtonsoft (không StringEnumConverter) sẽ vỡ ở mọi station.
- **Fix:** thống nhất serializer/converter cho cả hai phía.

---

## 🔵 Hiệu năng / cleanup (đáng làm, không chặn)

- [ ] **15. ⚠️ Active agent release truy vấn lại (join Blob) mỗi heartbeat** dù gần bất biến — `AgentService.cs:471`. Cache + vô hiệu khi publish/activate/delete.
- [ ] `ComputeSha256Async` trùng lặp — đã có `MProject.Domain.Utils.HashUtils` (dùng bởi OverrideFileService/SoftwareFileService). Backend `AgentReleaseService` nên gọi lại.
- [ ] `AppCatalogStore` chép lại cơ chế atomic-write/backup của `RuntimeStateStore` — tách `AtomicJsonFileStore<T>` dùng chung.
- [ ] `BlobGcService` 3 `AnyAsync` tuần tự/blob — gộp 1 round-trip.
- [ ] `CacheIndex` mở connection riêng mỗi file khi gỡ package — gộp vào 1 transaction.
- [ ] Projection metadata GAP-5 (BOM/FCD/FTU/FW/Region) copy-paste 4 chỗ trong `SoftwareVersionService` — gom 1 helper `VersionMetadata.From(version)`.
- [ ] Tên service `"MProjectAgent"` hardcode nhiều nơi (AgentUpdater/ApplyUpdate/Program/Installer) — đưa về 1 hằng/cấu hình.

---

## Thứ tự đề xuất
1. **#1–#3** (chặn chính tính năng self-update GAP-4, đặc biệt #3 trên Windows tiếng Việt).
2. **#4, #5, #7, #8, #9** (toàn vẹn dữ liệu/đĩa/an toàn xoá).
3. **#10–#14** (ổn định launcher/IPC).
4. **#15 + cleanup** khi rảnh.

Theo coding_rule: mỗi lỗi 1 thay đổi nhỏ, an toàn, tách commit (`≤5 file / ≤150 LOC`).
