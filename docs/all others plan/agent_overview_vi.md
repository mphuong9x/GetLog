# MProjectAgent — Tổng quan (Tiếng Việt)

## Giới thiệu

`MProjectAgent` là một agent chạy trên máy client (chủ yếu Windows) có nhiệm vụ tự động tải, triển khai và quản lý phần mềm theo chỉ thị từ server trung tâm. Agent có thể chạy trực tiếp hoặc được cài làm Windows Service.

## Cách chạy (tóm tắt)

- Chạy trực tiếp: `MProjectAgent.exe run` hoặc chỉ `MProjectAgent.exe`.
- CLI hỗ trợ: `install`, `uninstall`, `enroll`, `scan-bootstrap`, `maintenance`, `help`.

## Thành phần chính và vai trò (dễ hiểu)

- AgentWorker: bộ điều phối chính — khởi tạo, chạy các vòng định kỳ: heartbeat (gửi trạng thái), poll (lấy job), inventory (báo cáo).
- ServerClient: giao tiếp HTTP với server (heartbeat, poll, download, báo tiến độ).
- TokenStore: lưu/đọc token enrollment và trạng thái agent trên đĩa (JSON).
- BlobCacheService + CacheIndex: cache file theo SHA-256, quản lý ref-count và thu hồi LRU.
- InstallDirectoryService: deploy file từ cache tới thư mục cài đặt.
- InstallDirScanner: quét thư mục cài sẵn để thêm blob vào cache (bootstrap).
- JobExecutor: thực hiện job (resolve -> download -> deploy -> launch).
- ProcessSupervisor: giám sát tiến trình ứng dụng (restart, health-check, crash-loop protection).
- InteractiveProcessLauncher: khởi chạy tiến trình trong session người dùng (để GUI xuất hiện khi agent chạy như service).
- AgentCommandHandler: xử lý lệnh từ server (CancelJob, Restart, StopApp, v.v.).
- InventoryReporter: kiểm tra file đã deploy và báo server.
- RuntimeStateStore: lưu/khôi phục trạng thái giám sát (PID, exit reason, restart counts).

## Luồng xử lý chính (ví dụ minh họa)

1. Agent khởi động → `AgentWorker` khởi tạo cache và token.
2. Nếu chưa enroll → thực hiện flow enroll để lấy token và lưu vào `TokenStore`.
3. Agent chạy vòng `heartbeat` (báo trạng thái) và `poll` (hỏi server có job không).
4. Khi có job: `JobExecutor` resolve manifest, download các blob thiếu qua `BlobCacheService`, deploy qua `InstallDirectoryService`, báo hoàn tất.
5. Nếu job có entrypoint → `ProcessSupervisor` khởi chạy và giám sát ứng dụng.
6. `AgentCommandHandler` xử lý lệnh từ server (hủy job, restart, stop, v.v.).

## Các khái niệm quan trọng (cho người mới)

- Manifest / Job: danh sách file và metadata server gửi cho agent.
- Blob / SHA-256: file được định danh bằng mã băm để kiểm tra tính toàn vẹn và tránh trùng lặp.
- Cache + RefCount: cache lưu blob; refcount cho biết blob đang được dùng bởi bao nhiêu bản cài; blob có refcount=0 có thể bị xóa.
- Heartbeat vs Poll: `heartbeat` báo trạng thái runtime; `poll` hỏi server có job mới.
- Crash-loop protection: supervisor dừng auto-restart nếu app liên tục crash theo policy.

## File tham khảo để đọc tiếp

- `MProjectAgent/Program.cs` — entry + cấu hình DI.
- `MProjectAgent/Services/AgentWorker.cs` — vòng điều phối chính (heartbeat/poll/inventory).
- `MProjectAgent/Services/JobExecutor.cs` — xử lý job: resolve/download/deploy/launch.
- `MProjectAgent/Services/BlobCacheService.cs` — cache và eviction.
- `MProjectAgent/Services/ProcessSupervisor.cs` — giám sát tiến trình.
- `MProjectAgent/Storage/TokenStore.cs` — enroll token handling.

## Hướng tiếp theo (gợi ý)

- Nếu muốn đi sâu: yêu cầu giải thích chi tiết một file cụ thể (ví dụ `JobExecutor.cs` hoặc `ProcessSupervisor.cs`) với chú thích theo dòng.
- Hoặc yêu cầu sơ đồ sequence (vẽ luồng) cho lifecycle của một job.

---

Tài liệu này tóm tắt các phần lõi của `MProjectAgent` bằng ngôn ngữ dễ hiểu cho người mới. Nếu muốn, tôi có thể mở rộng thành README chi tiết hơn hoặc thêm sơ đồ luồng.

## Giải thích chi tiết: `JobExecutor.cs`

### Mục đích

`JobExecutor` là thành phần chịu trách nhiệm thực thi một "job" mà server gửi — tức là: từ khi nhận job cho tới khi hoàn tất báo về server. Nó thực hiện các bước: xác nhận job, tải các blob (file) còn thiếu vào cache, deploy file vào thư mục cài đặt, báo tiến độ/hoàn tất, và yêu cầu `ProcessSupervisor` khởi chạy nếu job có entrypoint.

### Luồng thực thi (tóm tắt bước theo bước)

1. `ExecuteAsync(jobSummary, ct)` được gọi khi `AgentWorker` hoặc Poll loop phát hiện job Pending.
2. Ghi log bắt đầu, kiểm tra xem job đã bị hủy qua `JobCancellationRegistry` hay chưa.
3. Gọi `ServerClient.AckAsync(jobId)` để thông báo server rằng agent đã bắt đầu xử lý.
4. Gọi `ResolveAsync` để hỏi server phần nào của manifest cần tải (server biết file nào agent đã có trong cache); nhận về một `resolved` job mô tả files cần download và delta size.
5. Nếu cần download, `DownloadMissingAsync(resolved, ct)` sẽ tải đồng thời các blob thiếu, ghi vào `BlobCacheService.PutAsync`, và gửi progress định kỳ về server.
6. Sau khi download xong, `DeployAsync(resolved, ct)` gọi `InstallDirectoryService.DeployAsync` để copy từ cache vào thư mục cài đặt thực tế.
7. Báo `CompleteAsync(jobId, status)` cho server (Completed hoặc Failed) cùng số byte, thời gian, hoặc thông tin lỗi.
8. Nếu job có `EntryPointPath`, gọi `ProcessSupervisor.LaunchAndSuperviseAsync` để khởi chạy ứng dụng và bắt đầu giám sát.

### Phương thức chính và vai trò

- `ExecuteAsync(AgentManifestJob jobSummary, CancellationToken ct)`: điểm vào. Quản lý lifecycle của job, xử lý hủy (cancellation), báo lỗi an toàn và gửi kết quả.

- `ResolveAsync(AgentManifestJob jobSummary, CancellationToken ct)`: trước khi tải, agent gửi danh sách hash blob mà nó đã có (`haveHashes`) để server trả về manifest đã tối giản (chỉ những file cần tải). Nếu server không bao gồm job trong phản hồi, job có thể đã bị cancel/hoàn tất ở nơi khác.

- `DownloadMissingAsync(AgentManifestJob job, CancellationToken ct)`: tải các file thiếu. Đặc điểm quan trọng:
	- Sử dụng `Parallel.ForEachAsync` để tải nhiều file song song; concurrency giới hạn bởi `AgentOptions.MaxDownloadConcurrency`.
	- Mỗi file được tải qua `ServerClient.OpenDownloadStreamAsync` và ghi vào cache bằng `BlobCacheService.PutAsync`.
	- Ghi báo tiến độ (`ProgressAsync`) có throttle (giảm số lần gửi) sử dụng `ProgressReportThrottleSeconds`.
	- Nếu tất cả files đã có sẵn thì vẫn báo tiến độ "Installing" và trả về 0 bytes tải.

- `DeployAsync(AgentManifestJob job, CancellationToken ct)`: tính `installRoot` dựa trên `InstallRootOptions.Base` và tên package, gọi `InstallDirectoryService.DeployAsync` để thực hiện copy/ghi file.

- `TryLaunchWithSupervisorAsync(string installRoot, AgentManifestJob job, CancellationToken ct)`: nếu job có entrypoint, xây đường dẫn tới exe, kiểm tra tồn tại, và yêu cầu `ProcessSupervisor.LaunchAndSuperviseAsync` để khởi chạy và quản lý nó. Nếu tập tin không tồn tại, sẽ log warning và bỏ qua.

- `SafeReportFailureAsync(Guid jobId, Exception ex, long durationMs, CancellationToken ct)`: khi có lỗi, phương thức này cố gắng ghi lại trạng thái 'Failed' lên server cùng với mã loại lỗi (qua `ClassifyError`) và mô tả ngắn; dùng try/catch để tránh lỗi báo lỗi làm mất exception gốc.

### Xử lý hủy và lỗi

- Mọi bước chính đều tôn trọng `CancellationToken` và ném `OperationCanceledException` nếu bị hủy.
- Trước khi bắt đầu mỗi phần quan trọng (`resolve`, `download`, `deploy`) có kiểm tra `JobCancellationRegistry.IsCancelled(jobId)` để bỏ qua job nếu server đã yêu cầu hủy.
- Các ngoại lệ không phải `OperationCanceledException` được bắt ở `ExecuteAsync`, ghi log và gọi `SafeReportFailureAsync` để báo server trước khi rethrow.

### Tương tác với các component khác

- `ServerClient`: resolve manifest, open download stream, ack/complete/progress API.
- `BlobCacheService`: lưu blob đã tải, kiểm tra hash tồn tại, và cung cấp cơ chế incr/decr ref nếu cần.
- `InstallDirectoryService`: thực hiện deploy các file từ cache vào đích.
- `ProcessSupervisor`: khởi chạy và giám sát ứng dụng nếu job yêu cầu.
- `JobCancellationRegistry`: đánh dấu/kiểm tra job bị cancel.

### Những điểm cần lưu ý (cho người mới)

- Tính song song: việc tải file diễn ra song song nhưng có giới hạn concurrency; thiết kế này giúp tận dụng băng thông mà không quá tải hệ thống.
- Tính toàn vẹn: sau khi tải xong, `BlobCacheService` kiểm tra SHA-256 và kích thước để đảm bảo file không bị lỗi.
- Thiết kế bảo toàn trạng thái: agent luôn báo ack trước khi thực hiện thao tác nặng, và báo complete/failed sau khi xong — giúp server theo dõi chính xác.
- An toàn khi báo lỗi: `SafeReportFailureAsync` cố gắng báo lỗi nhưng không để lỗi báo lỗi phá vỡ luồng xử lý.

### Kết luận ngắn

`JobExecutor` là phần trung tâm thực hiện nhiệm vụ cài đặt phần mềm — nó kết nối mạng (server), lưu trữ (cache), thao tác file (deploy) và khởi chạy ứng dụng (supervisor). Hiểu `JobExecutor` giúp nắm rõ hầu hết luồng hoạt động chính của agent.
