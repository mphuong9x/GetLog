# Agent Note — Xử lý tình huống thực tế khi triển khai trong nhà máy

> Tài liệu tư vấn cho 3 tình huống vận hành thực tế: worker tắt phần mềm test, mất điện đột ngột, và rollout version cho toàn bộ PC.

---

## Tóm tắt nhanh

| Tình huống | Mức cần thiết | Lý do |
|---|---|---|
| 1. Công nhân tắt phần mềm test | **Cao** (xảy ra hàng ngày) | Worker bấm nhầm, Alt-F4, Task Manager → mất data test |
| 2. Mất điện đột ngột | **Trung bình–Cao** | Hiếm hơn nhưng hậu quả nặng: file deploy dở dang, DB lệch trạng thái với PC |
| 3. Update version cho toàn bộ PC | **Cao** (vận hành) | Đây là use case chính của hệ thống, làm sai sẽ dừng cả line sản xuất |

---

## 1) Công nhân tắt phần mềm test khi đang chạy

**Bản chất vấn đề:** Test app được launch bằng `TryLaunchEntryPoint` (xem `MProjectAgent/Services/JobExecutor.cs:174`) chạy dưới quyền user — worker hoàn toàn có thể đóng nó. Agent service thì không sao (chạy LocalSystem).

### Cách xử lý — chọn theo độ "khó tính" của bạn

**Mức tối thiểu (recommend làm trước):**
- **Watchdog process**: Agent track PID của test app. Khi process exit, phân biệt:
  - Exit code = 0 → coi như completed bình thường
  - Exit code ≠ 0 hoặc bị kill → báo về server như `ComputerOperationalStatus.Error`, ghi vào `PcInstallationRecord` field `LastExitReason`
- **Auto-restart policy**: nếu app bị tắt ngoài ý muốn, restart N lần với backoff (3 lần, mỗi 30s). Quá ngưỡng → mark Error, chờ kỹ thuật can thiệp.
- **Reporting**: thêm enum `Killed`/`UnexpectedExit` để dashboard biết phân biệt với "test xong bình thường".

**Mức "khóa máy" (factory thường dùng):**
- **Kiosk mode / Shell replacement**: Group Policy chặn Task Manager, Alt-F4, Win+D. Trên PC test thường có account riêng (user `operator`) không có quyền tắt.
- **AppLocker / WDAC** cho phép chạy đúng test app + agent, chặn cmd, explorer.
- **UI overlay**: test app full-screen + tự bắt sự kiện close, hỏi password mới cho đóng.

### Cần chú ý
- Phân biệt **tắt có chủ đích** (kỹ thuật bảo trì) vs **tắt do worker bấm nhầm** → nên có nút "maintenance mode" để kỹ thuật ra khỏi auto-restart loop.
- Watchdog không nên restart vô hạn → tránh loop khi app có bug crash ngay khi start.

---

## 2) Mất điện đột ngột

### Đã làm tốt
`InstallDirectoryService.DeployFile` (xem `MProjectAgent/Services/InstallDirectoryService.cs:70`) deploy qua file `.tmp` rồi `File.Move(overwrite: true)` — atomic per-file trên NTFS. Cache có hash check.

### Còn lỗ hổng
- **Job state ở Backend**: Nếu PC mất điện lúc `Installing`, server vẫn thấy `InstallationJobStatus.Installing` → mãi mãi stuck.
- **Partial deploy**: Một số file đã ghi, một số chưa. Hash check khi resume sẽ xử lý được, nhưng cần đảm bảo agent **luôn re-run job từ đầu** khi khởi động lại.
- **Cache index**: `CacheIndex` có thể out-of-sync với file thực tế nếu crash đúng lúc đang `IncrementRefAsync`.
- **DB ↔ disk**: `PcInstallationRecord` có thể bảo "đã cài v1.2" nhưng đĩa chỉ có v1.1.

### Cách xử lý

- **Server-side job timeout**: thêm cron — nếu job `Downloading`/`Installing` quá X phút mà không có `ProgressAsync`, mark `Failed (timeout)`. Agent khi khởi động lại sẽ poll thấy không có job → server tự enqueue lại nếu vẫn cần version đó.
- **Agent recovery on startup**:
  1. Đọc lại `CacheIndex`, verify hash từng file trong cache (lazy: chỉ verify khi dùng).
  2. So sánh DB record (`installed_version`) vs scan thực tế thư mục install. Lệch → gửi `ReconcileAsync` lên server, server quyết định re-deploy hay accept.
- **Write-ahead log / journal** cho deploy:
  - Trước khi bắt đầu deploy job → ghi 1 file `deploy.inprogress.json` với jobId + danh sách target.
  - Khi xong → xóa file. Crash giữa chừng → khởi động lại thấy file này → rollback (xóa file `.tmp` còn sót) rồi re-run.
- **Idempotency**: Code hiện đã idempotent (hash check skip), nhưng server cần biết "job này có thể được agent re-execute" — đặt cờ `IsResumable` trên job.
- **Hardware**: UPS cho PC test chạy 24/7 (cho phép graceful shutdown 30s); tắt write cache OS nếu critical.

### Cần chú ý
Đừng cố "phục hồi đến đúng trạng thái cũ" — phức tạp và dễ sai. Triết lý đúng là **"luôn convergence về desired state từ server"**: server biết PC này phải có version X → agent chỉ cần re-converge tới X. Đây là model của Kubernetes/Ansible.

---

## 3) Update version cho toàn bộ PC

Đây là phần đáng đầu tư công nhất. Đã có khái niệm `Station`, `StationSoftwareAssignment`, `Approvals` — nền tảng tốt. Vấn đề là **rollout strategy**.

### Có nên chia nhóm? — CÓ, bắt buộc
Lý do:
- Push 1 phát toàn bộ → nếu version mới có bug → cả nhà máy dừng → mất tiền rất nhiều.
- Mỗi line sản xuất có thể khác model → version phù hợp khác nhau.
- Ca làm việc khác nhau → maintenance window khác nhau.

### Chiến lược recommend (theo độ phức tạp)

**A. Wait-for-idle (must-have)**
- Khi server enqueue job update, agent **không apply ngay** nếu `ComputerOperationalStatus = Updating` hoặc test app đang chạy.
- Đặt thành `Pending`, đợi `Idle` → mới execute.
- Tránh trường hợp đang test 1 sản phẩm thì phần mềm tự update giữa chừng.

**B. Maintenance window (must-have)**
- Mỗi Station/Group có field `UpdateWindow` (vd: "23:00-05:00 daily" hoặc "Sunday 06:00-08:00").
- Update job chỉ trigger trong window đó.

**C. Canary / Rolling rollout (nên có)**
- Tạo khái niệm `RolloutWave`: Wave 1 (1-2 PC test) → 24h theo dõi → Wave 2 (10% line) → Wave 3 (toàn bộ).
- UI cho admin: chọn wave, theo dõi success rate, **dừng rollout** nếu lỗi > threshold.

**D. Grouping (recommend dùng `Station` + tags)**
- Group theo line (Line A/B/C), theo model, theo ca, theo môi trường (QA/Prod).
- `StationSoftwareAssignment` đã có sẵn — chỉ cần thêm UI bulk-assign theo group.

**E. Rollback (must-have với prod)**
- Giữ lại version cũ trong cache + `previous_install_root` ít nhất 7 ngày.
- Nếu version mới crash > N lần → agent tự revert hoặc gửi alert cho admin one-click revert.

**F. Health check sau update (nên có)**
- Sau khi launch test app version mới, đợi vd 60s mà không thấy heartbeat từ test app → coi như deploy failed → revert.

### Cần chú ý
- **Lock version** cho line đã qua kiểm định: 1 số ngành (medical, automotive, aerospace) yêu cầu re-validate khi đổi version → phải có cờ "pinned, cấm auto-update".
- **Audit trail** (đã có `Approvals` — tận dụng): ai duyệt rollout nào, khi nào.
- **Băng thông**: 100 PC cùng tải 500MB lúc 23:00 → nghẽn mạng. JobExecutor đã có `MaxDownloadConcurrency`, nhưng cần **server-side rate limit** (chỉ enqueue X job/phút), hoặc P2P/local mirror trong nhà máy.

---

## Chính sách MAC address cho self-announce

`Computer.MacAddress` có unique index **global** ở DB (`DBContext.cs` — không filter theo `IsDeleted`), nên 1 MAC chỉ được provision **đúng 1 lần trong lịch sử**, kể cả sau khi computer record bị soft-delete.

- Self-announce với MAC trùng record đã soft-delete → từ chối ngay với lỗi rõ ràng ("MAC addresses cannot be reused for self-announce once provisioned"), không để rơi xuống DB unique-violation.
- **Không có đường tự động re-provision lại MAC cũ.** Nếu PC thật sự cần dùng lại (đổi mainboard/NIC giữ nguyên MAC, hoặc tái sử dụng máy cũ): admin pre-register lại bằng enrollment-token flow (gắn vào computer record mới) — không xóa cứng (hard-delete) record cũ trừ khi chắc chắn không còn cần audit trail của nó.
- Lý do chọn "cấm vĩnh viễn" thay vì cho phép purge/reuse: đơn giản, không cần entity/migration mới, và tránh rủi ro 1 MAC bị gán nhầm cho 2 computer record khác nhau theo thời gian (gây nhiễu audit/lịch sử cài đặt).

---

## Những điểm khác cần chú ý

1. **Time skew**: PC mất điện 2 ngày bật lại, đồng hồ sai → token expire / TLS lỗi. Force NTP sync trước khi heartbeat.
2. **Offline tolerance**: Mạng nhà máy thường flaky. Agent đã có retry trong vòng poll loop, nhưng cần test scenario "offline 24h" có recover sạch không.
3. **Disk space**: `BlobCacheService` cần retention policy — không thì cache phình to vô hạn sau nhiều version.
4. **Observability**: Đã có `chart-grafana.json` — đảm bảo metrics có `job_duration`, `deploy_failures`, `unexpected_exits`, `version_drift_count` (số PC chưa cùng version với assignment).
5. **Security**: 401 path đã handle tốt (không tự re-announce). Nên thêm: enrollment token expire sau 24h, log audit khi token được sử dụng.
6. **Test môi trường**: Có 1 staging PC giống prod 100% để test rollout trước khi push thật.
7. **Manual override**: Admin phải có nút "force install ngay không cần đợi idle" cho hotfix security khẩn cấp — nhưng có audit.

---

## Thứ tự ưu tiên triển khai

Cả 3 vấn đề đều là **baseline cho production deployment system trong nhà máy**. Recommend thứ tự:

1. **Trước nhất**: Wait-for-idle + Server-side job timeout + Watchdog cho test app (giải quyết 1 & 2 phần lớn).
2. **Tiếp**: Maintenance window + Grouping bằng Station.
3. **Sau**: Canary rollout + Rollback + Health check.

Lý do bắt đầu với **Wait-for-idle + Server-side job timeout**: chi phí thấp mà giải quyết được lỗ hổng lớn nhất.

---

# Phụ lục A — Plan chi tiết: Xử lý tình huống Worker tắt phần mềm test

> Chi tiết hóa phương án cho **Vấn đề 1**. Tài liệu này dùng làm tham chiếu khi triển khai; chưa có code thật.

## A.0 Mục tiêu & Success Criteria

**Mục tiêu:**
- Agent **làm chủ vòng đời** của test app sau khi launch (hiện đang "fire-and-forget").
- Phát hiện được test app bị tắt **không mong muốn**, phân biệt với "thoát bình thường".
- Tự động restart theo policy có giới hạn, tránh restart-loop khi app thực sự crash.
- Báo cáo trạng thái runtime về server để dashboard/admin thấy.
- Cho phép kỹ thuật vào **maintenance mode** để bảo trì mà không bị watchdog cản trở.
- (Tùy chọn) Khóa máy ở mức OS để worker không can thiệp được.

**Success criteria (đo được):**
- ≥ 99% sự kiện exit của test app được report về server trong < 30s.
- Test app bị Alt-F4 / Task Manager kill → tự restart trong < 10s (trừ khi đã hit max retries hoặc maintenance mode).
- Crash-loop được detect và **dừng** trong vòng ≤ 3 lần thử, không spam restart.
- Maintenance mode toggle có hiệu lực tức thì (trong vòng 1 heartbeat).
- Không regression trên flow deploy hiện tại.

## A.1 Hiện trạng & Gap analysis

### Code hiện tại
- `JobExecutor.TryLaunchEntryPoint` (xem `MProjectAgent/Services/JobExecutor.cs:174`):
  - Launch bằng `Process.Start(psi)` với `UseShellExecute = true`.
  - Log PID rồi **return ngay**. Không giữ tham chiếu `Process`, không subscribe `Exited` event.
  - Comment đã ghi nhận **Session 0 issue**: khi agent chạy LocalSystem service, GUI app launch ra session 0 sẽ không thấy trên desktop của user.
- `AgentWorker.HeartbeatTickAsync` (xem `MProjectAgent/Services/AgentWorker.cs:93`): chỉ gửi `AgentVersion`, không kèm trạng thái runtime của test app.
- `AgentHeartbeatRequest` (xem `MProjectAgent/Models/AgentApiModels.cs:39`): chỉ có `IpAddress`, `AgentVersion`.
- `ComputerOperationalStatus`: Idle / Updating / Error — **không có** trạng thái cho "test app đang chạy / đã dừng".
- `ComputerLiveStatus`: chỉ Online/Offline của agent, không phản ánh test app.

### Gap
| Khả năng cần có | Hiện trạng |
|---|---|
| Track PID + lifecycle của test app | Không |
| Phát hiện exit + lý do | Không |
| Restart policy | Không |
| Báo cáo runtime state về server | Không |
| Maintenance mode | Không |
| Phân biệt "test app long-running" vs "run-once" | Không (mặc định long-running theo giả định) |
| Launch GUI đúng session của user | Có comment ghi nhận, chưa giải quyết |

## A.2 Kiến trúc giải pháp (high-level)

```
┌──────────────────────────────────────────────────────────────────┐
│ MProjectAgent (Windows Service, LocalSystem)                     │
│                                                                  │
│  AgentWorker ── HeartbeatTickAsync ──► Server  (kèm RuntimeState)│
│      │                                                           │
│      ├──► PollTickAsync ──► JobExecutor.ExecuteAsync             │
│      │                          │                                │
│      │                          ▼                                │
│      │                  ProcessSupervisor.LaunchAndSuperviseAsync│
│      │                          │                                │
│      │                          ▼ (Session 0 → User Session)     │
│      │                  SessionLauncher (CreateProcessAsUser)    │
│      │                          │                                │
│      │                          ▼                                │
│      │                   ◄── Test App (user session) ──►         │
│      │                          │                                │
│      │                          │ Process.Exited                 │
│      │                          ▼                                │
│      │                  ExitClassifier → RestartPolicy           │
│      │                          │                                │
│      └──► RuntimeStateStore ◄───┘ (persist PID + last exit)      │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
                              ▲
                              │ maintenance.flag (file ở StateDirectory)
                              │
                         Technician CLI
```

**Nguyên tắc thiết kế:**
1. **Single source of truth** cho runtime state: `RuntimeStateStore` (file JSON ở `StateDirectory`, kèm `.bak` fallback và metric lỗi load). Persist mỗi khi state đổi → agent restart không mất context.
2. **Idempotent recovery**: agent khởi động lại đọc state file → nếu có PID đang sống thì re-attach, nếu không thì decide relaunch hay không dựa trên job state.
3. **Stateless server (cho phần này)**: server chỉ là kho hiển thị + nguồn lệnh (maintenance mode flag). Logic restart nằm hoàn toàn ở agent.
4. **Fail-open cho deploy**: nếu Supervisor lỗi, không được làm fail toàn bộ job deploy (chỉ log + report).

## A.3 Phân tách thành Milestones

| MS | Tên | Phụ thuộc | Kết quả |
|---|---|---|---|
| **M1** | Process tracking & exit detection | — | Agent biết test app sống/chết, log đầy đủ |
| **M2** | Restart policy + maintenance mode | M1 | Auto-restart có giới hạn, kỹ thuật can thiệp được |
| **M3** | Server reporting + dashboard | M1 | Admin thấy trạng thái real-time |
| **M4** ✅ DONE | Session-0 launch fix (`CreateProcessAsUser`) | — (song song được) | GUI app hiện đúng desktop user |
| **M5** | OS-level hardening (kiosk, AppLocker) | — | Worker khó tắt; mức operational, không cần code app |

> M1+M2+M3 là core; M4 là technical debt cần xử lý; M5 là customer-side IT, ngoài scope code nhưng phải có hướng dẫn.
>
> **M4 đã hoàn thành**: `MProjectAgent/Services/InteractiveProcessLauncher.cs` implement `CreateProcessAsUser` (P/Invoke `WTSEnumerateSessions` + `WTSQueryUserToken` + `DuplicateTokenEx`), được `ProcessSupervisor.StartProcessAsync` gọi qua `InteractiveProcessLauncher.Start(psi)`.

## A.4 Chi tiết M1 — Process Tracking & Exit Detection

### A.4.1 File thay đổi (Agent)

**Mới:**
- `MProjectAgent/Services/ProcessSupervisor.cs` — quản lý 1 supervised process.
- `MProjectAgent/Services/SessionLauncher.cs` — wrapper `CreateProcessAsUser` (M4 nhưng nên stub sẵn).
- `MProjectAgent/Storage/RuntimeStateStore.cs` — persist `SupervisedProcessState`.
- `MProjectAgent/Models/SupervisedProcessState.cs` — DTO state.
- `MProjectAgent/Models/ExitReason.cs` — enum.

**Sửa:**
- `JobExecutor.cs:174` — `TryLaunchEntryPoint` chuyển sang gọi `ProcessSupervisor.LaunchAndSuperviseAsync`.
- `Program.cs` — DI registration cho `ProcessSupervisor`, `RuntimeStateStore`, `SessionLauncher`.

### A.4.2 Enum `ExitReason`

```csharp
public enum ExitReason
{
    Unknown = 0,
    NormalExit = 1,         // exit code = 0 (hoặc trong allowlist của version)
    NonZeroExit = 2,        // exit code != 0, không có dấu hiệu kill bên ngoài
    KilledExternally = 3,   // bị TerminateProcess (Task Manager, taskkill /f) → exit code thường 1 hoặc 0xC000_xxxx
    WindowClosed = 4,       // user đóng cửa sổ chính (WM_CLOSE) — app graceful shutdown
    Crashed = 5,            // exit code là 0xC0000005 (AV), 0xE0434352 (.NET unhandled), v.v.
    AgentRequested = 6,     // chính agent ra lệnh dừng (update, maintenance)
    HostShuttingDown = 7    // máy đang shutdown
}
```

**Heuristics phân biệt (Windows):**
- Exit code `0xC000_xxxx` → `Crashed`.
- Exit code `1` + không có dấu console log "shutting down" gần đó → coi như `KilledExternally` (rough; không hoàn hảo).
- Có hook `IsWindowVisible` polling, hoặc subscribe `SystemEvents.SessionEnding` để biết `HostShuttingDown`.
- App nên expose **exit code convention**: `0` = ok, `2` = expected stop, để Supervisor whitelist không restart.

### A.4.3 `SupervisedProcessState` (persist JSON)

```csharp
public sealed class SupervisedProcessState
{
    public Guid JobId { get; set; }
    public Guid PackageId { get; set; }
    public Guid VersionId { get; set; }
    public string ExePath { get; set; } = null!;
    public int? Pid { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ExitedAt { get; set; }
    public int? ExitCode { get; set; }
    public ExitReason ExitReason { get; set; }
    public int RestartCount { get; set; }
    public DateTimeOffset? RestartWindowStart { get; set; }
    public bool MaintenanceMode { get; set; }
    public EntryPointMode Mode { get; set; } // LongRunning | RunOnce
}

public enum EntryPointMode { LongRunning = 0, RunOnce = 1 }
```

> Lưu tại `{StateDirectory}\runtime\supervised.json`. Ghi atomic (write tmp + rename).

### A.4.4 `ProcessSupervisor` — skeleton

```csharp
public sealed class ProcessSupervisor : IAsyncDisposable
{
    private Process? _process;
    private SupervisedProcessState _state = new();
    private CancellationTokenSource? _watchCts;
    private readonly RuntimeStateStore _store;
    private readonly SessionLauncher _launcher;
    private readonly SupervisorOptions _opts;
    private readonly ILogger<ProcessSupervisor> _log;

    public SupervisedProcessState Snapshot() => _state.DeepCopy();

    public async Task LaunchAndSuperviseAsync(LaunchRequest req, CancellationToken ct)
    {
        if (!_opts.Enabled) { _launcher.LaunchLegacy(req); return; }

        await StopIfAnyAsync(ExitReason.AgentRequested, ct);

        _state = new SupervisedProcessState
        {
            JobId = req.JobId, PackageId = req.PackageId, VersionId = req.VersionId,
            ExePath = req.ExePath, Mode = req.Mode, RestartCount = 0
        };
        await _store.SaveAsync(_state, ct);
        await StartProcessAsync(ct);
    }

    private async Task StartProcessAsync(CancellationToken ct)
    {
        _process = _launcher.StartInActiveUserSession(_state.ExePath);
        _state.Pid = _process.Id;
        _state.StartedAt = DateTimeOffset.UtcNow;
        _state.ExitedAt = null; _state.ExitCode = null; _state.ExitReason = ExitReason.Unknown;
        await _store.SaveAsync(_state, ct);

        _watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = WatchAsync(_process, _watchCts.Token);
    }

    private async Task WatchAsync(Process p, CancellationToken ct)
    {
        try { await p.WaitForExitAsync(ct); } catch (OperationCanceledException) { return; }

        _state.ExitedAt = DateTimeOffset.UtcNow;
        _state.ExitCode = p.ExitCode;
        _state.ExitReason = ExitClassifier.Classify(p.ExitCode, _state);
        await _store.SaveAsync(_state, CancellationToken.None);

        if (ShouldRestart(_state))
        {
            _state.RestartCount++;
            await Task.Delay(BackoffFor(_state.RestartCount), ct);
            await StartProcessAsync(ct);
        }
    }
    // … StopIfAnyAsync, ShouldRestart, BackoffFor, recovery on agent startup …
}
```

### A.4.5 Cấu hình mới — `SupervisorOptions`

Trong `AgentOptions.cs`:

```csharp
public sealed class SupervisorOptions
{
    public const string SectionName = "Supervisor";
    public bool Enabled { get; set; } = true;
    public int MaxRestarts { get; set; } = 3;
    public int RestartWindowSeconds { get; set; } = 300;       // 5 phút
    public int[] BackoffSeconds { get; set; } = { 10, 30, 60 };
    public int ConsiderHealthyAfterSeconds { get; set; } = 60; // reset RestartCount
    public string MaintenanceFlagPath { get; set; } =
        @"C:\ProgramData\MProjectAgent\maintenance.flag";
    public int[] NormalExitCodes { get; set; } = { 0 };
    public bool RestartOnWindowClose { get; set; } = true;     // worker bấm X → restart
}
```

Bổ sung vào `appsettings.json` section `Supervisor`.

### A.4.6 Recovery on agent startup

Trong `AgentWorker.ExecuteAsync` trước khi vào loop:

1. `RuntimeStateStore.LoadAsync()` → nếu có state:
   - Nếu `Pid` còn sống (`Process.GetProcessById` không throw, và `ProcessName` match) → **re-attach**: subscribe lại `Exited`.
   - Nếu PID đã chết và `Mode == LongRunning` và `ExitedAt` gần đây → tiếp tục restart policy.
   - Nếu `Mode == RunOnce` → không relaunch, chỉ giữ state để báo cáo.
2. Nếu không có state nhưng có job `Completed` gần đây → coi như chưa launch lần nào, để job poll loop tự xử.

## A.5 Chi tiết M2 — Restart Policy & Maintenance Mode

### A.5.1 Restart logic

Pseudo-code:

```
ShouldRestart(state):
    if state.MaintenanceMode: return false
    if state.Mode == RunOnce: return false
    if state.ExitReason in [NormalExit, AgentRequested, HostShuttingDown]: return false
    if state.ExitReason == WindowClosed and !opts.RestartOnWindowClose: return false

    now = UtcNow
    if state.RestartWindowStart == null or (now - state.RestartWindowStart) > opts.RestartWindowSeconds:
        state.RestartWindowStart = now
        state.RestartCount = 0   # reset cửa sổ

    return state.RestartCount < opts.MaxRestarts
```

### A.5.2 Crash-loop guard

Nếu `RestartCount >= MaxRestarts` trong cửa sổ:
- Báo về server: `OperationalStatus = Error`, `LastExitReason`.
- Ghi event `CrashLoopDetected` (M3).
- Dừng restart, **chờ admin can thiệp** hoặc maintenance mode được bật.

### A.5.3 Maintenance mode

**Cơ chế kích hoạt (3 đường):**
1. **Local file flag**: tạo file `maintenance.flag` ở `MaintenanceFlagPath` → Supervisor đọc khi quyết định restart. Kỹ thuật bật tay khi đứng trước máy.
2. **CLI command** trên agent: `MProjectAgent maintenance --on` / `--off` (tạo/xóa flag file). Thêm vào `MProjectAgent/Commands/`.
3. **Server-pushed**: heartbeat response trả về `MaintenanceMode: true` → agent áp dụng. Cho phép admin remote toggle.

**Hành vi khi maintenance ON:**
- Supervisor không auto-restart.
- Vẫn report exit events lên server.
- Vẫn cho phép deploy job mới (không block).
- Heartbeat báo `OperationalStatus = Maintenance` (cần thêm enum value).

### A.5.4 Enum cần mở rộng (Backend)

`ComputerOperationalStatus`:
```csharp
public enum ComputerOperationalStatus
{
    Idle = 0,
    Updating = 1,
    Error = 2,
    Running = 3,        // mới: test app đang chạy bình thường
    Maintenance = 4,    // mới: kỹ thuật bật maintenance, watchdog tạm dừng
    CrashLoop = 5       // mới: hit max restart, chờ can thiệp
}
```

> Lưu ý migration: enum đang là `int` trong DB → thêm value không phá. Phải update tất cả `switch` exhaustive.

## A.6 Chi tiết M3 — Reporting & Dashboard

### A.6.1 Extend `AgentHeartbeatRequest`

```csharp
public sealed class AgentHeartbeatRequest
{
    public string? IpAddress { get; set; }
    public string? AgentVersion { get; set; }
    public RuntimeStateReport? Runtime { get; set; }   // mới
}

public sealed class RuntimeStateReport
{
    public Guid? CurrentJobId { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public int? Pid { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public bool IsRunning { get; set; }
    public string OperationalStatus { get; set; } = "Idle"; // ánh xạ enum
    public LastExitInfo? LastExit { get; set; }
    public int RestartCountInWindow { get; set; }
    public bool MaintenanceMode { get; set; }
}

public sealed class LastExitInfo
{
    public DateTimeOffset ExitedAt { get; set; }
    public int? ExitCode { get; set; }
    public string ExitReason { get; set; } = "Unknown";  // ExitReason enum → string
}
```

### A.6.2 Extend `AgentHeartbeatResponse`

```csharp
public sealed class AgentHeartbeatResponse
{
    public string AssignmentState { get; set; } = null!;
    public string LiveStatus { get; set; } = null!;
    public string OperationalStatus { get; set; } = null!;
    public bool MaintenanceMode { get; set; }   // mới: server-pushed
    public RestartCommand? Command { get; set; } // mới: optional "kill/restart now"
}
```

### A.6.3 Backend changes

- `Computer` entity hoặc **new** `ComputerRuntimeStatus` (1:1):
  - `IsTestAppRunning bool`
  - `CurrentVersionId Guid?`
  - `CurrentPid int?`
  - `CurrentStartedAt DateTimeOffset?`
  - `LastExitAt DateTimeOffset?`
  - `LastExitCode int?`
  - `LastExitReason string?`
  - `RestartCountInWindow int`
  - `MaintenanceMode bool`
  - `UpdatedAt DateTimeOffset` (server-side)
- Heartbeat handler: nhận `RuntimeStateReport` → upsert `ComputerRuntimeStatus`.
- Audit event `TestAppExitEvent` table cho mỗi exit (PackageId, VersionId, ExitCode, ExitReason, OccurredAt). Dùng cho phân tích sau.

### A.6.4 Frontend

Trang chi tiết Computer:
- Badge: "Running v1.2.3 (PID 1234, 2h30m)" / "Stopped 5m ago — KilledExternally" / "Crash loop — 3/3 restarts" / "Maintenance".
- Bảng "Recent exits" (10 lần gần nhất).
- Nút "Maintenance mode: ON/OFF" (gọi API → server set flag → heartbeat tới push xuống agent).
- Nút "Restart test app" (server queue command, heartbeat response trả về).

Trang dashboard tổng:
- Đếm: PCs running / stopped / crash-loop / maintenance — chia theo Station.

## A.7 Chi tiết M4 — Session-0 Launch Fix

**Vấn đề:** Agent service chạy `Session 0`. `Process.Start` từ đó launch GUI app sẽ vô hình với user đăng nhập (`Session 1+`).

**Phương án:**

1. **`CreateProcessAsUser` từ token của session active** (recommend):
   - P/Invoke `WTSEnumerateSessions` → tìm session `WTSActive`.
   - `WTSQueryUserToken(sessionId, out hToken)`.
   - `DuplicateTokenEx(hToken, ..., TokenPrimary)`.
   - `CreateEnvironmentBlock(...)`.
   - `CreateProcessAsUserW(hToken, ..., CREATE_UNICODE_ENVIRONMENT, env, workDir, ...)`.
   - Wrap trong `SessionLauncher.StartInActiveUserSession(exePath)`.
2. **Helper user-mode launcher**:
   - 1 exe nhỏ `MProjectAgent.UserHelper.exe` auto-start khi user login (Run key hoặc Scheduled Task at logon).
   - Nó kết nối named pipe với agent service → service ra lệnh "launch app X" → helper exec trong session user.
   - **Đơn giản hơn `CreateProcessAsUser` rất nhiều**, an toàn hơn (không cần handle token), nhưng cần thêm 1 component và auto-login flow.

Chọn **(2)** nếu thời gian ngắn; **(1)** nếu cần triệt để + không muốn thêm process.

**Lưu ý:**
- Khi không có user đăng nhập (PC vừa boot, chưa ai login) → cả 2 cách đều không launch được. Phải đợi login (M4 phương án 2 tự nhiên handle; M4 phương án 1 cần retry khi `WTSQueryUserToken` thất bại).
- Auto-login operator account thường là chuẩn ở factory PC — phối hợp với M5.

## A.8 Chi tiết M5 — OS-level Hardening (Kiosk)

**Ngoài scope code app**, nhưng phải có doc bàn giao IT:

1. Tạo local user `operator` (standard, không phải Administrator).
2. Auto-login bằng `Sysinternals Autologon` hoặc registry `DefaultUserName`/`DefaultPassword` (DPAPI).
3. Group Policy hoặc Local Policy:
   - Disable Task Manager: `User Configuration > Administrative Templates > System > Ctrl+Alt+Del Options > Remove Task Manager`.
   - Disable Run, cmd, registry editor.
   - Hide drives, lock taskbar.
4. AppLocker rule chỉ cho phép chạy:
   - Test app executable đã deploy.
   - `MProjectAgent.UserHelper.exe`.
   - System binaries cần thiết.
5. (Tùy chọn) Shell replacement: thay `explorer.exe` bằng custom launcher chỉ chạy test app.
6. Disable USB autorun, lock screen timeout phù hợp.

Bàn giao thành 1 file `docs/operator-kiosk-setup.md` + 1 GPO export file `.pol`.

## A.9 Edge cases & cách xử lý

| Edge case | Cách xử lý |
|---|---|
| Test app spawn child processes | Dùng **Windows Job Object** (`CreateJobObject` + `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) để parent chết → con chết. Hoặc track tree qua `Process.GetProcesses()`. |
| Test app exit nhanh (< 5s) lặp lại | Crash-loop guard (A.5.2) — sau N lần trong window, dừng + báo CrashLoop. |
| Test app là batch (chạy xong là exit) | Set `EntryPointMode = RunOnce` trên `SoftwareVersion`. Supervisor không restart, coi exit code 0 là OK. |
| Agent restart trong khi app đang chạy | Recovery (A.4.6): đọc state, re-attach bằng PID. |
| App treo (deadlock, không exit) | Health probe optional: TCP port / named pipe ping. Quá X giây không phản hồi → kill + restart. **Out of scope M1-M3**, đưa vào backlog. |
| Worker tắt máy cứng (giữ nút nguồn) | Vấn đề 2 (mất điện) sẽ xử. Supervisor chỉ thấy `HostShuttingDown` nếu kịp. |
| Nhiều test app cùng PC | Phase 1: chỉ 1 supervised process. Phase sau: dictionary `Map<PackageId, Supervisor>`. |
| User logout giữa chừng | Process trong user session sẽ bị OS kill → Supervisor thấy exit. Coi như `HostShuttingDown` variant. |
| Maintenance flag bị quên không tắt | Heartbeat hiển thị Maintenance liên tục → dashboard alert sau 24h. |
| `CreateProcessAsUser` lỗi quyền | Fallback log + report `OperationalStatus = Error`, không crash agent. |

## A.10 Testing strategy

### Unit tests (`MProjectAgent.Tests`)
- `ExitClassifierTests` — map exit code → ExitReason.
- `RestartPolicyTests` — `ShouldRestart` với mọi tổ hợp state.
- `RuntimeStateStoreTests` — atomic write, backup recovery sau corrupted file.
- `SupervisorOptionsBinderTests` — config bind.

### Integration tests
- Tạo `fake-test-app.exe` rất nhỏ (Console app) có 3 mode:
  - `--run-forever` (long running)
  - `--exit-with N` (exit code N sau X giây)
  - `--crash` (throw unhandled exception)
- Test scenarios:
  - Long-running app, gửi kill → Supervisor detect, restart 1 lần, sống tiếp.
  - Crash 3 lần liên tiếp → Supervisor dừng, state = CrashLoop.
  - `RunOnce` mode, exit 0 → không restart.
  - Maintenance flag bật → kill app → không restart.

### Manual E2E (factory-like)
1. Deploy 1 GUI test app thật lên 1 PC test.
2. Đóng cửa sổ bằng nút X → check log + dashboard.
3. Kill bằng Task Manager → check restart.
4. Lặp lại 5 lần liên tiếp → kiểm tra crash-loop detection.
5. Bật maintenance qua server UI → kill app → confirm không restart.
6. Reboot PC trong khi app đang chạy → confirm agent recovery sau khi boot.

### Chaos
- Kill `MProjectAgent` service trong khi test app đang chạy → restart service → đọc state → re-attach.
- Xóa `supervised.json` thủ công → agent log warning, không crash, đợi job mới.

## A.11 Sequencing & timeline ước lượng

| Tuần | Việc | Ai làm |
|---|---|---|
| 1 | M1: `ProcessSupervisor`, `RuntimeStateStore`, `ExitClassifier`, unit test | Backend dev (agent) |
| 2 | M2: Restart policy, maintenance flag, CLI command | Backend dev (agent) |
| 3 | M3: API contract, backend handler, migration `ComputerRuntimeStatus` | Backend dev (server) |
| 3-4 | M3: Frontend hiển thị + maintenance toggle | Frontend dev |
| 4 | M4: SessionLauncher (chọn phương án 1 hoặc 2) | Backend dev (agent) — có thể song song |
| 5 | Integration test + manual E2E trên PC staging | QA |
| 6 | M5: Doc kiosk + GPO bàn giao IT | Tech lead / DevOps |

**Ước lượng:** ~5-6 tuần với 1 backend dev FT + hỗ trợ. Có thể nén còn 3-4 tuần nếu gộp người.

## A.12 Risks & Mitigations

| Risk | Mức | Mitigation |
|---|---|---|
| Phân biệt "user đóng X" vs "Task Manager kill" không hoàn hảo trên Windows | Trung | Document hạn chế; khuyến nghị app team dùng exit code convention. |
| `CreateProcessAsUser` privilege phức tạp, dễ lỗi | Cao | Đi đường helper user-mode (M4 phương án 2) cho phase đầu. |
| Restart loop khi version mới bị bug → mọi PC cùng restart-loop | Cao | M2 crash-loop guard + cross-cutting với canary rollout (Vấn đề 3). |
| Worker tìm cách bypass kiosk lock | Trung | M5 layered (GPO + AppLocker + auto-login standard user). Audit log. |
| Heartbeat tăng payload size | Thấp | `RuntimeStateReport` ~200 bytes/heartbeat — không đáng kể. |
| State file corruption khi crash | Trung | Atomic write (tmp + rename), keep 1 backup `supervised.json.bak`. |
| Migration enum làm vỡ existing switch | Trung | Code search `ComputerOperationalStatus` exhaustively, add default branch. |

## A.13 Bàn giao / Definition of Done

- [ ] `ProcessSupervisor` + tests merge vào `master`.
- [ ] Config doc cập nhật trong `appsettings.json` mẫu.
- [ ] Backend migration `ComputerRuntimeStatus` apply trên staging.
- [ ] Heartbeat API contract update + Swagger.
- [ ] Frontend hiển thị runtime state + maintenance toggle hoạt động.
- [ ] E2E manual pass trên ≥ 1 PC staging với 4 scenarios (close, kill, crash-loop, maintenance).
- [ ] Doc kiosk + GPO bàn giao IT customer.
- [ ] Grafana dashboard có metrics `test_app_unexpected_exits_total`, `test_app_crash_loops_total`, `pcs_in_maintenance_total`.
- [ ] Runbook cho oncall: "PC ở trạng thái CrashLoop thì làm gì".

## A.14 Mở rộng tương lai (out of scope nhưng nên ghi nhận)

- Health probe (HTTP / named pipe) để detect app treo.
- Resource limit (CPU/RAM) trên test app qua Job Object.
- Auto-rollback khi version mới gây crash-loop > N PC (cross-cut với Vấn đề 3).
- Remote command "restart app now" qua heartbeat response (đã ghi placeholder ở A.6.2).
- Capture screenshot khi crash (giúp debug từ xa).

