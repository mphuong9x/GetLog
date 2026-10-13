# 07 — Review production-support durable workflow

Ngày review: **2026-09-09**. Phạm vi: working tree tại `db480dffe3372b72028848eaa62fbb47d7b141b7`.

Prompt thực hiện: [07-production-support-durable-workflow.md](../prompts/07-production-support-durable-workflow.md), SHA-256 `804a0ea567ef2a4e7a440119089ff34b753829391a3d6cc7d988fd8743347a6c`. Đây là review mã hiện tại và kiểm chứng cục bộ; **không sửa production code**. Các báo cáo `06` có sẵn trong working tree được giữ nguyên.

Đã đọc `AGENTS.md`, `PROJECT_MEMORY.md`, `PROJECT_REVIEW_CHECKLIST.md`, hợp đồng và compatibility matrix. Các nhận định lịch sử được đối chiếu với implementation và test hiện tại. Không review lại named-pipe security, generic RBAC hay correctness của raw test-result parser thuộc `06`, `09`, `08`.

## Kết quả

Phát hiện **9 confirmed defects: 4 P1 và 5 P2**. Cơ chế commit SQLite, fingerprint và server receipt có nền tảng tốt, nhưng các nhánh recovery chưa khép kín: lỗi support có thể làm Agent không khởi động, update có thể nhắm nhầm request, thao tác cần sửa có thể kẹt, và terminal retention có thể không bao giờ thu hồi dung lượng.

P1 là lỗi cần ưu tiên trước rollout tiếp theo vì ảnh hưởng Agent hoặc tính đúng/khả năng tiếp tục của workflow. P2 là lỗi recovery, hiển thị hoặc cô lập dữ liệu cần xử lý có kế hoạch. Không dùng kết quả unit test để tuyên bố đủ điều kiện rollout nhà máy.

| ID | Severity | Finding | Evidence chính |
| --- | --- | --- | --- |
| R07-01 | P1 | Lỗi khởi tạo support thoát khỏi worker và ảnh hưởng core Agent | Probe `startup-schema`; worker/host composition |
| R07-02 | P1 | IPC update không mang request identity, có thể cập nhật request mới từ hộp thoại cũ | Probe `stale-modal`; Launcher DTO/call site |
| R07-03 | P1 | Không sửa được failed Resolved hoặc failed update có successor | Probes `failed-resolved`, `failed-predecessor` |
| R07-04 | P1 | Reconcile ghi lại thời điểm đóng, vô hiệu terminal retention | Probe `retention` và control prune |
| R07-05 | P2 | Update của request đã đóng tiếp tục retry vô hạn | Probe `force-resolved-pending` |
| R07-06 | P2 | Mất cả delivery lẫn ghi retry làm InFlight kẹt đến restart | Probe `inflight-without-lease` |
| R07-07 | P2 | Purge query không loại dữ liệu đang hiển thị khi đổi auth/team scope | Hai probe React với observer đang mount |
| R07-08 | P2 | Lỗi tải danh sách lần đầu hiển thị như không có yêu cầu | Probe render page với lỗi tải |
| R07-09 | P2 | Reconcile thất bại vẫn báo Synced khi queue rỗng | Probe `stale-sync`; heartbeat/Launcher call sites |

Mã probe và hướng dẫn tái hiện nằm trong [07-production-support-durable-workflow-probes.md](07-production-support-durable-workflow-probes.md). Probe dùng dữ liệu giả, clock điều khiển được và SQLite riêng; không gọi API thật hay đọc state của trạm.

## Phạm vi và tracing bắt buộc

| Trace | Luồng đã đối chiếu | Kết luận |
| --- | --- | --- |
| 1. Operator → durable enqueue | `MProjectLauncher/ViewModels.cs:677,706,776,787` → `SupportReportDialog`/`SupportUpdateDialog` → `Services/IpcRequestHandler.cs:61,74,111` → `ProductionSupportAgentService.ReportAsync/UpdateAsync` → `EnqueueOrReplaceFailedCreateAsync`/`EnqueueUpdateAsync`/`SupersedeAndEnqueueReplacementAsync` | Preview là snapshot có TTL; report/update chỉ trả receipt sau commit. R07-02 và R07-03 nằm ở định danh và correction, không phải giả lập synchronous server success. |
| 2. Durable delivery | `ProductionSupportDeliveryWorker.ExecuteAsync` → `InitializeAsync`/`RunCycleAsync` → SQLite claim → `DeliverAsync` → `ProductionSupportApiClient` → `ServerClient.SendAuthenticatedAsync` | Gửi theo dependency, giữ payload/ID, retry có jitter và Retry-After, pause auth/version. Startup, closure, retention và recovery InFlight có các lỗi R07-01/04/05/06. |
| 3. Server create/update/current | `AgentProductionSupportController` → `ProductionSupportService` → Agent/Computer context → advisory lock → request/receipt/update/log trong transaction → fingerprint/replay response | HTTP API hiện tại là `/agent/v2/support-requests`, không phải v1 trong một số ghi chú cũ. Replay được kiểm trước employee/current-state validation. Unique indexes và concurrency version là lớp bảo vệ bổ sung. |
| 4. Web recovery | `src/api/productionSupport.ts` → `use-production-support.ts` → `SupportRequests.tsx` → `SupportRequestsController` → `ProductionSupportQueryService`/`ProductionSupportRecoveryService` | Query dùng ownership hiện tại và membership còn hiệu lực; recovery kiểm permission trong controller và service, ghi timeline/audit. Mutation invalidate theo user. R07-07/08 ảnh hưởng dữ liệu nhìn thấy và diễn giải lỗi tải. |
| 5. Telemetry | `AgentWorker.BuildProductionSupportTelemetryAsync` → heartbeat DTO → `AgentController.Heartbeat` → `ObserveAgentTelemetry`; HTTP middleware và metrics collector đọc PostgreSQL riêng | Label được giới hạn; không đưa username/note vào label. Telemetry histogram là mẫu quan sát, không phải danh sách queue realtime theo từng PC. R07-09 làm sai nhãn sync; R07-01 có thể chặn Agent trước khi heartbeat chạy ổn định. |

Đã đối chiếu toàn bộ source/fixtures/shared compatibility tests dưới `MProject.ProductionSupport.Contracts`, các source support Agent trong prompt, IPC support branches, `AgentMSystemAddress`, support entities/models/options, hai expand migrations và model configuration, server services/controllers/metrics, frontend types/API/hooks/isolation/page/i18n cùng test liên quan. Các file parser, authentication, host và Launcher ngoài nhóm support chỉ được đọc khi quyết định hành vi của luồng này.

Các giá trị implementation cần giữ khi sửa: API `production-support-agent-v2`; capability report `support.report.v2`; IPC v2 vẫn nhận legacy core calls; OTA schema/protocol vẫn v1. Outbox mặc định 1.000 operations, 20 MiB logical database limit, retention 30 ngày, idle delivery 15 giây và reconcile 60 giây. API client dùng deadline 30 giây và đọc response tối đa 64 KiB. Server giữ dữ liệu support online tối thiểu 24 tháng; hiện không có automatic purge. `Agent.MSystemUrl` có thể cấu hình riêng, mặc định resolve `/support-requests` từ server origin; HTTPS bắt buộc ngoài loopback development.

## Findings chi tiết

### R07-01 — Lỗi support có thể ngăn core Agent hoạt động

- **Severity:** P1.
- **Confidence:** High. Đã tái hiện exception thoát khỏi `BackgroundService` khi SQLite có schema mới hơn; host composition và nhánh status xác nhận phạm vi lan truyền.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Support/ProductionSupportDeliveryWorker.cs:27`; `MProjectAgent/Support/ProductionSupportDeliveryService.cs:84`; `MProjectAgent/Support/SupportOutboxStore.cs:573`; `MProjectAgent/Program.cs:141`; `MProjectAgent/Services/AgentStatusProvider.cs:72`.
- **Symbol:** `ExecuteAsync`, `InitializeAsync`, `EnsureInitializedLockedAsync`, `AgentStatusProvider.GetStatusAsync`.
- **Evidence:** `await _delivery.InitializeAsync(stoppingToken)` nằm trước vòng lặp có `try/catch`. Initialize bao gồm mở/kiểm SQLite, reconcile và prune. Schema mới, database hỏng hoặc lỗi storage được ném ra ngoài. Worker được đăng ký cùng host với heartbeat/poll/IPC; không có cấu hình bỏ qua lỗi hosted service. Probe `startup-schema` ghi `user_version=999` trong database giả và quan sát `SupportOutboxCorruptionException` từ startup/ExecuteTask. Với lỗi runtime, core status cũng `await _supportStatus.GetStatusAsync` mà không cô lập exception.
- **Finding:** Một subsystem support không khả dụng có thể làm Agent fail startup hoặc khiến core status thất bại, mặc dù payload/catalog/enrollment không hỏng. Catch trong heartbeat telemetry không bảo vệ được startup hoặc IPC status.
- **Realistic failure scenario:** Mất điện làm SQLite outbox không đọc được; một lần mở database lỗi I/O; hoặc rollback binary về bản chưa hiểu schema outbox mới. Service được khởi động lại nhưng worker fault trước khi vòng retry bắt đầu. Nếu storage chỉ lỗi khi đang chạy, delivery loop có thể còn sống nhưng Launcher không nhận được core status.
- **Operational impact:** Heartbeat/poll/IPC hoặc khả năng điều khiển ứng dụng từ Launcher bị ảnh hưởng chỉ vì support storage. Restart lặp lại không sửa được schema incompatibility/corruption.
- **Root cause:** Error boundary chỉ bao quanh delivery cycle, không bao quanh initialization; core status phụ thuộc trực tiếp vào support snapshot.
- **Recommended direction:** Giữ database nguyên vẹn và từ chối ghi schema không hiểu. Đưa initialization/reconcile/prune vào cơ chế lỗi có retry/pause của riêng support; trả support-unavailable có mã an toàn, trong khi core status vẫn được dựng. Không thay bằng xóa/recreate database hoặc bỏ qua toàn bộ background failures.
- **Required regression test:** Host fixture với outbox hỏng, schema mới và lỗi mở database tạm thời; xác nhận core heartbeat/status vẫn chạy, report không ACK, outbox không bị sửa/downgrade; phục hồi storage cho phép support tự khởi tạo lại. Thêm lỗi support snapshot sau startup và assertion core status vẫn trả được.
- **Estimated change scope:** Medium.

### R07-02 — Update từ hộp thoại cũ có thể resolve request mới

- **Severity:** P1.
- **Confidence:** High tại service/IPC boundary; chưa chạy fixture WPF nhiều phiên đăng nhập thật.
- **Category:** confirmed defect.
- **File:** `MProjectAgent.Ipc.Contracts/ProductionSupportIpcContracts.cs:139`; `MProjectLauncher/SupportUpdateDialog.xaml.cs:39`; `MProjectLauncher/ViewModels.cs:706`; `MProjectAgent/Support/ProductionSupportAgentService.cs:230`.
- **Symbol:** `SupportUpdateRequestDto`, `SupportUpdateDialog.Submit_Click`, `ProductionSupportAgentService.UpdateAsync`.
- **Evidence:** Dialog giữ `_request` đã mở, nhưng chỉ gửi `EmployeeUsername`, `Status`, `Note`. Agent đọc `snapshot.CurrentRequest.LocalRequestId` tại lúc xử lý rồi enqueue vào graph đó. Không có expected local/canonical ID để so sánh. Probe `stale-modal`: chụp request A, reconcile A đóng và B trở thành current, gửi DTO của thao tác đã chuẩn bị cho A; receipt nhận canonical ID của B.
- **Finding:** Request mà operator đã xem không được ràng buộc với request mà Agent cập nhật. Server nhận canonical ID B hợp lệ nên validation “current request” ở server không bắt được nhầm đối tượng này.
- **Realistic failure scenario:** TE giữ dialog A; admin force-resolve A, một caller IPC hợp lệ khác báo B trên cùng PC, rồi dialog cũ gửi Resolved. Trường hợp chuyển phiên operator rồi quay lại dialog cũ cũng cần kiểm thử. Không cần bypass authentication để xảy ra khi có các caller hợp lệ đồng thời; một Launcher đơn lẻ đã khóa parent modal sẽ giảm khả năng phát sinh từ chính cửa sổ đó.
- **Operational impact:** Có thể đóng một sự cố mới bằng ghi chú xử lý của sự cố cũ và ghi sai ý định vào timeline.
- **Root cause:** IPC update mô tả “cập nhật current tại thời điểm xử lý” trong khi UI mô tả “cập nhật request đã mở”.
- **Recommended direction:** Thêm expected local request ID vào version/capability contract tương thích; truyền từ dialog và kiểm atomically trong transaction enqueue. Khi current đổi, trả lỗi typed yêu cầu tải lại; không tự nhắm sang graph khác. Canonical ID có thể chưa tồn tại ở chế độ offline nên chỉ yêu cầu canonical ID sẽ không đủ.
- **Required regression test:** Barrier tại bước dialog/snapshot A; resolve A và đưa B thành current; submit Resolved từ snapshot A phải bị từ chối và B không nhận operation. Bao phủ local request chưa được server map ID và hai caller IPC hợp lệ.
- **Estimated change scope:** Medium.

### R07-03 — Correction không xử lý được failed Resolved hoặc failed predecessor

- **Severity:** P1.
- **Confidence:** High; hai probe đi qua service thật và SQLite thật đều tái hiện.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Support/ProductionSupportAgentService.cs:239`; `MProjectAgent/Support/ProductionSupportAgentService.cs:266`; `MProjectAgent/Support/SupportOutboxContract.cs:327`; `MProjectAgent/Support/SupportOutboxStore.cs:457`.
- **Symbol:** `UpdateAsync`, `HasUnconfirmedResolved`, `FindActiveTail`, `SupersedeAndEnqueueReplacementAsync`.
- **Evidence:** `HasUnconfirmedResolved` tính cả `NeedsUserAction`; service từ chối trước nhánh supersede nếu cờ này true. Ngoài ra, service chỉ sửa khi **tail** là `NeedsUserAction`. Probe `failed-resolved` cho một Resolved bị `employee_invalid`, rồi nhập username đúng: vẫn nhận `InvalidArgument`. Probe `failed-predecessor` xếp hai InProgress offline; operation đầu fail, operation sau Pending; nhập sửa được ACK nhưng tạo thêm successor, operation lỗi vẫn còn và không operation nào dispatch được.
- **Finding:** API SQLite có primitive supersede/relink đúng, nhưng đường operator không tiếp cận được primitive cho hai hình dạng graph hợp lệ này. Retry không giải quyết lỗi employee và nhập lại có thể chỉ làm queue dài thêm.
- **Realistic failure scenario:** TE nhập nhầm mã nhân viên trong lần Resolved; hoặc nhiều ghi chú được lưu khi offline trước khi server kịp báo operation đầu không hợp lệ.
- **Operational impact:** Không hoàn thành được workflow tại trạm; request và queue tiếp tục báo cần nhập lại dù operator đã sửa. Có thể phải force-resolve trên web và vẫn còn operation mắc kẹt như R07-05.
- **Root cause:** Cờ “Resolved đang chờ xác nhận” không tách thao tác đang deliverable với thao tác cần correction; chọn tail thay vì operation đang chặn dependency chain.
- **Recommended direction:** Cho phép sửa đúng failed operation, bao gồm failed Resolved; kiểm pending-resolve trên graph sau replacement. Xác định operation cần sửa một cách rõ ràng, rồi dùng transaction supersede/relink hiện có. Giữ ID/payload cũ để audit và cấp operation ID mới cho nội dung đã sửa.
- **Required regression test:** Failed Resolved → nhập employee đúng → replacement được gửi và server resolve đúng một lần. Failed InProgress có một hoặc nhiều descendants → correction relink descendants, graph hợp lệ sau restart, không còn node Pending không thể dispatch. Test hiện có chỉ chứng minh sửa failed InProgress ở tail nên chưa bao phủ hai trường hợp này.
- **Estimated change scope:** Medium.

### R07-04 — Mỗi reconcile dời mốc retention, queue có thể đầy vĩnh viễn

- **Severity:** P1.
- **Confidence:** High; probe qua `ProductionSupportDeliveryService.InitializeAsync` và control prune xác nhận.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Support/SupportOutboxRuntime.cs:219`; `MProjectAgent/Support/SupportOutboxRuntime.cs:646`; `MProjectAgent/Support/SupportOutboxRuntime.cs:421`; `MProjectAgent/Support/ProductionSupportDeliveryService.cs:84`; `MProjectAgent/Support/SupportOutboxStore.cs:713`.
- **Symbol:** `ApplyReconcileAsync`, `CloseRequestAsync`, `PruneClosedTerminalGraphsAsync`, `EnsureCapacityAsync`.
- **Evidence:** Khi current absent/resolved, mọi graph có create Delivered lại gọi `CloseRequestAsync` bằng thời gian reconcile hiện tại. SQL `ON CONFLICT ... DO UPDATE SET ClosedAtUtc = excluded.ClosedAtUtc` thay mốc cũ. Initialize và chu kỳ reconcile gọi bước này trước prune. Probe tạo terminal graph đã đóng 31 ngày; initialize với current absent giữ nguyên graph. Control thêm 31 ngày rồi gọi prune trực tiếp, không reconcile trước, xóa được graph.
- **Finding:** Graph đã terminal không tích lũy tuổi retention khi Agent tiếp tục nhìn thấy current absent. Dừng Agent lâu hơn retention rồi khởi động lại cũng không cứu được trường hợp này vì startup ghi lại mốc trước prune.
- **Realistic failure scenario:** Trạm xử lý các support request trong nhiều ca rồi để queue rỗng giữa các sự cố. Mỗi reconcile 60 giây lại làm mới thời điểm đóng toàn bộ lịch sử Delivered.
- **Operational impact:** Các operation lịch sử tiếp tục tính vào giới hạn 1.000 rows/20 MiB. Khi chạm giới hạn, report/update mới bị `OutboxFull`; không thể dựa vào retention cấu hình để tự phục hồi. Payload PD/TE cũng lưu lâu hơn thời hạn dự kiến.
- **Root cause:** Closure timestamp được dùng như “last seen absent” thay vì “thời điểm graph chuyển sang closed”. Test prune gọi trực tiếp nên bỏ sót ordering của delivery worker.
- **Recommended direction:** Ghi closure khi chuyển từ active sang closed; giữ mốc đã có trong reconcile lặp lại. Chỉ xóa mốc khi graph thực sự được mở lại. Kiểm lại thứ tự prune và recovery khi queue đã đầy, không sửa bằng xóa pending rows.
- **Required regression test:** Chạy delivery cycle liên tục qua retention với current absent; terminal graph phải bị prune đúng hạn. Bao phủ restart sau 31 ngày, repeated absent reconcile, resolved response rồi absent, và xác nhận dung lượng được dùng lại. Pending/NeedsUserAction graph phải được bảo toàn.
- **Estimated change scope:** Small.

### R07-05 — Request bị force-resolve nhưng update cũ vẫn retry mãi

- **Severity:** P2.
- **Confidence:** High. Probe chạy hai delivery cycles; đối chiếu với nhánh server `RequestNotCurrent`.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Support/SupportOutboxRuntime.cs:219`; `MProjectAgent/Support/ProductionSupportDeliveryService.cs:309`; `MProjectAgent/Support/SupportOutboxStore.cs:344`; `MProjectBackend/MProject.Application/Services/Support/ProductionSupportService.cs:310`.
- **Symbol:** `ApplyReconcileAsync`, `HandleFailureAsync`, `TryClaimNextAsync`, `UpdateInTransactionAsync`.
- **Evidence:** Reconcile absent ghi closure và xóa current snapshot nhưng không chuyển các update chưa terminal sang trạng thái cuối. Claim chỉ kiểm state/dependency, không loại graph closed. `RequestNotCurrent` luôn thành RetryScheduled + reconcile. Server tìm receipt operation trước; nếu chưa có receipt và request đã đóng, nó tiếp tục trả `RequestNotCurrent`. Probe `force-resolved-pending` thấy current null nhưng cùng update được gửi lại ở cả hai cycles và vẫn RetryScheduled.
- **Finding:** Không có điểm kết thúc cho update đã được xác nhận không còn hợp lệ trên server. Retention cũng không xóa graph vì nó còn nonterminal operation.
- **Realistic failure scenario:** Agent lưu InProgress khi offline. Admin force-resolve request trên web trước khi Agent gửi operation này. Kết nối trở lại, Agent nhận 409 và lặp mãi.
- **Operational impact:** Queue/sync badge không sạch, HTTP retry và dung lượng tăng, operator không còn current request để sửa operation cũ. Nhiều graph như vậy tích lũy thành vấn đề capacity.
- **Root cause:** Reconcile current và disposition Reconcile chưa có quy tắc kết thúc graph dựa trên kết quả replay/current đã kiểm chứng.
- **Recommended direction:** Giữ đường replay để phân biệt mất response của update đã commit với update chưa bao giờ được nhận. Sau khi cùng operation được xác nhận không có replay và request không còn current, terminalize/supersede operation và descendants phù hợp, giữ lý do/audit rõ ràng. **Không đánh dấu Delivered chỉ từ GET current absent.**
- **Required regression test:** Offline update → web force-resolve → delivery/reconcile hội tụ, không gửi lại mãi. Control: update Resolved đã commit nhưng response mất vẫn phải replay thành công và không bị nhầm là chưa gửi. Thêm request A đã đóng và B trở thành current.
- **Estimated change scope:** Medium.

### R07-06 — InFlight không có cơ chế reclaim khi ghi recovery cũng thất bại

- **Severity:** P2.
- **Confidence:** High; fault injection SQLite tái hiện, sau khi gỡ fault queue vẫn kẹt.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Support/ProductionSupportDeliveryService.cs:144`; `MProjectAgent/Support/SupportOutboxStore.cs:344`; `MProjectAgent/Support/SupportOutboxStore.cs:573`; `MProjectAgent/Support/SupportOutboxContract.cs:202`.
- **Symbol:** `RecoverUnexpectedFailureAsync`, `TryClaimNextAsync`, `EnsureInitializedLockedAsync`, `CanDispatch`.
- **Evidence:** Recovery bắt lỗi ghi RetryScheduled rồi chỉ log; operation còn InFlight. Dispatcher chỉ nhận Pending/RetryScheduled; reset InFlight chỉ nằm trong initialization một lần khi `_initialized == false`. Probe `inflight-without-lease` chặn cập nhật row bằng trigger khi đang InFlight, làm delivery và ghi retry thất bại, gỡ trigger rồi tăng clock hai ngày: operation vẫn InFlight, không có lần gửi thứ hai.
- **Finding:** Lỗi storage tạm thời có thể trở thành lỗi recovery vĩnh viễn trong lifetime của service, dù storage đã hoạt động lại.
- **Realistic failure scenario:** Kết nối HTTP lỗi trong lúc SQLite không ghi được do disk/lock/I/O tạm thời; cả ghi kết quả lẫn ghi retry đều không thành công. Hệ thống storage sau đó phục hồi mà Agent không restart.
- **Operational impact:** Operation và mọi descendants bị bỏ lại, pending age tiếp tục tăng. Worker vẫn chạy nên nhìn vào process/service health không xác định được queue đã dừng tiến triển.
- **Root cause:** Claim không có lease/ownership metadata hoặc recovery sweep cho execution đã kết thúc nhưng chưa lưu được trạng thái mới.
- **Recommended direction:** Thêm lease/reclaim có kiểm soát, hoặc retry bền vững bước hoàn tất local transition sau khi network call đã kết thúc. Chỉ reclaim khi biết owner không còn đang deliver; giữ nguyên operation ID/payload để server replay an toàn. Không reset mọi InFlight mỗi cycle vì có thể gây gửi đồng thời.
- **Required regression test:** Sau claim, inject lỗi HTTP/commit và lỗi ghi retry; phục hồi database mà không restart; operation phải tự gửi lại và idempotency giữ một logical update. Có control không reclaim operation còn đang gửi, và restart vẫn giữ attempt count.
- **Estimated change scope:** Medium.

### R07-07 — Xóa cache không xóa dữ liệu support đang hiển thị ở scope cũ

- **Severity:** P2.
- **Confidence:** High; hai probe React sử dụng component isolation và query hook thật.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/components/auth/SupportQueryIsolation.tsx:17`; `MProjectFrontend/src/hooks/api/support/use-production-support.ts:12`; `MProjectFrontend/src/hooks/api/support/use-production-support.ts:35`.
- **Symbol:** `purgeSupportQueries`, `currentIdentity`, `supportQueryKeys`, `useSupportList`.
- **Evidence:** Isolation cancel rồi remove query, nhưng key chỉ chứa user ID, không chứa scope/generation. Với active observer, cached query bị xóa không đồng nghĩa hook đã xóa result đang giữ. Hai probe mount đồng thời `SupportQueryIsolation` và `useSupportList`: sau token event hoặc đổi active teams của cùng user, cache đã rỗng nhưng DOM vẫn hiện dữ liệu team cũ và chưa gọi API lại. Test sẵn có chỉ kiểm `client.getQueryData(...)` nên vẫn pass.
- **Finding:** Cơ chế isolation hiện chứng minh xóa cache entry, chưa đảm bảo boundary của dữ liệu đang render hoặc refetch ngay dưới scope mới.
- **Realistic failure scenario:** Nhân viên đang mở trang support khi membership/token thay đổi. Quyền server đã cập nhật nhưng màn hình tiếp tục hiện dữ liệu scope cũ cho đến một lần render/refetch khác. Với list/detail có timer, không nên diễn giải probe là dữ liệu tồn tại vĩnh viễn; vấn đề là không được loại ngay tại boundary.
- **Operational impact:** Hiển thị thông tin không còn thuộc scope hiện tại; operator có thể tiếp tục nhìn/đọc request cũ và hiểu nhầm việc đổi team đã cập nhật đầy đủ màn hình. Probe này không chứng minh server authorization bypass hay rò rỉ giữa hai user khác nhau.
- **Root cause:** Dựa vào cache removal trong effect bất đồng bộ để thay thế lifecycle của observer; scope change không đổi key/remount view. `keepPreviousData` cũng cần được giới hạn khi identity/scope đổi.
- **Recommended direction:** Đưa auth/scope generation vào query identity hoặc remount/gate phần support tại boundary; không giữ previous data giữa hai scope. Cancel request cũ, xóa state đang render và fetch dữ liệu mới bằng credential mới theo thứ tự rõ ràng. Giữ phân trang mượt trong cùng scope.
- **Required regression test:** Mount observer/page thật, đổi token, cùng user đổi team, logout/login user khác; kiểm DOM không còn dữ liệu cũ, response cũ bị chặn và query mới thật sự được gửi. Bao phủ list, drawer, facets và recovery-stations, thay vì chỉ kiểm QueryCache.
- **Estimated change scope:** Medium.

### R07-08 — API lỗi lần đầu bị trình bày như hàng đợi không có yêu cầu

- **Severity:** P2.
- **Confidence:** High; probe render page đã xác nhận.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/pages/SupportRequests/SupportRequests.tsx:119`; `MProjectFrontend/src/pages/SupportRequests/SupportRequests.tsx:292`; `MProjectFrontend/src/locales/en.ts:1967`.
- **Symbol:** `SupportRequestsPage`, `stale`, `initialLoading`, `Table.locale.emptyText`.
- **Evidence:** Cảnh báo chỉ hiện khi `isError && rows.length > 0`. Khi request đầu fail, `data` không có, `isPending=false`, `rows=[]`: trang vào Table với thông báo không có request phù hợp. Probe `review07-page-probe` đặt trạng thái lỗi tải và thấy empty message, không có alert. Nhánh background failure sau lần tải thành công trả danh sách rỗng cũng có cùng vấn đề.
- **Finding:** UI không phân biệt “server xác nhận không có request” với “không lấy được dữ liệu”.
- **Realistic failure scenario:** TE mở trang lúc backend/proxy lỗi 503, timeout hoặc support endpoint không khả dụng trong một lần chuyển phiên bản.
- **Operational impact:** Có thể bỏ sót việc triage vì màn hình tạo cảm giác không có yêu cầu; dữ liệu counts/facets cũng có thể thiếu mà không có trạng thái lỗi rõ.
- **Root cause:** Dùng số lượng row để quyết định có lỗi cần hiển thị; thiếu initial-error branch.
- **Recommended direction:** Render error và retry riêng khi chưa có dữ liệu thành công; hiển thị stale/failure ngay cả khi snapshot gần nhất là empty. Giữ empty message cho successful response với zero items. Dùng i18n hiện có và bổ sung message cần thiết cho ba ngôn ngữ.
- **Required regression test:** First load 503/timeout; empty-success rồi polling fail; có-data rồi polling fail; retry thành công trả zero items. Kiểm sự khác biệt trong DOM và khả năng retry.
- **Estimated change scope:** Small.

### R07-09 — Sync state dựa vào việc từng reconcile thành công, không phản ánh lỗi hiện tại

- **Severity:** P2.
- **Confidence:** High; probe trạng thái và call sites telemetry/Launcher xác nhận.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Support/ProductionSupportAgentService.cs:355`; `MProjectAgent/Support/SupportOutboxRuntime.cs:402`; `MProjectAgent/Services/AgentWorker.cs:292`; `MProjectAgent/Services/AgentStatusProvider.cs:82`; `MProjectLauncher/SupportUiPolicy.cs:39`.
- **Symbol:** `GetStatusAsync`, `RecordReconcileFailureAsync`, `BuildProductionSupportTelemetryAsync`, `SupportUiPolicy.BuildState`.
- **Evidence:** Với queue rỗng, chỉ cần `LastReconciledAtUtc.HasValue` là trả Synced; không xét lỗi reconcile mới hơn hoặc độ cũ của snapshot. Probe reconcile thành công rồi ghi `TemporarilyUnavailable`: `SyncState=Synced` cùng `SafeLastErrorCode` khác null. Heartbeat chuyển nguyên sync state này sang server. Core status chỉ đổi sang Offline nếu reachability chung của server false, nên không bắt được trường hợp core API healthy nhưng support API fail/rollback.
- **Finding:** State máy đọc có thể báo Synced trong khi current support state không xác minh được. Empty queue không đủ chứng minh state server đã đồng bộ.
- **Realistic failure scenario:** Server còn nhận heartbeat nhưng support API lỗi hoặc bị rollback; Agent đã từng reconcile trước đó và hiện không có queued operations. Current request cached có thể đã bị admin thay đổi mà trạm không biết.
- **Operational impact:** Telemetry gắn nhãn khỏe sai; Launcher có thể tiếp tục hiển thị current/không có request như bình thường. Safe error field vẫn tồn tại nhưng không được dùng để quyết định state chính.
- **Root cause:** “Đã từng sync” và “current state còn được xác nhận” dùng chung điều kiện; reconcile failure không tác động sync-state computation khi queue empty.
- **Recommended direction:** Theo dõi kết quả reconcile gần nhất và freshness riêng với delivery status; trả Offline/Unknown hoặc trạng thái stale tương thích khi lần kiểm hiện tại thất bại. Không chặn durable enqueue chỉ vì remote state chưa xác minh được. Truyền trạng thái này nhất quán vào UI và heartbeat.
- **Required regression test:** Successful reconcile → support-only 503, unauthorized hoặc version mismatch khi core reachability còn healthy và queue empty; sync không được Synced. Successful recovery phải phục hồi state. Bao phủ cả current null và cached active request.
- **Estimated change scope:** Small.

## Good decisions cần giữ

1. **ACK sau commit:** create/update receipt được trả sau SQLite transaction. IPC nói “saved to durable queue” và Launcher nói Agent đã lưu, sẽ tự đồng bộ; không tuyên bố server đã xử lý khi chỉ enqueue local.
2. **Durability và graph integrity:** WAL, synchronous FULL, foreign keys, checks/unique active successor, digest của exact JSON; graph validation chặn mapping không nhất quán, cycle và payload drift. Mapping create/canonical áp dụng atomically cho cả graph. Correction store giữ bản cũ và relink trong transaction.
3. **Server idempotency:** create receipt theo ComputerId/requestId, update theo operationId cùng canonical target fingerprint; replay trước current/employee validation cho phép phục hồi lost response sau resolve. Không lấy ComputerId từ body client. Stable lock namespace và partial unique index bảo vệ one-active-per-computer.
4. **Offline semantics:** không áp delivery-age cutoff cho create chưa nhận; log window tính từ ReportedAtUtc. Preview giữ snapshot TTL, không quét lại âm thầm lúc submit; tối đa ba FAIL logs, cùng PC; scanner có deadline, giữ gate cho traversal chưa kết thúc và phân biệt partial/no logs.
5. **Protocol và authentication:** route 404 không có typed problem vào PausedForServerVersion; 401 dùng token-generation recovery chung; Retry-After và timeout có phân loại. Update/report capabilities phân biệt phiên bản và station bundle giữ Agent/Launcher đồng bộ; OTA protocol không tăng theo IPC.
6. **Query/recovery authority:** visibility join ownership hiện tại và active membership, không dùng owner snapshot để cấp quyền. Recovery kiểm permission cả controller lẫn service, yêu cầu reason, serialize cùng request lock với Agent update, ghi timeline/audit trong luồng transaction. Reroute giữ riêng reported context để audit.
7. **Giữ state qua OTA:** state/outbox nằm ngoài replaceable bundle. `SelfUpdateSupportOutboxTests` kiểm cả apply và rollback với pending graph nguyên payload/ID/dependency; không dùng rollback để xóa queue hoặc chạy support Down migration.
8. **Observability có giới hạn label:** chỉ operation/outcome/status class/routing/sync/protocol/table được chuẩn hóa; không dùng note, username hoặc request ID làm label. Middleware nằm ngoài exception handler nên ghi nhận được typed error response được handler xử lý. Storage collector phân biệt estimated rows với số chính xác.

## Rejected suspicions và kết quả đối chiếu

| Nghi vấn | Kết luận dựa trên code/test hiện tại |
| --- | --- |
| Report ACK trước khi lưu bền vững | Bác bỏ: service nhận kết quả sau commit; test enqueue/capacity/rollback và receipt đều bảo vệ invariant này. Không đồng nghĩa retry IPC sau mất ACK đã có idempotency end-to-end. |
| GET current có thể tự coi local create đã được server nhận | Bác bỏ trong nhánh unmapped create bình thường: `ApplyReconcileAsync` giữ graph để POST create/replay xác nhận receipt. |
| Mất response create rồi current đã Resolved chắc chắn tạo request trùng | Bác bỏ với cùng ID/payload: server receipt được kiểm trước tạo mới; API client giữ original Created flag và chấp nhận replay của dedup đúng reporter semantics. |
| Bare HTTP 404 biến thành lỗi bắt operator nhập lại | Bác bỏ: API client tách route 404 không có stable code khỏi typed `request_not_found`, giữ queue và pause version. |
| Token rotation khiến support tự enroll lại hoặc tự tạo payload mới | Bác bỏ ở luồng support đọc được: dùng `SendAuthenticatedAsync` và token transition manager chung, giữ operation ID/payload. Mức bảo đảm auth tổng thể thuộc review `01`/`09`. |
| Admin và Agent luôn ghi đè trạng thái terminal vì dùng service khác | Chưa có bằng chứng lỗi đó: cùng request advisory lock, version concurrency, retry reload và kiểm active trước mutation. Không coi unit test SQLite là chứng minh PostgreSQL race đã chạy trong phiên này. |
| Snapshot owner cấp quyền cho team cũ sau reroute/owner đổi | Bác bỏ ở query backend: dùng live Model.OwnerTeam và active membership. Lỗi R07-07 nằm ở kết quả render của browser. |
| Queue full tự xóa pending rows để nhường chỗ | Bác bỏ: enqueue transaction bị từ chối; prune chỉ nhận closed fully-terminal graphs. R07-04/05 là các vấn đề khiến việc thu hồi không diễn ra như dự kiến. |
| Support telemetry có username/note làm Prometheus label | Bác bỏ: labels có allowlist; technical counters không chứa các nội dung này. |
| Memory còn nói support.report.v1/Stage-0 nên current Agent chưa có support | Bác bỏ: current code quảng bá report v2 khi service được inject; các ghi chú Stage-0 và tên test lịch sử không phải bằng chứng trạng thái runtime hiện tại. |

## Validation đã thực hiện

| Nhóm | Kết quả và giới hạn |
| --- | --- |
| Agent support/outbox/API/preview/compatibility/self-update | `dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProductionSupport\|FullyQualifiedName~SupportOutbox\|FullyQualifiedName~SelfUpdateSupportOutbox'`: **109 passed**, không failure. |
| Backend contract/model/create/update/recovery/query/API/migration/operational | `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore -p:OutputPath=bin/review07/ --filter 'FullyQualifiedName~ProductionSupport'`: runner báo **116 passed**. **9 ca PostgreSQL trả về sớm khi thiếu `MPROJECT_TEST_POSTGRES_CS`**, không thực thi DB assertion; không tính là PostgreSQL runtime evidence. |
| Frontend support page/hooks/isolation/i18n | Chạy Vitest trực tiếp bằng Node trên bốn test file: **11 passed**. Có cảnh báo jsdom về pseudo-element `getComputedStyle`, không có test failure. |
| C# review probes | Tái hiện các lỗi startup, stale target, hai dạng failed correction, retention và control prune, remote closure retry, InFlight recovery và stale sync bằng source Agent/SQLite thật cùng fake API/clock. Assertion của probe mô tả **hành vi lỗi hiện tại**, không phải acceptance test của bản sửa. |
| React review probes | Hai probe active-observer isolation và một probe initial-error rendering: **3 passed theo assertion tái hiện lỗi hiện tại**. |

Lệnh Backend output mặc định ban đầu bị DLL của tiến trình đang chạy khóa; đã dùng output riêng, không dừng tiến trình đó. `yarn.ps1` bị execution policy chặn, còn Yarn command không tìm được Vitest shim; đã gọi entrypoint Node hiện có. Esbuild cần quyền đọc thư mục cha ngoài sandbox để nạp config; lượt chạy được automatic approval cho phép. Probe .NET restore dùng package cache local để tránh phụ thuộc NuGet network. Không dùng các lỗi môi trường ban đầu làm finding production.

## Insufficient-evidence areas và gate cần giữ mở

- **PostgreSQL thật:** thiếu cấu hình test nên chưa chạy ba create/update concurrency tests, năm integration tests và một migrated catalog test. Cần chạy lại trên disposable database, đặc biệt concurrent create khác request ID, cùng operation ID, resolve-vs-InProgress, recovery-vs-Agent và audit rollback. SQLite/unit tests không xác minh advisory lock, SQL translation, isolation hay constraint behavior của PostgreSQL.
- **Power loss/Windows service/rollout:** probe dùng SQLite thật và fault injection nhưng không mô phỏng power-cut controller/storage hoặc pilot qua một ca nhà máy. Chưa chạy signed station bundle trên service thật với queue InFlight tại thời điểm OTA, rollback qua schema mới, hay fault giữa mọi commit/checkpoint.
- **Factory scan:** đọc scanner và chạy focused tests/synthetic tree; chưa đo cây log nhà máy thật dưới identity Agent. Không re-review raw parser của `08`.
- **IPC lost ACK:** local ID/operation ID được Agent tạo khi gọi service. Chưa có end-to-end fixture WPF cho commit local thành công rồi ACK pipe mất, operator retry sau timeout. Không suy ra HTTP idempotency tự bảo vệ cả đoạn Launcher→Agent.
- **Malformed success và mixed server instances:** tests hiện có bao phủ enum/JSON/size/known problem cơ bản, không chứng minh mọi tổ hợp response sai cấu trúc/ngữ nghĩa hoặc current/create/update bị route sang các bản Backend khác nhau. Cần fault fixture HTTP host đầy đủ trước kết luận compatibility rộng hơn matrix đã kiểm.
- **UI/browser thật:** React probes không thay thế browser smoke với JWT refresh thật, đổi scope thật và mutation/reload bị mất response. R07-02 đã xác nhận lỗi target ở service boundary nhưng chưa chạy nhiều phiên WPF thực tế.
- **Vận hành metrics:** chưa có số liệu production về cardinality, tốc độ tăng bảng/outbox, throughput hay truy vấn trên dữ liệu giữ 24 tháng. Heartbeat metrics hiện là mẫu histogram có giới hạn; không thể từ đó liệt kê chính xác PC nào có pending operation hoặc thao tác nào cần sửa.

## Thứ tự khắc phục đề xuất

Ưu tiên cô lập failure của support khỏi core Agent (R07-01), ràng buộc request identity và đóng đường correction (R07-02/03), rồi sửa retention/terminal reconciliation/reclaim (R07-04/05/06) trong một bộ fixture kiểm cả lost-response replay. Sau đó sửa boundary render, initial-error và sync freshness (R07-07/08/09).

Mỗi bản sửa cần chạy focused regression tương ứng trong finding, rồi hoàn tất các gate PostgreSQL/host/browser cần thiết cho chính thay đổi đó. Báo cáo này không đóng các mục rollout trong project memory/checklist và không thực hiện remediation production.
