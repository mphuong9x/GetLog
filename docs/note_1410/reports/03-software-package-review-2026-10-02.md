# Review Software / Software Package — 2026-10-02

> **Kiểm tra và sửa lại SW-02 ngày 2026-10-06:** Source tại thời điểm kiểm tra vẫn cập nhật versions/assignments trực tiếp, trái với kết luận sửa SW-02 bên dưới; 3 regression test selection thất bại. Đã sửa lại bằng cách vô hiệu hóa request ngay khi đổi selection, xóa dữ liệu cũ, đánh số request riêng cho versions/assignments và chỉ cập nhật kết quả/loading/thông báo lỗi của request hiện hành. Các lời gọi refresh từ thao tác cũ cho package khác và phản hồi sau unmount được bỏ qua. Có xử lý cả A→B→A trong cùng một batch và package biến mất khỏi danh sách.
>
> Kiểm chứng lần sửa này: 25 test pass trong 4 file frontend, sau đó bổ sung ca package bị xóa và chạy lại toàn bộ hook: 14/14 pass (26 test độc lập tổng cộng cùng các file đã chạy). Bao phủ chuyển/bỏ selection, dữ liệu cũ, lỗi cũ trong khi package mới đang tải, unmount, refresh cùng package, A→B→A, lỗi request hiện hành và package biến mất. `tsc -b` và ESLint hai file hook/test đã pass trước khi thêm test cuối. Không thay đổi backend hoặc áp dụng migration trong lần sửa SW-02 này.

> **Cập nhật 2026-10-03:** Đã sửa SW-01 đến SW-06 theo yêu cầu tiếp theo. Nội dung review bên dưới giữ nguyên làm mốc trước sửa; xem [kết quả khắc phục và kiểm chứng](#kết-quả-khắc-phục-2026-10-03) cuối file. Migration mới chưa được áp dụng vào database triển khai.

## Kết quả

Phát hiện **6 lỗi còn tồn tại: 1 High (P1), 4 Medium (P2), 1 Low (P3)**. Ưu tiên xử lý quyền xóa package chứa bản đã phát hành, dữ liệu version bị lẫn khi chuyển package, và recovery khi mất phản hồi upload cuối.

Đã giữ nguyên source, test, migration và báo cáo cũ. File này là tài liệu mới duy nhất được thêm.

| ID | Severity | Confidence | Category | Vấn đề |
| --- | --- | --- | --- | --- |
| SW-01 | High / P1 | High | confirmed defect | Xóa package vượt qua yêu cầu publish áp dụng cho xóa version đã phát hành |
| SW-02 | Medium / P2 | High | confirmed defect | Phản hồi muộn của package A ghi đè danh sách version đang hiển thị cho B |
| SW-03 | Medium / P2 | High | confirmed defect | Mất phản hồi PATCH cuối khiến upload thành công ở server nhưng frontend không finalize |
| SW-04 | Medium / P2 | High | confirmed defect | Clone thất bại sau khi đã lưu package đích, để lại package dang dở và chặn retry cùng tên |
| SW-05 | Medium / P2 | High | confirmed defect | Kiểm tra trùng tên package không bảo vệ được hai request đồng thời |
| SW-06 | Low / P3 | High | confirmed defect | API download-url với Local trả URL không dùng được với endpoint hiện tại |

Confidence nói về bằng chứng code/hành vi đã xác minh, không nói rằng sự cố đã xảy ra trong production. SW-02 và SW-03 đã được tái hiện bằng phép kiểm chứng chạy code frontend hiện tại với response giả lập. Những finding backend còn lại được xác minh bằng call chain, điều kiện kiểm tra và schema; chưa có phép tái hiện API/PostgreSQL riêng cho từng finding.

## Phạm vi và mốc diff

- Thực hiện theo `docs/code-review/prompts/03-software-content-upload-and-blob-lifecycle.md`, gồm CRUD package, upload, release, download và blob lifecycle.
- HEAD: `9b20e51` trên `master`. Working tree sạch lúc bắt đầu; không có staged/unstaged diff để review như một patch chưa commit.
- Dùng `09b66bd` — commit thêm báo cáo review trước — làm mốc tham chiếu cho `git diff 09b66bd HEAD` ở các file thuộc phạm vi. Đây là đối chiếu trạng thái sau review trước, không phải tự xác định merge base của một PR.
- Đọc các thay đổi của SoftwareFileService, SoftwareVersionService, UploadReservationService, TusUploadHandler, BlobGcService, Local/MinIO/CasPublication và frontend upload. Lần theo code CRUD, UI, authorization và schema liên quan dù không nằm trong diff.
- **Không quy cả 6 lỗi cho những commit mới:** SW-01/02/05 đã nằm trong code nền được lần theo; SW-03/06 là lỗi cũ còn tồn tại; SW-04 là thiếu tính nguyên tử có sẵn, nay còn được kích hoạt cụ thể bởi validation cây file mới ở `SoftwareVersionService.cs:250`.
- Đã đọc UPL-001, UPL-002, REL-001, STO-001, STO-002 trong `MProjectFrontend/PROJECT_REVIEW_CHECKLIST.md`; các phần upload/software/storage liên quan trong PROJECT_MEMORY; cấu hình storage/TUS và đăng ký middleware trong Program.cs.
- Không tìm thấy AGENTS.md ở root hoặc các thư mục cha áp dụng trong workspace cho code/report này. File tìm thấy ở `docs/Agent_Solutions/AGENTS.md` thuộc phạm vi thư mục khác.
- Không review sâu Agent execution, Agent release archive, config override hoặc station capability. Chỉ đọc điểm nối cần thiết để xác định download authorization, reference và tác động của xóa package.

## Findings

### SW-01 — Quyền CRUD đủ để xóa cả package có Released version

- **Severity:** High / P1.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Api/Controllers/Software/SoftwarePackagesController.cs:105-110`; `MProjectBackend/MProject.Application/Services/Software/SoftwarePackageService.cs:251-255`.
- **Symbol:** `DeletePackage`, `DeletePackageAsync`.
- **Evidence:** Controller chỉ yêu cầu `ManageSoftwarePackagesCrud` (`software.package.manage`). Service lấy mọi version của package và soft-delete mà không kiểm tra status hay `PublishSoftwareVersion`. Ngược lại, `SoftwareVersionService.cs:543-549` yêu cầu publish khi xóa một version không phải Draft. `PermissionAuthorizationFilter.cs:49-52` chỉ kiểm tra action được khai báo; không bổ sung quyền publish. Hai mã permission độc lập trong `AppPermissions.cs:36-37`.
- **Finding:** Người bị từ chối xóa Released version qua endpoint version có thể xóa chính version đó bằng endpoint package. Đây là đường vòng qua boundary đã được thực thi ở service version.
- **Realistic failure scenario:** Tạo role tùy chỉnh có `software.package.manage`, không có `software.version.publish`; package P chứa Released version V. `DELETE /api/v1/software-versions/V` bị từ chối, nhưng `DELETE /api/v1/software-packages/P` đi qua CRUD guard rồi xóa P, V và assignments.
- **Operational impact:** Catalog đã phát hành biến mất khỏi truy vấn thông thường; active jobs bị cancel (`SoftwarePackageService.cs:276-286`). Với installation records đang Installed, service còn tạo uninstall jobs (`:289-313`). Không kết luận bytes bị xóa vật lý: GC hiện giữ lịch sử.
- **Root cause:** Cascade deletion của package tự thực hiện lifecycle mutation mà không dùng cùng authorization invariant với deletion của version.
- **Recommended direction:** Kiểm tra publish trước mọi mutation nếu package chứa version không phải Draft; đặt guard trong service và bảo vệ tình huống release xảy ra đồng thời. Giữ khả năng quản lý package chỉ có Draft cho role CRUD theo contract hiện tại.
- **Required regression test:** Role chỉ CRUD bị từ chối xóa package có Released/Deprecated version; không đổi package/files/assignments/jobs/refcounts. Role có quyền phù hợp thành công. Thêm trường hợp release thắng race với delete package.
- **Estimated change scope:** Medium.

### SW-02 — Chuyển package có thể hiển thị và thao tác version của package khác

- **Severity:** Medium / P2.
- **Confidence:** High; đã tái hiện trên hook thực.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/pages/Software/hooks/use-software-page.ts:241-257`, `:347-356`.
- **Symbol:** `loadVersions`, `loadPackageAssignments`, effect theo `selectedPackageId`.
- **Evidence:** Mỗi request gọi trực tiếp `setVersions(await getAllVersions(packageId))`; không có request identity, abort hoặc kiểm tra package hiện được chọn. Assignments dùng cùng kiểu cập nhật. `SoftwarePackages.tsx:358-360` truyền `selectedPackage` hiện tại và mảng `versions` độc lập xuống panel. Panel hiển thị tên package ở `VersionPanel.tsx:326`, rồi duyệt nguyên mảng ở `:433`.
- **Finding:** Kết quả của request cũ có thể được hiển thị dưới tên package mới; loading cũng bị tắt bởi request đã hết hiệu lực.
- **Realistic failure scenario:** Chọn A rồi chọn B; response B về trước, response A về sau. Phép kiểm chứng dùng `renderHook` với code `useSoftwarePage` hiện tại thu được `selectedPackageId = B`, `versions = [{ id: vA, softwarePackageId: A }]`, `loadingVersions = false`.
- **Operational impact:** Người dùng thấy sai danh sách và thông tin deployment. Nếu thao tác release/delete trên dòng đó, handler gửi `version.id` của A (`use-software-page.ts:594`, `:619`), nên có thể thay đổi version A dù tiêu đề màn hình đang là B. Server không thể nhận ra sự nhầm lẫn vì ID version vẫn hợp lệ.
- **Root cause:** State của dữ liệu bất đồng bộ không được ràng buộc với package tạo request.
- **Recommended direction:** Dùng request generation hoặc dữ liệu cache keyed theo packageId; chỉ commit kết quả/loading khi request còn là request hiện hành. Áp dụng đồng bộ cho versions và assignments; xóa/ẩn dữ liệu cũ khi đổi selection.
- **Required regression test:** Resolve B trước A sau khi chọn A→B; UI chỉ chứa version/assignment của B và thao tác gửi ID của B. Kiểm tra thêm bỏ selection hoặc xóa package khi request còn chạy.
- **Estimated change scope:** Small.

### SW-03 — Mất phản hồi PATCH cuối làm frontend bỏ dở finalize

- **Severity:** Medium / P2.
- **Confidence:** High; đã tái hiện nhánh frontend và đối chiếu teardown backend.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/api/software.ts:540-544`, `:617-629`; `MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:331-336`.
- **Symbol:** `uploadBlobWithTus`, `getTusOffset`, `CompleteBlobUploadAsync`.
- **Evidence:** Backend gọi `PublishAsync`, lưu reservation Uploaded, rồi xóa TUS session trước khi client nhận response. Khi fetch PATCH lỗi mạng, frontend retry bằng HEAD vào session cũ; HEAD 404 bị ném lỗi và không thuộc transient statuses (`software.ts:688-689`). `reviseVersionFiles` không bắt lỗi này để gọi upload-init kiểm tra reservation; `completeUpload` ở `:444` không được chạy.
- **Finding:** Recovery cho PATCH giữa chừng không xử lý trường hợp bytes cuối cùng đã được lưu và session đã bị dọn.
- **Realistic failure scenario:** Proxy hoặc mạng mất phản hồi ngay sau khi server publish thành công. Phép kiểm chứng chạy implementation thật với chuỗi response `POST 201 → HEAD 0 → PATCH TypeError sau commit → HEAD 404` cho kết quả `stored=true`, `finalized=false`, lỗi `Unable to read TUS upload offset.`
- **Operational impact:** UI báo upload thất bại; file list của version chưa được cập nhật dù bytes đã lưu. Người dùng phải khởi động lại thao tác; upload-init lần sau có thể tận dụng Uploaded reservation. Không khẳng định mất bytes hay bắt buộc upload lại toàn bộ.
- **Root cause:** Client coi tuổi thọ session TUS là nguồn trạng thái duy nhất sau lỗi mạng, trong khi durable reservation tồn tại lâu hơn session.
- **Recommended direction:** Khi mất response ở completion boundary, re-init/reconcile với reservation trước khi kết luận thất bại. Chỉ finalize khi server xác nhận Uploaded hoặc blob đã được xác minh; không coi mọi 404 là thành công.
- **Required regression test:** Mất response PATCH cuối và HEAD 404 phải hoàn tất bằng recovery mà không gửi lại bytes; 404 do session thực sự mất trước publication vẫn phải retry upload hoặc báo lỗi đúng. Test sẵn có tại `software-upload.test.ts:84` chỉ mô phỏng HEAD còn trả offset 3.
- **Estimated change scope:** Medium.

### SW-04 — Clone thất bại vẫn để lại package đích

- **Severity:** Medium / P2.
- **Confidence:** High từ thứ tự commit và đường throw xác định; chưa chạy fixture backend riêng cho toàn bộ clone package.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwarePackageService.cs:133-146`.
- **Symbol:** `ClonePackageAsync`, `SoftwareVersionService.CreateDerivedVersionAsync`.
- **Evidence:** Package đích được `SaveChangesAsync` ở dòng 134 trước khi clone version hoặc copy config. Không có transaction bao ngoài hay compensation khi các bước sau ném lỗi. Validation mới ở `SoftwareVersionService.cs:250-251` có thể throw trước khi tạo version. Test hiện có `SoftwareVersionServiceTests.cs:212` xác nhận clone version từ cây file xung đột bị từ chối, nhưng chỉ assert số version, không đi qua clone package.
- **Finding:** API clone báo thất bại nhưng đã tạo dữ liệu đích một phần. Retry cùng tên bị kiểm tra trùng tên chặn.
- **Realistic failure scenario:** Version cũ được tạo trước khi có validation file tree chứa `bin` và `bin/app.exe`. Clone package sang tên mới: kiểm tra SourceVersionId hợp lệ, lưu package mới, sau đó clone version bị validation từ chối. Package đích rỗng vẫn tồn tại. Một trường hợp khác là version nguồn bị xóa sau bước kiểm tra thuộc package nhưng trước bước clone.
- **Operational impact:** Xuất hiện package dang dở, người dùng phải tìm/xóa thủ công hoặc đổi tên để retry. Luồng ChangeBom cũng chưa cleanup được failure bên trong ClonePackageAsync vì `createdPackageId` chỉ được gán sau khi lời gọi clone trả về.
- **Root cause:** Đơn vị thao tác nghiệp vụ là clone package + version + config, nhưng commit tách rời mà không có outcome/rollback thống nhất.
- **Recommended direction:** Prevalidate dữ liệu nguồn trước khi tạo đích, rồi thiết kế transaction chung hoặc compensation rõ ràng cho toàn bộ clone. Cần chỉnh cách tham gia transaction của clone version; helper hiện tại tự mở transaction, không chỉ bọc thêm một transaction ngoài là đủ.
- **Required regression test:** Clone package từ cây legacy xung đột phải không để lại package/version/files/config/refcount mới; retry cùng tên phải được phép. Inject lỗi copy config sau clone version để kiểm tra rollback/compensation.
- **Estimated change scope:** Medium.

### SW-05 — Hai request đồng thời có thể tạo package trùng tên

- **Severity:** Medium / P2.
- **Confidence:** High từ code và schema; chưa chạy barrier test PostgreSQL riêng.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwarePackageService.cs:49-69`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:621-643`.
- **Symbol:** `CreatePackageAsync`; các nhánh kiểm tra tương tự ở `ClonePackageAsync`, `UpdatePackageAsync`.
- **Evidence:** Service dùng `AnyAsync` rồi tạo entity/SaveChanges riêng, không khóa theo tên. Model SoftwarePackage và snapshot `AppDbContextModelSnapshot.cs:3296-3363` chỉ có key Id, index ModelId và AssignmentApprovalPolicyId; không có unique constraint cho Name. Concurrency token Version áp dụng trên từng row, không bảo vệ hai insert có ID khác nhau.
- **Finding:** Quy tắc duy nhất tên package đã được service đặt ra không được bảo đảm khi có concurrent requests.
- **Realistic failure scenario:** Hai kỹ sư cùng tạo hoặc clone tên `TEST_APP`; cả hai hoàn thành truy vấn nameExists=false trước khi bên kia insert. Hai SaveChanges đều có thể thành công. Tương tự, hai package khác nhau cùng được đổi sang một tên mới.
- **Operational impact:** Danh sách/search có hai package cùng tên, dễ chọn nhầm; hành vi retry và validation tên không còn nhất quán. Không suy rộng thành hỏng cài đặt trên máy trong phạm vi review này.
- **Root cause:** Dùng kiểm tra ở application làm cơ chế duy nhất cho invariant cần được database bảo vệ.
- **Recommended direction:** Bổ sung unique index cho tên đã chuẩn hóa của package còn sống, phù hợp chính xác với quy tắc so sánh hiện tại; xử lý conflict thành lỗi nghiệp vụ. Cần kiểm tra dữ liệu trùng trước migration, không tự động xóa/đổi tên dữ liệu đang có.
- **Required regression test:** PostgreSQL hai context cùng tạo/clone hoặc đổi tên sau barrier: chỉ một thao tác thành công, thao tác còn lại nhận conflict; xác nhận có thể tái sử dụng tên sau soft-delete theo contract hiện tại.
- **Estimated change scope:** Medium.

### SW-06 — Local download-url trả route không tương thích

- **Severity:** Low / P3; UI tải hiện hành dùng streaming endpoint nên phạm vi ảnh hưởng hẹp.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs:97-100`; `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:800-810`.
- **Symbol:** `GetPresignedDownloadUrlAsync`, `BuildDownloadResponseAsync`.
- **Evidence:** Provider Local trả `agent/v1/blobs/local?path=...`. `AgentController.cs:138-150` yêu cầu Agent authentication và query `cap`; không nhận `path` làm quyền tải. Cả `SoftwareFilesController` download-url và admin-download-url đều còn public route và đều dùng BuildDownloadResponseAsync.
- **Finding:** API trả URL thành công nhưng consumer theo URL không thể tải bằng credential đã gọi API; kể cả resolve đúng base URL vẫn thiếu capability bắt buộc.
- **Realistic failure scenario:** Client tích hợp dùng bearer user gọi `GET /api/v1/software-files/{id}/admin-download-url`, nhận URL Local, rồi tải URL đó bằng cùng bearer. Endpoint Agent từ chối authentication/capability.
- **Operational impact:** Download theo URL thất bại ở provider Local. Không phải bypass authorization; endpoint Agent đang chặn đúng. Các endpoint stream `/download` của browser không chịu lỗi này.
- **Root cause:** Contract URL cũ chưa được cập nhật sau khi Agent download chuyển sang capability và authentication riêng.
- **Recommended direction:** Trả URL tới user download endpoint phù hợp với caller, hoặc chính thức deprecate API URL cũ và chuyển consumer sang stream. Không mở lại raw path download ở Agent.
- **Required regression test:** Dùng credential user thực gọi URL trả về dưới Local và MinIO; đúng caller tải được bytes, caller thiếu quyền bị từ chối. Giữ test cách ly Agent/user.
- **Estimated change scope:** Small.

## Những luồng đã lần theo

| Luồng | Điểm đã đối chiếu |
| --- | --- |
| Browser → init → capability → TUS | Chuẩn hóa paths, SHA từ File đã chọn, dedup, reservation; HMAC bind actor/purpose/target/hash/size/expiry/reservation; metadata Upload-Length; CAS TusFileId và POST retry |
| TUS complete → storage → finalize | Verify SHA/size; PublishAsync giữ khóa theo SHA; provider stage/verify/publish; Uploaded; upload-complete xác minh bytes, thay file set, refcounts, consume reservation |
| Draft → release | Entry point/metadata chỉ sửa Draft; Version concurrency token; upload/delete touch version; canonical paths, case collision và file-vs-directory collision; AllowNoEntryPoint |
| Download/archive | User stream, Local/MinIO URL, temp ZIP dispose; effective archive kiểm tra quyền ReadOverrideFiles trên computer trước resolve |
| Cleanup/GC | Expiration/session cleanup; reservation sang tombstone; sweep lock + SHA lock + row lock; kiểm mọi typed reference kể cả lịch sử; DB DELETE chưa commit trước storage delete; rollback và retry theo candidate |
| CRUD/model/frontend | Model tồn tại, source version thuộc package, approval policy đúng target/active; projection ModelCode/ProductGroup; delete cascade; clone; selection và các thao tác version |
| Shared references | Blob FK/reference của SoftwareFile, UserDocument, OverrideFile, AgentRelease, RenderedConfigBlob và UploadReservation; document/software purpose isolation ở TUS |

## Những quyết định tốt cần giữ và nghi ngờ đã loại bỏ

- **Ghi blob bất biến đã cải thiện rõ:** Local ghi staging rồi move không overwrite; MinIO stage/verify, conditional publication và verify bytes sau publication. Client software hiện nhận `uploadUrl="tus"`; presigned PUT tới CAS bị từ chối. Không lặp lại finding cũ cho rằng browser vẫn PUT trực tiếp vào canonical CAS.
- **Hash cache cũ không còn quyết định payload:** `fileHash.ts:45-51` luôn hash File hiện tại. Không báo lại lỗi path/size/mtime chọn nhầm bytes.
- **DeleteFile/release race cũ đã có guard:** `SoftwareFileService.cs:505` touch version trước save; DBContext tăng Version cho entity Modified và dùng concurrency token. Full finalize dùng tracked version thay raw generation update cũ, nên không báo lại lỗi tự xung đột khi prune metadata.
- **Cây file được kiểm tra nhiều điểm:** upload, register, derived version và release đều dùng ValidateFileTree. Các test collision hiện có pass. Phần còn thiếu được chỉ rõ ở SW-04 là rollback của clone package khi validation từ chối.
- **GC giữ lịch sử là quyết định chủ động:** `docs/code-review/CNT01_EVIDENCE.md:14-16,35-41` xác nhận giữ tất cả typed historical references, kể cả Consumed/Expired/PurgedAt reservation. Vì vậy việc blob chưa bị thu hồi khi reservation lịch sử còn tồn tại không được báo thành bug. Chính sách này cần được tính vào dung lượng vận hành.
- **GC không còn xóa object trước khi biết FK có chặn hay không:** nhánh relational thực hiện DELETE trong transaction đang mở trước storage delete; nếu FK lỗi, bytes chưa bị đụng. Lỗi candidate được ghi và sweep tiếp tục. Không báo lại lỗi FK soft-delete của báo cáo cũ.
- **TUS purpose isolation có nhiều lớp:** capability, reservation exact storage path, permission hiện tại và target Draft; legacy completion bị khóa. Không thấy đường chứng minh user chỉ có document.own tạo SoftwareFile qua TUS.
- **ModelId không bị tin trực tiếp từ UI:** create/update kiểm model tồn tại; clone kiểm SourceVersionId thuộc source package. Chưa thấy căn cứ để báo lỗi chỉ từ việc package có thể không gắn model — entity/request cho phép nullable có chủ đích.

## Đánh giá tối ưu code và luồng

> **Cập nhật triển khai 2026-10-06:** Đã thực hiện hai mục ưu tiên: bỏ cache hash chỉ ghi và diff file set trong `CompleteUploadAsync`. Bảng dưới giữ nguyên nhận xét tại thời điểm review; chi tiết thay đổi và kiểm chứng được ghi tại mục cuối file.

Triển khai hiện tại có nền tảng bảo vệ toàn vẹn bytes và concurrency tốt hơn mốc review trước. Chưa thể kết luận “đã tối ưu”: chưa có benchmark, đồng thời có các chỗ tạo I/O và dữ liệu lịch sử dư rõ từ code. Các mục dưới đây là **optional improvement**, không tính vào 6 defect.

| Điểm cải thiện | Bằng chứng và hướng xử lý |
| --- | --- |
| Giảm thay thế toàn bộ SoftwareFile khi sửa một file | `SoftwareFileService.cs:347-448` soft-delete toàn bộ file set rồi insert lại cả các file không đổi. Với N file và K lần chỉnh sửa, riêng lịch sử SoftwareFile có thể tăng theo N×K. Vì retention giữ historical references, nên cân nhắc diff theo path/hash và chỉ thay phần đổi, vẫn giữ transaction và version guard. |
| Đo và giảm các lượt đọc/hash lặp | TUS hash bytes; CasPublication stage rồi hash lại; MinIO verify bằng GET; finalize tiếp tục hash mọi SHA trong manifest. Init cũng hash blob đã tồn tại. Phép sửa một file nhỏ trong package lớn vẫn đọc lại nhiều bytes giữ nguyên. Đo theo provider và kích thước trước; nếu tối ưu, cần giữ bằng chứng publication bất biến và đường phát hiện storage corruption. |
| Thu hẹp thời gian giữ transaction/khóa | `CompleteUploadAsync` lấy tất cả SHA locks rồi verify bytes trong transaction (`:329-333`). Publish cũng giữ khóa qua storage I/O. Đây là lựa chọn correctness có lý do; tối ưu bằng đo thời gian lock/I/O và thiết kế verified publication state, không đơn giản chuyển kiểm tra ra ngoài transaction. |
| Bỏ cache hash chỉ ghi mà không đọc | `fileHash.ts:50`, `:129-195` vẫn ghi IndexedDB sau mỗi lần hash nhưng không còn đọc cache để dùng lại. Nếu không phục vụ chức năng khác, bỏ việc ghi và dọn module sẽ giảm I/O và code thừa. Không khôi phục tái sử dụng hash dựa trên metadata file. |
| Dùng cùng contract cho mutation của package/version | SW-01 và SW-04 cho thấy quyền và transaction bị tách theo service method. Nên định nghĩa invariant chung cho lifecycle operation và thể hiện bằng integration tests; tránh chỉ vá ở nút frontend. |

Thứ tự đề xuất: SW-01 → SW-02/SW-03 → SW-04/SW-05 → SW-06; sau đó mới tối ưu thay file set và I/O có đo lường.

## Kiểm chứng đã thực hiện

| Nhóm | Kết quả trong lần review này |
| --- | --- |
| Backend: filter SoftwareFileServiceTests, SoftwareVersionServiceTests, SoftwarePackageServiceTests, BlobGcServiceTests, CasPublicationTests, TusUploadCapabilityServiceTests, UploadReservationCleanupServiceTests, UploadReservationServiceTests | 191 pass, 1 skip, 0 fail |
| Backend: filter TusUploadHttpAuthorizationTests, TusTempCleanupServiceTests, BlobReferenceCountTests, BlobLifecyclePostgresTests, MinioPublicationWireTests, LocalStorageServiceTests, PostgresConcurrencyIntegrationTests.Software | 15 pass, 16 skip, 0 fail |
| Frontend: software-upload.test.ts, fileHash.test.ts, use-software-page.test.ts, PackageList.test.tsx | 4 files, 14 pass, 0 fail |
| Phép kiểm chứng SW-03 qua stdin Node | Transpile source software.ts hiện tại, mock network, chạy reviseVersionFiles: POST/HEAD/PATCH/HEAD; lỗi 404, finalize không được gọi |
| Phép kiểm chứng SW-02 qua stdin Node | Transpile source hook hiện tại, React renderHook + jsdom, resolve B rồi A: selection B nhưng versions vA, loading=false |

Backend chạy bằng `dotnet test ... --no-restore`, lượt sau dùng thêm `--no-build`; frontend chạy Vitest với bốn file nêu trên. Lượt frontend đầu không khởi động được do sandbox chặn esbuild đọc thư mục cha; chạy lại với quyền thực thi được công cụ cho phép đã pass. Không thay config để chạy test. Hai phép kiểm chứng bổ sung chỉ chạy qua stdin, không thêm/sửa file test.

Test pass xác nhận các assertions hiện có, không phủ định các finding thiếu regression test. Kết quả test trong báo cáo cũ không được tính là test đã chạy lại ở đây.

## Giới hạn và khu vực chưa đủ bằng chứng

- Các test cần PostgreSQL/MinIO fixture bị skip khi thiếu điều kiện môi trường; không có tuyên bố đã chạy concurrency trên PostgreSQL thật hoặc MinIO E2E trong lần này. Không truy cập database/storage production.
- SW-02 là kiểm chứng hook với React/jsdom, SW-03 là kiểm chứng transport với response giả lập kết hợp call chain server; chưa phải browser–proxy–API E2E trên mạng lỗi thật.
- Chưa chạy fault injection mới cho crash đúng thời điểm storage/DB commit hoặc toàn bộ thứ tự lock giữa clone, cleanup, GC và release. Không khẳng định mọi interleaving đều an toàn chỉ vì unit tests pass.
- `SetOverridablePathsAsync` vẫn cho đổi policy trên Released (`SoftwareVersionService.cs:660-661`). Code thể hiện rõ chủ đích cho phép tới trước Deprecated; cần contract nghiệp vụ để kết luận đây là defect của release immutability. Không đưa vào danh sách lỗi đã xác nhận.
- TUS fetch không qua Axios refresh interceptor; expiry của access token/capability có thể ảnh hưởng upload dài. Chưa thực hiện phép kiểm chứng với clock/token lifecycle đầy đủ, nên không thêm một finding riêng.
- Chưa benchmark package lớn, số lượng reservation lịch sử, query plans hay throughput Local/MinIO. Nhận xét tối ưu dựa vào số lượt thao tác có thể xác định từ source, không phải số liệu hiệu năng đo được.

## Kết quả khắc phục 2026-10-03

Đã sửa **6/6 finding**. Thay đổi tập trung vào các đường gọi hiện có; không thay kiến trúc storage, retention policy hoặc quy trình phát hành.

| Finding | Bản sửa | Bằng chứng sau sửa |
| --- | --- | --- |
| SW-01 | Inject IPermissionService vào SoftwarePackageService; yêu cầu publish trước khi xóa package có version không phải Draft. Giữ concurrency token của toàn bộ version bị xóa. | Test role chỉ CRUD bị chặn với Released/Deprecated, vẫn xóa được Draft; PostgreSQL barrier xác nhận release thắng thì delete rollback, files/refcounts còn nguyên. |
| SW-02 | Gắn state versions/assignments với packageId; mỗi request có token riêng, bỏ kết quả và lỗi đã hết hiệu lực; ẩn dữ liệu cũ ngay khi đổi selection. | React tests cho A→B, bỏ selection, phản hồi A về sau B và thời gian B đang tải. |
| SW-03 | Dùng lại upload-init khi TUS trả 404 sau mất session; chỉ tiếp tục nếu server xác nhận Uploaded hoặc blob existing đã được verify. Dùng reservation được recovery trả về khi cần. | Tests mô phỏng mất final PATCH, HEAD 404, cả Uploaded và existing; trường hợp bytes chưa tồn tại vẫn bị từ chối và không gọi finalize. |
| SW-04 | Clone package/version/config/audit trong một transaction. Mở rộng ExecuteInTransactionAsync để tham gia transaction ngoài bằng savepoint; bỏ tracking của entities vừa rollback để ChangeBom/retry cùng context không ghi lại dữ liệu dang dở. | PostgreSQL: lỗi cây file legacy và lỗi ghi config sau khi version đã save đều rollback; refcounts/audit không rò rỉ, retry cùng tên/cùng context thành công. Test savepoint xác nhận transaction ngoài tiếp tục được sau rollback thao tác con. |
| SW-05 | Generated column NormalizedName = upper(Name), unique index trên package chưa soft-delete; tận dụng GlobalExceptionHandler để trả HTTP 409 khi xung đột. | PostgreSQL barrier cho create/clone/rename đồng thời; index chặn khác casing cả khi ghi trực tiếp, cho tái sử dụng tên sau soft-delete. Migration từ chối dữ liệu cũ trùng tên mà không đổi/xóa package. |
| SW-06 | Controller chuyển URL Local sang route stream của user, dùng Url.Action để giữ API version/PathBase. Admin có route stream dùng đúng quyền manage như endpoint cấp URL; tái sử dụng OpenFileDownloadAsync. | HTTP thật: cả download và admin-download chạy dưới /mproject, dùng credential được cấp URL tải đúng bytes; user thiếu quyền nhận 403, không đăng nhập nhận 401. URL HTTPS có signature từ provider được giữ nguyên. |

### Kết quả kiểm thử

| Phạm vi | Kết quả |
| --- | --- |
| Backend SoftwarePackageService, SoftwareFileService, transaction helper và model constraint check | 122 pass, 0 fail, 0 skip |
| Backend SoftwareVersionService, InstallationJobService, ResourceLookupService, TUS HTTP authorization và BlobGcService | 142 pass, 0 fail, 0 skip |
| PostgreSQL thật: SoftwarePackageLifecyclePostgresTests + DbConstraintNameTests | 9 test hiện hành đã pass; không skip. Lượt đầu 8 pass/1 fail do assertion fixture mong InvalidOperationException thay vì DbUpdateException mà EF bọc bên ngoài; đã sửa assertion, kiểm tra inner exception, và chạy lại ca này thành công. Ca concurrent names được mở rộng thêm clone và kiểm HTTP 409, chạy lại cũng pass. |
| Frontend: upload, hook selection, PackageList, fileHash | 20 pass trong 4 file |
| TypeScript `tsc -b` | Pass |
| ESLint trên 4 file frontend đã sửa | Pass, không có lỗi/cảnh báo lint ở source |
| EF `migrations has-pending-model-changes` | Model và migration khớp nhau |

Các nhóm test có thể bao gồm cùng một model-constraint test; không cộng các lượt chạy lại thành số test độc lập. PostgreSQL dùng `scripts/test-deployment-postgres.ps1`, cluster riêng bind loopback, database ngẫu nhiên và tự cleanup. Kết quả TRX nằm trong `artifacts/software-review-fixes/postgres/` và `artifacts/software-review-fixes/postgres-followup/`.

### Triển khai và giới hạn

- Cần áp dụng migration `20261003003243_EnforceSoftwarePackageNameUniqueness` khi triển khai. Migration có preflight phát hiện tên đang sống trùng nhau không phân biệt casing; nếu gặp, dừng với thông báo rõ và giữ nguyên dữ liệu để xử lý chủ động.
- Chưa chạy migration lên database đang vận hành, chưa deploy hoặc commit thay đổi.
- Không chạy full backend/frontend suite hoặc MinIO E2E. Nhánh URL presigned MinIO không đổi; có test bảo toàn URL/signature. Build còn các cảnh báo nullable/model relationship có sẵn ngoài phạm vi sửa.
- Các đề xuất hiệu năng như giảm replace-all file set, giảm lượt hash hoặc thay retention policy chưa được triển khai trong đợt sửa lỗi này. Cần đo lường và giữ nguyên các bảo đảm về bytes trước khi thực hiện.

## Hai cải tiến ưu tiên — 2026-10-06

### Thay đổi và review

- `fileHash.ts`: bỏ toàn bộ đường ghi cache IndexedDB không có người đọc; `calculateSha256` chỉ nhận File. Cập nhật cả software upload và document upload. Giữ worker/fallback và hash bytes thực tế. Cache cũ trên trình duyệt không được tự xóa trong thay đổi này.
- `CompleteUploadAsync`: diff active SoftwareFile theo path chính xác và SHA-256. Giữ ID, CreatedAt, UploadedBy của file không đổi; chỉ soft-delete file bị xóa/thay và insert file mới. Rename, kể cả đổi casing, tạo record mới và giữ lịch sử. Chỉ điều chỉnh refcount cho phần thêm/xóa, kể cả nhiều path chung hash; bỏ truy vấn lại stale rows.
- Review lại thứ tự save để giữ unique index trên active paths. Vẫn xác minh toàn bộ bytes/kích thước, Draft/concurrency guard, transaction và SHA locks. Reservation vẫn được validate/consume khi manifest không đổi. Metadata pointers được đối chiếu với manifest cuối cùng. Không có migration hoặc thay đổi retention policy.

### Kiểm chứng

- Thêm 5 ca unit test: file không đổi, thay hash, rename, đổi casing, thêm/xóa path chung hash. Kiểm tra ID/lịch sử, refcount, consumed reservation và finalize lặp.
- Thêm integration test PostgreSQL + Local storage thật: giữ file không đổi; thay/thêm hai path chung hash; inject lỗi insert sau cập nhật refcount và xác nhận rollback files/refcounts/reservation; retry thành công; finalize lặp không thêm lịch sử; bytes sai cùng kích thước vẫn bị từ chối.
- Backend: **221 pass, 0 fail, 6 skip**. Phạm vi: SoftwareFileService, SoftwareVersionService, SoftwarePackageService, BlobLifecyclePostgres, PostgresConcurrencyIntegration, BlobGcService, UploadReservationService, CasPublication. Các ca skip cần MinIO thật. PostgreSQL dùng cluster loopback riêng tự cleanup. Kết quả: `artifacts/software-optimizations-2026-10-06/postgres.trx`.
- Frontend: **62 pass trong 9 file**, gồm toàn bộ test dưới pages/Software, version-files, fileHash, software-upload và TransferCenter. Test hash xác nhận file trùng metadata nhưng khác bytes vẫn được hash riêng và không truy cập IndexedDB.
- `tsc -b`, ESLint trên bốn file frontend thay đổi và `git diff --check`: pass. Build backend còn cảnh báo nullable có sẵn ngoài phạm vi.

### Lợi ích và giới hạn

Manifest N file chỉ thay một file giữ N−1 record active cũ, soft-delete một record và thêm một record, thay vì thay toàn bộ N record. Manifest không đổi không tạo lịch sử SoftwareFile mới. Đây là giảm số record ghi được xác nhận bằng test; chưa benchmark thời gian/throughput. Việc đọc/hash toàn bộ manifest và retention blob lịch sử vẫn theo contract hiện tại. Chưa chạy full repository suite hoặc MinIO E2E, chưa deploy/commit.
