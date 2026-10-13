# 02 — Station deployment job lifecycle

Review ngày 2026-09-08 (Asia/Bangkok), theo prompt `docs/code-review/prompts/02-station-deployment-job-lifecycle.md`. Không sửa production code.

## Kết quả

Không tái hiện được confirmed defect mới trong phạm vi review. Luồng hiện tại có các barrier đáng giữ cho dedup job, execution generation, cancellation, journal resume, install-root containment và rollback pin. Có 2 design risk cần kiểm chứng ở môi trường vận hành và 1 khu vực chưa đủ bằng chứng để kết luận.

| ID | Severity | Confidence | Category | Nội dung |
| --- | --- | --- | --- | --- |
| DEP02-01 | Medium | Medium | design risk | Cancellation phụ thuộc command delivery và trạng thái registry trong memory để dừng tiến trình đang chạy |
| DEP02-02 | Medium | Medium | design risk | Inventory drift không có bằng chứng đủ về recovery khi catalog cục bộ bị mất hoặc stale sau crash |

Severity ở trên phản ánh tác động khi điều kiện xảy ra; không phải bằng chứng deployment hiện tại đang lỗi.

## Trace và quan sát

`AssignPackageModal`/`DeploymentMatrix` gọi các endpoint assignment; service tạo hoặc tái sử dụng assignment, kiểm tra version Released và gọi các cổng enqueue/reconcile. `InstallationJobService.PollAsync` lọc assignment active, bỏ version chưa Released, tạo job theo `(ComputerId, VersionId)` và bắt unique violation PostgreSQL để trả manifest canonical. `ResolveManifestAsync` ràng buộc agent vào computer, job active và effective file/blob; capability được bind theo AgentId, JobId, SHA và expiry.

Agent poll drain tuần tự. `JobExecutor` tạo journal trước ACK, lưu execution id và stage sau từng transition. Resume sau restart tiếp tục từ stage durable; ACK/progress/complete đều mang execution id. Download có thể song song trong job, còn deploy và process supervision tuần tự. Install root được resolve dưới package GUID, manifest và entry-point paths được preflight kiểm tra containment/reparse point trước mutation. Uninstall dừng supervisor, xóa deployment/cache theo reference accounting và hoàn tất server job.

Assignment remove/deactivate đánh dấu active jobs Cancelled và enqueue `CancelJob` cho job Downloading/Installing; khi `AutoRemoveOnUnassign` bật, service tạo uninstall intents từ installed records và interrupted installs. Khi tắt cờ, không tạo uninstall job. Rollback watchdog chỉ xét pin mới trong detection window, kiểm tra crash-loop ratio và gọi rollback service có guard pin/version đã quan sát.

## Findings chi tiết

### DEP02-01 — Dừng job đang chạy phụ thuộc command delivery

- **Severity:** Medium
- **Confidence:** Medium; source thể hiện rõ boundary, nhưng chưa có production canary mất kết nối đúng thời điểm này.
- **Category:** design risk
- **File:** `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs` (`CancelActiveJobsForPackageAsync`); `MProjectAgent/Services/JobCancellationRegistry.cs`; `MProjectAgent/Services/JobExecutor.cs` (`ExecuteInstallJobAsync`, `ExecuteUninstallJobAsync`).
- **Symbol:** `CancelActiveJobsForPackageAsync`, `CancelJob`, `IsCancelled`, cancellation catches.
- **Evidence:** Server chuyển row sang `Cancelled` và chỉ enqueue `CancelJob` cho Downloading/Installing. Agent registry là singleton in-memory; executor kiểm tra registry trước các boundary download/deploy và hủy linked token. Không có durable cancellation marker trong journal.
- **Finding:** Nếu command bị trễ hoặc Agent restart trước khi nhận command, tiến trình/filesystem có thể tiếp tục một khoảng thời gian dù server đã hiển thị Cancelled. Resume cuối cùng thường tự loại journal khi resolve thấy job không còn active, nhưng khoảng thời gian và side effect giữa các boundary phụ thuộc polling/command retry.
- **Realistic failure scenario:** Remove assignment đúng lúc download vừa xong hoặc Agent mất mạng; server commit Cancelled, command chưa tới, Agent tiếp tục resolve/deploy trước khi poll lại.
- **Operational impact:** Station có thể chạy payload đã bị remove trong một cửa sổ ngắn; operator thấy trạng thái Cancelled nhưng đĩa/process còn thay đổi. Đây là tính nhất quán eventual, không phải bypass authorization.
- **Root cause:** Cancellation signal control-plane không durable và không được đọc lại tại mọi filesystem/process syscall boundary.
- **Recommended direction:** Giữ server Cancelled là nguồn sự thật; thêm cancellation version/marker vào journal hoặc resolve response, kiểm tra ngay trước rename/delete/launch, và đo thời gian từ cancel commit tới process stop.
- **Required regression test:** Fault-injection sau download, trước deploy; mất mạng/restart Agent; xác nhận không có deploy/launch sau cancellation và journal được reconcile khi reconnect.
- **Estimated change scope:** Medium

### DEP02-02 — Inventory recovery khi local catalog stale chưa được chứng minh đầy đủ

- **Severity:** Medium
- **Confidence:** Medium; có test drift/reconcile cơ bản, chưa có crash matrix đầy đủ cho catalog mất một phần.
- **Category:** design risk
- **File:** `MProjectAgent/Services/InventoryReporter.cs`; `MProjectAgent/Storage/AppCatalogStore.cs`; `MProjectBackend/MProject.Application/Services/Software/PcInventoryService.cs`.
- **Symbol:** inventory report/process and catalog load/reconciliation paths.
- **Evidence:** Inventory gửi installed files/effective manifest và server cập nhật `PcInstallationRecord`/drift state. Catalog là file cục bộ dùng cho launcher và được commit sau server Complete; journal có thể bị xóa sau catalog commit. Review chưa tìm thấy một durable catalog generation/checksum được gửi kèm inventory để phân biệt catalog stale với payload drift.
- **Finding:** Sau crash hoặc thủ công chỉnh sửa catalog, inventory có thể báo payload đúng nhưng launcher metadata (entry point/install root/version) cũ; server drift reconciliation không nhất thiết phát hiện metadata-only inconsistency.
- **Realistic failure scenario:** Process chết giữa catalog write/replace hoặc catalog bị restore từ backup cũ; payload mới tồn tại nhưng launcher vẫn trỏ entry point cũ.
- **Operational impact:** Station có thể không start đúng app dù deployment job đã Completed; chẩn đoán qua UI khó vì file hashes vẫn khớp.
- **Root cause:** Inventory contract tập trung vào deployed files và không chứng minh tính toàn vẹn/phiên bản của catalog metadata.
- **Recommended direction:** Gắn catalog generation/hash vào journal và inventory, validate catalog paths/PackageId/VersionId trước launch, và rebuild catalog từ durable manifest khi checksum không khớp.
- **Required regression test:** Kill process tại từng bước catalog atomic replace; khởi động lại với catalog thiếu/stale; inventory phải phát hiện và rebuild hoặc đưa station vào trạng thái rõ ràng.
- **Estimated change scope:** Medium

## Quyết định tốt cần giữ

- Unique active-job constraint và xử lý conflict trả manifest canonical chống duplicate poll.
- Execution ID, journal stages và conflict/gone handling giúp ACK/progress/complete idempotent sau restart.
- Preflight manifest containment trước download/deploy; canonical package roots và reparse-point checks.
- `AutoRemoveOnUnassign=false` được tôn trọng; remove chỉ tạo uninstall khi package cho phép.
- Rollback có detection window, crash ratio/minimum count và guard khi pin đã thay đổi.

## Nghi vấn đã loại bỏ

- Không thấy đường tắt để Agent của computer khác resolve/complete job: service luôn bind Agent → Computer.
- Không thấy việc heartbeat tự reset deployment state hoặc rollback chạy trên assignment cũ sau pin change.
- Không coi progress byte không đổi là watchdog failure: keep-alive vẫn được gửi.

## Chưa đủ bằng chứng

Chưa có canary fault-injection trên Windows Service/LocalSystem cho network loss ở từng filesystem boundary, cũng chưa có test catalog corruption sau power loss. Các kết luận trên không thay thế kiểm thử PostgreSQL concurrency và station runtime đã yêu cầu trong checklist.

## Kiểm chứng đã chạy

Lệnh focused backend đã được chạy nhưng build bị khóa bởi tiến trình `MProject.Api` đang giữ DLL trong `bin\Debug\net8.0`; test runner chưa nhận được kết quả hợp lệ. Các test hiện có được rà soát gồm `InstallationJobServiceTests`, `StationSoftwareAssignmentServiceTests`, `InstallationJobWatchdogServiceTests`, `StationRollbackWatchdogServiceTests`, `PcInventoryServiceTests`, `InstallationJobConcurrencyTests`, `JobCancellationBarrierE2ETests`, `JobJournalRecoveryTests`, install-root, supervisor-recovery và inventory tests.
