# Kế hoạch triển khai chức năng báo lỗi sản xuất PD/TE

- Ngày lập: 2026-08-28
- Ngày review/chốt bổ sung: 2026-08-29
- Ngày hoàn tất rà soát sau khi khôi phục phiên CLI: 2026-08-30
- Ngày hoàn tất Giai đoạn 1 ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 2A runtime preview ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 2B typed API client ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 2C SQLite durable outbox core ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 2D delivery/reconcile/IPC/telemetry ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 3A Launcher layout/compact ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 3B Launcher QR tên máy ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 3C-3D Launcher PD/TE/support state ở mức code/test: 2026-08-31
- Ngày hoàn tất work package 3E-3F Frontend support/deep-link/cache isolation ở mức code/test: 2026-08-31
- Phạm vi hệ thống: MProjectLauncher, MProjectAgent, MProjectBackend, MProjectFrontend, MProjectAgent.Ipc.Contracts và MProject.ProductionSupport.Contracts
- Trạng thái: Giai đoạn 0-3 đã hoàn tất ở mức code/test. Agent quảng bá đủ `support.status.v1`, `support.preview.v1`, `support.report.v1` và `support.update.v1`; Launcher đã có layout compact, QR tên máy thuần cục bộ, modal PD/TE, trạng thái current/pending/sync, xử lý ContextMismatch/NeedsUserAction và nút mở web an toàn; Frontend đã có route/sidebar, list/counts/facets/detail/timeline, polling last-good/stale, Admin recovery, i18n, deep-link và cache isolation theo user/scope. QR không thuộc contract/API/web. Migration expand-only đã được kiểm chứng trên PostgreSQL 18 tạm thời nhưng chưa apply vào database dùng chung/production; phép đo scanner trên cây log nhà máy thật còn chờ vì máy phát triển không có `D:\UBNT_Test_Logs`; chức năng support chưa được deploy và các gate tích hợp/đóng gói/pilot vẫn thuộc Giai đoạn 4

## 1. Mục tiêu

Xây dựng một luồng hỗ trợ lỗi sản xuất đơn giản và tin cậy:

1. PD báo lỗi trực tiếp từ Launcher mà không cần đăng nhập.
2. Launcher tự lấy tối đa 3 lần FAIL liên tiếp gần nhất từ thư mục Sfis của UBNT Test Logs.
3. Agent lưu hàng đợi cục bộ và tự gửi lại khi máy chủ hoặc mạng tạm thời không khả dụng.
4. TE cập nhật tiến độ hoặc xác nhận đã sửa ngay trên Launcher bằng mã nhân viên và ghi chú.
5. Kỹ sư xem các yêu cầu thuộc Model do OwnerTeam của mình sở hữu trên trang web Yêu cầu hỗ trợ.
6. Quản trị viên xem được toàn bộ yêu cầu, kể cả yêu cầu chưa phân tuyến.
7. Mỗi máy chỉ có tối đa một yêu cầu đang hoạt động để tránh báo trùng.

Giải pháp ưu tiên tận dụng scanner, parser, xác thực Agent, cây tài nguyên và cơ chế OwnerTeam đã có. Phiên bản đầu không bổ sung đăng nhập phức tạp, realtime hoặc workflow nhiều cấp.

## 2. Các quyết định đã thống nhất

| Nội dung | Quyết định cho phiên bản 1 |
| --- | --- |
| Nguồn log | Chỉ Sfis tại D:\UBNT_Test_Logs; không dùng NoSfc hoặc Golden |
| Khoảng thời gian | Chỉ lấy log trong vòng 30 phút tại thời điểm PD báo |
| Quy tắc lấy lỗi | Lấy 1, 2 hoặc tối đa 3 FAIL liên tiếp mới nhất; gặp PASS thì dừng |
| Khi có từ 4 FAIL liên tiếp | Chỉ đính kèm 3 FAIL mới nhất |
| Không có FAIL phù hợp | Vẫn cho báo lỗi nhưng PD bắt buộc nhập ghi chú |
| Đăng nhập PD | Không yêu cầu |
| Nhận diện TE | Mã nhân viên chính là User.Username; trim, chuẩn hóa lowercase và phải khớp tài khoản Active |
| Mức xác thực TE | Username do TE tự khai báo, chỉ dùng attribution/audit vận hành; không phải xác thực chống giả mạo |
| Giới hạn TE | Không bắt buộc TE thuộc OwnerTeam của Model |
| Trạng thái | Mới, Đang xử lý, Đã sửa |
| Yêu cầu đang hoạt động | Mỗi máy chỉ có một yêu cầu chưa Đã sửa |
| Web phiên bản 1 | Kỹ sư chỉ xem; Admin có thêm thao tác recovery bắt buộc audit |
| Phạm vi hiển thị web | Chỉ cần là thành viên đang hoạt động của OwnerTeam hiện tại của Model, không phụ thuộc role Member/TeamLeader; quản trị viên xem tất cả |
| Model chưa có OwnerTeam | Vẫn nhận yêu cầu, đánh dấu Chưa phân tuyến và chỉ quản trị viên thấy |
| Log UNKNOWN/unreadable | Là ranh giới và cắt chuỗi FAIL; không nối FAIL ở hai phía |
| Thời gian log | Dùng FileModifiedAtUtc lấy từ LastWriteTimeUtc; không dùng timestamp trong tên file cho cửa sổ 30 phút |
| Log sai ngữ cảnh | Agent bỏ log có PC khác hostname; Backend từ chối nếu log này vẫn lọt vào payload. Model/Station không khớp mapping Backend thì request là ContextMismatch, admin-only |
| Định tuyến request offline | Dùng mapping tại thời điểm Backend nhận request; mismatch với log chuyển ContextMismatch |
| Mất mạng/máy chủ | Agent lưu bền vững và tự gửi lại |
| TE khi request còn local | Cho phép cập nhật và cho phép nhiều update offline; giữ thứ tự phụ thuộc create -> updates |
| Máy đã có request active | Không gộp note/log mới; trả canonical request hiện tại, lưu idempotency receipt và ánh xạ update local |
| Recovery | Admin được force-resolve/reroute với lý do bắt buộc và đầy đủ audit |
| Thiết kế Launcher | Làm mới toàn bộ theo ý tưởng ảnh mẫu, tối ưu cho màn hình tối thiểu 1366x768 ở 125% DPI |
| QR | Chỉ Launcher tạo cục bộ; chứa tên máy cục bộ đã chuẩn hóa. Web/Backend không tạo, lưu, hiển thị hoặc xử lý QR này |
| Mở web | Có nút mở nhanh trang Yêu cầu hỗ trợ |
| Dashboard Đã sửa | Đếm request được resolve trong ngày hiện tại theo Asia/Saigon |
| Thời gian chờ | Tính từ ReportedAtUtc, tức thời điểm PD bấm báo lỗi |
| Lưu lịch sử | 24 tháng online; sau đó archive/xóa theo quy trình được duyệt |
| Khôi phục dữ liệu | Mục tiêu RPO tối đa 24 giờ, RTO tối đa 4 giờ |
| Pilot | Cài thủ công trên một nhóm nhỏ máy do vận hành chọn, chạy ít nhất một ca và theo dõi đủ 24 giờ |
| Cohort OTA | Ngoài phạm vi v1; ghi backlog để triển khai sau và không làm chậm chức năng chính |

## 3. Đánh giá hệ thống hiện tại

### 3.1 Thành phần có thể tái sử dụng

- Agent đã liệt kê và đồng bộ metadata test log qua MProjectAgent\Services\TestResultScanner.cs; scanner hiện chưa parse result/error code.
- Cấu hình thư mục Sfis, NoSfc và Golden đã nằm trong MProjectAgent\Configuration\AgentOptions.cs và MProjectAgent\appsettings.json.
- Parser chuẩn đã được tách sang project thuần `MProject.TestResults`; Backend và Agent cùng tham chiếu đúng một implementation để lấy Model, Station, MO, PC, serial, result và error code.
- Dữ liệu test result và dịch vụ truy vấn hiện có tại TestResultRecord.cs và TestResultQueryService.cs.
- Cây tài nguyên hiện tại đã liên kết Computer với Station và Station với Model.
- Model đã có OwnerTeamId; UserTeam thể hiện thành viên đang hoạt động.
- Launcher và Agent đã giao tiếp bằng named pipe qua MProjectAgent.Ipc.Contracts.
- Agent đã có danh tính máy và cơ chế gọi API Backend.
- Backend đang phục vụ Frontend cùng origin, nên Agent có thể tạo `SupportRequestsUrl` từ `ServerUrl` đã cấu hình và kiểm tra để Launcher mở đúng trang web.

### 3.2 Khoảng trống cần bổ sung

- Scanner preview đã được đăng ký runtime, giữ snapshot bất biến bằng previewId opaque với TTL 2 phút và phục vụ Launcher qua IPC; phép đo read-only trên cây log nhà máy thật vẫn còn chờ pilot.
- IPC v2 đã quảng bá đầy đủ status/preview/report/update sau khi durable enqueue/reconcile được nối vào runtime.
- SQLite outbox đã persist state/dependency/canonical-remap, phục vụ durable report/update IPC, background delivery, persisted current snapshot và reconcile.
- Backend đã có thực thể, trạng thái, lịch sử cập nhật, receipt/idempotency và ràng buộc một yêu cầu hoạt động trên mỗi máy.
- Mô hình permission hiện tại không phù hợp với RequirePermission không có resource trên endpoint list/count: role Member/TeamLeader được scope theo Team resource, còn quyền xem support đã chốt theo active OwnerTeam membership.
- User chưa có EmployeeCode riêng; phiên bản 1 đã chốt dùng User.Username làm mã nhân viên.
- Launcher đã có QR tên máy cục bộ, khu vực báo lỗi/cập nhật TE, current/pending/sync state và mở web bằng URL đã kiểm tra.
- Frontend đã có trang Yêu cầu hỗ trợ, giữ deep-link nội bộ sau đăng nhập và xóa/cô lập React Query cache khi đổi user hoặc scope.
- Cơ chế Agent release hiện chỉ có một release active toàn cục, chưa hỗ trợ cohort OTA.

## 4. Kiến trúc đề xuất

Luồng tổng thể:

    PD/TE
      |
      v
    MProjectLauncher
      | named pipe
      v
    MProjectAgent
      | persistent outbox + Agent authentication
      v
    MProjectBackend
      | domain rules + database
      v
    Trang web Yêu cầu hỗ trợ

Nguyên tắc phân trách nhiệm:

- Launcher chỉ hiển thị giao diện, kiểm tra đầu vào cơ bản và gửi lệnh IPC.
- Agent đọc file cục bộ, quản lý hàng đợi, retry và gọi Backend bằng danh tính máy.
- Backend là nơi quyết định cuối cùng về máy, Model, OwnerTeam, routing status, trạng thái, Username TE và quyền truy cập.
- Frontend chỉ hiển thị dữ liệu Backend đã lọc; không tự quyết định quyền theo team.

Các nguyên tắc xuyên suốt:

- Thời điểm thao tác của Agent dùng cho nghiệp vụ/audit; thời gian máy chủ dùng cho thứ tự tiếp nhận và concurrency.
- Mọi create/update phải idempotent cả khi response bị mất.
- Request local và request canonical trên Backend có thể khác Id; Agent phải lưu ánh xạ bền vững.
- Kỹ sư thường chỉ đọc. Các thao tác recovery của Admin là ngoại lệ có permission riêng, lý do bắt buộc và audit.

## 5. Luồng nghiệp vụ chi tiết

### 5.1 PD báo lỗi

1. PD bấm Báo lỗi sản xuất trên Launcher.
2. Launcher yêu cầu Agent tạo preview.
3. Agent đọc các file Sfis gần nhất, bỏ log có PC khác hostname cục bộ và trả về tối đa 3 FAIL liên tiếp cùng error code, serial, FileModifiedAtUtc và tên file nếu phân tích được.
4. Agent giữ snapshot preview bằng previewId opaque trong tối đa 2 phút; Launcher không gửi lại danh sách log có thể chỉnh sửa.
5. Modal hiển thị rõ các lỗi sẽ được gửi và trạng thái scan: Success, NoLogs, RootMissing, AccessDenied, TimedOut hoặc Partial.
6. Nếu có ít nhất một FAIL phù hợp, ghi chú PD là tùy chọn; trạng thái Partial vẫn phải hiển thị cảnh báo. Nếu không có FAIL phù hợp vì bất kỳ lý do nào, gồm PASS mới nhất, NoLogs hoặc lỗi scan, ghi chú PD là bắt buộc. PdNote tối đa 1000 ký tự.
7. Khi PD xác nhận, Agent kiểm tra preview chưa hết hạn; nếu hết hạn thì yêu cầu preview lại.
8. Agent tạo requestId duy nhất, ghi thao tác vào outbox bền vững trước rồi trả trạng thái đã tiếp nhận cho Launcher; việc gửi Backend chạy nền.
9. Backend suy ra Computer từ danh tính Agent, tiếp tục suy ra Station và Model từ cây tài nguyên tại thời điểm nhận request.
10. Backend so sánh metadata log với mapping hiện tại. Model/Station không khớp làm request có RoutingStatus ContextMismatch và admin-only; không dùng metadata Agent để tự đổi routing.
11. Nếu máy đã có yêu cầu hoạt động, Backend không gộp note/log mới mà trả canonical request hiện tại với created=false, đồng thời lưu create receipt cho requestId vừa nhận.
12. Agent lưu ánh xạ requestId local sang canonical request Id, cập nhật các operation phụ thuộc và Launcher hiển thị rõ Mới, Đang chờ gửi, Đã có yêu cầu hoạt động hoặc lỗi cần xử lý.

### 5.2 Quy tắc chọn log

Nguồn duy nhất là Sfis. Thuật toán:

1. Chỉ xét file .log trong các nhánh ngày hiện tại và ngày trước đó cần thiết để bao phủ cửa sổ 30 phút.
2. Dùng LastWriteTimeUtc, chuyển thành FileModifiedAtUtc, làm thứ tự chính; dùng relative path làm khóa phụ để kết quả ổn định khi thời gian bằng nhau.
3. Tại thời điểm PD bấm T, chỉ giữ file thỏa T - 30 phút <= FileModifiedAtUtc <= T cộng tolerance lệch đồng hồ đã cấu hình. Thời điểm Backend nhận không làm log mất hợp lệ khi outbox gửi trễ.
4. Chỉ giữ log có PC đã chuẩn hóa khớp Environment.MachineName/hostname cục bộ. Log PC khác bị bỏ qua và ghi warning không chứa dữ liệu nhạy cảm.
5. Phân tích kết quả bằng parser dùng chung được tách sang thư viện độc lập để Backend và Agent cùng sử dụng.
6. UNKNOWN, unreadable hoặc file biến mất trong chuỗi là boundary: dừng, không nối các FAIL ở hai phía. Agent ghi trạng thái kỹ thuật để chẩn đoán.
7. Duyệt các test result từ mới đến cũ:
   - Nếu bản ghi đầu tiên là PASS: trả về không có FAIL.
   - Nếu là FAIL: thêm vào kết quả.
   - Tiếp tục thêm FAIL cho tới khi gặp PASS, UNKNOWN/unreadable hoặc đủ 3 bản ghi.
8. Nếu có hơn 3 FAIL liên tiếp, chỉ trả 3 FAIL mới nhất.

Ví dụ:

| Chuỗi mới đến cũ | Kết quả gửi |
| --- | --- |
| FAIL | 1 lỗi |
| FAIL, FAIL | 2 lỗi |
| FAIL, FAIL, FAIL | 3 lỗi |
| FAIL, FAIL, FAIL, FAIL | 3 lỗi mới nhất |
| FAIL, FAIL, PASS, FAIL | 2 lỗi đầu |
| FAIL, UNKNOWN, FAIL | 1 lỗi đầu; UNKNOWN cắt chuỗi |
| UNKNOWN, FAIL | Không đính kèm lỗi; PD phải nhập ghi chú |
| PASS, FAIL, FAIL | Không đính kèm lỗi; PD phải nhập ghi chú |
| Không có log trong 30 phút | Không đính kèm lỗi; PD phải nhập ghi chú |
| Root mất quyền/truy cập hoặc scan timeout | Vẫn cho báo bằng ghi chú bắt buộc và hiển thị cảnh báo rõ, không giả làm NoLogs |

Không gửi toàn bộ nội dung raw log trong phiên bản 1. Agent gửi relative path, Source và FileModifiedAtUtc từ snapshot preview; Backend parse lại bằng parser dùng chung, liên kết best-effort với test result của cùng Computer/Source/file và lưu snapshot phục vụ hiển thị. Việc chưa liên kết được TestResultRecord không làm mất request.

### 5.3 TE cập nhật xử lý

1. TE bấm Cập nhật xử lý trên Launcher của máy đang có yêu cầu hoạt động.
2. Modal yêu cầu:
   - Mã nhân viên, chính là User.Username.
   - Trạng thái Đang xử lý hoặc Đã sửa.
   - Ghi chú bắt buộc, tối đa 1000 ký tự.
3. Agent cho phép thao tác cả khi create request còn local hoặc Backend offline; tạo operationId, lưu vào outbox và gắn dependency theo request local.
4. Có thể có nhiều update offline trên cùng request. Agent giữ thứ tự create -> updates, sau đó ánh xạ sang canonical request Id khi Backend xác nhận.
5. Backend trim/lowercase Username và chỉ chấp nhận User có Status Active; đây là attribution do TE tự khai báo, không phải xác thực mạnh.
6. Backend lưu snapshot Username canonical, tên người cập nhật, ghi chú, trạng thái và thời gian trong cùng transaction với việc đổi aggregate.
7. Khi Đã sửa được Backend xác nhận, yêu cầu kết thúc và máy có thể tạo yêu cầu mới.

Chuyển trạng thái được phép:

- Mới sang Đang xử lý.
- Mới sang Đã sửa trong trường hợp lỗi được xử lý ngay.
- Đang xử lý sang Đang xử lý để bổ sung cập nhật.
- Đang xử lý sang Đã sửa.
- Đã sửa là trạng thái kết thúc; không mở lại trong phiên bản 1.

Nếu thao tác Đã sửa còn nằm trong hàng đợi và chưa được Backend xác nhận, Launcher vẫn coi máy đang có yêu cầu hoạt động. Điều này tránh tạo yêu cầu mới trong lúc mạng gián đoạn. Một update NeedsUserAction không được chặn update sửa thay thế; operation mới phải supersede operation lỗi theo state machine outbox.

### 5.4 Kỹ sư xem trên web

- Chỉ cần là thành viên đang hoạt động của OwnerTeam hiện tại của Model thì được xem yêu cầu thuộc Model đó; không yêu cầu role Member/TeamLeader.
- Người thuộc nhiều team được xem tập hợp các Model do các team đó sở hữu.
- Khi OwnerTeam của Model thay đổi, quyền xem chuyển theo OwnerTeam mới.
- Quản trị viên có quyền toàn cục xem mọi yêu cầu.
- Yêu cầu không xác định được Model, Model chưa có OwnerTeam hoặc RoutingStatus là ContextMismatch được gắn Chưa phân tuyến và chỉ quản trị viên thấy.
- Kỹ sư không cập nhật trạng thái trên web. Admin chỉ có các thao tác recovery ở mục 5.5.

### 5.5 Admin recovery

- Admin có permission productionsupport.recover được force-resolve request khi Agent/máy không còn hoạt động hoặc operation không thể hoàn tất.
- Admin được reroute request Unassigned/ContextMismatch sau khi sửa hoặc xác nhận mapping Model/Station.
- Mọi thao tác bắt buộc có reason từ 1 đến 1000 ký tự, lưu ActorUserId, snapshot Username/tên, thời gian máy chủ và giá trị trước/sau trong timeline.
- Force-resolve đặt ResolvedAtUtc bằng thời gian máy chủ, không giả mạo thời gian TE báo.
- Reroute không thay đổi snapshot ban đầu; nó cập nhật routing hiện tại và thêm một audit event.
- API/UI recovery không hiển thị cho user thường và không thay thế luồng TE trên Launcher.

## 6. Thiết kế dữ liệu Backend

### 6.1 ProductionSupportRequest

ProductionSupportRequest kế thừa VersionedEntity để dùng optimistic concurrency.

- Id: GUID của request được tạo thực tế; thường là requestId do Agent tạo.
- ComputerId và ReportedByAgentId: bắt buộc, lấy từ danh tính Agent.
- StationId và ModelId: nullable khi cây tài nguyên chưa đầy đủ; Admin reroute có thể thay thế hai giá trị này.
- RoutingStatus: Routed, Unassigned hoặc ContextMismatch.
- SnapshotComputerName, SnapshotStationName, SnapshotModelName.
- SnapshotOwnerTeamId và SnapshotOwnerTeamName dùng cho lịch sử/audit; không dùng làm nguồn quyết định quyền hiện tại.
- Status: Open, InProgress hoặc Resolved.
- PdNote: plain text, trim, tối đa 1000 ký tự.
- ReportedAtUtc: thời điểm Agent ghi nhận thao tác; dùng tính thời gian chờ.
- CreatedAtUtc và UpdatedAtUtc: thời gian máy chủ.
- ResolvedAtUtc: null khi yêu cầu đang hoạt động; khi resolved dùng thời gian máy chủ xác nhận.

Ràng buộc/index bắt buộc:

- Partial unique index UX_ProductionSupportRequests_Computer_Active trên ComputerId với filter ResolvedAtUtc IS NULL.
- Check constraint bảo đảm Status = Resolved khi và chỉ khi ResolvedAtUtc IS NOT NULL.
- Index active admin theo Status, ReportedAtUtc, Id.
- Index owner list theo ModelId, RoutingStatus, Status, ReportedAtUtc, Id.
- Index history theo ModelId, ResolvedAtUtc, Id.
- Foreign key tới Computer, Agent, Station và Model dùng Restrict, thống nhất với cơ chế soft-delete hiện tại; snapshot giữ nguyên dữ liệu lịch sử.
- Không soft-delete lịch sử support trong thời gian retention online.

### 6.2 ProductionSupportLog

Mỗi yêu cầu có tối đa 3 bản ghi:

- Id.
- ProductionSupportRequestId.
- Sequence từ 0 đến 2, trong đó 0 là lỗi mới nhất.
- Source, bắt buộc là Sfis.
- RelativePath và FileModifiedAtUtc.
- TestResultRecordId nếu liên kết được dữ liệu đã đồng bộ.
- SnapshotResult, ErrorCode, SerialNumber, MoNumber, ModelName, StationName, PcName và các metadata cần hiển thị.

- Unique index theo ProductionSupportRequestId và Sequence.
- Check Sequence từ 0 đến 2, Source = Sfis và SnapshotResult = Fail.
- Không cho trùng normalized relative path trong cùng request.
- TestResultRecordId dùng SetNull để snapshot support tồn tại kể cả test result bị dọn.
- Backend parse lại RelativePath và reject atomically toàn create nếu một log không hợp lệ; việc không tìm thấy TestResultRecord chỉ là best-effort link failure.

### 6.3 ProductionSupportUpdate

- Id do server tạo.
- OperationId nullable; bắt buộc và duy nhất cho thao tác Agent, nullable cho admin action được server tạo.
- ProductionSupportRequestId.
- UpdateType: TeStatus, AdminReroute hoặc AdminForceResolve.
- NewStatus nullable; bắt buộc cho TeStatus/AdminForceResolve.
- EmployeeUserId, SnapshotEmployeeUsername và SnapshotEmployeeName cho TeStatus.
- ActorUserId, SnapshotActorUsername và SnapshotActorName cho admin action.
- Old/NewStationId, Old/NewModelId và Old/NewRoutingStatus cho reroute.
- Note hoặc Reason: plain text, trim, bắt buộc, tối đa 1000 ký tự.
- ReportedAtUtc cho Agent action và CreatedAtUtc do server cấp.

OperationId có unique index để cùng một thao tác retry nhiều lần chỉ tạo một update. Timeline có index ProductionSupportRequestId, CreatedAtUtc, Id. Cùng operationId nhưng request hoặc payload khác trả support.idempotency_key_reused.

### 6.4 ProductionSupportCreateReceipt

Receipt giải quyết idempotency cả khi requestId được dedup sang một active request khác:

- RequestId do Agent tạo; có unique index theo cặp ComputerId và RequestId.
- ComputerId và SubmittedByAgentId.
- ResultRequestId là canonical request trả cho Agent.
- Created và PayloadHash của payload đã chuẩn hóa.
- CreatedAtUtc và LastReplayedAtUtc.

Backend tra receipt trước mọi rule active-request. Cùng key và cùng hash trả lại cùng logical result; cùng key khác hash trả 409 support.idempotency_key_reused; key của máy khác không làm lộ dữ liệu. Request, logs và receipt phải commit atomically. Receipt được giữ ít nhất bằng retention request là 24 tháng.

### 6.5 Transaction, concurrency và retention

- Create được serialize theo Computer bằng transaction/row lock hoặc PostgreSQL advisory lock; partial unique index là lớp bảo vệ cuối.
- Insert ProductionSupportUpdate và đổi trạng thái/routing aggregate phải nằm trong cùng transaction.
- Khi DbUpdateConcurrencyException xảy ra, Backend reload và đánh giá lại transition; không ghi đè Resolved bằng một InProgress cũ.
- Replay operationId được tra trước kiểm tra request active để replay của một Resolved thành công vẫn trả đúng kết quả.
- Lưu request/update/log/receipt online 24 tháng. Sau đó archive/xóa bằng job và quy trình được duyệt, có metric dung lượng và cảnh báo trước ngưỡng.
- Với quy mô dưới 2.000 máy, tối đa khoảng 1,46 triệu request và 4,38 triệu support-log trong hai năm ở giả định cực đại một request/máy/ngày; chưa cần partition ở v1 nhưng phải benchmark/index và theo dõi table/index growth.

## 7. Thiết kế API

### 7.1 API dành cho Agent

GET /agent/v1/support-requests/current

- Trả yêu cầu đang hoạt động của Computer gắn với Agent.
- Trả trạng thái, các log, cập nhật gần nhất và thông tin định tuyến.
- Không nhận ComputerId từ Launcher hoặc query string.
- Khi không có request, trả 200 với request bằng null để Agent reconcile nhất quán.

POST /agent/v1/support-requests

Payload dự kiến:

    {
      "requestId": "guid",
      "reportedAtUtc": "utc timestamp",
      "note": "ghi chú PD",
      "logs": [
        {
          "source": "Sfis",
          "relativePath": "...",
          "fileModifiedAtUtc": "utc timestamp"
        }
      ]
    }

Quy tắc:

- logs có từ 0 đến 3 phần tử.
- Chỉ chấp nhận source Sfis và result FAIL.
- Nếu logs rỗng thì note bắt buộc.
- Backend parse lại relativePath, kiểm tra độ dài/định dạng và yêu cầu T - 30 phút <= FileModifiedAtUtc <= T + clock-skew tolerance, trong đó T là reportedAtUtc. Không so cửa sổ này với thời điểm server nhận.
- ReportedAtUtc quá xa tương lai trả stable validation code; delivery trễ hợp lệ không bị từ chối chỉ vì đã qua 30 phút. Nếu áp dụng giới hạn tuổi cho create chưa từng được Backend nhận, giá trị và hành vi NeedsUserAction phải được khóa ở Giai đoạn 0; Agent không được âm thầm xóa operation. Replay đã có receipt luôn theo retention của receipt.
- Log có PC khác hostname bị từ chối; Model/Station mismatch không dùng để route mà làm request ContextMismatch/admin-only.
- Backend tra ProductionSupportCreateReceipt trước. Cùng requestId và payload hash trả cùng logical result; khác hash trả 409 support.idempotency_key_reused.
- Nếu đã có yêu cầu hoạt động khác trên máy, trả request đó với created=false, idempotentReplay=false và canonicalRequestId; không gộp note/log nhưng vẫn lưu receipt cho requestId mới.
- Request mới trả 201; replay hoặc dedup trả 200. Response luôn có request, canonicalRequestId, created và idempotentReplay.

POST /agent/v1/support-requests/{requestId}/updates

Payload dự kiến:

    {
      "operationId": "guid",
      "status": "InProgress | Resolved",
      "employeeUsername": "User.Username được nhập như mã nhân viên",
      "note": "mô tả xử lý",
      "reportedAtUtc": "utc timestamp"
    }

Quy tắc:

- Backend chỉ cho Agent cập nhật yêu cầu đang hoạt động của chính Computer đó.
- employeeUsername được trim/lowercase và phải khớp User có Status Active; không có EmployeeCode riêng trong v1.
- note bắt buộc, tối đa 1000 ký tự.
- Backend tra operationId trước kiểm tra trạng thái; replay của operation Resolved đã thành công phải trả cùng logical result.
- Cùng operationId khác request/payload trả 409 support.idempotency_key_reused.
- Chuyển trạng thái/concurrency không hợp lệ trả stable error code; update và đổi request commit atomically.
- Request Id trên URL là canonicalRequestId đã được Agent reconcile.

### 7.2 API dành cho Web

- GET /api/v1/support-requests
  - Phân trang, pageSize mặc định 25 và tối đa 100; mọi sort có Id làm tie-breaker.
  - Lọc theo active/resolved, trạng thái, Model, Station, khoảng ngày và từ khóa.
  - Mặc định chỉ trả yêu cầu đang hoạt động trong phạm vi quyền.
- GET /api/v1/support-requests/counts
  - Trả số lượng Mới và Đang xử lý hiện tại; Đã sửa chỉ đếm request resolved trong ngày hiện tại theo Asia/Saigon.
  - Dùng cùng ApplyVisibility và filter với list.
- GET /api/v1/support-requests/facets
  - Trả Model/Station filter options đã giới hạn theo phạm vi quyền; không tải toàn bộ danh mục không cần thiết.
- GET /api/v1/support-requests/{id}
  - Trả chi tiết log và timeline cập nhật.
- POST /api/v1/support-requests/{id}/reroute
  - Chỉ productionsupport.recover; reason bắt buộc; chỉ dùng cho Unassigned/ContextMismatch.
- POST /api/v1/support-requests/{id}/force-resolve
  - Chỉ productionsupport.recover; reason bắt buộc; dùng khi Agent/máy không thể hoàn tất luồng thường.

Mọi endpoint đọc áp dụng đúng một hàm ApplyVisibility tại service/query layer. Nếu người dùng truy cập trực tiếp một id ngoài phạm vi, API trả 404 để không làm lộ sự tồn tại của dữ liệu. Frontend không tự lọc OwnerTeam.

### 7.3 Quyền và thuật toán visibility

Thêm hai permission quản trị:

- productionsupport.read_all: xem mọi request, kể cả Unassigned/ContextMismatch; cấp global cho Admin hoặc role toàn cục tương đương.
- productionsupport.recover: reroute và force-resolve; mặc định chỉ cấp Admin.

User thường không cần productionsupport.read. Điều kiện xem là:

1. User đăng nhập và có Status Active.
2. User có UserTeam đang hiệu lực theo khoảng half-open StartTime <= now < EndTime, hoặc không có biên tương ứng.
3. Request có RoutingStatus Routed, Model còn xác định và Model.OwnerTeamId thuộc một active TeamId của user.

Member/TeamLeader/Viewer không phải điều kiện độc lập; một active OwnerTeam membership là đủ theo quyết định nghiệp vụ. Controller dùng Authorize để yêu cầu đăng nhập, sau đó service áp dụng ApplyVisibility cho list/count/facets/detail. Global productionsupport.read_all bỏ qua owner filter và thấy cả unassigned. Cần chuẩn hóa một helper active membership, không copy các predicate EndTime > hoặc >= khác nhau.

### 7.4 Error contract và retry semantics

ProblemDetails phải có code ổn định, traceId, retryable và fieldErrors an toàn. Tối thiểu có:

| Nhóm | Ví dụ code | Hành vi Agent |
| --- | --- | --- |
| Delivered/replay | support.created, support.replayed, support.active_exists | Ack local, lưu canonical Id và reconcile |
| Retry | support.temporarily_unavailable, HTTP 408/429/5xx, network/timeout | Retry exponential có jitter; tôn trọng Retry-After |
| Auth paused | support.agent_unauthorized/HTTP 401 | Dừng gửi, kích hoạt credential recovery; không hammer server |
| Reconcile | support.concurrent_update, support.request_not_current | GET current, remap rồi đánh giá lại |
| Needs user action | support.employee_invalid, support.validation_failed | Đưa item sang NeedsUserAction; không chặn item sửa thay thế |
| Idempotency misuse | support.idempotency_key_reused | NeedsUserAction/technical alert; không tự đổi payload cùng key |
| Version mismatch | support.server_version_unsupported hoặc capability thiếu | PausedForServerVersion và retry sau deploy; không coi 404 route của Backend cũ là validation vĩnh viễn |
| Foreign/out of scope | support.request_not_found/HTTP 404 | Không lộ request của máy/user khác; reconcile hoặc dừng theo operation type |

## 8. Thay đổi trong Agent

### 8.1 Dịch vụ đọc lỗi gần nhất

- Tạo project .NET thuần dùng chung, ví dụ MProject.TestResults, chứa parser, parse outcome và result kind; Backend và Agent cùng reference project này. Không đặt parser trong IPC contracts và không để Agent reference toàn bộ Application của Backend.
- Tách phần liệt kê file dùng chung từ TestResultScanner, nhưng tối ưu riêng đường quét gần nhất: lọc thư mục ngày sớm, kiểm tra cancellation ở từng cấp/file, materialize enumeration bên trong try/catch và giới hạn một lượt scan đồng thời.
- Chỉ truy cập nguồn Sfis cho chức năng hỗ trợ.
- Kết quả preview chứa previewId opaque, expiresAtUtc, trạng thái scan, error code, serial, FileModifiedAtUtc và relative path đã kiểm tra không rooted/không có `..`.
- Trạng thái scan tối thiểu gồm Success, NoLogs, RootMissing, AccessDenied, TimedOut và Partial. RootMissing/AccessDenied/TimedOut không được trình bày như không có FAIL; PD vẫn có thể báo bằng ghi chú sau khi thấy cảnh báo.
- UNKNOWN hoặc file không đọc/không nhận diện được là boundary cắt chuỗi FAIL. File biến mất trong lúc đọc được ghi nhận là Partial/UNKNOWN thay vì âm thầm nối hai FAIL ở hai phía.
- Preview có TTL 2 phút. Khi xác nhận sau TTL, Agent yêu cầu quét lại; Backend vẫn parse lại metadata nhận được để phòng sai khác phiên bản hoặc payload bị sửa.
- Đo thời gian quét trên cây log thật trong pilot; scan phải có cancellation budget và không được giữ named-pipe connection vô hạn.

### 8.2 Persistent outbox

Lưu SQLite dưới:

    C:\ProgramData\MProjectAgent\runtime\support-outbox.db

Thiết kế:

- Dùng Microsoft.Data.Sqlite đã có trong Agent; bật WAL, foreign keys và durability phù hợp. Mọi enqueue, đổi trạng thái, canonical remap và dequeue được transaction hóa.
- Mỗi operation có OperationId, type, localRequestId, canonicalRequestId nếu đã biết, payload/digest, dependency, state, attemptCount, nextAttemptAtUtc, createdAtUtc và safe last error.
- State machine tối thiểu: Pending, InFlight, RetryScheduled, PausedForAuth, PausedForServerVersion, NeedsUserAction, Delivered và Superseded. Nếu Agent dừng khi InFlight, lần khởi động sau đưa operation về trạng thái có thể retry an toàn.
- FIFO chỉ áp dụng cho operation đủ điều kiện theo dependency của cùng request. NeedsUserAction hoặc Superseded không chặn operation độc lập; operation thay thế dùng OperationId mới và atomically supersede operation lỗi.
- Cho phép enqueue nhiều TE update khi create còn local. Delivery luôn giữ create trước updates và các update theo thứ tự nhập. Khi create được dedup sang request B, persist mapping local A -> canonical B rồi remap toàn bộ update phụ thuộc trước khi gửi.
- Một pending Resolved local khóa việc tạo PD mới cho tới khi Backend xác nhận, reconcile cho biết không còn request active, hoặc Admin recovery hoàn tất. Agent cũng atomically chặn PD thứ hai, không chỉ dựa vào Launcher.
- Thử gửi ở background ngay sau enqueue; IPC trả thành công ngay khi durable commit hoàn tất và tuyệt đối không chờ HTTP Backend.
- Retry lỗi mạng/timeout, 408, 429 và 5xx bằng exponential backoff có jitter, mặc định bắt đầu 15 giây và tối đa 5 phút; tôn trọng Retry-After. Với 401, pause queue và dùng chung luồng credential recovery thay vì gọi dồn dập.
- Phân loại theo stable ProblemDetails code tại mục 7.4, không chỉ theo HTTP status. 409 cần reconcile; 404 do Backend cũ/capability thiếu là PausedForServerVersion, không phải validation vĩnh viễn.
- Reconcile current khi startup, sau khi khôi phục kết nối/xác thực, sau create/update và khi nhận conflict. Kết quả reconcile cùng support snapshot được persist để Launcher không hiển thị sai sau restart.
- Đặt giới hạn cấu hình cho số operation/dung lượng, mặc định 1.000 operation hoặc 20 MB; khi gần đầy/disk full phải cảnh báo rõ và không báo thao tác đã được nhận nếu durable enqueue thất bại.
- Giữ operation terminal đủ lâu để điều tra rồi dọn theo retention cấu hình; không ghi nguyên note hoặc username vào technical log.

Việc ghi outbox phải thành công trước khi Launcher báo đã tiếp nhận thao tác.

### 8.3 IPC contracts

Mở rộng wire contract bằng ProtocolVersion, Capabilities, CorrelationId, typed payload/response và lỗi máy đọc được gồm ErrorCode, Retryable, FieldErrors. Version IPC độc lập với protocolVersion của manifest OTA hiện tại.

Thêm các operation:

- support-preview.
- support-report.
- support-update.

Mở rộng status response với:

- SupportRequestsUrl hoàn chỉnh, được Agent tạo từ origin đã cấu hình/kiểm tra.
- CurrentSupportRequest.
- PendingSupportOperations và tổng theo state.
- LastDeliverySuccessAtUtc, LastDeliveryError an toàn và LastReconciledAtUtc.
- SupportSyncState: Unknown, Offline, Syncing, Synced hoặc NeedsUserAction.

Giữ nguyên các operation status, run, stop và restart hiện tại để tương thích ngược. Launcher dùng capability negotiation để ẩn chức năng support nếu Agent cũ chưa hỗ trợ, thay vì thử operation rồi treo/lỗi khó hiểu.

Transport phải có connect/read/write/per-operation timeout, frame tối đa 64 KiB, cancellation cho preview và xử lý được client treo mà không chặn status hoặc client khác. Server nhận kết nối đồng thời có giới hạn; Launcher dùng single-flight cho refresh và command để timer không tạo request chồng nhau. Bổ sung wire-compat test cho DTO mới, Unicode, null, timestamp, oversized frame và cả serializer đang hỗ trợ.

### 8.4 Telemetry của Agent

Heartbeat bổ sung số operation Pending/RetryScheduled/NeedsUserAction, tuổi operation pending cũ nhất, lần delivery/reconcile thành công gần nhất, safe error code và phiên bản Agent/Launcher/IPC. Log correlation theo OperationId, RequestId, ComputerId và AgentId; không log nguyên ghi chú hoặc username. Các chỉ số này là gate bắt buộc của pilot, không chỉ phục vụ debug sau sự cố.

## 9. Thiết kế Launcher

### 9.1 Bố cục

Thiết kế lại gần với ảnh ý tưởng:

- Header: M-System TE và trạng thái kết nối Agent/Server.
- Khối nhận diện máy: Model, Trạm, PC, IP và QR hostname.
- Khu ứng dụng hiện tại: giữ nguyên khả năng run, stop và cảnh báo vận hành.
- Khu hành động nổi bật:
  - Thẻ đỏ Báo lỗi sản xuất (PD).
  - Thẻ xanh Cập nhật xử lý (TE).
  - Nút Mở Yêu cầu hỗ trợ trên web.
- Khu hoạt động gần đây: trạng thái yêu cầu hiện tại, các thao tác đang chờ đồng bộ và cập nhật TE mới nhất.

Kích thước rộng khoảng 1180x680 DIP chỉ là layout thường ở màn hình đủ lớn. Cấu hình nghiệm thu 1366x768 ở 125% còn khoảng 1093x570 DIP sau khi trừ taskbar, vì vậy phải có breakpoint compact theo `SystemParameters.WorkArea`: không cuộn ngang, hạ MinWidth/MinHeight, chỉ cuộn nội dung phụ và luôn nhìn thấy thao tác run/stop chính cùng hai hành động PD/TE.

### 9.2 Trạng thái thẻ PD

- Chưa có yêu cầu: cho phép tạo.
- Đang chờ gửi/đồng bộ: hiển thị số operation và không tạo PD thứ hai.
- Mới: hiển thị thời gian đã chờ.
- Đang xử lý: hiển thị TE/cập nhật gần nhất nếu có.
- Chưa phân tuyến/ContextMismatch: hiển thị đã tiếp nhận nhưng cần Admin phân tuyến; không gợi ý PD gửi lại.
- Gửi thất bại cần người dùng sửa: hiển thị nguyên nhân và cho tạo operation thay thế để supersede lỗi cũ.
- Đã sửa: trở lại trạng thái có thể tạo yêu cầu mới sau khi Backend xác nhận.

### 9.3 Modal PD

- Hiển thị số lỗi tìm được, trạng thái scan và thời điểm preview hết hạn.
- Mỗi lỗi hiển thị error code, serial, FileModifiedAtUtc đã đổi sang giờ địa phương để đọc và tên file.
- Không cho sửa thủ công danh sách log.
- Ghi chú tối đa 1000 ký tự, tùy chọn khi có FAIL và bắt buộc nếu không có FAIL.
- RootMissing/AccessDenied/TimedOut/Partial phải có cảnh báo khác với NoLogs; người dùng vẫn được báo bằng ghi chú.
- Có trạng thái loading, preview hết hạn, timeout và thông báo Agent không khả dụng. Nếu hết TTL 2 phút thì quét lại trước khi enqueue.

### 9.4 Modal TE

- Mã nhân viên chính là `User.Username`.
- Trạng thái Đang xử lý hoặc Đã sửa.
- Ghi chú bắt buộc, tối đa 1000 ký tự.
- Hiển thị request hiện tại, Model, Station, hostname và các error code để tránh cập nhật nhầm.
- Cho phép nhiều cập nhật khi offline/create còn local; UI hiển thị rõ thứ tự pending. Pending Resolved vô hiệu hóa PD mới cho tới khi server xác nhận.

### 9.5 Hành vi kết nối

- Agent offline: vô hiệu hóa PD/TE vì Launcher không thể ghi outbox.
- Backend offline nhưng Agent hoạt động: vẫn cho thao tác và hiển thị Đang chờ đồng bộ.
- Nút mở web chỉ dùng SupportRequestsUrl hoàn chỉnh do Agent cung cấp; URL phải thuộc HTTP/HTTPS origin đã cấu hình, không ghép từ input của người dùng.

- QR được tạo cục bộ bằng QRCoder 1.8.0 và chỉ mã hóa `Environment.MachineName` đã trim/normalize theo cùng quy tắc nhận diện Computer.
- Tên máy hiển thị và payload QR dùng chung đúng một giá trị cục bộ trong Launcher. QR không đi qua IPC/API, không được lưu vào Backend và không thuộc phạm vi Frontend.
- Kiểm tra gói publish Launcher có đầy đủ DLL của QRCoder.
- Mọi nút gọi IPC dùng single-flight, có timeout và trạng thái busy; double-click không enqueue hai operation. Modal có keyboard focus hợp lý, thông báo không chỉ dựa vào màu và dùng được với high contrast.

## 10. Trang web Yêu cầu hỗ trợ

Route:

    /support-requests

Tên hiển thị:

    Yêu cầu hỗ trợ

Chức năng phiên bản 1:

- Web không tạo, lưu, hiển thị hoặc giải mã QR tên máy; QR chỉ phục vụ nhân viên quét trực tiếp trên Launcher để lấy chuỗi tên máy.

- Mục sidebar kèm badge đỏ số yêu cầu Mới, dùng chung counts query với trang.
- Thẻ tổng quan Mới, Đang xử lý và Đã sửa hôm nay theo Asia/Saigon.
- Mặc định hiển thị yêu cầu đang hoạt động.
- Tab hoặc bộ lọc lịch sử Đã sửa có phân trang.
- Polling mỗi 15 giây; không dùng SignalR trong phiên bản 1.
- Danh sách hiển thị:
  - Trạng thái và thời gian chờ tính từ ReportedAtUtc, không phải thời điểm server nhận.
  - Model, Station, hostname.
  - OwnerTeam hoặc Chưa phân tuyến.
  - Ghi chú PD.
  - Tối đa 3 error code.
- Drawer chi tiết hiển thị metadata test và timeline cập nhật TE.
- Thứ tự yêu cầu hoạt động: Mới trước Đang xử lý, sau đó yêu cầu cũ nhất trước.
- Thứ tự lịch sử: mới được sửa gần nhất trước.
- Kỹ sư chỉ đọc. User có productionsupport.recover thấy thao tác Reroute và Force resolve; reason bắt buộc, có xác nhận và hiển thị kết quả audit.
- Bổ sung bản dịch Việt, Anh và Trung theo cấu trúc i18n hiện tại.

Facet Model/Station lấy từ endpoint owner-scoped tại mục 7.2, không tải toàn bộ catalog. Khi polling lỗi tạm thời, giữ last-good data và đánh dấu stale; không đưa badge về 0. Khi request đổi OwnerTeam hoặc API detail trả 404 vì mất scope, đóng drawer và purge detail cache.

Mọi React Query key của support phải chứa `me.userId`. Cancel và remove support queries khi logout, token bị thu hồi hoặc đăng nhập user khác để không lộ cache giữa tài khoản. Luồng đăng nhập lưu `pathname + search + hash` nội bộ; chỉ chấp nhận relative URL cùng ứng dụng để tránh open redirect. Nếu Launcher mở /support-requests khi chưa đăng nhập, sau đăng nhập thành công người dùng được trả lại đúng route khi còn quyền.

## 11. Bảo mật và audit

- PD không cần đăng nhập để giảm thao tác tại chuyền; yêu cầu vẫn được gắn với Agent và Computer đã đăng ký.
- Username TE chỉ là lời khai nhận diện người thực hiện để vận hành/audit v1, không chứng minh chính người đó đã nhập và không phải xác thực mạnh/non-repudiation.
- Backend không tin ComputerId, ModelId, StationId hoặc OwnerTeamId do Launcher gửi.
- Backend kiểm tra Agent chỉ thao tác trên yêu cầu của máy gắn với token đó.
- Username được trim, so khớp theo quy tắc Username hiện có và phải thuộc tài khoản active.
- Snapshot mã/tên TE được giữ trong lịch sử ngay cả khi tài khoản đổi tên sau này.
- Không gửi raw log để giảm dữ liệu và tránh vô tình đẩy bí mật trong file.
- Giới hạn độ dài note/username/path/error code, body API, IPC frame và rate theo Agent/Computer. Backend parse lại, kiểm tra ownership và không dùng relative path để truy cập filesystem.
- Named pipe chỉ cho interactive local users phù hợp; ưu tiên ACL InteractiveSid và deny NetworkSid sau khi kiểm tra tương thích môi trường. Luôn có timeout/frame limit/concurrency limit để client cục bộ không giữ pipe hoặc gửi payload vô hạn.
- Ghi structured audit cho create, update, reroute và force-resolve; audit recovery có actor, reason, before/after và timestamp. Technical log chỉ ghi ID/correlation/safe error code, không ghi nguyên note hoặc username.
- Nếu sau này cần xác thực TE mạnh hơn, có thể bổ sung quét thẻ hoặc đăng nhập ngắn mà không đổi mô hình dữ liệu lịch sử.

### 11.1 Capacity, retention và phục hồi

- Quy mô thiết kế v1: dưới 2.000 máy (dự kiến khoảng 1.000), khoảng 50 log/máy/ngày và dưới 20 người dùng web đồng thời.
- Chức năng chỉ lưu tối đa 3 metadata log cho mỗi request, không lưu toàn bộ khoảng 50 log/ngày. Kịch bản bảo thủ 2.000 máy x 1 request/ngày x 24 tháng tạo khoảng 1,46 triệu request và tối đa 4,38 triệu support-log; PostgreSQL đáp ứng được với index/phân trang đã nêu.
- Giữ request/log/update online 24 tháng. Chưa partition ở v1; theo dõi kích thước/index/bloat và bổ sung archive/partition khi xu hướng thực tế yêu cầu, không để job retention khóa bảng lâu.
- Mục tiêu dữ liệu support: RPO 24 giờ, RTO 4 giờ. Backup/restore drill phải fingerprint thêm các bảng ProductionSupport thay vì chỉ chứng minh database kết nối được.

### 11.2 Observability và pilot gate

- Backend metrics: create/update theo outcome, replay/dedup, latency/4xx/5xx, active unassigned/context-mismatch count và age, force-resolve/reroute count.
- Fleet metrics: phiên bản Agent/Launcher, pending depth, oldest pending age, NeedsUserAction và lần delivery thành công. Dashboard/alert không chứa note hay username.
- Trước rollout rộng phải chứng minh không có operation pilot bị mất, không có queue mắc kẹt, không có 5xx kéo dài, request unassigned được xử lý theo quy trình và restore đạt RPO/RTO.

## 12. Xử lý lỗi và trường hợp biên

| Tình huống | Hành vi |
| --- | --- |
| Máy chưa map Station/Model | Nhận yêu cầu, đánh dấu Chưa phân tuyến, admin-only |
| Model không có OwnerTeam | Nhận yêu cầu, admin-only |
| ComputerId/hostname dùng để định tuyến do client tự gửi | Không thuộc contract hoặc bị bỏ qua; luôn dùng Computer gắn với token Agent |
| PcName phân tích từ log khác Computer của token | Backend từ chối create bằng stable validation code; Agent bình thường đã lọc log này trước preview |
| Model/Station payload khác mapping Backend tại lúc nhận | Nhận request ở ContextMismatch, admin-only; Admin reroute, không để PD gửi lại |
| Bấm PD nhiều lần | Launcher chặn; Backend unique constraint và idempotency chặn lần cuối |
| Backend mất kết nối | Agent giữ outbox và retry |
| Agent dừng sau khi bấm gửi | Outbox được nạp lại khi service khởi động |
| Backend đã commit nhưng response mất | Retry cùng requestId nhận receipt/canonical request, không sinh request mới |
| Local request A trùng active request B | Không merge note/log A; lưu receipt A -> B, remap update phụ thuộc và UI chuyển sang B |
| Active request B đã resolve trước khi retry create A | Receipt A vẫn trả B/result ban đầu; không tạo request stale mới |
| TE nhập mã không tồn tại/inactive | Không lưu cập nhật; Launcher hiển thị lỗi và giữ request active |
| Nhiều TE update khi offline | Enqueue theo thứ tự; create được gửi trước, rồi từng update theo dependency/canonical Id |
| Update validation lỗi | Chuyển NeedsUserAction; operation sửa dùng key mới và supersede lỗi, không chặn operation độc lập |
| Log bị xóa giữa preview và gửi | Gửi metadata preview; Backend liên kết nếu có, không làm mất yêu cầu |
| UNKNOWN/unreadable giữa hai FAIL | Cắt chuỗi; không nối thành FAIL liên tiếp giả |
| Sfis root mất/AccessDenied/timeout | Cảnh báo khác NoLogs; vẫn cho PD báo bằng note bắt buộc |
| Delivery muộn hơn 30 phút | Vẫn hợp lệ nếu FileModifiedAtUtc nằm trong cửa sổ của ReportedAtUtc và timestamp qua kiểm tra skew |
| OwnerTeam thay đổi | Quyền xem chuyển theo owner hiện tại; snapshot cũ chỉ dùng audit |
| Hai request tạo đồng thời | Database unique constraint chọn một request active; request còn lại nhận bản ghi hiện tại |
| TE chọn Đã sửa khi offline | Ghi pending; chưa cho tạo request PD mới tới khi server xác nhận |
| Backend/Agent không cùng capability | Pause theo ServerVersion, cảnh báo vận hành và retry sau deploy; không chuyển 404 route thành lỗi terminal/NeedsUserAction |
| Máy/Agent không thể resolve request | Admin force-resolve với reason và audit; Admin có thể reroute Unassigned/ContextMismatch |

## 13. Kế hoạch triển khai theo giai đoạn

### Giai đoạn 0 — Khóa contract và spike rủi ro

Trạng thái 0A ngày 2026-08-30: đã hoàn tất mục 1 bằng contract assembly,
JSON fixtures và contract tests; quyết định clock-skew là 2 phút và v1 không áp
giới hạn tuổi delivery cho create chưa từng đến Backend. Chi tiết tại
`MProject.ProductionSupport.Contracts/CONTRACT.txt` và các JSON fixture cùng project.

Trạng thái 0B ngày 2026-08-30: đã hoàn tất mục 2 bằng IPC v2 contract,
fixture tương thích System.Text.Json/Newtonsoft, giới hạn/deadline transport và
state machine outbox có dependency chain, full-graph canonical mapping, restart
recovery và atomic supersede/relink. Agent hiện chỉ quảng bá capability IPC nền;
capability support vẫn tắt cho tới khi scanner, SQLite durable enqueue và HTTP
handler tồn tại. Chi tiết tại `MProjectAgent.Ipc.Contracts/IPC_CONTRACT.txt` và
các JSON fixture cùng project.

Trạng thái 0C ngày 2026-08-30: đã tách parser và enum kết quả sang project thuần
`MProject.TestResults`, dùng chung một implementation cho Backend/Agent và khóa 17
fixture outcome bằng cùng source test ở hai test assembly. Scanner spike chỉ đọc Sfis,
lọc đúng PC/cửa sổ 30 phút cộng 2 phút clock skew, có PASS/UNKNOWN/file-disappeared
boundary, tie-break ổn định, cancellation, timeout 8 giây và single traversal. Cây
synthetic 192 log/384 nhánh hoàn tất trong 264,7 ms; `D:\UBNT_Test_Logs` không có trên
máy phát triển nên phép đo read-only trên cây thật vẫn là gate bắt buộc trước khi bật
capability. Chi tiết tại `MProject.TestResults/PARSER_CONTRACT.txt` và
`MProjectAgent/Support/PREVIEW_SCANNER_SPIKE.md`.

Trạng thái 0D ngày 2026-08-30: đã khóa matrix máy đọc được gồm 7 tổ hợp mixed-version,
5 bước rollout, capability gates, expand/backfill/contract và reverse rollback giữ nguyên
StateDirectory/outbox. Agent API v1, IPC v2 và OTA manifest protocol v1 được tách độc lập;
updater tiếp tục từ chối OTA protocol 2 và package script bị contract test khóa ở v1.
Server phải deploy trước station; rollback sau traffic ưu tiên station bundle trước rồi
server `-SkipSchema`, không chạy support `Down`. Chi tiết tại
`MProject.ProductionSupport.Contracts/COMPATIBILITY_CONTRACT.txt`, fixture cùng project và
`docs/production-support-compatibility-matrix.md`.

1. Khóa enum/trạng thái domain, create receipt, routing status, stable error codes, response 201/200, clock-skew tolerance và giới hạn tuổi create chưa giao nếu áp dụng.
2. Khóa IPC ProtocolVersion/Capabilities, typed DTO, timeout/frame limit và state machine outbox/dependency/canonical remap.
3. Tạo project parser dùng chung và chạy parity test với dữ liệu log đại diện; đo preview trên cây log thật.
4. Chốt migration expand-only, compatibility matrix Agent/Launcher/Backend và rollback matrix.
5. Ghi rõ feature/capability disabled trên Agent cũ; không đổi protocolVersion của manifest OTA nếu updater cũ chưa hỗ trợ.

Kết quả: Backend, Agent và Frontend có thể phát triển song song mà không tự suy diễn contract.

### Giai đoạn 1 — Backend domain và database

Trạng thái 1A ngày 2026-08-30: đã thêm bốn entity
`ProductionSupportRequest`, `ProductionSupportLog`, `ProductionSupportUpdate` và
`ProductionSupportCreateReceipt`, các `DbSet`, optimistic concurrency cho request,
snapshot audit và toàn bộ EF metadata. Model khóa partial unique active request,
ba log Sfis/Fail tối đa theo sequence, normalized relative-path uniqueness,
operation/create idempotency indexes, timeline/queue indexes, `Restrict`/`SetNull`/`Cascade`
theo vòng đời và check constraint cho state/actor/reroute/hash. DDL PostgreSQL được
generate kiểm tra trong test; migration và model snapshot cố ý chưa thay đổi để dành cho
work package 1B. Focused ProductionSupport 24/24 pass; full Backend 986 pass và đúng bốn
lỗi LibGit2Sharp permission baseline, không có regression mới.

Trạng thái 1B ngày 2026-08-30: đã sinh migration expand-only
`20260830070559_AddProductionSupportDomain` và cập nhật model snapshot. `Up` chỉ có
`CreateTable`/`CreateIndex` cho bốn bảng support; không alter/drop/rename cấu trúc hiện hữu.
Tám migration contract test khóa column/FK/delete behavior/check/index, generated idempotent
PostgreSQL script và `HasPendingModelChanges=false`; focused ProductionSupport 32/32 pass.
Full Backend có 994 pass và đúng bốn lỗi LibGit2Sharp permission baseline. Chưa apply migration
vào database thật vì `MPROJECT_TEST_POSTGRES_CS`/`MPROJECT_TEST_POSTGRES_CONSTRAINT_CS`
không được cấu hình trên máy này; catalog integration test sẽ tự chạy khi CI cung cấp DB.
Production vẫn phải dùng artifact/deploy script, không auto-migrate và không chạy support `Down`
sau khi có traffic.

Trạng thái hoàn tất Giai đoạn 1 ngày 2026-08-31: đã triển khai create/update/current,
receipt và canonical response, optimistic-concurrency retry, một visibility predicate dùng chung
cho list/count/facets/detail, Agent API, Web API và Admin recovery API. Quyền
`productionsupport.read_all` và `productionsupport.recover` đã được khóa bằng test; user thường
chỉ thấy model hiện đang sở hữu. Reroute/force-resolve ghi timeline và structured audit.
Backend có metric outcome/status/latency, active routing age/count, recovery count và thống kê
dung lượng bốn bảng với label hữu hạn; retention online bị chặn cấu hình dưới 24 tháng.
Hai script backup/restore đã fingerprint đủ bốn support table và chặn vượt RTO 4 giờ; câu SQL
fingerprint của cả hai script đã chạy trên PostgreSQL 18, còn full `pg_dump`/`pg_restore` drill
được giữ ở release gate.

1. [x] Thêm enum và các entity ProductionSupportRequest, ProductionSupportLog, ProductionSupportUpdate, ProductionSupportCreateReceipt.
2. [x] Thêm mapping, migration, foreign key, check constraint, partial unique index và index truy vấn.
3. [x] Thêm service tạo/update/current, receipt/canonical response, concurrency handling và cùng một ApplyVisibility cho list/count/facets/detail.
4. [x] Thêm Agent API, Web API và Admin recovery API.
5. [x] Thêm productionsupport.read_all và productionsupport.recover; không tạo productionsupport.read làm điều kiện cho user thường.
6. [x] Thêm metric/audit, retention config và fingerprint support tables cho restore drill.
7. [x] Viết test PostgreSQL thật cho race, idempotency, visibility, routing, recovery và transition.

Kết quả xác minh: focused ProductionSupport 100/100 pass; PostgreSQL 18 provider gate 8/8 pass
trên cluster tạm thời đã được dừng và xóa; Backend build 0 warning/0 error; full Backend 1.062 pass
và đúng 4 lỗi LibGit2Sharp do quyền thư mục Temp, không có lỗi Production Support. API hoàn chỉnh,
Agent cũ không bị ảnh hưởng và Frontend/Agent mới có contract ổn định.

### Giai đoạn 2 — Agent, parser và IPC

Trạng thái 2A ngày 2026-08-31: đã đăng ký scanner Sfis dùng chung vào runtime Agent,
thêm preview service lưu snapshot bất biến trong bộ nhớ với previewId opaque dạng GUID v4 128-bit,
TTL đúng 2 phút và clone DTO ở biên để Launcher không thể sửa snapshot đã lưu. Handler IPC
đã phục vụ `support-preview`, kiểm tra typed request body và quảng bá động đúng duy nhất
`support.preview.v1`; `support-report`, `support-update` và support status vẫn khóa cho tới
khi durable outbox/reconcile tồn tại. Focused scanner/snapshot/IPC/wire suite 69/69 pass;
full Agent 340 pass và đúng 15 lỗi môi trường baseline; Launcher net48 build 0 warning/0 error.
Cây `D:\UBNT_Test_Logs` thật không có trên máy phát triển nên performance gate read-only vẫn
chờ pilot, và capability chưa được deploy ra station.

Mốc kiểm chứng 2A: baseline commit `7824c7e`, Windows NT 10.0.26200.0, PowerShell
5.1.26100.9278 và .NET SDK 10.0.400-preview.0.26322.102. Dependency assets ban đầu không
còn, nên đã restore sạch bằng `dotnet restore MProjectAgent.Tests\MProjectAgent.Tests.csproj -v minimal`
và `dotnet restore MProjectLauncher\MProjectLauncher.csproj -v minimal`. Các gate đã chạy là
focused filter scanner/snapshot/IPC/wire, full
`dotnet test MProjectAgent.Tests\MProjectAgent.Tests.csproj --no-restore -v minimal`, cùng
`dotnet build --no-restore -v minimal` cho Agent và Launcher. Full test chỉ có đúng 15 lỗi
quyền hệ điều hành đã nêu ở baseline, không có regression của work package 2A.

Trạng thái 2B ngày 2026-08-31: đã thêm typed client cho GET current, POST create và POST
update bằng Agent credential hiện tại, request timeout 30 giây và response-body cap 64 KiB.
Client kiểm tra outcome/status/canonical Id của response thành công, đọc stable ProblemDetails
code/traceId/fieldErrors, phân loại network/timeout/408/429/5xx thành Retry, tôn trọng cả
Retry-After delta và HTTP-date, và phân biệt typed `support.request_not_found` với bare 404
của Backend cũ. Bare 404, response quá cỡ hoặc response success không đúng contract được đưa
về `support.server_version_unsupported`/PauseForServerVersion. HTTP 401 dùng chung đúng một
singleton token transition manager với heartbeat/poll, không tự lặp HTTP request và vẫn trả
PausedForAuthentication nếu durable credential recovery lỗi. Technical log chỉ ghi path,
status, stable code và traceId, không ghi response body/note/username.

Mốc kiểm chứng 2B: baseline commit `7824c7e`, cùng môi trường Windows/.NET đã ghi ở 2A;
`dotnet restore MProjectAgent.Tests\MProjectAgent.Tests.csproj -v minimal` xác nhận toàn bộ
dependency up-to-date. Focused API/contract 20/20 và combined ProductionSupport/IPC 96/96
pass. Full Agent có 356 pass và đúng 15 lỗi môi trường baseline; Agent và Launcher net48
đều build 0 warning/0 error. Typed client chưa có background caller vì durable SQLite outbox,
delivery worker và reconcile thuộc các work package kế tiếp; report/update capability vẫn tắt.

Trạng thái 2C ngày 2026-08-31: đã thêm SQLite store tại
`StateDirectory\runtime\support-outbox.db`, bật WAL, foreign key, `synchronous=FULL`,
busy timeout và hard `max_page_count`. Schema lưu tên enum thay vì ordinal, có check/unique/FK
cho linear dependency graph và quota mặc định 1.000 operation/20 MiB. Create/update enqueue,
FIFO đủ dependency, claim InFlight, attempt count, RetryScheduled, safe error code, restart
InFlight -> Pending, full-graph canonical mapping và NeedsUserAction supersede/relink đều nằm
trong transaction. Store atomically chặn PD thứ hai khi request local còn active, chỉ giải phóng
sau Resolved đã Delivered; payload digest/corruption, future schema version, disk/persistence
failure và quota đều làm enqueue thất bại thay vì báo nhận sai. DI/config đã đăng ký store nhưng
chưa có IPC caller hoặc hosted delivery worker, nên `support-report`, `support-update` và
`support.status.v1` vẫn không được quảng bá.

Mốc kiểm chứng 2C: baseline commit `f2ffab1d`, restore sạch bằng
`dotnet restore MProjectAgent.Tests\MProjectAgent.Tests.csproj -v minimal`.
Focused SQLite store 13/13 và combined outbox/IPC contract 45/45 pass; full Agent 384/384
pass khi chạy ngoài restricted sandbox; Agent build 0 warning/0 error. Test bao phủ persistence
qua restart, concurrent enqueue, active-request race, dependency dispatch, attempt/retry,
canonical transaction rollback, supersede/relink rollback, quota, corrupt payload digest,
future schema và durable-path failure. Delivery/reconcile, status snapshot/telemetry và IPC
durable acknowledgement là work package 2D; capability tiếp tục tắt cho tới khi 2D hoàn chỉnh.

Trạng thái 2D ngày 2026-08-31: đã nối IPC report/update vào transaction durable enqueue và
chỉ trả receipt sau commit; report/update/status capability được quảng bá động cùng preview.
Persisted runtime giữ current request qua restart/offline, overlay pending Resolved, delivery/error
và reconcile timestamp. Hosted worker giao đúng dependency order, retry exponential từ 15 giây
tới 5 phút với jitter/Retry-After, pause theo auth/server version, reconcile sau startup, định kỳ,
auth recovery, success, conflict và lost response; reconcile authoritative remap toàn graph hoặc
import current Backend mà không làm mất create local chưa xác nhận. Update NeedsUserAction được
sửa bằng supersede/relink atomic. Graph đã đóng và hoàn toàn terminal được dọn theo retention cấu
hình mặc định 30 ngày. Heartbeat gửi sync state, pending/retry/needs-action count, oldest age,
delivery/reconcile time và stable safe error code; Prometheus chỉ dùng nhãn sync-state/IPC hữu hạn,
không dùng agent ID, note hoặc username.

Mốc kiểm chứng 2D: baseline commit `f2ffab1d`; focused Agent 2D 50/50 và focused Backend
production-support/API 25/25 pass. Full Agent 402/402, full Backend 1.075/1.075; Agent, Backend API
và Launcher net48 build 0 warning/0 error. Test bao phủ durable acknowledgement, restart snapshot,
crash/lost response, ordering create -> nhiều update, Retry-After, auth/version pause và auth
recovery reconcile, canonical remap/import, corrected NeedsUserAction, terminal retention, IPC
capability/typed errors và bounded metric labels. Chưa thực hiện station pilot, log-tree performance
gate, package/self-update-with-pending-outbox hay Launcher UI; các mục đó vẫn thuộc Giai đoạn 3/4.

1. Thêm project parser dùng chung; refactor file enumeration từ TestResultScanner với cancellation/error status.
2. Cài thuật toán preview tối đa 3 FAIL liên tiếp trong cửa sổ 30 phút theo FileModifiedAtUtc, chỉ Sfis và UNKNOWN cắt chuỗi.
3. Thêm typed API client đọc ProblemDetails/Retry-After và dùng chung credential recovery.
4. Thêm SQLite outbox, state machine, dependency ordering, receipt/canonical remap, reconcile và telemetry.
5. Mở rộng IPC contracts, capability negotiation và harden pipe transport.
6. Thêm test scanner/parser parity, outbox crash recovery, retry/reconcile và IPC concurrency/compatibility.

Kết quả: Launcher có thể thao tác ổn định kể cả khi Backend tạm thời offline, không báo nhận trước durable enqueue.

### Giai đoạn 3 — Frontend và Launcher

Hai nhánh bắt đầu sau Giai đoạn 0 và có thể chạy song song với phần phù hợp của Giai đoạn 1/2.

Trạng thái 3A ngày 2026-08-31: `MainWindow` đã chuyển sang khung 1180x680 DIP và tự
fit trong work area với lề 12 DIP mỗi cạnh. Breakpoint compact kích hoạt khi viewport
nhỏ hơn 1120x620 DIP; tại cấu hình nghiệm thu khoảng 1093x570 DIP, cửa sổ còn
1069x546 DIP, giảm padding/icon/card và ẩn riêng lịch sử hoạt động phụ để giữ vùng
ứng dụng. Scroll ngang bị khóa. Mỗi card ứng dụng có nút Chạy/Dừng 44 DIP bind lại
đúng `RunEnabled`/`StopEnabled`, đồng thời vẫn giữ double-click, context menu và xác
nhận dừng hiện hữu. Policy layout thuần có test cho 1366x768@125%,
1920x1080@100%/125% và contract XAML; focused 8/8, full Agent 410/410 và Launcher
net48 build 0 warning/0 error. Visual QA trên màn hình/DPI thật vẫn thuộc Giai đoạn 4;
QR, modal PD/TE, pending/support state và nút mở web chưa nằm trong work package này.

Mốc kiểm chứng 3A: baseline commit `6645e9a`, Windows PowerShell 5.1.18362.1171
và .NET SDK 10.0.400. Work package không thêm dependency; `dotnet restore` cho
Agent tests và Launcher xác nhận assets hiện tại up-to-date. Các gate cuối là
`dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --filter FullyQualifiedName~LauncherLayoutPolicyTests --no-restore -v minimal`,
full `dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore -v minimal`,
`dotnet build MProjectLauncher/MProjectLauncher.csproj --no-restore -v minimal` và
`git diff --check`; build server đã được shutdown sau xác minh.

Trạng thái 3B ngày 2026-08-31: Launcher tạo QR ngay tại máy bằng QRCoder 1.8.0,
dùng cùng một giá trị `Hostname` cục bộ đã chuẩn hóa cho cả tên PC hiển thị,
tooltip và payload QR. QR không đi qua IPC/API, không được Backend lưu và Frontend
không có code liên quan. Focused test giải mã lại PNG đúng hostname và khóa quy
tắc chuẩn hóa; layout test khóa kích thước QR ở profile thường/compact. Build
Launcher net48 bằng Visual Studio MSBuild thành công, output có `QRCoder.dll` và
không chứa decoder chỉ dùng trong test. Script package có fail-fast check cho
`QRCoder.dll`; chạy package đầy đủ và visual QA trên màn hình thật vẫn là gate
Giai đoạn 4.

Trạng thái 3C-3D ngày 2026-08-31: Launcher đã nối modal PD vào preview TTL hai
bước và durable report IPC; modal TE xác thực username/trạng thái/note và gửi durable
update. MainWindow hiển thị current request, pending operation/sync state, khóa PD khi
có request hoặc pending Resolved, xử lý rõ Offline, ContextMismatch và
NeedsUserAction, đồng thời chỉ mở `SupportRequestsUrl` HTTP/HTTPS hợp lệ. Các command
dùng IpcClient single-flight/timeout hiện hữu; modal có Enter/Esc, focus đầu vào,
automation name và system high-contrast brushes. Policy/state, validation và XAML
contract đã có focused tests; full Agent regression và Launcher net48 build đều qua.

Trạng thái 3E-3F ngày 2026-08-31: Frontend đã có route `/support-requests`, sidebar
badge, owner-scoped list/counts/facets/detail/timeline và polling 15 giây giữ
last-good data khi stale. Recovery reroute/force-resolve chỉ hiển thị theo permission
riêng và bắt reason; 404 detail đóng drawer và purge cache. Query key chứa userId,
support cache được cancel/remove tại token, user và active-team boundary. Login giữ
an toàn pathname/search/hash nội bộ, từ chối external return URL. i18n Việt/Anh/Trung,
focused quyền/cache/polling/stale/deep-link tests, full Frontend regression, lint và
production build đều qua. Browser smoke với Backend thật vẫn thuộc Giai đoạn 4.

Launcher:

1. [x] Tái cấu trúc MainWindow nhưng giữ nguyên chức năng chạy/dừng ứng dụng; thêm breakpoint compact.
2. [x] Thêm QR tên máy thuần cục bộ bằng QRCoder và kiểm tra dependency trong bundle; không thêm QR vào web/backend.
3. [x] Thêm thẻ/modal PD, TE, pending queue, ContextMismatch/NeedsUserAction và nút mở SupportRequestsUrl.
4. [x] Thêm single-flight, timeout, accessibility, keyboard/high-contrast và test ViewModel/state transitions.

Frontend:

1. [x] Thêm route, sidebar, API/query hooks theo active membership và Admin permission.
2. [x] Xây list/counts/facets/detail/timeline, polling last-good/stale và Admin recovery actions.
3. [x] Cô lập cache theo userId, purge ở auth boundary/404 và giữ deep-link nội bộ an toàn.
4. [x] Thêm i18n Việt/Anh/Trung và test quyền/cache/polling/detail/deep-link.

Kết quả: hoàn thành luồng tại chuyền và luồng theo dõi/recovery trên web.

### Giai đoạn 4 — Tích hợp, đóng gói và rollout

1. Chạy migration/test Backend trên PostgreSQL, test Frontend ở chế độ run, test Agent/IPC và smoke E2E create -> update -> resolve -> history.
2. Visual QA Launcher tại 1366x768@125% có taskbar và 1920x1080@100%/125%; xác minh run/stop/restart cũ không regression.
3. Package phải có Launcher, Agent, contracts, config, SQLite/provider, parser chung và QRCoder; chạy Launcher trực tiếp từ bundle và kiểm thử self-update/rollback khi outbox còn pending.
4. Tăng phiên bản Agent/Launcher/IPC đồng bộ. Backend/Frontend phải được deploy trước nhưng vẫn tương thích Agent cũ.
5. Trước khi dùng OTA, xác minh `SelfUpdateEnabled`, public key, chữ ký release và đường rollback trên fleet. Nếu chưa đủ điều kiện, tiếp tục rollout thủ công; không kích hoạt release active toàn cục chỉ để thử pilot.
6. Tạo artifact bằng `scripts/prepare-deploy.ps1`; deploy server bằng `scripts/update-server.ps1` theo `docs/deployment_rollback_runbook.md`. Trước bàn giao, sửa tham chiếu `docs/factory_deploy_guide.md` hiện không tồn tại trong `prepare-deploy.ps1` sang runbook thật hoặc bổ sung đúng tài liệu. Production giữ AutoMigrate=false, lưu DB dump/code archive/migration head và chạy smoke sau `/health/ready`.
7. Migration phải expand-only để rollback code an toàn. Thực hành rollback riêng Server, Frontend và Agent/Launcher; Backend cũ thiếu support route không được khiến queue thành lỗi vĩnh viễn.
8. Pilot cài bundle thủ công trên một số ít máy do vận hành chọn. Chạy ít nhất một ca sản xuất và theo dõi thêm 24 giờ; chỉ rollout khi đạt toàn bộ gate chức năng, queue, metric, audit và rollback.
9. Sau pilot đạt gate, đủ điều kiện self-update/signing và có phê duyệt rollout toàn cục mới publish OTA cho fleet theo cơ chế hiện có. Cohort/pause/promote/rollback OTA theo nhóm là backlog sau v1 và không làm chậm hoạt động chính.

## 14. Kế hoạch kiểm thử

### Backend

- Tạo request với 0, 1, 2 và 3 log.
- Từ chối hơn 3 log, source khác Sfis, log không phải FAIL hoặc nằm ngoài 30 phút so với ReportedAtUtc.
- Request tạo lúc T với log T-5 phút nhưng giao tới Backend lúc T+2 giờ vẫn hợp lệ; kiểm tra clock-skew/future timestamp riêng.
- Bắt buộc PD note khi không có log.
- Giới hạn note/username/path/payload và relative path traversal.
- Idempotency theo requestId/operationId, cùng key khác payload, mất response sau commit và replay sau khi canonical request đã resolve.
- Hai create đồng thời trên PostgreSQL thật: partial unique index chọn một active request và create receipt của cả hai ổn định.
- Active collision trả canonical request nhưng không merge note/log của request mới.
- Toàn bộ transition hợp lệ/không hợp lệ.
- Username active, inactive, không tồn tại; role/team của TE không ảnh hưởng quyền ghi qua Agent.
- Agent không thể cập nhật request của máy khác.
- Model/Station mismatch tạo ContextMismatch; Computer identity do client tự gửi bị bỏ qua, còn PcName phân tích từ log không khớp thì create bị từ chối.
- User active chỉ cần active OwnerTeam membership, bất kể Member/TeamLeader/Viewer; boundary StartTime/EndTime dùng half-open thống nhất.
- User inactive/không có membership không thấy dữ liệu; Admin read_all thấy tất cả và thấy Chưa phân tuyến/ContextMismatch.
- User không thấy id ngoài scope kể cả gọi trực tiếp.
- Đổi OwnerTeam làm quyền xem chuyển sang team mới.
- List/count/facets/detail dùng cùng ApplyVisibility; Resolved count theo ngày Asia/Saigon và wait từ ReportedAtUtc.
- Reroute/force-resolve chỉ productionsupport.recover, reason bắt buộc, before/after audit đầy đủ; user thường bị từ chối.
- Verify query plan/index và pagination trên dữ liệu cỡ 1,5 triệu request/4,5 triệu log hoặc dataset đại diện.

### Agent

- Chuỗi 0/1/2/3/4 FAIL.
- PASS và UNKNOWN/unreadable đều cắt chuỗi; cover FAIL-UNKNOWN-FAIL và file biến mất giữa scan.
- Cửa sổ đúng 30 phút theo FileModifiedAtUtc, case qua nửa đêm và timestamp bằng nhau có Id/path tie-break ổn định.
- Chỉ đọc Sfis.
- Phân biệt NoLogs, RootMissing, AccessDenied, TimedOut và Partial; cancellation trên cây lớn, scan đồng thời và preview hết TTL.
- Parser Agent/Backend cho cùng outcome trên bộ log fixture.
- SQLite outbox tồn tại sau restart/mất điện giả lập; crash trước/sau commit, InFlight recovery, database corrupt/disk full/đạt quota đều không báo nhận sai.
- Retry network/timeout/408/429/5xx với jitter/Retry-After; 401 pause và recovery; Backend cũ/capability thiếu pause theo version.
- NeedsUserAction không chặn item độc lập; operation sửa supersede item lỗi.
- Create local rồi nhiều update offline giữ đúng thứ tự; canonical A -> B remap toàn bộ dependency; pending Resolved chặn PD mới.
- Mất response sau server commit và restart trước dequeue không tạo trùng.
- Reconcile startup, reconnect, auth recovery, success và conflict.
- IPC mới không phá operation cũ; test hung client, oversized frame, timeout, status trong lúc preview, Backend treo và hai Launcher đồng thời.

### Frontend

- Route yêu cầu đăng nhập; visibility theo membership, Admin actions theo permission riêng.
- Owner-scoped list và admin list.
- Counts, active/history, sorting và pagination.
- Detail drawer hiển thị log và timeline đúng.
- Polling cập nhật dữ liệu; transient failure giữ last-good/stale và cleanup timer/request khi unmount.
- Cache isolation: account A -> logout/token expiry -> account B không thấy dữ liệu A; request đang bay bị cancel/remove.
- OwnerTeam/membership đổi khi drawer mở: 404 đóng drawer và purge cache.
- Deep-link giữ pathname/search/hash sau đăng nhập, chặn URL ngoài ứng dụng và xử lý route không còn quyền.
- Kỹ sư không có action; Admin recovery hiển thị đúng quyền, bắt reason/confirm và refresh timeline.
- i18n key parity Việt/Anh/Trung, keyboard/accessibility và browser-level direct-route/login.

### Launcher và package

- Layout tại 1366x768@125% với taskbar (~1093x570 DIP) và 1920x1080@100%/125%: không cuộn ngang, PD/TE và run/stop luôn thấy.
- Modal PD với/không có log.
- Modal TE validation, nhiều pending update và pending Resolved.
- Agent offline, Backend offline, Syncing, NeedsUserAction, ContextMismatch và version mismatch.
- Double-click/single-flight không enqueue trùng; timeout không treo UI; keyboard focus/high contrast không phụ thuộc màu.
- QR giải mã đúng hostname.
- Nút web chỉ mở SupportRequestsUrl hợp lệ và route/deep-link đúng.
- Run/stop/restart ứng dụng hiện tại không bị ảnh hưởng.
- Parser, SQLite/provider, QRCoder và IPC contracts có mặt trong gói; Launcher chạy được trực tiếp từ bundle.
- Self-update và rollback thực tế khi outbox có pending operation không làm mất queue.
- Xác minh `SelfUpdateEnabled`, public key/chữ ký release và bảo đảm release active toàn cục chỉ được bật sau gate pilot.
- Smoke sau deploy: Agent cũ vẫn hoạt động; Agent mới create/update/reconcile; rollback matrix cho Server/Frontend/Agent được diễn tập.

Lưu ý baseline hiện tại (cập nhật sau khi hoàn tất Giai đoạn 1 ngày 2026-08-31):

- Backend build 0 warning/0 error; full test có 1.062 pass và đúng 4 lỗi môi trường baseline do LibGit2Sharp không được truy cập thư mục Temp, không có lỗi Production Support.
- Agent build 0 warning/0 error; full Agent test sau 2D có 402/402 pass khi chạy ngoài restricted sandbox. Launcher net48 build 0 warning/0 error với IPC contract 2D.
- Focused ProductionSupport 100/100 và PostgreSQL 18 provider gate 8/8 pass; fixture PostgreSQL tạm thời đã được dừng và xóa sau test.
- Parser Backend 19/19, parser parity Agent 2/2 và scanner preview Agent 21/21 pass; phép đo synthetic là 264,7 ms cho 192 log/384 nhánh.
- Focused Agent 2D 50/50 và focused Backend production-support/API 25/25 pass; report/update/status capability đã bật cùng delivery/reconcile/status/telemetry.
- Compatibility matrix Backend 4/4 và Agent 4/4; Agent integration/package-script/OTA rejection 4/4 pass.
- Frontend đã chạy `node.exe node_modules\vitest\vitest.mjs --run`: 26 file/204 test pass trong 122,94 giây.

Các số trên chỉ là baseline tại thời điểm review, không thay cho release gate. Khi bắt đầu/hoàn tất implementation phải ghi commit, môi trường, clean dependency install và command cụ thể; phân biệt lỗi môi trường với regression của chức năng mới.

## 15. Tiêu chí nghiệm thu

1. PD tạo được yêu cầu từ Launcher mà không đăng nhập.
2. Danh sách gửi kèm đúng 1, 2 hoặc 3 FAIL liên tiếp mới nhất từ Sfis trong 30 phút tính theo FileModifiedAtUtc so với ReportedAtUtc; PASS và UNKNOWN/unreadable đều cắt chuỗi.
3. Không có FAIL vẫn báo được khi PD nhập ghi chú.
4. Root log lỗi được cảnh báo khác NoLogs nhưng PD vẫn báo được bằng ghi chú; preview hết TTL buộc quét lại.
5. Bấm lặp/race/mất response không sinh hai yêu cầu hoạt động; mỗi requestId luôn trả cùng receipt/canonical request và request trùng không merge note/log.
6. Thao tác PD/TE durable trong SQLite trước khi UI báo nhận, không mất khi Agent restart hoặc Backend/mạng gián đoạn; item NeedsUserAction/Superseded không khóa operation độc lập.
7. TE cập nhật được bằng `User.Username` active và ghi chú bắt buộc, kể cả create còn local; nhiều update offline được giao đúng thứ tự sau canonical remap.
8. Chỉ Backend xác nhận Resolved/Admin recovery mới giải phóng máy để tạo PD mới; pending Resolved vẫn khóa PD.
9. Model/Station mismatch được nhận ở ContextMismatch và chỉ Admin thấy; identity client không thay đổi Computer của Agent, còn log có PcName khác máy bị từ chối.
10. User active chỉ cần active OwnerTeam membership để xem đúng Model của team, bất kể role; Admin read_all xem tất cả.
11. Yêu cầu Unassigned/ContextMismatch không lộ cho user thường; Admin reroute/force-resolve bằng reason và có audit before/after.
12. List/count/facets/detail áp dụng cùng visibility; Đã sửa tính theo ngày Asia/Saigon và thời gian chờ từ ReportedAtUtc.
13. Cache web không lộ dữ liệu khi đổi tài khoản/mất scope; Launcher mở đúng trang và deep-link nội bộ được giữ sau đăng nhập.
14. Giao diện dùng tốt tại 1366x768@125% có taskbar, PD/TE và run/stop luôn thấy, không làm hỏng chức năng Launcher hiện tại.
15. Lịch sử request/log/update/receipt/recovery được lưu đủ audit nhưng technical log/metric không chứa nguyên note hoặc username.
16. Retention 24 tháng, RPO 24 giờ và RTO 4 giờ có backup/restore drill cho bảng support.
17. Pilot thủ công trên một số ít máy chạy ít nhất một ca và theo dõi thêm 24 giờ; queue/metric/smoke/rollback đạt gate trước rollout rộng.

## 16. Ngoài phạm vi phiên bản 1

- Tự động tạo request ngay khi phát hiện 3 FAIL.
- Tự động đóng request khi có PASS mới.
- SignalR, push notification hoặc âm thanh cảnh báo.
- Workflow chỉnh trạng thái thường từ web; ngoại lệ v1 chỉ có Admin reroute và force-resolve để recovery.
- Workflow phân công cá nhân, SLA/escalation nhiều cấp.
- Upload toàn bộ nội dung log hoặc file đính kèm.
- Xác thực TE bằng mật khẩu, badge hoặc sinh trắc học.
- Mở lại request đã Đã sửa.
- OTA target cohort/pause/promote/rollback theo nhóm máy; v1 pilot bằng cài thủ công, sau đó mới dùng OTA hiện có.
- Tự động reroute theo lịch sử/heuristic; v1 chỉ Admin reroute thủ công.
- Partition bảng ngay từ đầu; chỉ bổ sung khi metric dung lượng/query chứng minh cần thiết.

## 17. Điểm cần giữ khi bắt đầu code

- Dùng một project parser thuần chung cho Backend/Agent; không tạo parser thứ hai và không đặt parser trong IPC contracts.
- Dùng đúng một ApplyVisibility ở query/service layer; không để Frontend tự lọc OwnerTeam và không dùng resource-scoped permission sai ngữ cảnh.
- Không tin Model/Station/Team do Launcher cung cấp.
- Username TE v1 chỉ là attribution của tài khoản active, không quảng bá thành xác thực mạnh.
- Ghi SQLite outbox trước khi báo thao tác thành công; state/dependency/canonical mapping phải sống qua crash/restart.
- Database constraint, transaction, create receipt và idempotency là lớp bảo vệ cuối chống trùng/race.
- So cửa sổ log với ReportedAtUtc; delivery time không làm request offline hợp lệ thành lỗi.
- UNKNOWN/unreadable cắt chuỗi FAIL; RootMissing/AccessDenied/TimedOut không đồng nghĩa NoLogs.
- IPC có capability/version riêng, timeout/frame limit/concurrency; support-report không chờ Backend.
- Query cache support luôn gắn userId và được xóa ở auth/scope boundary.
- Stable error code quyết định retry/reconcile/user action; không suy luận chỉ từ HTTP status.
- Backend/Frontend phải được deploy trước Agent/Launcher mới nhưng vẫn tương thích Agent cũ; Agent và Launcher mới phát hành cùng bundle.
- Deploy theo `scripts/prepare-deploy.ps1`, `scripts/update-server.ps1` và `docs/deployment_rollback_runbook.md`; production không tự migrate.
- Pilot thủ công nhỏ không được kéo theo việc xây cohort OTA trong v1; cohort được giữ trong backlog.
- Theo dõi queue depth/age, ContextMismatch/Unassigned, API outcome và fleet version; retention/RPO/RTO phải có bằng chứng vận hành.

## 18. Hướng dẫn chọn Sol reasoning effort khi triển khai

Mục này quy định mức reasoning effort đề xuất cho GPT-5.6 Sol khi thực hiện từng work package. Đây là mức suy luận của model, không phải ước lượng người-ngày.

Theo OpenAI Docs, Sol dùng `medium` làm mặc định và hỗ trợ `none`, `low`, `medium`, `high`, `xhigh`, `max`. Dùng `high`/`xhigh` khi độ sâu suy luận đem lại lợi ích chất lượng; dành `max` cho phần khó nhất cần khám phá và xác minh sâu. Nếu Codex hiển thị `ultra`, chỉ dùng như chế độ điều phối nhiều workstream độc lập, không coi là mức thay thế `max` cho một tác vụ tuần tự.

Nguồn tham khảo hiện hành:

- https://developers.openai.com/api/docs/models/gpt-5.6-sol
- https://developers.openai.com/api/docs/guides/latest-model

### 18.1 Quy mô code dùng để hiệu chỉnh effort

Snapshot tại thời điểm lập hướng dẫn, không tính `bin`, `obj`, `node_modules`:

| Thành phần | Số file | Số dòng xấp xỉ |
| --- | ---: | ---: |
| Backend production C# | 337 | 35.991 |
| Backend tests | 71 | 27.507 |
| Agent production C# | 47 | 8.431 |
| Agent tests | 31 | 5.274 |
| Launcher C#/XAML | 6 | 1.182 |
| IPC contracts | 1 | 98 |
| Frontend TS/TSX/CSS | 295 | 53.356 |
| EF migrations sinh tự động | 86 | 132.246 |

Code migration sinh tự động không dùng để nâng effort một cách máy móc. Effort chủ yếu phụ thuộc concurrency, durability, security, compatibility và số thành phần bị ảnh hưởng.

### 18.2 Mức mặc định theo loại công việc

| Mức | Áp dụng |
| --- | --- |
| `medium` | Scaffold DTO/enum sau khi contract đã khóa, i18n, QR, đổi tên, chỉnh tài liệu và thay đổi cơ học phạm vi nhỏ |
| `high` | UI độc lập, parser, API/query thông thường, metric và unit/component tests |
| `xhigh` | Một feature xuyên 2-3 project, migration/index, authorization, cache isolation, retry client và compatibility matrix |
| `max` | Durable outbox, idempotency, race/concurrency, IPC hardening, crash recovery, self-update/rollback và E2E toàn luồng |
| `ultra` tùy chọn | Review contract cuối Giai đoạn 0 hoặc readiness review trước pilot khi có thể chia Backend, Agent/IPC, Frontend và rollout thành các workstream độc lập |

### 18.3 Effort theo work package

| Giai đoạn/phần việc | Effort đề xuất |
| --- | ---: |
| Giai đoạn 0 tổng thể nếu làm trong một phiên | `max` |
| Domain enum, DTO và API response 201/200 | `xhigh` |
| Receipt, canonical ID và idempotency contract | `max` |
| Outbox state machine, dependency và canonical remap contract | `max` |
| IPC version/capability/error contract | `max` |
| Parser dùng chung và scanner spike | `high` |
| Compatibility và rollback matrix | `xhigh` |
| Backend entity/enum/EF mapping | `high` |
| Backend migration, FK, check constraint và partial unique index | `xhigh` |
| Backend create service và ProductionSupportCreateReceipt | `max` |
| Backend update/state transition/concurrency | `max` |
| ApplyVisibility cho list/count/facets/detail | `xhigh` |
| Admin reroute/force-resolve/audit | `xhigh` |
| Backend metrics, retention và restore fingerprint | `high` |
| PostgreSQL race/idempotency/security tests | `max` |
| Tách parser chung và parity tests | `high` |
| Scanner 30 phút, UNKNOWN và Partial | `xhigh` |
| Typed API client, ProblemDetails, Retry-After và auth recovery | `xhigh` |
| SQLite schema và durable enqueue | `max` |
| Dependency ordering, canonical remap và reconcile | `max` |
| IPC timeout/frame/concurrency/capability | `max` |
| Crash/restart/disk-full/hung-pipe tests | `max` |
| Launcher layout/breakpoint compact | `high` |
| Launcher QR và mở SupportRequestsUrl | `medium` |
| Launcher modal PD/TE và validation | `high` |
| Launcher pending/NeedsUserAction/canonical state | `xhigh` |
| Launcher single-flight và IPC timeout | `xhigh` |
| Frontend route/sidebar/list/filter/drawer | `high` |
| Frontend counts/facets/polling/last-good | `high` |
| Frontend query cache isolation và xử lý mất scope | `xhigh` |
| Frontend deep-link và Admin recovery UI | `high` |
| Frontend i18n Việt/Anh/Trung | `medium` |
| E2E create -> update -> resolve -> history | `max` |
| Package dependency và wire compatibility | `xhigh` |
| Self-update khi outbox còn pending | `max` |
| Migration/deploy/rollback server | `max` |
| Pilot telemetry và go/no-go review | `xhigh` |
| Final acceptance/security/readiness review | `max`; `ultra` tùy chọn nếu review song song |

### 18.4 Quy tắc trước khi bắt đầu mỗi phần

1. Không gom toàn bộ Giai đoạn 1-4 vào một phiên Codex dài. Mỗi hàng rủi ro cao trong bảng trên nên là một work package riêng.
2. Trước khi sửa code, Codex phải thông báo `Sol effort đề xuất`, lý do ngắn gọn, phạm vi file/project và lệnh kiểm thử dự kiến để người dùng có thể điều chỉnh effort.
3. Nếu phạm vi phát hiện thực tế lớn hơn dự kiến, chạm thêm project, thay đổi contract hoặc xuất hiện concurrency/security risk, Codex phải đề xuất tăng effort trước khi tiếp tục phần mở rộng đó.
4. Không dùng `max` cho thay đổi cơ học chỉ vì repo lớn. Ngược lại, một thay đổi ít dòng nhưng ảnh hưởng durable state, authorization hoặc rollback vẫn phải dùng `xhigh`/`max`.
5. Cuối mỗi work package phải chạy kiểm thử tương ứng và ghi rõ phần đã hoàn tất, phần chưa xác minh và effort đề xuất cho work package kế tiếp.
