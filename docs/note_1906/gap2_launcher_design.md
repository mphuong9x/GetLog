# GAP-2 — Thiết kế `MProjectLauncher` (tray app cho operator tại trạm)

> Mục tiêu: cho operator tại trạm **tự thấy danh sách app test được gán cho trạm/máy** + trạng thái, và **tự chọn chạy / dừng / restart** — giảm phụ thuộc người vận hành hệ thống. Blocker P0 cuối để bỏ `UIStore`.
> Phạm vi: **hiển thị + thao tác chạy app**. KHÔNG login operator, KHÔNG truy vết người dùng, KHÔNG SFIS/MES, KHÔNG quản lý assignment/upload/config (vẫn ở web cho engineer). Deploy **tự động do engineer gán** — operator KHÔNG tự tải.
> Ngày: 2026-06-19. Trạng thái: **thiết kế FREEZE — đã chốt toàn bộ quyết định, sẵn sàng code.**

## Quyết định đã chốt (2026-06-19)

1. **IPC** = **Named pipe** cục bộ (không port, ACL Windows, 0 dependency thêm).
2. **UI tech** = **WPF trên .NET Framework 4.8** (KHÔNG net8). Lý do: nhiều PC nhà máy chỉ có sẵn 4.8 (in-box), không có .NET 8; 4.8 WPF nhẹ (vài MB), đúng y stack UIStore cũ đã chứng minh trên chính các PC này. Agent net8 publish **self-contained** nên không xung đột; 2 process nói chuyện qua named pipe (primitive OS, xuyên framework OK). Contracts dùng chung target **`netstandard2.0`** để cả agent (net8) lẫn launcher (net48) cùng ref. JSON: `System.Text.Json` (NuGet) hoặc Newtonsoft. Tray: `Hardcodet.NotifyIcon.Wpf` / `System.Windows.Forms.NotifyIcon`.
3. **Mô hình vận hành** = **nhiều app/trạm, mỗi lúc chạy 1 app**, operator chọn/đổi. (Supervisor đơn-process giữ nguyên, dùng cơ chế swap sẵn có.)
4. **Deploy** = **tự động do engineer gán trên web**; operator chỉ chạy app đã deploy (không deploy-on-demand).
5. **Policy auto-run** = **R1 + R2 + R3** (xem §2): trạm 1 app → auto-run; trạm nhiều app chưa chọn → **chờ operator** (không tự chạy); app đã chọn → tự chạy lại qua reboot. Không thêm khái niệm "app mặc định" server-side.
6. **Feature-parity UIStore** (operator-facing): list + đèn trạng thái + Run/Stop/Restart + **trạng thái đang-update (gate Run)** + **icon app** + **badge "vừa cập nhật vX"** + **pane sự kiện gần đây**. Bỏ login/PC-allowlist/config-edit (xem §5.1). UI sạch hơn UIStore; cải tiến sau.
7. **Phân phối** = **bundle launcher với agent (MVP)**; cập nhật tập trung để sau qua **GAP-4 component self-update** (xem §11). KHÔNG đưa launcher vào catalog test-app.

---

## 0. Insight kiến trúc — vì sao mô hình "nhiều app, 1 active" map gọn vào cái đã có

Đã verify trong code:

- **Server đã có catalog nhiều app/trạm:** `StationSoftwareAssignment` (nhiều dòng/station, mỗi dòng = package + `TargetVersionId` pin + `IsActive`). `InstallationJobService.PollAsync` lấy **mọi assignment IsActive** → tạo install job cho **từng package** → agent **deploy tất cả app được gán** (mỗi package 1 install dir riêng theo `PackageName`). ⇒ **Không cần entity/endpoint server mới** cho danh sách.
- **App "active" = chính cái `ProcessSupervisor` đang giám sát.** `SupervisedProcessState` (trong `RuntimeStateStore`) đã lưu `PackageId/VersionId/ExePath` của app đang chạy, **sống qua reboot/agent-restart** nhờ `RecoverAsync`. ⇒ "app nào đang active" đã bền vững sẵn.
- **Đổi app = swap, supervisor đã làm được:** `LaunchAndSuperviseAsync(LaunchRequest)` gọi `StopCurrentAsync` rồi start app mới, ghi đè state. ⇒ operator chọn app X = agent gọi `LaunchAndSuperviseAsync(X)`. **Không viết lại** crash-restart/health/quarantine.
- **Catalog dữ liệu để hiển thị + để launch** đã nằm trong manifest jobs agent vốn nhận: `AgentManifestJob.{PackageId, VersionId, PackageName, VersionNumber, EntryPointPath, EntryPointMode, HealthCheckUrl}`.

### ⚠️ Thay đổi hành vi DUY NHẤT chạm logic launch hiện có

Hôm nay `JobExecutor.ExecuteAsync` **auto-launch supervisor cho MỖI job deploy xong** ([JobExecutor.cs:97](../MProjectAgent/Services/JobExecutor.cs#L97), [:210](../MProjectAgent/Services/JobExecutor.cs#L210)). Với nhiều app, mỗi deploy gọi `LaunchAndSuperviseAsync` → `StopCurrentAsync` → **các app tranh nhau, app deploy sau cùng thắng** (không xác định).

⇒ **Phải đổi:** deploy KHÔNG còn auto-launch vô điều kiện. Thay bằng **"chỉ (re)launch app đang là active"**. Chi tiết policy ở §2 (điểm cần bạn chốt). *Đây là chỗ duy nhất sửa logic chạy; supervisor core + recovery + health giữ nguyên.*

---

## 1. Kiến trúc tổng thể

```
┌──────────────── Operator session (interactive) ────────────────┐
│  MProjectLauncher.exe  (WPF tray, KHÔNG login)                  │
│   • list app (catalog) + đèn trạng thái  • poll status ~1–2s    │
│   • chọn app để Run/Stop/Restart                                │
└───────────────▲──────────────────────────┬────────────────────┘
                │ named pipe (local, ACL)   │
        request │ JSON line protocol        │ response
                │                           ▼
┌─────────────────────── Session 0 (LocalSystem) ────────────────┐
│  MProjectAgent (Windows service — core giữ nguyên)             │
│   + IpcServer (BackgroundService MỚI)                          │
│   + AppCatalogStore (MỚI: list app đã deploy + entry point)    │
│   ProcessSupervisor (active app, swap)  ◄── select/stop/restart│
│   JobExecutor: deploy mọi app, ghi catalog, KHÔNG auto-thrash  │
└────────────────────────────────────────────────────────────────┘
```

- Launcher **chỉ nói với agent cục bộ** → chịu mất mạng, không lộ creds, một nguồn sự thật.
- Agent vẫn là bên duy nhất nói với server. Deploy tự động giữ nguyên.

---

## 2. Policy auto-run (ĐÃ CHỐT — R1+R2+R3)

Khi bỏ auto-launch-mọi-deploy, "khi nào app tự chạy mà không cần operator bấm":

- **(R1) Trạm chỉ có 1 app được gán:** auto-run app đó sau deploy (giữ trải nghiệm unattended như hiện tại). ✅ bật.
- **(R2) Trạm có nhiều app, chưa từng chọn:** KHÔNG tự chạy cái nào — chờ operator chọn. ✅ (an toàn; tránh chạy nhầm app). KHÔNG dùng "app mặc định".
- **(R3) Đã có app active (operator từng chọn):** sau reboot/agent-restart/deploy version mới của chính app đó → tự (re)launch lại đúng app active (đã có qua `RecoverAsync`; deploy version mới của app active sẽ relaunch nó). ✅
- **(R4) App active bị xóa assignment / không còn deploy:** clear active, về trạng thái chờ operator chọn.

> "Số app được gán" để xét R1 vs R2 = số `CatalogApp` đã deploy cho máy (từ `AppCatalogStore`). Trạm 1-app deploy xong = auto active; có app thứ 2 deploy về thì KHÔNG đổi active đang chạy.

---

## 3. Thành phần agent thêm mới (additive, trừ 1 điểm §0)

### 3.1 `AppCatalogStore` (mới) — danh sách app deploy cục bộ
- Lưu `catalog.json` (cùng kiểu atomic-write như `RuntimeStateStore`): list `CatalogApp { PackageId, VersionId, PackageName, VersionNumber, ExePath, Mode, HealthCheckUrl, InstallRoot, DeployedAt }`.
- Ghi/cập nhật trong `JobExecutor` sau khi deploy thành công (đã có sẵn `installRoot` + `job.EntryPointPath`).
- Dùng để: (a) launcher hiển thị danh sách offline, (b) agent biết `ExePath` để `LaunchAndSuperviseAsync` khi operator chọn.
- (Tùy chọn) đối soát với `CacheIndex.GetAllDeployedFilesAsync` để dọn app không còn.

### 3.2 `IpcServer : BackgroundService` (mới)
- Đăng ký `AddHostedService<IpcServer>()` trong `Program.cs` cạnh `AgentWorker`.
- Inject `ProcessSupervisor`, `AppCatalogStore`, `TokenStore`, và cờ `serverReachable` (đọc-only từ trạng thái heartbeat).
- `NamedPipeServerStream` (PipeSecurity ACL cho Interactive Users) → parse op → gọi method có sẵn.

### 3.3 Sửa `JobExecutor` (điểm §0)
- Sau deploy: **ghi catalog** + áp policy §2 thay cho auto-launch vô điều kiện.
- Tách quyết định launch thành helper nhỏ (active-pointer aware). Supervisor/health/recovery **không đổi**.

**KHÔNG đụng:** `ProcessSupervisor`, `AgentCommandHandler`, `RuntimeStateStore`, `InteractiveProcessLauncher`, luồng server. Backend/FE web: **không đổi**.

---

## 4. API agent phơi qua IPC (named pipe, JSON line, camelCase)

### 4.1 `status` — `{ "op": "status" }`
```jsonc
{
  "machine": {
    "computerId": "...", "hostname": "STATION-07",
    "assignmentState": "Active", "serverReachable": true, "maintenanceMode": false
  },
  "activePackageId": "...",            // app đang được supervisor giám sát (null nếu chưa chọn)
  "apps": [                            // catalog: mọi app đã deploy cho máy này
    {
      "packageId": "...", "versionId": "...",
      "packageName": "CPEI_MFG (Model X)", "versionNumber": "1.4.2",
      "status": "Running",             // Running|Idle|CrashLoop|Maintenance|Updating
      "isActive": true,
      "pid": 12345, "startedAt": "2026-06-19T...",
      "lastExit": { "exitedAt": "...", "exitCode": 0, "exitReason": "WindowClosed" },
      "deploy": {                      // null nếu không có job đang chạy cho package này
        "state": "Downloading",        // Downloading|Installing (từ InstallationJob progress)
        "bytesDownloaded": 12345678, "totalBytes": 50000000
      },
      "iconBase64": "...",             // icon trích từ ExePath (cache, gửi 1 lần / đổi version)
      "justUpdatedTo": "1.4.2"         // set 1 nhịp sau khi deploy version mới xong (badge "vừa cập nhật")
    },
    { "packageId": "...", "packageName": "Burn-in Tool", "status": "Idle", "isActive": false }
  ],
  "recentEvents": [                    // (pane sự kiện) vài dòng gần nhất: started/stopped/updated/crashed
    { "at": "2026-06-19T...", "packageName": "CPEI_MFG", "kind": "Started" }
  ]
}
```
- App active: `status`/`pid`/`lastExit` lấy từ `ProcessSupervisor.GetSnapshot()` + `GetOperationalStatus()`.
- App không active: `status = "Idle"` (đã deploy, chưa chạy). `status = "Updating"` khi `deploy != null`.
- **`deploy`**: agent đọc tiến trình `InstallationJob` đang chạy cho package (gating: launcher disable **Run** khi `deploy != null`, giống `IsRunnable` của UIStore). `iconBase64`/`recentEvents`: phần feature-parity với UIStore.

### 4.2 `run` — `{ "op": "run", "packageId": "..." }`
- Tra catalog → `LaunchAndSuperviseAsync(LaunchRequest từ CatalogApp)` → supervisor swap sang app này, set active.
- Response: `{ "ok": true, "message": "running CPEI_MFG", "status": { ...như /status... } }`.

### 4.3 `stop` — `{ "op": "stop", "packageId": "..." }`
- Guard `packageId == activePackageId` → `RequestStopAsync` (dừng + quarantine, không auto-restart).

### 4.4 `restart` — `{ "op": "restart", "packageId": "..." }`
- Guard active → `RequestRestartAsync(gracefulFirst: true)`.

> Chọn app khác đang chạy = `run` app mới (supervisor tự stop cái cũ). Mỗi lúc chỉ 1 app chạy (đúng mô hình đã chốt).

### 4.5 Real-time
MVP: launcher **poll `status` mỗi 1–2s** qua pipe (cực rẻ vì cục bộ). Đủ mượt cho đèn trạng thái. (Nâng cấp push qua connection giữ mở: để sau, không bắt buộc.)

---

## 5. Launcher (WPF) hiển thị / thao tác

> **Mục tiêu MVP: dựng lại feature-set operator của UIStore** (bỏ login/PC-allowlist/config-edit), UI sạch & chuyên nghiệp hơn. Cải tiến/đổi giao diện về sau.

- **Tray icon** đổi màu theo app active: 🟢 Running · ⚪ Idle/chưa chọn · 🔵 Updating · 🔴 CrashLoop/Error · 🟡 Maintenance · ⚫ mất kết nối agent.
- **Cửa sổ chính (từ tray):**
  - Header: Hostname + ComputerId (rút gọn) + đèn "Agent connected" + "Server reachable" + Maintenance badge.
  - **List card mỗi app** (catalog): **icon app** + tên + version + đèn trạng thái + badge "Active/Running". Card active hiện PID/uptime + lastExit nếu lỗi. Badge **"đang cập nhật"** (+ % nếu Downloading) khi `deploy != null`; badge **"vừa cập nhật vX"** khi `justUpdatedTo`.
  - Nút theo card: **Run** (app chưa active; **disable khi đang Updating**) / **Stop** + **Restart** (app đang active).
  - **Pane "Sự kiện gần đây"**: vài dòng started/stopped/updated/crashed (thay LogLines của UIStore).
- **Offline-tolerance:** server down → vẫn chọn/chạy/đổi app đã deploy (đi qua agent cục bộ). Pipe down (agent dừng) → disable nút, báo "Agent offline".
- **KHÔNG** kiosk full-screen ở MVP (follow-up tùy chọn, không login).

### 5.1 Đối chiếu feature UIStore (parity operator-facing)
| UIStore | MVP launcher |
|---|---|
| List nhiều app/trạm | ✓ catalog |
| Đèn Running/Standby/Updating/Extracting/Failed | ✓ Running/Idle/Updating/CrashLoop/Maintenance |
| Launch / Close | ✓ Run / Stop (+ Restart mới) |
| Gate Run khi đang update (`IsRunnable`) | ✓ disable Run khi `deploy != null` |
| Icon app | ✓ `iconBase64` |
| HasNewVersion / "vừa cập nhật" | ✓ `justUpdatedTo` badge |
| Log pane | ✓ pane sự kiện gần đây |
| App Info (FW/FCD/BOM/FTU) | ⏳ GAP-5 (chỉ packageName+version ở MVP) |
| Operator login / PC allowlist / config-edit | ❌ bỏ (scope) — config-edit đã ở GAP-1 web |

---

## 6. Auto-start launcher trong session operator (đã chốt: agent bootstrap)

- Agent dùng `InteractiveProcessLauncher.Start` bung `MProjectLauncher.exe` vào active console session khi: (a) agent khởi động, (b) đổi console session (đăng nhập mới / đổi ca). **Idempotent**: chỉ start nếu chưa có instance trong session đó → tự bật lại nếu operator tắt.
- Fallback phụ: Startup-shortcut per-user (đề phòng agent chưa kịp).

## 7. Session-0 isolation & vòng đời
- Agent (s0) không vẽ UI ⇒ launcher bắt buộc ở session operator; named pipe xuyên session OK.
- Đổi ca → `WTSGetActiveConsoleSessionId` đổi → agent bung launcher mới vào session mới; launcher cũ chết theo session logoff.
- MVP phục vụ **active console session** (đúng như `InteractiveProcessLauncher` hiện tại); RDP đa-session để sau.

## 8. Bảo mật
- Pipe ACL local; op chỉ Run/Stop/Restart trên **app đã được gán + đã deploy** (không nhận path/exe tùy ý, không config, không token, không deploy).
- Launcher không biết server, không giữ creds. Không login, không PII operator.

---

## 9. Kế hoạch triển khai (nhỏ gọn, giữ test xanh)

> **Tiến độ (2026-06-19):** L-0 → L-2 ✅; L-3 **slice `status`** ✅; **L-4 `run/stop/restart`** ✅; **L-5 auto-start launcher** ✅; **L-6 WPF launcher MVP** ✅ DONE (agent test 80/80; launcher net48 build OK qua VS MSBuild). Còn: L-3 slice 2 (deploy-progress + recentEvents + icon/justUpdated) + L-7 (đóng gói + thử trạm thật). Chi tiết: xem cuối §9.

1. **L-0** ✅ Project `MProjectAgent.Ipc.Contracts` (**target `netstandard2.0`**; DTO: `StatusResponse`, `CatalogAppDto`, `OpRequest`, `OpResult` + nested `MachineInfoDto`/`LastExitDto`/`DeployProgressDto`/`RecentEventDto`, `IpcOps`) — agent (net8) đã `ProjectReference` (chưa có .sln nên ref từ agent để build chung); dependency-free.
2. **L-1** ✅ `AppCatalogStore` (atomic json `runtime\catalog.json`, backup-recovery như `RuntimeStateStore`) + model `CatalogApp` + ghi catalog trong `JobExecutor` sau deploy. Unit test `AppCatalogStoreTests`.
3. **L-2** ✅ Đổi policy launch trong `JobExecutor` theo §2 (active-pointer aware) — tách `LaunchPolicy.Decide` (pure) + `ProcessSupervisor.GetActivePackageId()` (read-only, additive). Test `LaunchPolicyTests`: 1 app auto-run; nhiều app chờ chọn; deploy version mới app active → relaunch; app khác active → leave.
4. **L-3** `IpcServer` (named pipe + `status`) — **slice `status` ✅ DONE**: `IpcServer : BackgroundService` (named pipe + ACL `BuiltinUsers`+`LocalSystem`, JSON-line, op `status`/unknown), `AgentStatusProvider` (map catalog+supervisor+token→`StatusResponse`), `ServerReachabilitySignal` (time-based, set ở heartbeat OK), DI + `IpcProtocol.PipeName` (shared contracts). Test `AgentStatusProviderTests` + `IpcServerTests` (round-trip pipe thật). **CÒN LẠI (chưa làm):** `deploy` per-package (đọc InstallationJob đang chạy) + `recentEvents` ring buffer + `iconBase64`/`justUpdatedTo` → để slice 2.
   - ACL types (`NamedPipeServerStreamAcl`/`PipeSecurity`) **in-box net8** (ref-pack), KHÔNG cần NuGet `System.IO.Pipes.AccessControl` → offline-safe.
   - Pipe single-instance (1 client/lúc), JSON-line, đọc-1-dòng/trả-1-dòng trong vòng lặp; op lỗi bất ngờ trả `OpResult.Fail` (chừa OCE) thay vì drop connection.
5. **L-4** Op `run/stop/restart` ✅ DONE: `Services/IpcRequestHandler.cs` (tách khỏi `IpcServer` — server giờ thuần transport, delegate `HandleAsync`). `run {packageId}`→tra catalog→`LaunchAndSuperviseAsync` (JobId=Guid.Empty vì operator-initiated, không phải deploy job); `stop`/`restart` guard `packageId==activePackageId`→`RequestStopAsync`/`RequestRestartAsync(gracefulFirst:true)`. Mỗi op OK trả `StatusResponse` mới (refresh 1 round-trip). Test `IpcRequestHandlerTests` (validation + guard, KHÔNG spawn process) + assertion `stop` non-active trong `IpcServerTests`.
   - Op lỗi runtime (vd `run` exe đã mất) → `IpcServer` dispatch-catch trả `OpResult.Fail("op 'run' failed")` an toàn (chừa OCE). Stale catalog (app gỡ nhưng còn entry) → dọn bằng **R4** (deferred).
   - **Chưa làm (gate Run khi Updating):** agent chưa hard-block `run` lúc deploy đang chạy; UI (L-6) gate bằng `deploy != null` + slice-2 cấp field `deploy`. Không có rủi ro corrupt (deploy ghi file atomic tmp+move).
6. **L-5** Auto-start launcher từ agent ✅ DONE: `Services/LauncherBootstrapper.cs` (`BackgroundService`, mỗi 30s đảm bảo `MProjectLauncher.exe` cạnh agent đang chạy — idempotent qua `Process.GetProcessesByName`; chỉ start khi có console session qua `InteractiveProcessLauncher.HasActiveConsoleSession()` mới thêm; **no-op khi launcher chưa bundle** → deploy agent-only không ảnh hưởng). DI `AddHostedService`.
7. **L-6** `MProjectLauncher` (WPF net48) — **MVP ✅ DONE, build OK**: project `MProjectLauncher/` (SDK-style net48, `UseWPF`+`UseWindowsForms`, ref contracts + Newtonsoft 13.0.1 cached). `IpcClient` (named pipe, net48 không có ConnectAsync → `Connect` sync trên `Task.Run`, Newtonsoft camelCase khớp wire STJ). `MainWindow` (header hostname/computerId/server-reachable/maintenance + list card đèn-status/badge ACTIVE/PID + Run‖Stop+Restart, **gate Run khi `deploy!=null`**, poll 1.5s, in-place update tránh flicker). Tray `System.Windows.Forms.NotifyIcon` (in-box; tooltip theo app active; close→hide, Thoát ở menu). Offline-tolerant: pipe down → "Agent offline" + disable nút.
   - **Build:** VS MSBuild (`VS/18/Community`) — `dotnet build` không build được WPF net48 (markup compiler ở MSBuild Framework). Lệnh: `MSBuild MProjectLauncher.csproj -restore -p:Configuration=Debug`.
   - **MVP cắt bớt (đúng tầm data agent đang trả):** icon app, pane "sự kiện gần đây", badge "vừa cập nhật vX", tray-icon đổi màu (đang dùng tooltip text). Bổ sung khi L-3 slice 2 cấp `iconBase64`/`recentEvents`/`justUpdatedTo` + tô màu icon runtime.
   - **Chưa verify E2E** (launcher ↔ agent đang chạy thật, cần desktop): seam rủi ro nhất = JSON Newtonsoft↔STJ (đã thiết kế khớp camelCase; DTO toàn type cơ bản) → là item chính của L-7.
8. **L-7** **Phân phối + đóng gói** (xem §11): bundle launcher vào agent install + `ServiceInstaller` drop cạnh agent. Thử trên 1 trạm thật: 1-app auto-run, 2-app chọn/đổi, đang-update gate Run, mất mạng, đổi ca, reboot giữ active. **+ verify E2E launcher↔agent (JSON wire).**

**Giữ xanh:** backend 424/424 + FE `tsc -b` (GAP-2 không đụng); thêm test cho `AppCatalogStore`, policy launch mới, `IpcServer`.

### Chi tiết cụm L-0 → L-2 đã code (2026-06-19) — cho review

**File mới:**
- `MProjectAgent.Ipc.Contracts/` — `MProjectAgent.Ipc.Contracts.csproj` (netstandard2.0, 0 dep) + `IpcContracts.cs` (toàn bộ wire DTO §4, camelCase do serializer ở tầng IPC quyết định, POCO serializer-agnostic).
- `MProjectAgent/Models/CatalogApp.cs` — model agent-internal (giữ `ExePath`/`InstallRoot`, KHÔNG phơi qua IPC vì lý do bảo mật §8).
- `MProjectAgent/Storage/AppCatalogStore.cs` — atomic write + backup recovery, semaphore bao read-modify-write cho `UpsertAsync`; KHÔNG refactor chung với `RuntimeStateStore` (design dặn không đụng nó).
- `MProjectAgent/Services/LaunchPolicy.cs` — `enum LaunchDecision {AutoRun, Relaunch, Wait, LeaveActive}` + `Decide(activePackageId, deployedPackageId, deployedAppCount)` (pure) + `ShouldLaunch()`.
- Tests: `LaunchPolicyTests`, `AppCatalogStoreTests`.

**File sửa:**
- `MProjectAgent/Services/JobExecutor.cs` — `TryLaunchWithSupervisorAsync` → `RecordCatalogAndLaunchAsync`: ghi catalog rồi áp `LaunchPolicy` thay cho auto-launch vô điều kiện (điểm §0). App không có entry point hợp lệ → không catalog, không launch (giữ parity).
- `MProjectAgent/Services/ProcessSupervisor.cs` — thêm `GetActivePackageId()` (read-only, không đổi hành vi; active = `ExePath` non-empty → `PackageId`; sống qua StopApp-quarantine & reboot).
- `MProjectAgent/Program.cs` — DI `AddSingleton<AppCatalogStore>()`.
- `MProjectAgent/MProjectAgent.csproj` — `ProjectReference` tới contracts.

**Quyết định nhỏ khi code:**
- "Số app để xét R1 vs R2" = `catalog.Count` sau upsert (đúng §2). Hệ quả đã-biết & chấp nhận: trạm nhiều-app, app deploy *đầu tiên* lúc đó catalog=1 → auto-run & trở thành active; app sau KHÔNG đổi active đang chạy (khớp ghi chú §2). R2 (chờ operator) chỉ xảy ra khi catalog>1 mà active=null (vd app đầu không entry point / auto-run lỗi / active bị clear).
- **R4** (active bị xóa assignment → clear active) CHƯA code: nó cần trigger từ manifest/inventory reconciliation, không phải từ deploy. Để dành (ngoài cụm L-0→L-2).
- **Chưa wire IPC**: contracts đã có nhưng `IpcServer`/map DTO để ở L-3. Agent đã ref contracts để build chung (offline-safe vì 0 NuGet dep).
- **Lưu ý known-limit (pre-existing):** deploy version mới của app *đang chạy* sẽ kẹt ở bước ghi đè exe đang bị lock (SharingViolation) — đây là gap "wait-for-idle" cũ, không thuộc scope cụm này.

**Sửa sau review (2026-06-19, agent test 72/72):**
- **#1 (regression):** catalog upsert/launch sau `CompleteAsync` giờ bọc trong try/catch best-effort (`when (ex is not OperationCanceledException)`) — IO catalog lỗi KHÔNG còn làm job đã Completed bị báo lại Failed; cancellation vẫn propagate.
- **#2 (efficiency):** `AppCatalogStore.UpsertAsync` trả về list đã cập nhật → `JobExecutor` lấy count trực tiếp, bỏ `LoadAsync` đọc đĩa lần 2. Test `Upsert_ReturnsUpdatedCatalog_ForCaller`.
- Note test: nhánh "#1 catalog lỗi → job vẫn Completed" KHÔNG thêm integration test riêng (JobExecutor cần mock cả ServerClient/cache/installer — chưa có harness, dựng sẽ over-engineer); đảm bảo là **structural** qua catch-when-not-OCE.

---

## 11. Phân phối launcher (cách đưa launcher lên PC trạm)

Tham khảo fleet tools thực tế: **agent cài 1 lần (imaging/MSI); UI component đi kèm agent HOẶC cập nhật qua kênh self-update riêng — KHÔNG bỏ vào catalog phần mềm payload.**

- **MVP = bundle với agent (ĐÃ CHỐT):** agent install package chứa luôn `MProjectLauncher.exe`; `ServiceInstaller` (hoặc bước cài) drop nó cạnh agent. Agent đã tự bung + giữ-sống launcher (§6). Ưu: có UI ngay cả offline, không chicken-egg, đơn giản, tin cậy.
- **Sau = GAP-4 mở rộng (component self-update):** thêm kênh agent kéo/cập nhật **component hạ tầng** (agent + launcher) tập trung, KHÔNG re-image — đây là "cập nhật tự động tập trung" mong muốn, tách khỏi catalog test-app.
- **TRÁNH:** coi launcher như 1 app trong pipeline catalog (B): launcher tự xuất hiện trong list nó hiển thị, operator có thể "stop" chính nó, máy mới offline không có UI, lẫn infra với payload.

> Dù chọn cách nào, cơ chế **agent bootstrap + giữ-sống launcher (§6) không đổi** — chỉ khác ở "ai đặt binary lên đĩa".

---

## 10. Ghi chú nhỏ khi code (không chặn)

- **Tên/version hiển thị:** dùng `PackageName` + `VersionNumber` từ manifest job (đã có), lưu vào `CatalogApp` lúc deploy → khỏi gọi server, vẫn đẹp.
- **Build/test:** `MProjectAgent.Ipc.Contracts` (netstandard2.0) + `MProjectLauncher` (net48 WPF) thêm vào solution. Lưu ý launcher net48 cần MSBuild/.NET Framework dev pack để build (CI phải có) — hoặc tách solution riêng cho launcher. Giữ test agent xanh; thêm test cho `AppCatalogStore`, policy launch mới, `IpcServer`.
- **Đóng gói:** launcher net48 = vài MB, copy cạnh agent self-contained. Agent bung launcher qua `InteractiveProcessLauncher`.
- **PipeSecurity ACL** set ở phía agent (net8) — cần package `System.IO.Pipes.AccessControl`.
