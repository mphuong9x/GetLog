# Review 06 — Launcher IPC và giám sát tiến trình

Ngày review: 2026-09-09. Phạm vi và định dạng theo `docs/code-review/prompts/06-launcher-ipc-and-process-supervision.md`.

Repository revision: `db480dffe3372b72028848eaa62fbb47d7b141b7`. Prompt Git blob hash: `7bcda17ad2e57e6954ab1d172052512085b7a57e`. Review dựa trên source hiện tại; các file kết quả là phần bổ sung riêng của lần review này.

## Kết quả

Phát hiện **9 lỗi: 4 P1 và 5 P2**. Bốn lỗi đầu có fixture tái hiện trên mã hiện tại. Các lỗi còn lại được xác nhận bằng luồng mã; chưa có fixture desktop/multi-session cho chúng. Không sửa mã production, contract, project manifest hoặc checklist lịch sử.

| ID | Severity | Confidence | Category | Vấn đề |
| --- | --- | --- | --- | --- |
| L06-01 | P1 — High | High, có repro | confirmed defect | Run đồng thời có thể cùng chạy entry point của request thứ hai |
| L06-02 | P1 — High | High, có repro | confirmed defect | Hủy trong lúc attach để lại process chưa có watcher và durable PID |
| L06-03 | P1 — High | High, có repro | confirmed defect | HTTP timeout đầu tiên chấm dứt vòng health-check |
| L06-04 | P1 — High | High, có repro | confirmed defect | Attach nhầm process đã tồn tại, không kiểm session/nguồn khởi tạo |
| L06-05 | P2 — Medium | High, static | confirmed defect | Launcher ở session cũ ngăn bootstrap Launcher cho console mới |
| L06-06 | P2 — Medium | High, static | confirmed defect | UI cho xếp hàng nhiều Run trước response, gây khởi động lại ngoài ý muốn |
| L06-07 | P2 — Medium | High, static | confirmed defect | Fallback sang wrapper nhưng status/recovery vẫn kiểm đường dẫn watch |
| L06-08 | P2 — Medium | High, static | confirmed defect | Icon lớn làm mất toàn bộ status và response của thao tác |
| L06-09 | P2 — Medium | High, static | confirmed defect | Stop/restart bỏ qua kết quả thất bại và trả thông báo thành công |

P1 ở đây là nguy cơ chạy sai chương trình, mất giám sát hoặc dừng sai process trên trạm; không phải kết luận đã khai thác leo thang đặc quyền.

## Xác minh đã thực hiện

- Đọc `AGENTS.md`, `PROJECT_MEMORY.md`, `PROJECT_REVIEW_CHECKLIST.md`, đặc biệt IPC-001/AGT-001; đối chiếu lại với code. Không dùng trạng thái COMPLETED lịch sử làm bằng chứng cho các race mới.
- Đọc shared IPC source, manifests/fixtures; Agent/Launcher/test project references và runtime composition; Launcher `App`, `MainWindow`, `ViewModels`, transport, support capability/error handling, layout, QR; IPC server/authorizer/handler, status, supervisor, launch builder, interactive launcher, catalog/runtime stores, recent events và call sites liên quan.
- Kiểm tra `LauncherBootstrapper`, `ServiceInstaller`, đoạn package bundle và `scripts/e2e-wire-check.ps1` trong phạm vi vị trí executable, dependency và transport.
- Chạy nhóm test hiện có: **131 passed, 0 failed, 0 skipped**, target net8.0. Bao phủ IPC client/server/peer/protocol/wire/handler, launch policy, entry builder, recovery, runtime/catalog store, layout, QR và support UI policy.
- Chạy thêm bốn review probes: **4 passed, 0 failed, 0 skipped**. Các assertion chứng minh **hành vi lỗi hiện tại**, không phải regression tests khẳng định hệ thống đã đúng.
- Build Launcher net48 bằng Visual Studio MSBuild: exit code 0, không có warning/error trong output.
- Không chạy live bundle E2E: kiểm tra chỉ đọc thấy một `MProjectAgent` đang chạy; script dùng pipe cố định và chủ động từ chối trường hợp này để tránh nhận response từ Agent khác. Không stop service hiện hữu. Vì vậy không tuyên bố đã xác minh fresh bundle, LocalSystem/session 0 hoặc UI thật trong lần review này.

Lệnh test đã dùng:

```powershell
dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~Ipc|FullyQualifiedName~LaunchPolicy|FullyQualifiedName~LauncherLayout|FullyQualifiedName~EntryPointCommandBuilder|FullyQualifiedName~ProcessSupervisorRecovery|FullyQualifiedName~RuntimeStateStore|FullyQualifiedName~AppCatalogStore|FullyQualifiedName~MachineNameQrCode|FullyQualifiedName~SupportUiPolicy" --verbosity minimal
dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter FullyQualifiedName~LauncherReviewProbes --verbosity minimal
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' MProjectLauncher/MProjectLauncher.csproj /t:Build /p:Configuration=Debug /v:minimal /nologo /nr:false
```

Mã fixture được lưu kèm tại [Phụ lục mã tái hiện](06-launcher-ipc-and-process-supervision-probes.md). Khi chạy, file nằm tạm trong `MProjectAgent.Tests`; sau đó đã chuyển về thư mục báo cáo nên không còn được compile vào test project. Muốn tái hiện, lưu khối C# trong phụ lục thành file tạm `MProjectAgent.Tests/LauncherReviewProbes.cs` rồi chạy filter trên. Fixture dùng thư mục GUID trong temp, process riêng và HTTP handler giả không gọi mạng; chỉ dọn process/file do fixture tạo. Reflection chỉ dùng để đặt barrier/quan sát trạng thái private cho review. Khi sửa lỗi, cần đổi assertion sang hành vi mong muốn và đưa test thích hợp vào suite chính thức.

## Các đường gọi đã truy vết

| Trace bắt buộc | Luồng thực tế và kết luận |
| --- | --- |
| 1. WPF → IPC → UI | `MainWindow` tick 1,5 giây → `MainViewModel.RefreshAsync`/`SendOp` → `IpcClient.SendAsync` → semaphore theo client → một pipe/request → deserialize → `Apply` hoặc `GoOffline`. Có timeout và bound; semaphore không gộp refresh và không bỏ Run trùng. |
| 2. Server → peer → dispatch | Bốn `RunListenerAsync` → `CreatePipe` → bounded UTF-8 frame → authorize sau read → version/correlation validation → handler với linked deadline → bounded response. Chưa có khóa lifecycle chung giữa bốn handler. |
| 3. Run/stop/restart → state | Run lấy catalog theo GUID rồi gọi supervisor. Stop/restart kiểm active package trước khi gọi API không nhận package ID. Catalog/runtime lock chỉ bảo vệ I/O; snapshot lock không bảo vệ cả transition qua `await`. |
| 4. Launch → OS process | Luồng thật là `JobExecutor` → `LaunchPolicy` → supervisor; manual IPC Run đi thẳng supervisor. Supervisor → `EntryPointCommandBuilder` → `InteractiveProcessLauncher`; service dùng console user token, desktop `winsta0\\default`, working directory của entry. Sau đó attach/watch/probe/persist. `LaunchPolicy` không nằm bên trong supervisor và không phải mutex. |
| 5. Bundle → Launcher | Bootstrapper tìm `MProjectLauncher.exe` cạnh Agent. Package build net48 và copy exe/dll/config cùng thư mục, kiểm contracts/Newtonsoft/QRCoder và version chung. App dùng named mutex/event cho single instance; bootstrap kiểm desktop process chưa đúng active session. |

## Findings chi tiết

### L06-01 — Serialize toàn bộ lifecycle, không chỉ snapshot/I/O

- **Severity:** P1 — High.
- **Confidence:** High; đã tái hiện bằng `ConcurrentLaunches_BothStartSecondRequestsEntryPoint`.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/ProcessSupervisor.cs:119`, `:141`, `:198`; `IpcRequestHandler.cs:177`; `IpcServer.cs:83`.
- **Symbol:** `LaunchAndSuperviseAsync`, `StartProcessAsync`, `GuardActive`, `ExecuteAsync`.
- **Evidence:** `_state = newState` xảy ra trước `await _store.SaveAsync`; `StartProcessAsync` đọc lại global snapshot thay vì state riêng của request. Fixture giữ semaphore của store, đưa Run A và Run B cùng đến Save rồi thả: marker A không tồn tại, marker B được ghi hai lần. Hai lời gọi đều hoàn tất.
- **Finding:** hai mutation dùng chung `_state`, `_process`, watcher/probe và kill reason nhưng không có serialization/generation cho cả operation. Stop guard cũng tách rời thao tác lên active process. Watch cũ chỉ guard việc xóa PID, vẫn ghi exit metadata vào global state.
- **Realistic failure scenario:** hai IPC client được phép gửi Run gần nhau, hoặc Run từ Launcher trùng launch sau deploy. Cả hai có thể chạy B; một process có thể không còn được track. Stop A vượt guard rồi Run B đổi active trước khi Stop đọc `_process` có thể dừng B.
- **Operational impact:** chạy sai package, duplicate/orphan, runtime persistence sai và nguy cơ dừng nhầm chương trình test.
- **Root cause:** lock chỉ bảo vệ từng snapshot hoặc file write; không bảo vệ chuỗi check → stop → state → start → persist/watch.
- **Recommended direction:** một cổng serialization ở supervisor cho tất cả entry points, kèm expected package/generation cho stop/restart và generation check cho callback. Truyền launch context cố định vào start. Thiết kế việc cancel/join watcher tránh tự chờ deadlock.
- **Required regression test:** barrier hai Run khác package; Run/Stop/Restart chéo nhau; callback exit cũ đến sau launch mới; recovery trùng IPC. Assert đúng package, chỉ một owned process, state và disk cùng generation.
- **Estimated change scope:** Medium.

### L06-02 — Deadline trong attach làm process mất giám sát

- **Severity:** P1 — High.
- **Confidence:** High; đã tái hiện bằng `CancelDuringWatchAttach_LeavesLivePidWithoutWatcherOrDurablePid`.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/ProcessSupervisor.cs:198`, `:218`, `:255`, `:613`; `MProjectAgent/Configuration/AgentOptions.cs:224`; `MProjectAgent.Ipc.Contracts/IpcContracts.cs:24`.
- **Symbol:** `StartProcessAsync`, `TryAttachWatchProcessAsync`, `GetServerOperationTimeout`.
- **Evidence:** process được tạo trước khi chờ watch. Attach nuốt `OperationCanceledException` rồi trả null; start gán PID nhưng dùng token đã hủy để Save, trước `StartWatch`. Fixture timeout 1 giây xác nhận process vẫn sống, `_watchTask` đã completed và state trên đĩa chưa có PID. Production IPC core deadline 7 giây ngắn hơn mặc định attach 30 giây.
- **Finding:** cancellation sau side effect OS không có bước thu hồi hoặc hoàn tất registration độc lập với request token.
- **Realistic failure scenario:** legacy wrapper cần hơn 7 giây mới sinh watch process. Run timeout, wrapper vẫn tiếp tục, Agent không bắt exit/crash; restart Agent thấy durable PID trống và có thể khởi chạy thêm.
- **Operational impact:** mất health/exit/recovery supervision và phản hồi thao tác không phản ánh process thật.
- **Root cause:** lifetime của request được dùng cho cả commit process ownership; không có cleanup trong nhánh canceled/persist failed sau CreateProcess.
- **Recommended direction:** định nghĩa rõ kết quả khi request hết hạn sau launch: dừng/thu hồi process đã tạo hoặc hoàn tất đăng ký bằng budget cleanup riêng rồi trả trạng thái còn đang xử lý. Bảo đảm wrapper lẫn child không bị bỏ quên; điều chỉnh deadline theo lifecycle đã chọn.
- **Required regression test:** hủy tại trước start, trong attach, trước PID save và lúc save lỗi; kiểm cả RAM/disk/watch/child. Chạy wrapper chậm bằng production timeout qua pipe thật.
- **Estimated change scope:** Medium.

### L06-03 — Timeout HTTP bị hiểu thành hủy vòng probe

- **Severity:** P1 — High.
- **Confidence:** High; đã tái hiện bằng `HttpTimeout_EndsProbeAfterOnlyOneAttempt`.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/ProcessSupervisor.cs:283`, `:312`, `:334`.
- **Symbol:** `ProbeAsync`.
- **Evidence:** inner catch rethrow mọi `OperationCanceledException`; outer catch kết thúc loop. Fixture dùng `HttpClient.Timeout=1s`, handler chờ token, threshold 3: probe kết thúc sau đúng một call dù token supervisor chưa hủy.
- **Finding:** health endpoint treo làm mất hẳn cơ chế phát hiện unhealthy, thay vì tăng failure count.
- **Realistic failure scenario:** app còn PID nhưng không trả HTTP do deadlock; timeout đầu tiên khiến Agent ngừng probe cho lifetime hiện tại.
- **Operational impact:** UI có thể tiếp tục báo Running, threshold/auto-restart không còn tác dụng khi ứng dụng treo.
- **Root cause:** không phân biệt timeout của HttpClient với cancellation của supervisor.
- **Recommended direction:** chỉ thoát khi token vòng probe bị hủy; tính timeout của từng HTTP request vào consecutive failures và giữ interval/threshold.
- **Required regression test:** black-hole đủ threshold phải kill với `HealthCheckFailed`; timeout rồi success phải reset count; cancel supervisor phải dừng không tính lỗi/kill.
- **Estimated change scope:** Small.

### L06-04 — Watch attach không chứng minh process thuộc lần launch

- **Severity:** P1 — High.
- **Confidence:** High; đã tái hiện process tồn tại trước launch. Nhánh cross-session chưa thử runtime.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/ProcessSupervisor.cs:218`, `:626`; `MProjectAgent/Models/ProcessSupervisorModels.cs:28`.
- **Symbol:** `TryAttachWatchProcessAsync`, `FindProcessByMainModule`, `SupervisedProcessState`.
- **Evidence:** tìm mọi process theo basename rồi lấy match đầu có `MainModule.FileName` bằng path; không kiểm start time tương đối với launch, session hay ownership. Fixture tạo watch process trước, wrapper mới chỉ exit 0, nhưng supervisor nhận PID cũ và Stop giết PID đó.
- **Finding:** path equality không đủ xác định child của launch, đặc biệt khi cùng payload chạy ở session khác.
- **Realistic failure scenario:** operator đã mở app ngoài Launcher hoặc app còn sống trong session cũ; Run tạo instance mới nhưng attach instance cũ. Instance mới thoát khỏi supervision và Stop tác động nhầm instance cũ.
- **Operational impact:** sai session, sai process được stop/probe/recover và duplicate không được quản lý.
- **Root cause:** thiếu launch identity/session/ownership contract; không xử lý ambiguous matches.
- **Recommended direction:** ràng buộc attach với session/token và generation của lần start; chốt riêng chính sách nhận lại process có sẵn. Dùng ownership/job/process-tree khi phù hợp với wrapper legacy; không tự nhận match đầu nếu mơ hồ. Lưu identity thực tế cho recovery.
- **Required regression test:** cùng path ở session khác; process có trước launch; hai match cùng session; wrapper sinh child chậm. Assert không nhận/kill process ngoài ownership đã định nghĩa.
- **Estimated change scope:** Medium.

### L06-05 — Bootstrapper bỏ sót console sau chuyển user/session

- **Severity:** P2 — Medium.
- **Confidence:** High, static; chưa diễn tập chuyển console thật.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/LauncherBootstrapper.cs:38`, `:45`, `:73`.
- **Symbol:** `EnsureRunning`, `IsRunningOnADesktop`, `_showRequestedForServiceRun`.
- **Evidence:** bất kỳ process cùng tên có `SessionId != 0` được coi là Launcher đã có. Cờ show chỉ reset khi service được tạo lại, không theo session. Kiểm tra cũng không đối chiếu executable path.
- **Finding:** session cũ/disconnected ngăn việc bảo đảm Launcher có mặt trong active console mới.
- **Realistic failure scenario:** service đã mở Launcher cho user A; fast user switching sang B giữ process A sống. Vòng 30 giây thấy process A và return, cờ show đã true nên không start cho B.
- **Operational impact:** B không có UI vận hành; A lại không đủ điều kiện active-console IPC. Process trùng tên từ vị trí khác cũng có thể làm bootstrap bỏ qua Launcher đúng bundle.
- **Root cause:** desktop bất kỳ được dùng thay active console; trạng thái bootstrap theo service run thay vì session/identity.
- **Recommended direction:** so session active và canonical executable path; theo dõi lần show theo session. Chỉ xử lý process thuộc Launcher/bundle đã xác minh.
- **Required regression test:** A → B khi A vẫn sống; logoff/logon; stale session 0; process trùng basename khác path. B nhận đúng một Launcher.
- **Estimated change scope:** Small.

### L06-06 — Run trùng được xếp hàng và chạy lại sau response

- **Severity:** P2 — Medium.
- **Confidence:** High, static; chưa chạy UI automation.
- **Category:** confirmed defect.
- **File:** `MProjectLauncher/ViewModels.cs:92`, `:445`; `MProjectLauncher/IpcClient.cs:63`; `MProjectLauncher/MainWindow.xaml:189`.
- **Symbol:** `MarkRunRequested`, `MainViewModel.Run`, `IpcClient.SendAsync`.
- **Evidence:** Run kiểm `RunEnabled` nhưng `MarkRunRequested` chỉ ghi timestamp; `RunEnabled` chỉ thay trong lần `Update` sau response/status. Semaphore transport xếp hàng, không loại command trùng và không kiểm lại row khi đã vào gate.
- **Finding:** nhiều double-click/Open trước response có thể gửi nhiều Run cho cùng package.
- **Realistic failure scenario:** app mở chậm; operator double-click lần nữa trước phản hồi. Request thứ hai đợi request đầu, rồi supervisor gọi `StopCurrentAsync` và khởi động lại app vừa chạy.
- **Operational impact:** cửa sổ khởi động bị đóng, mất tiến độ khởi tạo và cảm giác app crash do thao tác lặp.
- **Root cause:** UI chưa khóa mutation ngay khi nhận intent; single-flight transport bị dùng như bảo đảm không lặp thao tác.
- **Recommended direction:** đánh dấu pending/disable Run đồng bộ trước await, giải phóng trong finally và reconcile status; không giữ Run dư trong queue. Gộp refresh đang chờ để thao tác không phải đợi nhiều poll cũ.
- **Required regression test:** fake IPC trì hoãn response, gọi Run hai lần; chỉ một request. Timeout/error cho retry có chủ ý; queued refresh không làm Run/Stop sai trạng thái.
- **Estimated change scope:** Small.

### L06-07 — Fallback wrapper bị status và recovery nhận là đã chết

- **Severity:** P2 — Medium.
- **Confidence:** High, static.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/ProcessSupervisor.cs:233`, `:83`, `:100`, `:575`.
- **Symbol:** `StartProcessAsync`, `WatchTargetPath`, `IsAlive`, `RecoverAsync`.
- **Evidence:** nhánh không tìm thấy watch và wrapper còn sống chọn supervise wrapper; `_state.WatchProcessPath` vẫn giữ path child. `GetOperationalStatus` và recovery luôn dùng `WatchProcessPath ?? ExePath` làm expected executable.
- **Finding:** PID wrapper có main module khác watch `.exe`, nên status là Idle dù wrapper sống; recovery cũng không reattach PID đó.
- **Realistic failure scenario:** launch sau deploy chờ hết 30 giây mà child chưa xuất hiện, hoặc attach timeout được cấu hình ngắn hơn deadline IPC. Wrapper còn hoạt động nhưng card báo sẵn sàng. Restart service có thể launch thêm wrapper.
- **Operational impact:** Run được bật sai, Stop bị tắt vì status không Running, duplicate sau recovery.
- **Root cause:** trộn desired watch path với identity của process thực tế đang supervise.
- **Recommended direction:** lưu riêng effective supervised executable/session/start time và loại attach/fallback; status/recovery dùng identity thực tế. Không xóa metadata desired watch làm mất chính sách của lần launch sau.
- **Required regression test:** wrapper sống vượt attach timeout: status Running, PID chính xác, recover không tạo thêm process, Stop hoạt động; giữ tương thích state cũ.
- **Estimated change scope:** Medium.

### L06-08 — Icon lớn vô hiệu toàn bộ status envelope

- **Severity:** P2 — Medium.
- **Confidence:** High, static.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/AgentStatusProvider.cs:105`, `:117`; `IpcServer.cs:216`; `MProjectLauncher/ViewModels.cs:484`.
- **Symbol:** `GetIcon`, `LoadIconBase64`, `WriteResponseAsync`, `Apply`.
- **Evidence:** ảnh png/jpg/bmp/gif được đọc nguyên file và cache base64 theo version/path; không có size budget/thumbnail. Server serialize toàn bộ status rồi mới thay bằng `ipc.response_too_large` nếu vượt 4 MiB. UI nhận `Ok=false` chỉ đổi status bar, giữ card/enablement cũ.
- **Finding:** optional icon có thể làm status, run, stop và restart mất phần kết quả chính. Một ảnh 4 MiB sau base64 đã lớn hơn toàn bộ frame budget; tổng nhiều ảnh cũng gây lỗi.
- **Realistic failure scenario:** metadata icon trỏ vào ảnh BMP lớn trong payload. Mọi poll sau đó đều lỗi; command có thể đã tạo/dừng process nhưng response bị thay bằng failure vì icon. Nếu từng có status thành công, UI giữ trạng thái cũ.
- **Operational impact:** mất khả năng quan sát/điều khiển đáng tin cậy, retry command ngoài ý muốn; cache theo version không eviction còn giữ dữ liệu ảnh cũ.
- **Root cause:** chỉ bound wire sau khi đã materialize dữ liệu không bị bound; phần trình bày không có degradation độc lập với trạng thái điều khiển.
- **Recommended direction:** giới hạn input/decoded dimensions, tạo thumbnail với byte budget và bỏ icon khi hết tổng budget; giới hạn cache. Status lỗi phải đánh dấu stale và cập nhật action availability; giữ response bound hiện có.
- **Required regression test:** một ảnh lớn, tổng nhiều icon vượt budget, nhiều version liên tục; status cốt lõi vẫn nhận được, command không mất receipt vì icon, UI không giữ trạng thái hợp lệ giả.
- **Estimated change scope:** Medium.

### L06-09 — Handler báo stopped/restarted khi supervisor trả false

- **Severity:** P2 — Medium.
- **Confidence:** High, static.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/IpcRequestHandler.cs:161`, `:169`; `ProcessSupervisor.cs:417`, `:452`.
- **Symbol:** `StopAsync`, `RestartAsync`, `RequestStopAsync`, `RequestRestartAsync`.
- **Evidence:** hai API supervisor trả bool; stop trả false khi kill lỗi, restart có nhánh false khi không thực hiện được. Handler bỏ giá trị và gọi `WithStatusAsync("stopped"/"restarted")` với `Ok=true` nếu lấy status thành công.
- **Finding:** result không phân biệt thành công, no-op đã dừng, supervisor disabled và lỗi thực thi.
- **Realistic failure scenario:** kill ném lỗi OS trong khi process còn sống và request token còn hiệu lực. Supervisor log rồi trả false; Launcher nhận thông báo stopped dù PID vẫn hoạt động. Nhánh timeout có thể bị status read chuyển thành typed timeout nên không khẳng định mọi false đều hiện success.
- **Operational impact:** operator tin chương trình đã dừng trước thao tác tiếp theo; diagnostics mâu thuẫn với response thành công.
- **Root cause:** bool quá ít ngữ nghĩa và không được handler sử dụng.
- **Recommended direction:** typed outcome phân biệt stopped/already-stopped/failed/canceled/unsupported; chỉ trả success khi postcondition phù hợp đã đạt. Giữ legacy ok/message và thêm error code tương thích.
- **Required regression test:** fault-injected kill denied phải trả failure cùng trạng thái thực; already-stopped xử lý idempotent; restart bị hủy không báo restarted.
- **Estimated change scope:** Small.

## Quyết định tốt cần giữ

- ACL Interactive + LocalSystem kết hợp kiểm active console sau khi đọc dữ liệu; không thay bằng Builtin Users. Chính sách pure-function hiện từ chối sai session/thiếu Interactive SID và cho phép LocalSystem.
- Một request mỗi connection; bốn listener; request 64 KiB, support response 64 KiB, legacy response 4 MiB; strict UTF-8, read/write deadlines, generic exception response và typed errors. Giữ các giới hạn dù cần sửa cách hoàn tất lifecycle sau timeout.
- Client dùng token impersonation mức Identification, timeout bao gồm chờ semaphore, connect/read/write; kiểm version không hỗ trợ và correlation khác giá trị.
- Shared POCO netstandard2.0, camelCase, fixture Unicode/null/timestamp và test hai serializer; version IPC độc lập OTA. `support.report.v2` được quảng bá riêng và UI đòi đúng capability, tránh client v1 gửi thiếu reporter.
- Entry command explicit cho exe/com/cmd/bat/jar/py, `UseShellExecute=false`, working directory theo entry; batch từ chối `%`/`!`, `/d` tắt AutoRun. Interactive launcher dùng user token và desktop console khi chạy service.
- Catalog/runtime có semaphore, atomic replace và backup; bảo vệ file I/O này vẫn hữu ích dù không thay thế lifecycle serialization.
- QR chỉ dựa hostname local và nằm trong Launcher; layout dùng DIPs/work area; recent events giới hạn 12; script package mang đầy đủ các loose dependencies cạnh executable.

## Nghi vấn đã loại hoặc không nâng thành finding

- **“Mọi local user đều điều khiển được Agent”:** không đúng với code hiện tại; ngoài ACL còn kiểm token/session. Test identity dùng dữ liệu giả chưa thay thế matrix token Windows thật.
- **“Bốn listener đã giải quyết process race”:** chỉ giải quyết concurrency transport; repro L06-01 chứng minh không giải quyết shared lifecycle.
- **“Single-instance WPF khiến race không thể xảy ra”:** mutex không serialize mọi IPC client hoặc caller từ Agent worker. Ngay một UI vẫn có lệnh được queue trùng (L06-06).
- **“IPC Run phải gọi LaunchPolicy”:** manual Run là lựa chọn của operator; policy hiện dành cho launch sau deploy. Không coi sự khác biệt đó là lỗi kiến trúc.
- **“Thiếu version/correlation luôn là invalid”:** omission hỗ trợ legacy v1 có chủ ý. Client vẫn chấp nhận v2 response thiếu correlation vì chỉ so khi có giá trị; ghi nhận khoảng trống validation, chưa thấy đường response hợp lệ hiện tại làm gửi nhầm kết quả trên mô hình một request/connection.
- **“Tất cả shell-associated entry vẫn hỏng trong service”:** builder và đường CreateProcessAsUser đã tồn tại, fixture batch hiện có pass. Chưa đủ chứng cứ về Python/Java thật trên service để kết luận toàn bộ đã tốt hoặc hỏng.
- **“Không có operation deadline”:** đã có linked token. Nó là cooperative cancellation; lỗi đã xác nhận là quản lý side effect sau cancel, không phải thiếu hoàn toàn deadline.

## Khoảng trống bằng chứng và follow-up

- Cần matrix thật dưới LocalSystem: active console, stale/disconnected RDP, batch/network token, chuyển session giữa authorize và start; test hiện tại phần lớn inject authorizer và `protectPipe:false`. Chưa chứng minh server-identity/pipe-squatting resistance bằng negative test; không tuyên bố có exploit.
- Cần packed net48 runtime E2E trên máy/VM cô lập. Test transport link source Launcher vào net8 và build net48 không chứng minh mọi cancellation behavior của .NET Framework 4.8 hoặc dependency load của bundle thật.
- `e2e-wire-check.ps1` có vòng deadline ngoài nhưng dùng `StreamReader.ReadLine()` đồng bộ không bound bên trong; fixture server không phản hồi có thể treo kiểm tra. Đây là hạn chế công cụ xác minh, chưa gộp thành lỗi production. Script cũng tự chạy Launcher qua bootstrap; cần xác minh ownership của process trong cleanup khi dùng lab.
- Cần thử real monitor/DPI, multi-monitor và accessibility/UI state khi pipe partial/invalid/timeout. `SystemParameters.WorkArea` và unit layout tests chưa chứng minh monitor đang chứa cửa sổ ở mọi cấu hình.
- Request reader bỏ CR cuối mỗi read buffer ngay cả khi chưa gặp LF (`IpcServer.cs:187`), thay vì chỉ strip CR thuộc delimiter. Cần fixture fragmentation kiểm malformed JSON có CR ở boundary và exact byte accounting; chưa khẳng định đây là bypass authorization hoặc tài nguyên không giới hạn.
- Cần test saturated cả bốn connection, khách không đọc response, disconnect sau mutation, slow/canceled status serialization và stale UI khi typed operation failure. Một slow-client test không chứng minh fairness dưới tải liên tục.
- `IsAlive` có fallback chấp nhận khi không đọc được process identity và không lưu session; cần xác minh PID reuse/permission failure trong recovery lab. Không suy diễn lỗi hiện tại thành việc có thể kill tùy ý mọi PID.
- Theo đúng exclusions của 06, không deep-review remote command delivery, deploy/job state machine, binary OTA hoặc support outbox/business semantics. Chỉ theo call sites của chúng khi đi vào process/IPC boundary.

## Thứ tự xử lý đề xuất

1. L06-01 và L06-02: chốt quyền sở hữu process và transition/cancellation trước; đây là nền để sửa race mà không tạo orphan mới.
2. L06-03, L06-04, L06-07: sửa probe và identity attach/recovery; giữ compatibility của state cũ.
3. L06-05, L06-06, L06-08, L06-09: hoàn thiện đúng session, action pending và phản hồi trạng thái/thao tác.
4. Chạy lại focused regression, rồi LocalSystem/multi-session và packed net48 E2E trên lab. Không đóng finding chỉ dựa vào 131 test cũ pass hoặc build thành công.
