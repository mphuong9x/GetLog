# 04 — Configuration, overrides, baselines và config approval

Ngày review: **2026-09-08, Asia/Saigon**. Prompt: `docs/code-review/prompts/04-config-overrides-baselines-and-approval.md`. Repository revision: **`09b66bd071b4aad6f4736e9adf45a6f4307d8afb`**. SHA-256 của prompt: `b6a563ffbdadae3ef558d67fe14ea7c598105bd8ee7219559684a85234f3a6f9`.

Rà soát source hiện tại, không sửa production code, migration, project memory hoặc checklist. Tuân thủ `AGENTS.md`, gồm quyết định giữ workflow installer dùng chung install token để các PC tự đăng ký và xuất hiện ở `/computer`. Không mở lại token reuse/enrollment, generic approval authorization/snapshot, ordinary upload/blob ownership hoặc Agent deployment execution đã thuộc các prompt khác.

## Kết quả

**15 confirmed defects và 1 design risk**. Những điểm cần ưu tiên là baseline không kiểm tra đủ nguồn cấu hình thực sự được render, render chấp nhận kết quả thiếu một phần cấu hình, ghi INI qua mirror không chặn newline, migration có thể kích hoạt dữ liệu chưa được duyệt, và modal có thể lưu nhầm máy khi response cũ về muộn.

| ID | Severity | Confidence | Category | Nội dung |
| --- | --- | --- | --- | --- |
| CFG04-01 | High | High | confirmed defect | Baseline “effective” bỏ whole-file overrides và secondary targets; API validate bỏ station scope |
| CFG04-02 | High | High | confirmed defect | Render lỗi vẫn trả file gốc hoặc file áp dụng một phần, không có trạng thái lỗi cấu hình trong manifest |
| CFG04-03 | High | High | confirmed defect | Giá trị JSON có newline đi qua INI mirror và tạo thêm key ngoài khai báo |
| CFG04-04 | High | High | confirmed defect | Mirror dùng snapshot suy ra từ primary, rồi đánh Stale toàn bộ override khi mirror khác base |
| CFG04-05 | High | High | confirmed defect | Render từ snapshot cũ có thể đánh Stale bản override vừa được người dùng cập nhật |
| CFG04-06 | High | High | confirmed defect | Migration chuyển Draft/Pending whole-file override thành Active values và để approval Pending mồ côi |
| CFG04-07 | High | High | confirmed defect | Migration không bảo toàn cấu hình: phục hồi key đã bị bỏ và snapshot Computer sai tầng Station |
| CFG04-08 | Medium | High | confirmed defect | Migration commit values trước khi retire file, lỗi lần save sau để lại trạng thái nửa chuyển đổi |
| CFG04-09 | Medium | High | confirmed defect | Apply to same model programs báo lỗi sau khi đã lưu chương trình đầu tiên |
| CFG04-10 | Medium | High | confirmed defect | Sửa metadata/targets và đổi lifecycle whole-file override thiếu cờ redeploy |
| CFG04-11 | Medium | High | confirmed defect | Config approval promote giá trị không còn hợp lệ theo declaration hiện tại |
| CFG04-12 | High | High | confirmed defect | Response cũ thay view máy mới; Save gửi về máy cũ dưới tiêu đề máy mới |
| CFG04-13 | Medium | High | confirmed defect | Pending revision không xuất hiện trong scope/matrix DTO và pending counter |
| CFG04-14 | Medium | High | confirmed defect | UI cho Reconfirm computed-stale nhưng API từ chối; preview cache có thể ngăn materialize Stale |
| CFG04-15 | Medium | High | confirmed defect | `NaN` vượt qua Decimal range validation rồi làm JSON render ném exception |
| CFG04-16 | Medium | High về code; Medium về tác động thực tế | design risk | Stale-impact acknowledgement chỉ ràng buộc count; writes không mang revision của view |

Severity mô tả hậu quả khi điều kiện của finding xảy ra, không khẳng định fleet hiện tại đã gặp lỗi. Các ví dụ giá trị là dữ liệu fixture; báo cáo không chứa credential, token hoặc log vận hành.

## Phạm vi và validation

Đã đọc `AGENTS.md`, `docs/PROJECT_REVIEW_CHECKLIST.md:646-656,825-832` (CFG-001/CFG-002), layering, target paths, application/domain config models, EF mappings, version manifest và phần Agent nhận file. Checklist CFG-001 đóng ở lifecycle set/delete thông thường; nhánh migration, computed-stale và DTO vẫn có khoảng trống dưới đây. CFG-002 vẫn chưa hoàn tất.

Đã trace bốn controllers; các config, baseline, override, migration và permission services được chỉ định; hai config approval handlers; chỉ phần `ApprovalService` gọi handler và save. Đọc phần assignment/pin dùng baseline và impact, phần `InstallationJobService` resolve effective files, cùng read-only effective preview/archive vì chúng dùng chung render cache. Frontend gồm Config Baselines, Override Files, ConfigValues/Parameters/Targets/MigrateOverrideFile modals, Computer drift, API/types, pin-impact và các test liên quan.

| Kiểm chứng | Kết quả cuối |
| --- | --- |
| Backend hiện có: config/override/baseline/assignment, cộng các test manifest/target liên quan | **282 test khác nhau pass**, 0 fail, 0 skip; 283 lượt chạy vì một test xuất hiện ở hai filter |
| Frontend hiện có: config-paths, config-file-order, pin-impact | **11 pass**, 0 fail |
| Diagnostic backend bổ sung, gọi production services/reader/writer/resolver | **16 pass**, 0 fail |
| Diagnostic frontend bổ sung, render `ConfigValuesModal` với response bị đảo thứ tự | **1 pass**, 0 fail |

Diagnostic assertions xác nhận **hành vi lỗi hiện tại**, không chứng minh đã sửa lỗi. Backend diagnostics dùng EF InMemory và fixture data hiện có; một ca inject lỗi vào lần `SaveChangesAsync` thứ hai. Ca stale snapshot chạy thứ tự resolve cũ → cập nhật row → render cũ, nên chứng minh vấn đề revision mà không cần giả định timing ngẫu nhiên. Frontend diagnostic dùng React Testing Library/jsdom với API và lớp trình bày Ant Design được mock; component và callbacks là source thật.

Lần build đầu gặp assets của một project trỏ vào cache NuGet khác và NU1301 trong sandbox. Đã restore từ cache package cục bộ `C:/Users/Admin/.nuget/packages`, tắt NuGet audit cho lượt restore, sau đó build/test thành công. Không cài dependency mới hay sửa project files. Không chạy PostgreSQL/MinIO/IIS/fleet E2E, full suite hoặc frontend build/lint; không dùng database production.

## Sáu luồng bắt buộc

| Trace | Hành vi source hiện tại |
| --- | --- |
| 1. UI/API → Set/Bulk → scope → Fold → persist | `ConfigValuesModal.save` gọi PUT đơn, lấy **entity target ID từ view**, không gửi station resource ID. Trong source frontend hiện tại không thấy caller cho bulk import; bulk vẫn tồn tại ở controller/service/tests. `SetAsync` chỉ chấp nhận Station/Computer, kiểm MaxScope/type/manage permission, đọc released pin và tầng dưới, yêu cầu base đã biết. `BulkSetAsync` lấy target thuộc station, gom duplicate `(parameter,target)` bằng last item, xử lý Station trước Computer, stage Station value để Computer snapshot đúng giá trị cùng batch; có một problem thì không apply batch. |
| 2. Approval-required → Submit → handlers → redeploy | New value ở Draft → PendingApproval → Active. Khi thay Active value, `PendingValue/PendingBaseValueAtCreation/PendingStatus` giữ proposal riêng; runtime vẫn dùng Active cũ. Set của cùng requester cancel request cũ rồi submit request mới trong transaction. Delete cancel pending request cùng mutation. Approve promote proposal, clear pending và flag installed records; reject/cancel bỏ pending, giữ Active cũ. Tuy nhiên handler không revalidate declaration hiện hành; DTO cũng không trả proposal. |
| 3. Assignment/pin → baseline và impact | Assignment với target version và pin mới gọi `EnforceConfigBaselineAsync`; kiểm version thuộc package và Released. Active baseline kiểm packaged values cùng Station/Computer **primary** value layers. Pin yêu cầu `AcknowledgedStaleCount` bằng count vừa preview; unpin không có incoming base nên bỏ qua. Preview đọc base của incoming version nhưng chỉ primary paths và Active rows, chưa đại diện cho toàn bộ effective files/targets. |
| 4. ResolveOverrides → resolver/render/cache → Agent | Whole-file precedence: Computer > Station > Model, chỉ Active, chỉ paths nằm trong `OverridablePaths`. Value overrides chỉ Active/live parameter, lọc MaxScope, mở rộng mirrors/templates/list separators; không cần whole-file `OverridablePaths`. Mỗi file: packaged blob → whole-file blob nếu có → rendered values. Glob target phải khớp đúng một live path. Cache miss đọc base, kiểm snapshot, ghi từng value, mark Stale, hash UTF-8 và persist cache/blob. Cache hit trả trước bước kiểm/persist Stale. Manifest giữ **live package path**, thay SHA/size/blob download capability; Agent nhận file đã resolve, không tự fold values nữa. |
| 5. Whole-file create/edit/migrate/delete | Source hiện tại đã bỏ create/upload/edit whole-file override. API còn list/detail/content/migration-preview/migrate/delete; generic approval endpoint vẫn dùng `OverrideFileApprovalHandler` cho legacy Draft/Pending. Migration diff theo released comparison version, tạo/reuse parameters và Active values, rồi retire file. Chỉ chặn Model scope, unsupported file, thiếu key, thiếu permission hoặc parameter cần approval; không chặn chính whole-file Draft/Pending, không đảm bảo transaction toàn migration. |
| 6. ReloadConfig | `ComputersController.OperatorIssuableCommands` vẫn cho gửi ReloadConfig. Agent handler trả chuỗi `no-op: ReloadConfig not implemented in this agent version`; dispatcher vẫn ACK kết quả này. Không thấy caller ReloadConfig trong frontend config workflow hoặc `ConfigValueRedeploy`. Config values được hứa áp qua redeploy ở poll/update window, không qua command này. Đây là gap CFG-002 đã biết, không phải bằng chứng config-value workflow phụ thuộc vào reload. |

Package/version manifest thông thường (`SoftwareVersionService.GetManifestAsync:695-736`) chứa packaged hashes. Agent manifest và effective preview/archive có lớp resolve riêng; hai loại manifest khác nhau là chủ ý. Agent `InstallDirectoryService:39-67` vẫn tôn trọng PreserveLocal, nên rendered SHA trong desired manifest không chứng minh một file local được preserve đã thay đổi. Không mở lại review deployment execution của prompt 02.

## Findings chi tiết

### CFG04-01 — Baseline effective không dùng cùng nguồn với runtime render

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigBaselineValidator.cs:78-127,138-161,226-262`; `ConfigBaselineService.cs:275-280` cùng thư mục; `InstallationJobService.cs:735-772` cùng thư mục.
- **Symbol:** `ValidateAsync`, `LoadLayersAsync`, `EvaluateEffective`, `ConfigBaselineService.ValidateAsync`, `ResolveOverridesAsync`.
- **Evidence:** Validator lấy blob từ `SoftwareFiles` và layers từ `ConfigParameters.TargetRelativePath/Sector/Key`; không resolve whole-file override hoặc `ConfigParameterTargets`, không lọc MaxScope như `OverrideResolver`. API nhận `stationResourceId` nhưng luôn gọi `ValidateVersionForModelAsync`. Diagnostic `BaselineEffective_IgnoresSecondaryTarget` cho primary=1, mirror=1, override=2: baseline Exact mirror=1 **Passed**, trong khi resolver/writer cho mirror=2.
- **Finding:** Kiểm tra được gọi là effective có thể pass cấu hình mà Agent manifest sẽ thay đổi thành giá trị vi phạm baseline. Chiều ngược lại cũng có thể block pin do layer runtime thực sự đã bỏ vì MaxScope.
- **Realistic failure scenario:** Kỹ sư khai báo một tham số điều khiển thêm vị trí mirror được baseline quản lý. Pin version được chấp nhận vì validator đọc mirror trong packaged file; manifest render mirror theo override và gửi giá trị khác. Legacy whole-file override đổi key được quản lý tạo cùng loại chênh lệch.
- **Operational impact:** Baseline không còn là chốt kiểm đáng tin cậy cho cấu hình station sẽ nhận; màn Validate và thao tác pin cũng có thể cho kết quả khác nhau.
- **Root cause:** Có nhiều cách dựng effective values, nhưng validator chỉ triển khai một phần của pipeline runtime; tham số scope trong API bị bỏ.
- **Recommended direction:** Dùng một kế hoạch resolve/render có kiểm chứng chung cho preview, baseline và manifest. Khi có station scope, kiểm toàn bộ effective outputs cần quản lý, gồm file precedence, target expansion và scope eligibility; giữ riêng API packaged-only nếu cần.
- **Required regression test:** So sánh baseline result với bytes render cho whole-file override, mirror ở file khác, glob, MaxScope bị thu hẹp và Computer layer. API validate có station ID phải khác packaged-only khi effective value khác.
- **Estimated change scope:** Large.

### CFG04-02 — Render lỗi vẫn xuất bản cấu hình thiếu hoặc áp dụng một phần

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:71-95,111-153,287-307`; `InstallationJobService.cs:764-776,849-858,1033` cùng thư mục.
- **Symbol:** `RenderAsync`, `ApplyValues`, `PersistAsync`, `ResolveOverridesAsync`, installation-success record update.
- **Evidence:** Không đọc được base hoặc persist cache thất bại không recover được thì trả `null`. Lỗi writer `ConfigKeyNotFoundException/InvalidOperationException` được catch theo từng value; các value khác tiếp tục, file thiếu change vẫn được cache. Caller hiểu `null` là dùng blob trước render. Diagnostic `Render_WriterFailureCachesPartiallyAppliedFile`: requested JSON `valid=3, typed=not-number` từ numeric base cho file `valid=3, typed=2`, có rendered blob và cache hit lần sau.
- **Finding:** API/manifest không phân biệt “không có thay đổi” với “không áp được cấu hình yêu cầu”; có thể hoàn thành deployment của file fallback/partial như kết quả bình thường.
- **Realistic failure scenario:** Một key đổi type trong release, hoặc declaration String ghi vào JSON number. Operator lưu nhiều giá trị; một key fail lúc render nhưng manifest vẫn giao file chứa phần còn lại. Với lỗi persistence sau khi bytes đã render, máy có thể nhận base cũ.
- **Operational impact:** Máy chạy tổ hợp giá trị chưa được kiểm tra như một cấu hình hoàn chỉnh. Không có error/retry marker cho render failure trong manifest; khi job thành công, record được đặt Healthy. Việc lặp render có thể tiếp tục lấy partial cache.
- **Root cause:** Kiểu trả về nullable gộp no-op và failure; render áp dụng best-effort từng value và cache đầu ra đó.
- **Recommended direction:** Trả kết quả phân biệt success/no-change/stale/failure; chỉ publish output đáp ứng kế hoạch đã kiểm chứng. Lỗi cần chặn job hoặc giữ configuration revision cũ cùng trạng thái retry rõ ràng. Stale theo policy phải được phân biệt với lỗi parse/write/storage.
- **Required regression test:** Inject key/type/write/persist failure ở file thứ hai; assert không có manifest success chứa partial change, có retry state và retry sau phục hồi áp đủ values. Test cache không ghi nhận partial success.
- **Estimated change scope:** Large.

### CFG04-03 — INI mirror cho newline tạo key ngoài khai báo

- **Severity:** High.
- **Confidence:** High; đã chạy validator, template và renderer thật.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigValueValidator.cs:25-34`; `OverrideResolver.cs:206-224` cùng thư mục; `ConfigFileWriter.cs:57-100` cùng thư mục; `ConfigParameterService.cs:856-883` cùng thư mục.
- **Symbol:** `ConfigValueValidator.Validate`, `ExpandTargetsAsync`, `ConfigParameterTarget.Render`, `ConfigFileWriter.SetIni`, `NormalizeTargets`.
- **Evidence:** Newline chỉ bị từ chối khi **primary parameter.Format == Ini**. Mirror chuyển sang Format Ini sau validation; writer ghép nguyên value vào line. Diagnostic `JsonValueWithNewline_InjectsUndeclaredIniKeyThroughMirror` xác nhận String/JSON value `COM9\nUnexpectedSetting=enabled` qua validation, rồi INI render chứa thêm key `UnexpectedSetting` có giá trị `enabled`.
- **Finding:** Giới hạn một override chỉ viết các vị trí đã khai báo không được giữ khi chuyển format. Template/list separator có newline cũng chưa bị chặn ở boundary INI write.
- **Realistic failure scenario:** Parameter String được khai báo ở JSON và mirror sang INI. Người có quyền sửa value dán chuỗi nhiều dòng; newline trở thành cấu trúc INI thay vì dữ liệu của một value.
- **Operational impact:** Có thể thêm/đổi setting ngoài vị trí dự kiến của parameter, vượt phạm vi key mà declaration/approval đang mô tả, hoặc làm hỏng cấu hình.
- **Root cause:** Validation dựa trên format primary; không validate giá trị **sau template** theo từng destination format. INI writer không tự bảo vệ invariant một line.
- **Recommended direction:** Từ chối CR/LF cho mọi INI output, bao gồm template và separator; kiểm tất cả targets trước persist/submit. Thêm invariant này ngay tại writer để mọi caller đều được bảo vệ.
- **Required regression test:** JSON primary → INI mirror với LF/CRLF; template/separator chèn newline; cả Set, Bulk, ApplyValues và approved proposal phải không tạo key ngoài khai báo và không publish partial output.
- **Estimated change scope:** Medium.

### CFG04-04 — Snapshot của mirror bị suy diễn từ primary rồi làm Stale toàn override

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:242-250,795-826`; `OverrideResolver.cs:206-224` cùng thư mục; `ConfigRenderService.cs:116-131,168-181` cùng thư mục.
- **Symbol:** `SetTargetsAsync`, `SimulateAsync`, `ExpandTargetsAsync`, `ApplyValues`, `MarkStaleAsync`.
- **Evidence:** SetTargets chỉ chứng minh có thể ghi sample và đọc lại; không yêu cầu current mirror bằng rendered primary base. Resolver gán mirror snapshot bằng `Render(primary.BaseValueAtCreation, template, separator)` và giữ cùng OverrideId. Diagnostic `TargetDeclaration_AllowsDifferentBaseThenRenderStalesWholeParameter`: primary COM20, mirror COM40; target lưu hợp lệ, override COM9 snapshot COM20; render mirror bỏ change và chuyển row duy nhất sang Stale.
- **Finding:** Target có thể được nhận là hợp lệ nhưng không dùng được ngay từ lần render đầu. Một mirror stale làm primary và mọi mirror khác bị resolver loại ở lần tiếp theo.
- **Realistic failure scenario:** Thêm mirror vào file đang chứa default khác, hoặc pin release chỉ đổi mirror mà giữ primary. Primary có thể được render thành COM9 trong lượt hiện tại; mirror giữ COM40 và row bị Stale. Lượt resolve sau bỏ cả primary, tạo file set khác.
- **Operational impact:** Các vị trí phải đồng bộ lại phân kỳ; effective output phụ thuộc lượt resolve/thứ tự files. Primary-only stale-impact preview không cảnh báo việc mirror đổi base.
- **Root cause:** Một snapshot và một status theo parameter được dùng cho nhiều physical destinations có base riêng, không có invariant đồng bộ base được enforce.
- **Recommended direction:** Hoặc enforce tất cả mirrors luôn tương thích với primary/template trên version liên quan, hoặc snapshot theo destination/context. Đánh giá toàn bộ parameter trước publish; quyết định stale không được phát sinh từ một mirror rồi vô hiệu hóa âm thầm các target đã render.
- **Required regression test:** Primary và mirror có default khác; release đổi riêng mirror; một parameter ghi hai files với thứ tự đảo ngược. Preview/pin/render phải thống nhất và không có output thay đổi giữa hai lần resolve cùng revision.
- **Estimated change scope:** Large.

### CFG04-05 — Old render có thể vô hiệu hóa revision mới

- **Severity:** High.
- **Confidence:** High; interleaving được tái hiện tuần tự với snapshot tách biệt.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Interface/Software/IOverrideResolver.cs:27-39`; `Services/Software/OverrideResolver.cs:94-110,126-135` và `Services/Software/ConfigRenderService.cs:128-130,168-181`, dưới `MProjectBackend/MProject.Application`.
- **Symbol:** `ResolvedConfigValue`, `ResolveValuesAsync`, `MarkStaleAsync`.
- **Evidence:** Resolved value có OverrideId nhưng không có Version của row. MarkStale load row hiện tại theo ID và Active, rồi đổi Status. Diagnostic `OldRenderSnapshot_MarksNewlyUpdatedOverrideStale`: resolve proposal cũ COM3/base COM7; user lưu COM8/base COM20; render cũ so với file COM20 đánh bản **COM8/base COM20** thành Stale.
- **Finding:** Quyết định dựa trên revision cũ được áp vào revision mới. EF concurrency token không giúp trường hợp update mới đã commit **trước khi** MarkStale load row.
- **Realistic failure scenario:** Agent poll/preview đang tải base từ storage; operator sửa hoặc reconfirm override; render cũ chạy tiếp và tắt cấu hình vừa được lưu thành công.
- **Operational impact:** Giá trị mới hợp lệ biến mất khỏi effective resolver, cần người dùng xử lý lại; một Station override có thể ảnh hưởng nhiều PC.
- **Root cause:** Stale side effect không ràng buộc revision hoặc snapshot đã dùng để quyết định; identity chỉ là row ID.
- **Recommended direction:** Carry expected Version/snapshot vào resolved plan và cập nhật Stale có điều kiện. Nếu row/base context đã đổi, bỏ kết quả cũ và resolve lại; không mutate bản mới.
- **Required regression test:** Barrier resolve-old → Set/Reconfirm/Approve-new → MarkStale-old, với hai DbContext PostgreSQL; assert bản mới vẫn Active, không có audit auto-stale cho revision mới.
- **Estimated change scope:** Medium.

### CFG04-06 — Migration kích hoạt whole-file chưa duyệt và để request Pending mồ côi

- **Severity:** High.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs:122-156,217-249,311-328,352-377`; `Services/Approvals/OverrideFileApprovalHandler.cs:50-56`, dưới `MProjectBackend/MProject.Application`; `MProjectFrontend/src/pages/OverrideFiles/OverrideFiles.tsx:499-508`.
- **Symbol:** `MigrateAsync`, `LoadAsync`, `UpsertValueAsync`, `RetireOverrideFileAsync`, `OverrideFileApprovalHandler.OnApprovedAsync`.
- **Evidence:** Load/migrate không kiểm whole-file Status; parameter mới mặc định RequiresApproval=false; Upsert luôn Active. Retire chỉ soft-delete file, không cancel pending approvals. UI migration không gate theo Active status. Diagnostic `Migration_PendingFileBecomesActiveValuesAndLeavesPendingApproval` xác nhận resolver trả Active values, whole-file bị delete, ApprovalRequest vẫn Pending.
- **Finding:** Quy tắc giữ cấu hình chưa duyệt khỏi runtime không được bảo toàn qua migration; CFG-001 deletion protection không được dùng ở nhánh này.
- **Realistic failure scenario:** Legacy override đang chờ duyệt được người có cả manage-override và package CRUD chuyển thành parameters. Values được dùng ngay, trong khi approver sau đó approve target cũ sẽ gặp not-found và request không hoàn tất.
- **Operational impact:** Áp cấu hình chưa qua quyết định review đang chờ, đồng thời tạo approval không còn target sống.
- **Root cause:** Migration được coi như dữ liệu Active sẵn có nhưng không xác minh điều kiện đó và không phối hợp approval lifecycle.
- **Recommended direction:** Chỉ migrate Active file, hoặc chuyển nguyên trạng thái/proposal sang workflow approval mới. Cancel/replace request cũ cùng transaction, có bảo vệ concurrent approval/delete.
- **Required regression test:** Draft, Pending, Active, rejected/cancelled legacy file; migration-vs-approve/delete với hai contexts. Không có Pending request trỏ target đã retire và không có Active values từ payload chưa được duyệt.
- **Estimated change scope:** Medium.

### CFG04-07 — Migration không bảo toàn effective configuration

- **Severity:** High.
- **Confidence:** High; hai ca khác nhau cùng chứng minh mất tương đương trước/sau migration.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs:96-118,241-249,311-313,444-491`; `ConfigValueLayering.cs:26-43` cùng thư mục.
- **Symbol:** `Diff`, `UpsertValueAsync`, `ResolveComparisonVersionAsync`, `ConfigValueLayering.Fold`.
- **Evidence:** Diff chỉ duyệt entries của override; key chỉ có trong packaged file không xuất hiện trong preview. Computer snapshot luôn dùng `entry.PackagedValue`, không phải effective Station/lower file layer. `Migration_ReintroducesKeysRemovedByWholeFile` cho packaged A=1/B=2, override chỉ A=3: preview cho phép migrate A, output lại có B=2. `Migration_ComputerSnapshotIgnoresActiveStationValue` cho packaged COM20, Station COM5, Computer whole-file COM3: migrated Computer snapshot COM20 bị Fold bỏ, effective còn COM5.
- **Finding:** Thành công của migration chưa có nghĩa giữ nguyên cấu hình trước đó. Preview “mọi giá trị thay đổi” thiếu cấu trúc/key bị xóa; base snapshot cũng không đúng với scope precedence sau khi retire file.
- **Realistic failure scenario:** Legacy file cố ý bỏ một setting để phần mềm dùng fallback, hoặc Computer file đè giá trị Station. Chuyển thành values theo UI làm setting bị phục hồi hoặc override máy mất hiệu lực ngay.
- **Operational impact:** Thay đổi behavior ngoài những dòng được operator thấy trong migration preview; port/setting máy có thể quay về giá trị của Station.
- **Root cause:** Migration so sánh một chiều các scalar với packaged file và không mô phỏng pipeline effective sau migration.
- **Recommended direction:** Trước commit, render candidate values trên đúng lower layers sau khi bỏ file cũ và kiểm tương đương ngữ nghĩa. Chặn những thay đổi deletion/type/null/structure chưa biểu diễn được; snapshot Computer theo effective Station và whole-file precedence còn lại.
- **Required regression test:** Key bị bỏ, JSON null/type change/array structure; model/station whole-file layers; Station value + Computer file cùng key; comparison version đổi. Không retire file nếu candidate output khác ngoài change được xác nhận rõ.
- **Estimated change scope:** Medium.

### CFG04-08 — Migration không có transaction bao trùm values và retire file

- **Severity:** Medium.
- **Confidence:** High về các commit tách rời; chưa chạy PostgreSQL fault injection.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs:246-249,316-337`.
- **Symbol:** `MigrateAsync`, `RetireOverrideFileAsync`.
- **Evidence:** Save parameters/values, rồi gọi retire và SaveChanges lần hai, sau đó decrement reference. Không có `ExecuteInTransactionAsync` quanh toàn thao tác. Diagnostic `Migration_SecondSaveFailureLeavesValuesCommittedAndFileLive` inject lỗi lần save thứ hai; sau Clear tracker/reload, values Active và whole-file live cùng tồn tại.
- **Finding:** Thao tác UI mô tả all-or-nothing có thể kết thúc ở trạng thái nửa chuyển đổi; request retry không phải rollback của request trước.
- **Realistic failure scenario:** Save đầu thành công, DB/cancellation/concurrency error xảy ra trước retire. Agent poll giữa hai commit áp values trên blob whole-file cũ, nên snapshot packaged có thể bị coi là Stale trước khi retry migration.
- **Operational impact:** Khó khôi phục trạng thái từ thông báo lỗi đơn; có thể persist stale side effects hoặc để người dùng thấy cả file lẫn values.
- **Root cause:** Atomicity dừng ở từng SaveChanges thay vì toàn chuyển đổi domain.
- **Recommended direction:** Một transaction cho parameters, values, retire, approval transitions và reference bookkeeping; preflight trước mutation, có concurrency guard trên source file/version. Retry cần đối chiếu migration identity/current state.
- **Required regression test:** PostgreSQL fault trước/sau mỗi Save, concurrent resolve và duplicate migration; hoặc toàn trạng thái cũ còn nguyên, hoặc toàn trạng thái mới hoàn tất, không quan sát được hai representations cùng Active.
- **Estimated change scope:** Medium.

### CFG04-09 — Apply to all programs lưu một phần rồi mới báo lỗi

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs:911-943,992-1015`; `MProjectBackend/MProject.Tests/ConfigValueOverrideServiceTests.cs:729-742`; `MProjectFrontend/src/components/files/ConfigValuesModal.tsx:156-184`.
- **Symbol:** `SetAsync`, `ApplyToSiblingProgramsAsync`, `ConfigValuesModal.save`.
- **Evidence:** Set transaction của chương trình chính đã commit trước khi gọi sibling preflight; mỗi sibling lại gọi Set độc lập. Test hiện có tên `RefusesBeforeWritingWhenASiblingLocksTheScope` chỉ assert sibling không có row. Diagnostic `Fanout_ThrowsButOriginalProgramAlreadyChanged` chạy test đó rồi xác nhận chương trình chính đã có COM9.
- **Finding:** API báo operation fail nhưng một phần giá trị đã đổi; lời hứa áp đồng loạt giữa các BOM programs không được giữ.
- **Realistic failure scenario:** Máy chạy hai programs cùng model; program B khóa MaxScope rộng hơn. Chọn apply all trên A trả lỗi nhưng A đã đổi port. Người dùng tưởng chưa chương trình nào thay đổi.
- **Operational impact:** Cùng một máy có config không đồng bộ giữa programs; retry có thể cancel/resubmit approval của phần đã thành công.
- **Root cause:** Preflight và transaction được đặt trong từng Set, không ở cấp toàn fan-out.
- **Recommended direction:** Resolve/validate tất cả programs trước mutation, rồi commit cùng transaction khi hợp đồng là all-or-nothing. Nếu chọn partial semantics, response/UI phải chỉ rõ từng phần đã apply/pending/fail và retry idempotently.
- **Required regression test:** Sibling cuối sai scope/type/base hoặc approval submission fail; assert chương trình chính và mọi sibling đều giữ giá trị/request cũ khi request fail. Mở rộng test hiện có để kiểm main row.
- **Estimated change scope:** Medium.

### CFG04-10 — Thay đổi effective config không phải lúc nào cũng tạo redeploy

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:158-171,255-275`; `OverrideFileService.cs:182-190` và `ConfigValueRedeploy.cs:27-38` cùng thư mục; `MProjectBackend/MProject.Application/Services/Approvals/OverrideFileApprovalHandler.cs:50-56`; `Services/Software/InstallationJobService.cs:210-227` dưới application.
- **Symbol:** `SetForFileAsync`, `SetTargetsAsync`, `DeleteOverrideFileAsync`, `OverrideFileApprovalHandler.OnApprovedAsync`, `ConfigValueRedeploy.FlagAsync`.
- **Evidence:** Value set/delete/approve có flag; thay MaxScope/target/template hoặc whole-file approve/delete chỉ save, không flag affected installed records. Diagnostic `TargetMetadataChange_DoesNotFlagInstalledMachine` tạo Active override và Healthy installation, thêm target thành công nhưng record vẫn Healthy. Poll same installed version chỉ tạo redeploy khi có drift phù hợp; ConfigChanged là trigger độc lập AutoHeal.
- **Finding:** Desired effective output thay đổi mà không có delivery trigger nhất quán cho máy đang cài cùng version.
- **Realistic failure scenario:** Kỹ sư thêm/sửa mirror hoặc retire legacy override để máy dùng base. Preview/manifest mới sẽ resolve khác, nhưng máy đang Healthy không có job mới cho đến một trigger khác; AutoHeal có thể tắt.
- **Operational impact:** Máy mới cài và máy đã cài cùng version nhận cấu hình khác nhau; operator không thấy việc còn cần redeploy.
- **Root cause:** Invalidation gắn với value CRUD thay vì mọi mutation làm thay đổi effective configuration.
- **Recommended direction:** Dùng một cơ chế đánh dấu configuration revision/affected installations cho value, declaration/targets và legacy file lifecycle. Tính theo package và scope; xử lý cùng commit domain.
- **Required regression test:** Same-version Healthy installation, AutoHeal off; sửa/remove target, thu hẹp MaxScope, approve/delete Active file phải tạo ConfigChanged và job phù hợp ở poll kế tiếp. No-op metadata edit không cần redeploy.
- **Estimated change scope:** Medium.

### CFG04-11 — Approval không kiểm lại declaration hiện hành

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Approvals/ConfigValueOverrideApprovalHandler.cs:57-76`; `ApprovalService.cs:170-179` cùng thư mục; `MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:158-168`.
- **Symbol:** `ConfigValueOverrideApprovalHandler.OnApprovedAsync`, `ApprovalService.ApproveAsync`, `ConfigParameterService.SetForFileAsync`.
- **Evidence:** ApprovalService gọi `ValidateApprovalAsync`, nhưng config-value handler không override validation hook; OnApproved load Parameter rồi promote và flag. Không gọi ConfigValueValidator, kiểm target eligibility hoặc baseline. Diagnostic `Approval_PromotesValueInvalidUnderCurrentDeclaration` submit COM8, đổi DataType sang Int rồi approve; Validate hiện tại từ chối COM8 nhưng handler vẫn đặt Active.
- **Finding:** Proposal hợp lệ khi submit có thể không còn hợp lệ khi quyết định duyệt có hiệu lực; config handler vẫn chấp nhận như bình thường.
- **Realistic failure scenario:** Trong thời gian approval Pending, package maintainer đổi AllowedValues/Min/Max/DataType hoặc target mappings. Approver duyệt proposal cũ và máy nhận giá trị không còn phù hợp declaration, hoặc renderer bỏ change.
- **Operational impact:** Trạng thái Approved/Active không bảo đảm payload có thể được áp theo cấu hình hiện hành; dễ dẫn đến fallback/partial render của CFG04-02.
- **Root cause:** Validation chỉ nằm ở Set; handler coi proposal và declaration là ổn định trong suốt thời gian chờ.
- **Recommended direction:** Bind proposal với declaration/config plan revision và revalidate ở final approval trong commit boundary. Nếu có thay đổi làm proposal không còn hợp lệ, yêu cầu cập nhật/resubmit; giữ Active cũ.
- **Required regression test:** Đổi type/range/allowed values/MaxScope/targets, chuyển station hoặc pin trong khi Pending; approve không được promote payload không còn hợp lệ. Test giữ Active revision cũ khi revalidation fail.
- **Estimated change scope:** Medium.

### CFG04-12 — Response cũ làm Save nhắm nhầm máy

- **Severity:** High.
- **Confidence:** High; component interaction được tái hiện bằng React Testing Library.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/components/files/ConfigValuesModal.tsx:98-121,152-164,636-643`; `MProjectFrontend/src/pages/Computer/components/ComputerDetailDrawer.tsx:615-620`.
- **Symbol:** `load`, load effect, `save`, modal title/target props.
- **Evidence:** Mỗi load gọi `setView(loaded)` và `setLoading(false)` mà không abort/generation check khi target đổi. Title ưu tiên `targetName` prop; save lấy `view.targetId`. Diagnostic mở A, đổi B, cho B response trước rồi A response sau: title vẫn B, value là A, click Edit/Save gọi API với `targetId=A`.
- **Finding:** View không được ràng buộc với target đang chọn; mutation dùng identity của response đã lỗi thời.
- **Realistic failure scenario:** Operator đóng/chuyển sang PC khác khi request trước còn chờ; response cũ đến sau. Họ nhìn tiêu đề PC mới và sửa cấu hình nhưng request tác động PC trước.
- **Operational impact:** Đổi cấu hình nhầm máy với quyền hợp lệ của operator; không phải vượt authorization, nhưng có thể làm máy khác redeploy.
- **Root cause:** Async state updates không kiểm target/request generation; target ID của view và props có thể thuộc hai lần mở khác nhau.
- **Recommended direction:** Cancel hoặc bỏ responses của generation cũ, clear state khi target/scope đổi, và chỉ cho write khi view identity khớp current target. Áp cùng nguyên tắc cho matrix và các modal/config baseline selectors dùng load tương tự.
- **Required regression test:** Deferred A/B responses về đảo thứ tự, close/reopen và scope/byResource đổi; chỉ current view được render và mọi Set/Delete/Reconfirm phải nhắm current target. Diagnostic hiện tại cần đảo assertion sau fix.
- **Estimated change scope:** Small.

### CFG04-13 — Pending revision bị bỏ khỏi read contract

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Models/ConfigValueOverrideModels.cs:8-16`; `Services/Software/ConfigValueOverrideService.cs:251-261,1312-1335,1367-1375` dưới application; `MProjectFrontend/src/types/configValueOverrides.ts:16-24`; `src/components/files/ConfigValuesModal.tsx:140-146,279-312` dưới frontend.
- **Symbol:** `ConfigValueLayerDto`, `ToLayer`, `GetDriftAsync`, `startEdit`, `renderLayer`.
- **Evidence:** Domain đã có PendingValue/PendingStatus nhưng DTO/ToLayer chỉ trả Active Value/Status. Drift count group theo `o.Status`, không xét PendingStatus. Diagnostic `PendingReplacement_IsMissingFromViewAndPendingCount`: row Active + PendingApproval replacement, scope DTO chỉ có Active value và pending count=0.
- **Finding:** Sau khi submit replacement thành công, refresh config screen làm proposal biến mất khỏi view; matrix/drift không báo đang chờ duyệt.
- **Realistic failure scenario:** Operator sửa COM8 thành COM9, nhận toast Pending, mở lại thấy COM8 Active không có proposal. Họ nhập lại hoặc thử xử lý row, tạo thêm cancel/resubmit hoặc thao tác delete ngoài ý định.
- **Operational impact:** Người dùng không biết proposal hiện hành là gì và có thể quản lý nhầm Active revision. Approval drawer ở màn khác không sửa được thiếu sót của scope read contract.
- **Root cause:** Read models/frontend chưa được cập nhật theo domain pending-revision model của CFG-001.
- **Recommended direction:** Trả Active và Pending revision riêng, kèm request identity/status cần hiển thị; counters tính pending replacement; edit/cancel/clear phải có nghĩa rõ đối với từng revision.
- **Required regression test:** Active + Draft/Pending replacement, reject/cancel/approve và reopen modal; kiểm cả current effective value, proposal value, pending badge/count và edit defaults.
- **Estimated change scope:** Medium.

### CFG04-14 — Computed-stale, stored-stale và cache không đồng bộ

- **Severity:** Medium.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigValueLayering.cs:26-33`; `ConfigValueOverrideService.cs:1257-1258` và `ConfigRenderService.cs:61-68,88-89` cùng thư mục; `SoftwareFileService.cs:711-712` cùng thư mục; `MProjectFrontend/src/components/files/ConfigValuesModal.tsx:512-523`.
- **Symbol:** `Fold`, `ReconfirmAsync`, `RenderAsync`, `ResolveEffectiveBlobsAsync`, reconfirm action.
- **Evidence:** Fold có thể đặt DTO IsStale=true khi DB Status vẫn Active; UI gate theo IsStale nhưng endpoint chỉ nhận DB Stale. Diagnostic `ComputedStale_ShowsReconfirmButServerRejects` xác nhận nút hợp lệ theo view nhưng service trả “Only a stale value…”. Preview/archive render với markStale=false vẫn tạo cache; `PreviewCacheHit_SkipsStalePersistence` cho preview tạo partial cache, sau đó deployment markStale=true cache-hit giữ row Active.
- **Finding:** Người dùng không reconfirm được stale value trước khi một render materialize status; cache hit còn có thể ngăn bước materialize này lâu dài cho cùng input.
- **Realistic failure scenario:** Pin version mới hoặc thay Station value trong khi PC offline. Scope view chỉ ra Computer stale, operator bấm reconfirm bị từ chối. Nếu đã xem effective preview có một value khác vẫn apply, cache được tạo trước deployment và DB vẫn Active dù override bị skip.
- **Operational impact:** Khó xử lý drift đúng lúc; fleet drift panel dựa stored status không hiện đủ overrides đang bị bỏ.
- **Root cause:** Stale là computed state ở một số read paths, mutable state ở render path, nhưng action eligibility và cache side effects không dùng cùng định nghĩa.
- **Recommended direction:** Reconfirm kiểm stale against current effective lower layer, hoặc materialize trạng thái ở một revision-aware boundary độc lập cache. Cache chỉ reuse bytes; quyết định lifecycle không được bị bỏ vì có cached bytes.
- **Required regression test:** Reconfirm trước poll; preview→deployment cache hit với stale+valid values; hai PC có cùng cache key nhưng OverrideId khác. Status/count/action phải thống nhất, không mutate stale revision theo CFG04-05.
- **Estimated change scope:** Medium.

### CFG04-15 — Non-finite Decimal vượt validation và làm render lỗi

- **Severity:** Medium.
- **Confidence:** High; chạy trên .NET 8 hiện tại.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Software/ConfigValueValidator.cs:44-48,99-108`; `ConfigFileWriter.cs:282-286` và `ConfigRenderService.cs:141-151` cùng thư mục.
- **Symbol:** Decimal branch of `Validate`, `EnsureInRange`, `BuildNodeMatchingType`, `ApplyValues`.
- **Evidence:** Double parser chấp nhận NaN, nhưng cả `parsed < Min` và `parsed > Max` đều không từ chối nó. Diagnostic `DecimalNaN_PassesRangeValidationThenCrashesJsonRender` dùng Min=0/Max=10: Validate(NaN) thành công, numeric JSON writer ném ArgumentException khi serialize; ApplyValues không catch loại exception này.
- **Finding:** Giá trị không hữu hạn được nhận như Decimal hợp lệ và có thể lưu Active/Pending, nhưng không thể tạo JSON runtime hợp lệ.
- **Realistic failure scenario:** API hoặc text input gửi NaN cho tham số numeric. Mọi lần Agent resolve job có override đó đều có thể fail trước khi trả manifest cho đến khi sửa/xóa giá trị.
- **Operational impact:** Chặn delivery của job/poll response thay vì báo validation error cho người nhập; INI có thể chứa NaN dù range hữu hạn.
- **Root cause:** Không kiểm finite sau parse; render exception contract cũng không bao trùm lỗi serialize numeric node.
- **Recommended direction:** Chặn non-finite values ngay tại validator và numeric writer; kiểm bounds đầu vào tương tự. Lỗi phải trả dưới dạng validation/render failure có chủ đích theo CFG04-02.
- **Required regression test:** NaN, Infinity, -Infinity và exponent overflow cho Decimal có/không bounds; JSON numeric và INI; assert không persist/activate giá trị không hữu hạn và không phát sinh unhandled manifest exception.
- **Estimated change scope:** Small.

### CFG04-16 — Count acknowledgement và writes chưa gắn với revision đã xem

- **Severity:** Medium.
- **Confidence:** High về logic; Medium về tần suất/tác động vận hành.
- **Category:** design risk.
- **File:** `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:330-346,393-401`; `ConfigValueOverrideService.cs:826-841,1393-1491` cùng thư mục; `MProjectBackend/MProject.Application/Models/ConfigValueOverrideModels.cs` (`SetConfigValueOverrideRequest`, `BulkSetConfigValuesRequest`); `MProjectFrontend/src/utils/pin-impact.tsx` (`confirmPinImpact`).
- **Symbol:** `EnsureImpactAcknowledged`, `PreviewAssignmentImpactAsync`, `SetAsync`, `BulkSetAsync`, `confirmPinImpact`.
- **Evidence:** Pin chỉ kiểm `acknowledgedStaleCount == impact.Entries.Count`; preview không trả digest/revision. Set/Bulk requests không có expected row/config revision hoặc operation ID; server load current row tại thời điểm request chạy. EF Version chống updates cạnh tranh sau load, không chống request từ view cũ được xử lý sau khi người khác đã commit. Kiểm baseline/impact cũng xảy ra trước pin transaction.
- **Finding:** Cùng count không chứng minh operator đã xem đúng các overrides sẽ mất hiệu lực. Delayed retry từ view cũ có thể thành một update mới hợp lệ và ghi đè intent mới hơn.
- **Realistic failure scenario:** Preview chỉ ra một override A bị stale; trước khi pin, A được bỏ và B được tạo, tổng vẫn là một. Count cũ vẫn được chấp nhận dù operator chưa thấy B. Hoặc response của Set bị mất, user khác cập nhật rồi request cũ được retry và thay giá trị mới.
- **Operational impact:** Acknowledgement/audit không ràng buộc chính xác tập tác động đã được review; khó phân biệt retry với yêu cầu thay đổi mới.
- **Root cause:** API dùng count và mutation current-state thay vì expected revision/preview identity. Không có bằng chứng mọi endpoint phải cung cấp idempotency theo hợp đồng hiện tại, nên phân loại design risk.
- **Recommended direction:** Nếu cần bảo toàn quyết định đã xem, trả impact token/digest gồm assignment/version/declaration/override revisions; verify tại commit. Gửi expected revision cho edits và operation ID cho retry/fan-out có approval side effects.
- **Required regression test:** A→B nhưng count không đổi, value mới trong lúc pin, delayed duplicate Set sau update khác, hai approvers và edit/delete pending đồng thời. Phân biệt conflict do view cũ với retry cùng operation.
- **Estimated change scope:** Medium.

## Good decisions cần giữ

- Giữ model values trong packaged version; runtime value overrides chỉ Station/Computer. Set từ chối base unknown; resolver lọc Active/live parameter và MaxScope.
- Giữ precedence whole-file Computer > Station > Model, sau đó value layer; các `OverridablePaths` chỉ dành cho whole-file, không dùng để vô hiệu hóa value overrides có khai báo.
- Giữ Active revision tách Pending proposal; Set/Delete thông thường phối hợp pending requests, approval promote/reject và redeploy. Không quay lại cách thay ngay Active value bằng proposal.
- Giữ bulk preflight toàn batch, duplicate normalization và Station-before-Computer snapshot staging. Cần mở rộng guarantees này cho fan-out và migration.
- Giữ JSON reader/writer cùng tolerance với BOM/comments/trailing comma, xử lý nested objects/arrays và bảo toàn JSON scalar type. Element-match phải xác định duy nhất và preview từ chối match tự mất sau write.
- Giữ safe relative-path checks và glob exactly-one-match. Manifest file path luôn lấy từ live package file; config declaration không được tự tạo path bên ngoài file set.
- Giữ typed validation, giới hạn đọc config 5 MiB và value length 4096; bổ sung destination-format/finite checks thay vì bỏ validation.
- Giữ rewrite Draft files trong transaction với conditional Draft version touch; dry-run dựng toàn write plan trước lưu. Giữ content-addressed rendered output và cache reuse giữa máy cho inputs giống nhau, nhưng tách lifecycle side effects khỏi cache.
- Giữ EF Version concurrency tokens, unique/check constraints theo scope và soft-delete filters. Giữ sự phân biệt desired config trên server với file runtime/local thực tế.

## Rejected suspicions và giới hạn diễn giải

| Nghi vấn | Kết luận sau trace |
| --- | --- |
| Whole-file upload/edit hiện tại bỏ approval | Không có create/upload/edit route trong current controller/service; không review lại đường upload đã bỏ. Finding migration chỉ áp dụng legacy rows còn tồn tại. |
| Delete pending luôn để request kẹt như CFG-001 cũ | Set/Delete value thông thường đã cancel cùng transaction và tests pass. Whole-file Delete cũng xử lý requests; nhánh **migration** mới thiếu xử lý đó. |
| Pending replacement làm Active value biến mất ngay | Không đúng cho flow thường; domain và handler giữ Active riêng. Lỗi ở visibility/validation, không phủ nhận fix lifecycle đã có. |
| Bulk import bypass RequiresApproval | Bulk từ chối parameter RequiresApproval, validate mọi item trước apply và không apply batch khi có problem. Không thấy current frontend caller bulk; không suy diễn từ comment cũ rằng UI đang import. |
| Bất kỳ target path nào cũng đi thẳng xuống filesystem Agent | Không đúng: MapToFiles/caller áp trên live version files, manifest giữ file.RelativePath; Agent validate files trước deploy. Không mở finding path traversal từ NormalizePath thiếu vài quy tắc Windows khi chưa có đường biến path đó thành arbitrary manifest destination. |
| Glob match nhiều files có thể ghi hết các files | Không đúng: current code yêu cầu đúng một match và bỏ target nếu 0/>1; việc bỏ target không có error state vẫn thuộc CFG04-02. Exact path so sánh Ordinal trong khi glob ignore-case là behavior cần chú ý, chưa gọi là path escape. |
| Hai approvers chắc chắn có thể promote cùng proposal không có bảo vệ | Chưa có bằng chứng: ApprovalRequest và config row đều versioned, final mutations được SaveChanges cùng nhau. Không mở lại generic approval concurrency/security. Race CFG04-05 vẫn xảy ra vì nó load revision mới trước khi mutate. |
| Approve sau pin đổi phải áp proposal trên base mới | Không phải invariant hiện tại: snapshot cũ được coi là stale và base mới thắng là chủ ý. Cần UI/state đúng và không vô hiệu hóa revision mới; không đề nghị tự rebase proposal âm thầm. |
| ReloadConfig ACK chứng minh runtime reload | Không đúng: result ghi rõ no-op/unimplemented. Endpoint vẫn cho gửi nên CFG-002 còn mở, nhưng config-value save không gửi command này. Không đếm lại thành defect mới trong bảng. |
| Rendered hash xác nhận machine đã chạy đúng config | Không đủ: manifest là desired server state; PreserveLocal giữ file local đã có, còn outcome của deployment thuộc prompt 02. Báo cáo không đồng nhất server EffectiveValue với đo đạc runtime. |
| Tất cả cache persistence failures đều được retry bằng reload | Không đúng: chỉ reload cache row sau DbUpdateException; nếu không tìm thấy cache thì fallback null. Diagnostic current coverage không chứng minh mọi storage/provider failure đã được xử lý. |

## Insufficient-evidence areas

- Chưa chạy PostgreSQL barrier cho migration-vs-approval, concurrent Set/Bulk, rule-edit-vs-pin hoặc duplicate cache insertion. Source/diagnostics xác nhận các nhánh tuần tự; không dùng InMemory pass để khẳng định isolation, FK, unique constraints hoặc rollback của provider production.
- Baseline được enforce ở assignment/pin; không thấy enforcement ở mọi value update/approval, và Active baseline rules còn sửa được tại chỗ. Nếu baseline phải là invariant liên tục của fleet thì cần guard/revalidation ở các activation boundaries. Phân biệt rõ yêu cầu đó với chức năng hiện tại chỉ gate pin; chưa mở thêm finding về một hợp đồng continuous enforcement chưa được xác nhận.
- SetTargets simulation và Draft ApplyValues có một số overlap checks, nhưng runtime resolver không có ownership conflict pass tương đương cho mọi primary/mirror/element-match overlap. Chưa có fixture chứng minh một thứ tự cụ thể trong production, đặc biệt hai selectors khác nhau trỏ cùng JSON element; cần thêm regression về ordering/overlap trước khi kết luận riêng.
- Chưa kiểm chứng legacy data thực tế có Draft/Pending file, key deletion/type changes, mirrors khác base, duplicate INI keys hoặc case-variant paths hay không. Các điều kiện này làm thay đổi khả năng xảy ra, không thay đổi behavior đã tái hiện.
- Reader `ReadAll` và `ReadValue` có cách xử lý duplicate/case-sensitive identity khác nhau ở một số paths; migration dùng dictionary của scalar entries. Cần ma trận INI duplicate section/key và JSON tên property có dấu chấm/brackets để kết luận đầy đủ; không khẳng định mọi file người dùng sửa tay đều round-trip được.
- Chưa fault-test actual MinIO/LocalStorage render upload, cache expiry/GC interaction, encoding legacy và files sát ngưỡng kích thước. Ordinary blob ownership/GC defects đã thuộc report 03; không lặp lại ở đây.
- API scopes/permissions được trace ở boundary config; không audit lại toàn bộ role inheritance và generic approval snapshot. Frontend pages Baselines/Targets/Migration cũng có async load chưa có generation guard ở một số chỗ; chỉ ConfigValuesModal được tái hiện tới mutation sai target trong CFG04-12.
- Không có canary Agent/Windows Service để xác nhận runtime setting reload, timer changes hoặc preservation effect ngoài đoạn handoff đã đọc. CFG-002 cần test Unsupported hoặc chứng minh setting thật sự đổi nếu được triển khai.

## Dấu vết kiểm chứng và test cần giữ khi sửa

Fixtures tạm nằm trong `.tmp/review04/Program.cs`, `.tmp/review04/Review04.csproj`, `MProjectFrontend/.tmp/review04-config.test.tsx`. Kết quả backend TRX trong `.tmp/review04/test-results`; diagnostics trong `.tmp/review04/diagnostics.log` và `.tmp/review04/frontend-diagnostic.log`. Đây là artifacts cục bộ, không phải bổ sung vào production test suite. Danh sách dưới đây lưu tên và kết luận để báo cáo vẫn có ý nghĩa nếu thư mục tạm được dọn.

| Diagnostic | Kết quả xác nhận | Finding |
| --- | --- | --- |
| `Fanout_ThrowsButOriginalProgramAlreadyChanged` | Main value đã đổi dù sibling validation làm request fail | 09 |
| `ComputedStale_ShowsReconfirmButServerRejects` | View IsStale=true, stored Active, Reconfirm bị từ chối | 14 |
| `PendingReplacement_IsMissingFromViewAndPendingCount` | Proposal có trong DB nhưng DTO/counter không thể hiện | 13 |
| `Migration_PendingFileBecomesActiveValuesAndLeavesPendingApproval` | Active values + deleted file + Pending request | 06 |
| `Migration_ReintroducesKeysRemovedByWholeFile` | Migration preview thiếu key deletion; key packaged quay lại | 07 |
| `Migration_ComputerSnapshotIgnoresActiveStationValue` | Migrated Computer value bị stale vì Station lower layer | 07 |
| `Migration_SecondSaveFailureLeavesValuesCommittedAndFileLive` | Lỗi save thứ hai không hoàn tác values đã lưu | 08 |
| `TargetDeclaration_AllowsDifferentBaseThenRenderStalesWholeParameter` | Mirror hợp lệ khi khai báo nhưng render làm Stale cả parameter | 04 |
| `OldRenderSnapshot_MarksNewlyUpdatedOverrideStale` | Render cũ đánh Stale revision vừa được cập nhật | 05 |
| `PreviewCacheHit_SkipsStalePersistence` | Preview cache ngăn deployment materialize stored Stale | 14 |
| `Render_WriterFailureCachesPartiallyAppliedFile` | Cache lưu JSON chỉ áp được một trong hai values | 02 |
| `DecimalNaN_PassesRangeValidationThenCrashesJsonRender` | NaN pass range rồi render throw ArgumentException | 15 |
| `BaselineEffective_IgnoresSecondaryTarget` | Baseline pass nhưng mirror output không đạt rule | 01 |
| `Approval_PromotesValueInvalidUnderCurrentDeclaration` | Giá trị sai current DataType vẫn được promote | 11 |
| `TargetMetadataChange_DoesNotFlagInstalledMachine` | Target mới không đổi Healthy record thành ConfigChanged | 10 |
| `JsonValueWithNewline_InjectsUndeclaredIniKeyThroughMirror` | INI output có key ngoài declaration | 03 |
| Frontend: late A response → B title → Save | API Set nhận target A trong lúc UI title B | 12 |

Các filter backend đã chạy:

```text
FullyQualifiedName~Config|FullyQualifiedName~OverrideFile|FullyQualifiedName~StationSoftwareAssignmentServiceTests
FullyQualifiedName~InstallationJobServiceTests&FullyQualifiedName~Config|FullyQualifiedName~InstallationJobServiceTests&FullyQualifiedName~Override|FullyQualifiedName~BaselineEvaluatorTests
FullyQualifiedName~InstallationJobServiceTests.Poll_WritesOneValue|FullyQualifiedName~InstallationJobServiceTests.Poll_RewritesTheList|FullyQualifiedName~InstallationJobServiceTests.Poll_ResolvesAGlob|FullyQualifiedName~InstallationJobServiceTests.Poll_Drops
```

Ưu tiên remediation theo invariant: effective plan chung và validation destination (01–04), stale revision và migration lifecycle/atomicity (05–08), đúng target và delivery triggers (09–12), rồi read contract/action/counter (13–14) và finite/revision safeguards (15–16). Chuyển diagnostic assertions sang expected behavior và chạy provider/UI tests tương ứng trước khi đóng từng finding.
