# 03 — Software content, upload và blob lifecycle

Ngày review: 2026-09-08, Asia/Bangkok. Thực hiện theo `docs/code-review/prompts/03-software-content-upload-and-blob-lifecycle.md`. Không sửa production code, migration, project memory hay checklist.

## Kết quả

Phát hiện **9 confirmed defects và 1 design risk**. Các vấn đề cần ưu tiên là tính toàn vẹn bytes ở nhánh MinIO/Local, race xóa file sau release, GC bị kẹt bởi khóa ngoại của bản ghi soft-delete, và hash cache chọn nhầm nội dung. Các trạng thái COMPLETED trong checklist không bao phủ hết những đường gọi này.

| ID | Severity | Confidence | Category | Nội dung |
| --- | --- | --- | --- | --- |
| CONTENT03-01 | High | High | confirmed defect | Nhánh upload trực tiếp có thể thay bytes của CAS; sửa blob đã biết bỏ kiểm hash |
| CONTENT03-02 | High | High | confirmed defect | Local upload ghi đè tại chỗ, lỗi copy làm hỏng object đã được finalize |
| CONTENT03-03 | High | High | confirmed defect | Xóa file không phải entry point có thể commit sau release mà generation không đổi |
| CONTENT03-04 | High | High | confirmed defect | GC xóa object rồi vướng FK soft-delete, lặp lỗi và chặn upload lại cùng SHA |
| CONTENT03-05 | High | High | confirmed defect | Hash cache dùng path/size/mtime làm identity, có thể finalize nội dung cũ thay file vừa chọn |
| CONTENT03-06 | Medium | High | confirmed defect | Prune metadata trong upload-complete tự xung đột với generation vừa tăng |
| CONTENT03-07 | Medium | High | confirmed defect | Upload/release chấp nhận một đường dẫn vừa là file vừa là thư mục cha |
| CONTENT03-08 | Medium | High | confirmed defect | Mất response PATCH cuối khiến retry báo lỗi dù bytes đã lưu thành công |
| CONTENT03-09 | Low | High | confirmed defect | API download-url trả local URL không tương thích endpoint Agent hiện tại |
| CONTENT03-10 | Medium | High về behavior; Medium về hợp đồng | design risk | Released version vẫn cho sửa OverridablePaths/PreserveLocalPaths tại chỗ |

Severity phản ánh tác động khi điều kiện của finding xảy ra. Đây không phải kết luận rằng dữ liệu vận hành hiện tại đã bị hỏng. Nhánh MinIO chưa được chạy với server MinIO thật; giới hạn bằng chứng được ghi riêng trong finding 01.

## Phạm vi và nguồn bằng chứng

Đã đọc `AGENTS.md`, `MProjectFrontend/PROJECT_MEMORY.md`, `PROJECT_REVIEW_CHECKLIST.md`, đặc biệt UPL-001, UPL-002, REL-001, STO-001, STO-002. Kết luận dựa vào code và test hiện tại, không kế thừa trạng thái đóng lỗi từ tài liệu.

Đã trace frontend `software.ts`, `fileHash.ts`, version-file utilities, `ManifestModal`, `VersionPanel`, đường gọi upload/release; ba software controllers; `SoftwareFileService`, `SoftwareVersionService`, các đường create/clone/delete của `SoftwarePackageService`; capability/reservation services, TUS handler/cleanup; Local/MinIO storage; Blob GC và reference extensions; EF model, soft-delete filters, generation và các migration reservation/tombstone/TUS session.

Chỉ đọc phần document/config liên quan tới purpose isolation, shared object references và effective download. Không review sâu Agent execution, OTA archive, cấu hình override hoặc station authorization thuộc các prompt khác.

### Kiểm thử đã thực hiện

| Nhóm | Kết quả cuối |
| --- | --- |
| Backend hiện có: software file/version/package, blob GC/reference, LocalStorage, TUS capability/HTTP, reservation/cleanup | 192 pass, 0 fail |
| PostgreSQL hiện có: release/upload, GC/writer, cleanup/reservation/quota; loại hai test job/watchdog khỏi filter | 9 pass, 0 fail; chạy trên PostgreSQL thật |
| Kiểm chứng bổ sung: 6 ca service trên PostgreSQL và 1 ca LocalStorage trên filesystem thật | 7 pass, 0 fail |
| Frontend hiện có: software-upload, version-files | 23 pass, 0 fail |
| Kiểm chứng frontend bổ sung: hash cache, mất final PATCH response, chọn raw PUT | 3 pass, 0 fail |

Các ca kiểm chứng bổ sung xác nhận behavior được mô tả, không chứng minh đã sửa lỗi.

PostgreSQL 18 chạy trong cluster test riêng, chỉ bind loopback, mỗi test tạo database có tên ngẫu nhiên và migrate schema hiện tại, rồi tự drop database. Không sử dụng connection string hay dữ liệu production. Không chạy full suite, frontend build/lint, IIS canary hoặc MinIO E2E; không cần thay production code để thực hiện review này.

## Các luồng đã trace

| Trace | Hành vi thực tế và boundary |
| --- | --- |
| 1. Browser → init → capability → TUS create | Browser chuẩn hóa relative path, lấy hash, gửi toàn bộ file set. Init yêu cầu Draft, phân nhóm SHA, touch blob đã finalize, kiểm object/size, tạo reservation cho phần thiếu. Controller ký capability actor/purpose/target/hash/size/expiry/reservation. Local upload POST TUS; metadata và Upload-Length được đối chiếu. CAS `TusFileId` ngăn hai session cùng reservation; POST lặp trả 409 + Location để resume. **MinIO URL đi qua raw PUT, không qua các TUS checks.** |
| 2. Bytes → Uploaded → domain finalize | TUS đọc toàn bộ SHA/size trước khi gọi storage, sau đó MarkUploaded và xóa session. Upload-complete chấp nhận Pending để phục vụ direct PUT, preverifies SHA chưa có trong Blobs, touch Draft trong transaction, soft-delete file set cũ, cập nhật refcount, thêm file set mới và consume reservation. Blob đã có chỉ được kiểm size. Prune metadata cuối transaction dùng entity version đã load trước raw generation update. |
| 3. Draft edits → release → manifest | Entry point và metadata thông thường yêu cầu Draft. Release kiểm file set không rỗng, canonical paths, duplicate path không phân biệt hoa thường và entry point/data-only opt-in; lưu trạng thái với optimistic concurrency. Full upload có version touch; DeleteFile thiếu touch đối với non-entry file. File/folder prefix collision chưa được kiểm. Released path policies có đường sửa riêng. |
| 4. Download/archive | Browser hiện gọi các endpoint stream qua Axios Bearer. Archive giữ relative paths, giới hạn subtree bằng prefix có dấu `/`, dùng temp ZIP DeleteOnClose và bỏ file soft-delete. Archive/effective content theo computer kiểm `ReadOverrideFiles` trên computer resource trước khi resolve file/value overrides. API download-url cũ vẫn dùng URL storage; Local trả `path=...` trong khi Agent endpoint yêu cầu `cap` và Agent auth. |
| 5. Reservation cleanup → GC | Reservation hết hạn được đánh dấu Expired; cleanup dùng lock theo SHA để giao object orphan cho tombstone hoặc giao trách nhiệm cho reservation còn sống. GC có advisory lock toàn sweep, claim có điều kiện, writer reject claimed row, storage delete trước DB delete, retry storage failure. Nhưng eligibility dùng live references, trong khi FK vẫn nhìn thấy lịch sử soft-delete. |
| 6. Shared purpose/reference | TUS rechecks document ownership/permission hoặc software CRUD + Draft, ràng buộc metadata với reservation. Shared references có software, documents, override files, agent releases và rendered config. GC không xem ReferenceCount đơn lẻ là bằng chứng orphan. Các barrier hiện có bảo vệ được những thứ tự interleaving đã test; không đồng nghĩa mọi object overwrite đều được serialize. |

Trong source config được đọc, provider mặc định là Local, `VerifyUploadHash=true`, TUS max file size 10 GiB; software upload expiry 30 phút, download URL expiry 15 phút. GC mặc định grace 7 ngày, batch 500 và chạy hằng ngày lúc 02:00 UTC. Đây là cấu hình source, không phải audit effective configuration của deployment đang vận hành.

## Findings chi tiết

### CONTENT03-01 — Direct upload không bảo vệ identity của object CAS

- **Severity:** High.
- **Confidence:** High cho lỗi bỏ hash verification khi sửa blob đã biết và việc frontend dùng raw PUT. Khả năng replay URL lên MinIO cụ thể chưa được chạy E2E.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:204-227,289-314,361-380,445-473`; `MProjectBackend/MProject.Infrastructure/Storage/MinioStorageService.cs:132-145`; `MProjectFrontend/src/api/software.ts:415-429,634-647`.
- **Symbol:** `InitializeUploadAsync`, `PreverifyNewUploadedBlobsAsync`, `CompleteUploadAsync`, `GetPresignedUploadUrlAsync`, `uploadRemoteBlob`.
- **Evidence:** MinIO ký PUT cho chính `blobs/{sha-prefix}/{sha-rest}`, với Content-Type và expiry, không có checksum bắt buộc. Frontend gửi PUT trực tiếp. Nếu Blob row đã tồn tại nhưng object mất/sai size, init cấp URL sửa; preverify loại toàn bộ SHA đã có row và finalize chỉ kiểm size. Fixture `KnownBlobRepair_FinalizationAcceptsDifferentBytesOfSameSize` trên PostgreSQL, bật `VerifyUploadHash=true`, ghi bytes khác cùng độ dài vào fake object storage rồi xác nhận finalize thành công, reservation Consumed, DB SHA khác SHA thực tế. Fixture frontend xác nhận chỉ có PUT và không gửi TUS capability.
- **Finding:** Bất biến “Blob row đã có nghĩa là bytes đã được xác minh và không đổi” không còn đúng sau khi cấp quyền ghi lại canonical object. Reservation consumption và trạng thái Released không thu hồi presigned PUT đang còn hạn.
- **Realistic failure scenario:** Storage restore thiếu một object dùng chung; kỹ sư reupload qua MinIO, nhưng payload sai cùng kích thước vẫn được chấp nhận dưới SHA cũ. Một trường hợp khác: URL đã cấp cho blob mới vẫn còn hạn sau finalize/release và bị sử dụng lại để ghi bytes khác. Tài liệu S3 xác nhận presigned URL có thể dùng nhiều lần trước expiry và PUT cùng key thay object; việc áp dụng vào cấu hình MinIO hiện hành là suy luận từ nhánh S3-compatible, cần negative E2E riêng. [AWS: Download and upload objects with presigned URLs](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html).
- **Operational impact:** Các version dùng chung SHA có thể trỏ tới bytes sai; browser/archive trả bytes đó, còn Agent kiểm SHA sẽ từ chối tải/cài. Không có bằng chứng Agent thực thi được bytes sai hash.
- **Root cause:** Client có quyền ghi trực tiếp vào namespace CAS đã finalize; hash check quyết định theo sự tồn tại của DB row thay vì theo lần ghi object.
- **Recommended direction:** Mọi lần publish bytes vào CAS phải có hash/size verification và một boundary commit không cho client ghi lại object đã finalize. Có thể dùng TUS thống nhất, hoặc presigned URL cho object staging theo reservation rồi server promote có kiểm chứng. Bổ sung xác minh cho repair, không chỉ SHA mới. Chuyển qua TUS đơn thuần vẫn cần xử lý finding 02.
- **Required regression test:** MinIO thật: bytes sai cùng size khi repair phải bị từ chối; PUT lặp sau finalize/release không được đổi canonical bytes; thêm hai reservation cùng SHA và PUT hoàn tất muộn. Assert hash, Blob, SoftwareFile và reservation sau từng lỗi.
- **Estimated change scope:** Medium.

### CONTENT03-02 — LocalStorage truncate object trước khi copy thành công

- **Severity:** High.
- **Confidence:** High; tái hiện trên filesystem thật.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs:30-46`; `MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:309-336`; `MProjectBackend/MProject.Application/Services/Uploads/UploadReservationService.cs:83-95,127-146`.
- **Symbol:** `LocalStorageService.UploadAsync`, `CompleteBlobUploadAsync`, `ReserveAsync`.
- **Evidence:** `FileMode.Create` mở trực tiếp canonical path trước `CopyToAsync`. TUS luôn gọi storage upload sau hash check, không kiểm object đã được một reservation khác finalize. Reservation dedup bind actor/purpose/target, nên hai actor hoặc target có thể có reservation độc lập cho cùng SHA. Lock theo SHA của ReserveAsync kết thúc trước khi TUS copy bytes. `FailedLocalOverwrite_TruncatesPreviouslyVerifiedObject` lưu object đúng, inject lỗi đọc ở lần upload tiếp theo và xác nhận object còn tồn tại nhưng đã bị truncate về 0 byte.
- **Finding:** Tính toàn vẹn của object đã dùng không được giữ khi copy lần sau lỗi hoặc bị cancel.
- **Realistic failure scenario:** Hai kỹ sư init cùng một dependency mới ở hai version. A upload/finalize trước. B sau đó hoàn tất TUS hash check, bắt đầu copy cùng SHA; client disconnect hoặc I/O failure xảy ra sau khi destination bị truncate. Blob của A vẫn được tham chiếu nhưng bytes bị hỏng.
- **Operational impact:** Upload thất bại ở B có thể làm hỏng payload hợp lệ của A và mọi version chia sẻ blob. Cần repair trước khi triển khai/tải tiếp.
- **Root cause:** Validation source trước copy không tạo atomic commit cho destination; copy vào object đang sống không được bảo vệ bởi transaction DB hay TUS session binding.
- **Recommended direction:** Ghi vào file tạm riêng cùng volume, kiểm chứng trước commit; nếu canonical object đã đúng hash/size thì reuse, nếu cần thay thì atomic replace trong boundary phối hợp với GC/writers. Lỗi/cancel phải giữ nguyên bytes cũ.
- **Required regression test:** Hai reservation cùng SHA, cho A finalize rồi inject cancel/disk failure giữa copy của B. Sau lỗi, bytes A phải exact, version A tải được; chạy với LocalStorage thật và thêm barrier GC đối với lần promote.
- **Estimated change scope:** Medium.

### CONTENT03-03 — Non-entry file bị xóa sau khi version đã Released

- **Severity:** High.
- **Confidence:** High; barrier test với hai DbContext trên PostgreSQL thật.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:479-498`; `MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:451-497`; `MProjectBackend/MProject.Domain/Entities/Software/SoftwareFile.cs:5`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:1131-1165`.
- **Symbol:** `DeleteFileAsync`, `ReleaseVersionAsync`, `AppDbContext.SaveChangesAsync`.
- **Evidence:** DeleteFile kiểm Draft trước SaveChanges. Chỉ sửa version khi file là EntryPointPath; file thường không touch version hoặc recheck Draft trong transaction. SoftwareFile là BaseEntity. Fixture `DeleteNonEntryFilePausedBeforeSave_RemovesContentAfterReleaseCommits` pause xóa dependency trước SaveChanges, release từ context khác, rồi resume: file bị xóa, trạng thái vẫn Released, generation vẫn bằng generation ngay sau release, manifest giảm từ 2 file còn 1.
- **Finding:** Generation guard của full upload không bảo vệ endpoint DELETE từng file.
- **Realistic failure scenario:** Một tab/API client xóa DLL khỏi Draft trong lúc kỹ sư khác phát hành cùng version. Release trả thành công và máy đầu tiên đã nhận manifest; lệnh xóa commit sau đó khiến máy kế tiếp nhận thiếu DLL cho cùng VersionId.
- **Operational impact:** Released artifact không còn ổn định; máy cùng version có thể nhận file set khác nhau hoặc thiếu dependency.
- **Root cause:** Trạng thái được kiểm trước mutation nhưng mutation không tham gia concurrency boundary của version khi không sửa entry point.
- **Recommended direction:** Bọc xóa file, version touch có điều kiện Draft, metadata cleanup và decrement reference trong cùng transaction, dùng cùng invariant với upload-complete.
- **Required regression test:** Giữ barrier hai context ở trên, thêm thứ tự ngược và cả file entry/non-entry. Release hoặc delete phải conflict; không có file set thay đổi sau release thành công.
- **Estimated change scope:** Small.

### CONTENT03-04 — Soft-delete history khiến GC kẹt vĩnh viễn ở một SHA

- **Severity:** High.
- **Confidence:** High; PostgreSQL thật tái hiện hai sweep liên tiếp và retry upload.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs:168-185,214-244,390-421,83-93`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:725-735`; `MProjectBackend/MProject.Application/Services/Common/IntervalSweepService.cs:47-62`.
- **Symbol:** `SweepAsync`, `DeleteClaimedIfUnreferencedAsync`, `TryFinishClaimedBlobAsync`.
- **Evidence:** Candidate/claim/final-delete predicates chỉ xét file chưa soft-delete. FK SoftwareFiles → Blobs dùng Restrict và vẫn tồn tại trên bản ghi đã soft-delete. GC xóa storage trước khi gọi ExecuteDeleteAsync; exception DB nằm ngoài catch xử lý storage failure. `SoftDeletedSoftwareFile_GcDeletesBytesThenForeignKeyBlocksEverySweepAndReupload` xác nhận object mất, row còn tombstone, hai sweep đều lỗi PostgreSQL `23001` (RESTRICT), retry init bị chặn bởi kiểm lịch sử FK trong TryFinishClaimedBlobAsync.
- **Finding:** Một vòng đời bình thường — upload, xóa file cuối cùng, đợi grace — tạo tombstone không thể finalize hay reuse. Exception còn làm dừng phần còn lại của sweep.
- **Realistic failure scenario:** Version Draft được thay file hoặc xóa; dependency cũ không còn live reference. Sau grace 7 ngày, GC xóa bytes rồi vướng FK history. Mỗi ngày retry lại gặp cùng row. Khi kỹ sư cần upload lại dependency đó, init tiếp tục báo awaiting garbage collection.
- **Operational impact:** Một SHA không thể phục hồi qua luồng upload thông thường; GC các blob khác có thể bị trì hoãn liên tục và disk usage tăng. Finding này chứng minh mất khả năng finalize/reupload, không khẳng định GC đã xóa bytes có live SoftwareFile reference.
- **Root cause:** Retention của lịch sử soft-delete và schema FK không thống nhất với eligibility của GC; lỗi DB không được cô lập theo candidate.
- **Recommended direction:** Chốt và triển khai một chính sách nhất quán cho historical references: giữ object/row khi FK còn cần, hoặc tách/purge history theo retention đã phê duyệt trước khi reclaim. Kiểm eligibility trước object delete và ghi lỗi/retry riêng từng candidate. Không tự xóa lịch sử để “mở khóa” upload.
- **Required regression test:** Lifecycle thật upload → replace/delete → grace → sweep trên PostgreSQL, lặp sweep và reupload cùng SHA; thêm document/override/release history tương tự. Một candidate lỗi không được chặn reclaim các candidate hợp lệ khác.
- **Estimated change scope:** Medium.

### CONTENT03-05 — Hash cache có thể khiến upload chọn nhầm bytes cũ

- **Severity:** High.
- **Confidence:** High; frontend fixture tái hiện deterministic.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/utils/fileHash.ts:43-55,132-149,166-167`; `MProjectFrontend/src/api/software.ts:335-364,443`; `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:204-210`.
- **Symbol:** `calculateSha256`, `getCachedHash`, `buildHashCacheKey`, `reviseVersionFiles`.
- **Evidence:** Cache key chỉ gồm relativePath, file.size và file.lastModified; cache hit trả SHA mà không đọc bytes. Fixture `hash-cache.test.ts` tạo hai File có nội dung `AAAA`/`BBBB`, cùng path, size, mtime, xác nhận lần hai vẫn trả hash lần một và hasher chỉ chạy một lần. Init dedup blob cũ hợp lệ sẽ trả existing và browser không gửi bytes lần hai.
- **Finding:** Cache được coi là identity của nội dung dù metadata filesystem không xác định duy nhất nội dung. Server hash verification không phát hiện được khi browser không upload bytes mới vì cache báo SHA cũ.
- **Realistic failure scenario:** Hai bộ phần mềm/template có file cùng relative path và size, giữ cùng timestamp khi copy/unzip hoặc dùng timestamp build cố định. Upload bộ thứ hai trong cùng browser origin reuse cache bộ thứ nhất.
- **Operational impact:** Version mới được finalize hợp lệ bằng nội dung cũ thay vì file người dùng chọn, có thể đem config hoặc dependency sai lên station. Hash ở server/Agent vẫn khớp vì cả manifest và bytes đều thuộc nội dung cũ.
- **Root cause:** Optimization persistent cache được dùng làm bằng chứng rằng hai File có cùng bytes; root/path của lần chọn không tạo identity đáng tin cậy.
- **Recommended direction:** Tính hash từ File của lần chọn hiện tại, hoặc chỉ cache trên identity có bằng chứng nội dung không đổi. Thêm package/version vào key chỉ giảm va chạm, không khắc phục việc file cùng tên/size/mtime bị sửa.
- **Required regression test:** Upload liên tiếp hai File khác bytes, cùng path/size/mtime, server đã có blob thứ nhất. Lần hai phải có SHA mới, chuyển đúng bytes mới và finalize đúng hash; kiểm cả chọn folder với strip root.
- **Estimated change scope:** Small.

### CONTENT03-06 — Upload-complete tự thất bại khi prune launch metadata

- **Severity:** Medium.
- **Confidence:** High; tái hiện PostgreSQL dù không có request cạnh tranh.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:286,319,131-135,412-424`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:1095-1103,1131-1146`; `MProjectFrontend/src/pages/Software/components/ManifestModal.tsx:984-996,1027-1055`.
- **Symbol:** `CompleteUploadAsync`, `TouchEditableVersionAsync`, `runRevision`.
- **Evidence:** GetEditableVersionAsync load tracked entity generation N. TouchEditableVersionAsync tăng DB thành N+1 qua ExecuteUpdateAsync, không cập nhật original concurrency value trong tracker. Cuối finalize, khi entry/icon/watch hoặc path policies bị prune, SaveChanges dùng original N và bị concurrency exception. Fixture `RemovingEntryPointThroughCompleteUpload_ConflictsWithItsOwnVersionTouch` xác nhận DbUpdateConcurrencyException và rollback nguyên file set/reference count cũ.
- **Finding:** Những thao tác cần đổi metadata sau khi thay file set tự xung đột với chính transaction của mình. Test prune hiện có chạy InMemory nên không thấy khác biệt raw SQL/tracker này.
- **Realistic failure scenario:** Thay folder chứa entry executable và đổi tên folder. UI đã tính đường dẫn mới, dự định finalize files rồi set entry point mới; backend prune đường dẫn cũ trước khi commit nên thất bại, không đến bước cập nhật đường dẫn mới. API full upload loại icon/watch path cũng gặp trường hợp tương tự.
- **Operational impact:** Không thể hoàn tất các revision này qua luồng thông thường; bytes mới có thể đã Uploaded và chờ retry/cleanup. Đây là rollback an toàn của DB, không phải file set được commit một nửa.
- **Root cause:** Kết hợp bulk generation update và tracked metadata update mà không đồng bộ concurrency state.
- **Recommended direction:** Thống nhất cách giữ lock/generation và cập nhật metadata: đồng bộ/reload tracked version sau conditional touch hoặc dùng một mutation có điều kiện cho toàn bộ metadata. Vẫn giữ barrier chống release; không bỏ concurrency token để chữa lỗi.
- **Required regression test:** PostgreSQL: rename entry folder, bỏ icon/watch, prune Overridable/PreserveLocal lists phải commit khi không cạnh tranh; đồng thời release race phải tiếp tục reject an toàn.
- **Estimated change scope:** Small.

### CONTENT03-07 — Bỏ sót collision giữa file và thư mục cha

- **Severity:** Medium.
- **Confidence:** High; upload và release trên PostgreSQL đều chấp nhận fixture.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:961-980`; `MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:473-479`; `MProjectBackend/MProject.Application/Utils/WindowsPathRules.cs:14-28`.
- **Symbol:** `ValidateAndNormalizeFiles`, `ReleaseVersionAsync`, `NormalizeRelativeFilePath`.
- **Evidence:** Validate từng path và duplicate full path không phát hiện quan hệ prefix file/parent. `FileAndParentDirectoryCollision_PassesUploadAndRelease` gọi production init, complete, set entry và release với hai file `bin` và `bin/app.exe`; version trở thành Released.
- **Finding:** Server phát hành được file tree không thể materialize trên Windows: `bin` không thể vừa là regular file vừa chứa `app.exe`.
- **Realistic failure scenario:** Client API gửi cây tổng hợp hoặc clone/propagate ghép file từ hai version, một cây có file tên `bin`, cây kia có thư mục `bin`. Trường hợp thư mục khác casing cũng cần xử lý theo quy tắc Windows.
- **Operational impact:** Version vượt release gate nhưng triển khai thất bại ở validation/filesystem, hoặc archive không thể giải nén đầy đủ vào cây hợp lệ. Không cần giả định Agent có path escape.
- **Root cause:** Validation tập trung vào từng path và full-path equality, chưa kiểm cấu trúc cả cây.
- **Recommended direction:** Dùng kiểm tra prefix theo segment, không phân biệt hoa thường: không file nào được là ancestor của file khác. Áp ở ingest và release, kể cả derived version.
- **Required regression test:** `bin` + `bin/app.exe`, `Bin` + `bin/app.exe`, nhiều cấp ancestor phải bị từ chối trước finalize; `bin/a` + `bin2/b` vẫn hợp lệ. Kiểm clone/propagate output.
- **Estimated change scope:** Small.

### CONTENT03-08 — Retry không phục hồi được response PATCH cuối bị mất

- **Severity:** Medium.
- **Confidence:** High; frontend fixture và thứ tự cleanup trong backend khớp nhau.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:333-336`; `MProjectFrontend/src/api/software.ts:540-543,616-629,687-688`; `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:262-270`.
- **Symbol:** `CompleteBlobUploadAsync`, `uploadBlobWithTus`, `getTusOffset`, `withUploadRetry`.
- **Evidence:** Backend MarkUploaded rồi DeleteFileAsync session trước khi PATCH response tới browser. Browser retry sau TypeError luôn HEAD session cũ; HEAD 404 không transient. `upload-recovery.test.ts` mô phỏng POST/HEAD/final PATCH mất response/HEAD 404 và xác nhận promise reject. Test hiện có cho lost PATCH dùng session còn tồn tại, chưa bao phủ completion teardown.
- **Finding:** Resume offset xử lý được PATCH trung gian nhưng không xử lý commit thành công đã xóa session.
- **Realistic failure scenario:** Gateway hoặc Wi-Fi mất response cuối sau khi server lưu object và xóa TUS temp. Browser hiển thị upload lỗi và không gọi domain finalize.
- **Operational impact:** Bytes nằm ở Uploaded reservation đến khi người dùng bắt đầu lại hoặc cleanup; phải lặp thao tác thủ công. Init mới đã có `alreadyUploaded` để cứu trường hợp này, vì vậy đây không phải mất bytes vĩnh viễn trong mọi trường hợp.
- **Root cause:** Client chỉ dùng tồn tại/offset của TUS session làm tín hiệu, trong khi completion đã chuyển nguồn sự thật sang reservation.
- **Recommended direction:** Khi final PATCH có kết quả mơ hồ và HEAD trả not-found, reconcile qua init/reservation của đúng actor-target-hash; chỉ tiếp tục finalize khi server xác nhận Uploaded. Không coi mọi 404 là upload thành công.
- **Required regression test:** HTTP TUS thật lưu bytes/MarkUploaded/xóa session rồi cố ý bỏ response; browser reconcile và complete một lần. Thêm 404 do session hết hạn/chưa upload để chứng minh không báo thành công giả.
- **Estimated change scope:** Small.

### CONTENT03-09 — URL tải Local do API trả ra không thể dùng với auth hiện hành

- **Severity:** Low; frontend hiện tại dùng streaming endpoints, nên tác động giới hạn ở caller của hai API URL.
- **Confidence:** High về contract mismatch; không chạy HTTP với production identity.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs:72-75`; `MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:507-527,782-792`; `MProjectBackend/MProject.Api/Controllers/Software/SoftwareFilesController.cs:36-41,63-68`; `MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:138-144`.
- **Symbol:** `GetPresignedDownloadUrlAsync`, `GetDownloadUrlAsync`, `GetAdminDownloadUrlAsync`, `DownloadLocalBlob`.
- **Evidence:** URL trả về có dạng `agent/v1/blobs/local?path=...`. Endpoint hiện tại dùng Agent authentication và query `cap`; không còn nhận plain storage path làm authorization. Cả human download-url và admin-download-url chuyển nguyên URL từ storage ra response.
- **Finding:** API trả một URL không có đủ contract để caller đã được human authorization tải file.
- **Realistic failure scenario:** Integration client dùng admin-download-url thay streaming endpoint rồi GET URL bằng Bearer token của user. Request bị từ chối do thiếu Agent credential/capability, dù bước cấp URL thành công.
- **Operational impact:** Hai endpoint công khai bị lỗi chức năng ở provider Local. Không phải authorization bypass; cũng không chứng minh browser download hiện tại bị hỏng.
- **Root cause:** LocalStorage URL contract cũ chưa được cập nhật sau khi agent blob route chuyển sang capability.
- **Recommended direction:** Trả URL tới human streaming route thích hợp hoặc dùng capability riêng cho human download; thống nhất semantics expiry. Nếu deprecate hai API này, cần ghi rõ và xử lý compatibility, không nới Agent authorization.
- **Required regression test:** HTTP test gọi cả hai endpoint bằng đúng permission, theo URL trả về với auth dự kiến và nhận đúng bytes ở Local; đối chiếu MinIO. User thiếu quyền vẫn phải bị từ chối.
- **Estimated change scope:** Small.

### CONTENT03-10 — Released path policies vẫn mutable

- **Severity:** Medium.
- **Confidence:** High về mutation thực tế; Medium về việc đây có phải ngoại lệ nghiệp vụ được chấp thuận hay không.
- **Category:** design risk.
- **File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:651-694`; `MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:756-763,834-857`; `MProjectBackend/MProject.Api/Controllers/Software/SoftwareVersionsController.cs:90-95`.
- **Symbol:** `SetOverridablePathsAsync`, effective blob resolution, manifest `PreserveLocal` mapping.
- **Evidence:** Setter chỉ reject Deprecated, cho phép Released và chỉ cần software CRUD ở controller. `ReleasedVersion_AcceptsPreserveLocalPolicyMutation` trên PostgreSQL release version rồi thêm `config.ini` vào PreserveLocalPaths thành công. Manifest đọc trực tiếp các list hiện tại; OverridablePaths quyết định lấy override blob, PreserveLocalPaths quyết định policy triển khai. Cơ chế này là đường gọi riêng, không phải race.
- **Finding:** “Immutable release” hiện đóng băng một phần metadata nhưng không đóng băng hai policy ảnh hưởng bytes hiệu dụng và hành vi triển khai. Chưa thấy trong phạm vi review một hợp đồng giải thích rõ ngoại lệ này hoặc policy revision riêng.
- **Realistic failure scenario:** Kỹ sư đổi config từ managed sang preserve-local trên version đã phát hành; job resolve trước và sau mutation mang cùng VersionId nhưng policy khác. Với OverridablePaths, file override đang tồn tại có thể được đưa vào hoặc loại ra ở lần resolve tiếp theo.
- **Operational impact:** Khó giải thích artifact/policy nào đã được review và cùng version có thể có behavior khác giữa các lần deploy. Không kết luận mọi config customization theo station phải bất biến; config overlay là tính năng chủ ý của sản phẩm.
- **Root cause:** Policy version và artifact version nằm chung entity nhưng có freeze rule khác nhau, trong khi tuyên bố immutable release chưa phân định boundary.
- **Recommended direction:** Chốt ngoại lệ ở mức hợp đồng: hoặc chỉnh hai list qua Draft mới, hoặc biểu diễn/audit một policy revision có identity và quyền thay đổi riêng để manifest/history truy ra được. Không âm thầm bỏ tính năng hiện tại chỉ để khớp wording của tài liệu.
- **Required regression test:** Kiểm caller có CRUD nhưng không publish khi sửa Released; hai lần resolve quanh policy edit phải tuân theo contract đã chọn. Kiểm generation/history và khả năng giải thích policy đã dùng cho job cũ.
- **Estimated change scope:** Medium.

## Những quyết định tốt cần giữ

- HMAC capability dùng encoding canonical và constant-time signature comparison; metadata bind actor, purpose, target, SHA, size, expiry và reservation. TUS rechecks permission/Draft lúc thao tác, không chỉ tin quyền ở init.
- Durable reservation tồn tại trước bytes; CAS TusFileId giúp POST lặp resume đúng session, claim tạo dở có recovery và session bind được hoàn tất dù client mất POST response.
- New zero-byte object vẫn có reservation; Uploaded reservation retry dùng `alreadyUploaded` nhưng vẫn phải finalize/consume. Tombstone recovery không biến bytes chưa xác minh thành Blob reusable.
- New SHA có hash verification trước transaction finalization; startup từ chối tắt VerifyUploadHash ngoài Development. Giữ hash check ở Agent: nó giới hạn tác động của storage corruption thành rejection thay vì chạy bytes sai.
- Conditional version touch của full upload và optimistic concurrency của metadata/release chặn được release-winning race hiện có. Cần mở rộng invariant đến DeleteFile, không loại bỏ guard.
- GC dùng tombstone, advisory lock, actual references và touch của reusable blob; storage failure giữ row để retry. ReferenceCount không được dùng đơn lẻ làm oracle cho delete.
- Browser sửa file trên Released bằng clone Draft, archive giữ cây thư mục, effective download kiểm quyền computer scope và temp ZIP được dispose khi có lỗi.

## Các nghi ngờ đã loại bỏ hoặc giới hạn lại

| Nghi ngờ | Kết luận sau đối chiếu |
| --- | --- |
| User chỉ có document.own có thể gửi TUS software metadata | Không tái hiện trong nhánh TUS hiện tại: purpose binding, permission và Draft check chặn; HTTP tests pass. Finding 01 là direct PUT của software uploader, không phải bỏ qua kết quả này. |
| POST retry luôn tạo hai session cùng reservation | Không đúng với code hiện tại: CAS claim + stored TusFileId + 409 Location đã có, HTTP tests cover. |
| Mọi retry Uploaded bắt buộc gửi lại toàn bộ bytes | Không đúng: init có alreadyUploaded cho reservation cùng actor/target, kể cả empty blob. Finding 08 chỉ ra client chưa tự chuyển sang recovery đó sau final PATCH mơ hồ. |
| GC chỉ dựa vào ReferenceCount hoặc xóa DB trước storage | Không đúng. Defect nằm ở live-vs-historical FK và exception boundary, không ở thứ tự delete đã được sửa. |
| Full upload có thể ghi sau release-winning barrier | Test PostgreSQL hiện có vẫn pass và reject mutation; finding 03 thuộc DELETE từng file. |
| Prune metadata lỗi sẽ để lại partial file/refcount state | PostgreSQL fixture cho thấy transaction rollback đúng; finding 06 là thao tác bị chặn, không phải partial DB commit. |
| Traversal/ADS/reserved filename đơn lẻ được ingest/release chấp nhận | WindowsPathRules kiểm các trường hợp này. Finding 07 là quan hệ cấu trúc giữa nhiều path hợp lệ riêng lẻ. |
| Local URL thiếu capability mở đường tải blob trái phép | Endpoint Agent hiện từ chối nó; đây là API compatibility failure, không phải bypass. |
| RegisterUploadedFileAsync hiện là legacy TUS completion entry công khai | Handler hiện khóa legacy upload type; đường TUS đang chạy chỉ lưu bytes/reservation và để upload-complete finalize. Không kết luận method còn tồn tại đồng nghĩa client gọi được nhánh legacy. |

## Những khu vực chưa đủ bằng chứng

- Chưa kiểm tra MinIO thật về replay, signing headers, CORS, quyền bucket hoặc Object Lock. Bằng chứng 01 gồm production service + PostgreSQL + fake bytes store và frontend branch; semantics presign S3 có nguồn chính thức, không thay thế MinIO E2E.
- Chưa chạy HTTP fault injection cho client disconnect đúng giữa LocalStorage copy hoặc sau final PATCH; các boundary tương ứng được chứng minh riêng bằng filesystem fixture và frontend response fixture.
- TUS fetch không qua Axios refresh interceptor; token hết hạn có thể làm upload dài dừng ở 401. Đã ghi nhận trong project memory, nhưng review này chưa có test clock/token expiry đầy đủ qua browser thật và không gộp nó vào finding lost-response.
- Chưa kiểm mọi interleaving mới của expiry/cleanup trong khi final object write đang kéo dài, cleanup candidate bị context khác thay state, hai người finalize SHA mới cùng lúc, hoặc mismatch cleanup chạy cạnh writer khác. Không suy từ 9 barrier tests pass rằng toàn bộ state machine đã linearizable.
- Software reservations không dùng document quota branch; chưa có định nghĩa quota software/staging toàn hệ thống trong phạm vi được đọc. Không dùng con số quota document để khẳng định software uploads đã được bound.
- Chưa audit live rows/storage, lịch sử FK tồn đọng, thời gian GC thực tế hoặc object đã hỏng trong deployment. Các finding là lỗi/hành vi tái hiện được trên fixture, không phải báo cáo sự cố vận hành.
- Mutable Released path policy cần chủ sản phẩm xác nhận contract trước khi chọn remediation. Báo cáo nêu design risk, không mặc định gỡ khả năng đổi policy.

## Thứ tự xử lý đề xuất

1. Bảo vệ identity và commit của canonical bytes: CONTENT03-01/02; sửa GC history/FK và isolate lỗi candidate: CONTENT03-04.
2. Khép kín release boundary với DeleteFile: CONTENT03-03; sửa hash cache gây chọn sai payload: CONTENT03-05.
3. Sửa self-concurrency ở metadata prune và kiểm cấu trúc cây: CONTENT03-06/07; bổ sung recovery cho final PATCH: CONTENT03-08.
4. Chốt contract Released policies: CONTENT03-10; sửa/deprecate download-url Local sau khi đối chiếu caller: CONTENT03-09.

Chỉ đóng từng finding sau khi test mong muốn trước fix fail và sau fix pass, cùng kiểm chứng provider/HTTP thật tương ứng. Các fixture quan sát behavior hiện tại cần đổi assertions khi thực hiện remediation.
