# Review 08 — Test-result ingestion and monitoring

Ngày: 2026-09-09. Commit: `21c8cb3b5cb12217d8fba4581e33f12e43f635b0`.

Thực hiện theo `docs/code-review/prompts/08-test-result-ingestion-and-monitoring.md`. Không sửa production code. Đã đọc AGENTS.md, LOG-001, cấu hình Agent/API, toàn bộ shared parser/corpus/shared tests và các luồng trong phạm vi. Giữ nguyên quyết định dùng chung install token và tự đăng ký PC; đây không phải finding.

## Kết quả

Có **7 findings: 5 confirmed defect, 2 design risk**. Ưu tiên xử lý monitor không tự cập nhật và mất log đến muộn khi liên tục đầy batch. LOG-001 đã khắc phục backlog cùng timestamp và replay quay vòng lúc rảnh; chưa bảo đảm fairness khi liên tục có dữ liệu mới.

| ID | Severity | Category | Vấn đề |
|---|---|---|---|
| ING-01 | High / P1 | confirmed defect | Monitor mở lâu không lấy kết quả mới |
| ING-02 | High / P1 | confirmed defect | Log đến muộn bị loại khỏi overlap trước khi được replay |
| ING-03 | Medium / P2 | confirmed defect | Response bộ lọc cũ ghi đè dữ liệu bộ lọc mới |
| ING-04 | Medium / P2 | design risk | Khóa basename dùng chung mọi source làm nguồn đến trước quyết định dữ liệu |
| ING-05 | Medium / P2 | confirmed defect | Snapshot metrics cũ ghi đè snapshot mới |
| ING-06 | Medium / P2 | design risk | File mtime được coi là thời điểm test, thiếu provenance |
| ING-07 | Medium / P2 | confirmed defect | Tìm serial chỉ hiển thị 20 dòng, không truy cập trang tiếp theo |

## Kiểm chứng và artifacts

- Agent: **31 passed, 0 failed**, gồm scanner, metrics reporter và shared parser corpus. [TRX](08-agent-tests.trx).
- Backend: **66 passed, 0 failed**, gồm parser/shared corpus, query, shift và các test `AgentServiceTests.ReportTest*`. [TRX](08-backend-tests.trx).
- Probe C# gọi implementation đã build, dùng fixture scanner thực trên thư mục tạm và AgentService với EF InMemory: xác nhận late-file starvation, metrics đảo thứ tự, dedup khác source và mtime-as-TestedAt. [Kết quả](08-runtime-results.json), [source](08-probes/Program.cs).
- Probe React: **2/2 passed**, render component thật, mock API/controls, kiểm tra 10 phút không refresh và response đảo thứ tự: [source](08-probes/monitor.test.tsx), [kết quả máy đọc](08-frontend-probes.json). Đây là reproduction tests: assertion mô tả lỗi hiện tại, không phải regression tests chứng minh đã sửa lỗi.
- Không tìm thấy TestMonitor/testResults API test có sẵn trong frontend source. Không chạy full suite ngoài phạm vi, trình duyệt thật, tải production hoặc PostgreSQL integration.
- Lần đầu restore project probe bị `NU1301` do NuGet network bị chặn. Đã chuyển probe sang tham chiếu DLL vừa build và restore offline; chạy thành công. Build probe có warning xung đột assembly do tham chiếu test binaries; các nhánh dùng trong probe hoàn tất bình thường.

Lệnh test đã chạy từ repository root:

```powershell
dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter 'FullyQualifiedName~TestResultScannerTests|FullyQualifiedName~TestMetricsReporterTests|FullyQualifiedName~TestResultParserCorpusContractTests' --logger 'trx;LogFileName=08-agent-tests.trx' --results-directory C:/dev/MProject/docs/code-review/reports
dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore --filter 'FullyQualifiedName~TestResultParser|FullyQualifiedName~TestResultQueryServiceTests|FullyQualifiedName~TestShiftCalculatorTests|FullyQualifiedName~AgentServiceTests.ReportTest' --logger 'trx;LogFileName=08-backend-tests.trx' --results-directory C:/dev/MProject/docs/code-review/reports
dotnet restore docs/code-review/reports/08-probes/Review08.csproj --source C:/dev/MProject/.tmp -p:NuGetAudit=false
dotnet run --no-restore --project docs/code-review/reports/08-probes/Review08.csproj -- docs/code-review/reports/08-runtime-results.json
node MProjectFrontend/node_modules/vitest/vitest.mjs run --config docs/code-review/reports/08-probes/vitest.config.mjs --reporter=default --reporter=json --outputFile=C:/dev/MProject/docs/code-review/reports/08-frontend-probes.json
```

Probe C# cần test DLL Debug/net8.0 đã được build bởi hai lệnh test trước. Không dùng database hoặc registry production.

## Required tracing

1. **Roots → scanner → worker → HTTP.** `MProjectAgent/appsettings.json:22,36-47`: metrics 60s, TestLogs enabled, ba root Sfis/NoSfc/Golden, scan 300s, tail 2 ngày, overlap 15 phút, batch 1000. Defaults trong options tắt ingestion cho đến khi cấu hình. Scanner đi đúng 5 cấp `model/station/mo/pc/day`, chỉ lấy `.log` trực tiếp trong day folder; gửi relative path + UTC mtime + source, không mở/parse body. Worker sau initialization chạy loop riêng, có startup jitter; mỗi root gửi một batch mỗi tick. ServerClient gửi credential Agent và yêu cầu HTTP thành công, deserialize response rồi scanner mới lưu cursor.
2. **Endpoint → storage → acknowledgement.** `AgentController.cs:184-197` yêu cầu Agent authentication, truyền `CurrentAgentId`; `AgentService.cs:938-1004` resolve ComputerId từ Agent không thuộc PC đã xóa. Request không được chọn ComputerId. Server parse path bằng shared parser, normalize field lengths, collapse basename trong batch, lookup basename theo ComputerId, insert rồi SaveChanges trước response. Schema/migration có unique `(ComputerId, FileName)`. Sequential retry sau commit trở thành duplicates. Scanner ghi cả main/replay cursor qua temporary file + replace.
3. **Metrics.** Đây là snapshot registry, không phải aggregation log: reporter đọc `HKEY_USERS/<sid>/Software/CPEI_MFG/Unit*/Counter`, chọn hive đầu tiên có slot, gửi Pass/Fail/RJ45/PROBE. Server upsert theo `(ComputerId, SlotIndex)`, clamp âm về 0 và lưu CollectedAt/UpdatedAt. `GetTestCountersAsync` trả per-slot snapshot cho computer detail; Test Monitor lấy số liệu từ TestResultRecords, không cộng registry counters vào log totals.
4. **Monitor query → frontend.** Các endpoint TestResultsController đều có `ReadTestResults`. Query mặc định Sfis/current shift, date là calendar day trong timezone cấu hình. `[from,to)` tránh đếm hai lần ở boundary. Default shifts 07:00/19:00, SE Asia Standard Time. Summary/grouping chủ yếu chạy ở DB; fail rate dùng Pass+Fail, Unknown không thành Pass. Streak dùng lookback 24h, cố ý vượt shift boundary, giữ máy fail rồi im lặng. Search serial cố ý bỏ date/shift nhưng giữ source/model/station. Frontend API khớp enum strings, gọi sáu endpoint; component không có vòng polling.
5. **Support link boundary.** `ProductionSupportService.cs:702-724` lookup theo ComputerId + Sfis + basename; thiếu kết quả thì `TestResultRecordId` null (`:245`). FK `ProductionSupportModelConfiguration.cs:132-135` dùng SetNull. Ingestion không sửa support request/log. Retry sequential không tạo thêm result ID. ING-04 có thể làm mất liên kết Sfis; chưa có bằng chứng rằng nó sửa nội dung support record khác. Không mở rộng review semantics support.

## Findings chi tiết

### ING-01 — Monitor không tự cập nhật

- **Severity:** High / P1.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx:95-129`.
- **Symbol:** `TestMonitor.load`, effect gọi `load`.
- **Evidence:** Chỉ `useEffect(...,[load])` gọi load; không timer, subscription, focus refresh hoặc nút refresh. API module chỉ là axios wrappers.
- **Finding:** Dữ liệu giữ nguyên khi trang mounted và các dependencies không đổi, kể cả đã chuyển ca hoặc server nhận thêm failures.
- **Realistic failure scenario:** Operator mở màn hình đầu ca rồi để theo dõi dây chuyền. Sau đó Agent gửi nhiều FAIL nhưng màn hình vẫn báo attention bằng 0.
- **Operational impact:** Cảnh báo đến muộn không giới hạn cho đến khi đổi filter/remount/reload trang.
- **Root cause:** Fetch theo thay đổi filter được dùng làm cơ chế cập nhật duy nhất.
- **Recommended direction:** Poll có cleanup, tránh request chồng nhau, refresh khi tab hoạt động trở lại; hiển thị thời điểm cập nhật và stale/error state.
- **Required regression test:** Fake timers, mount, thêm dữ liệu API, advance interval; assert summary/attention/shift đổi mà không tương tác và không poll sau unmount.
- **Estimated change scope:** Small.

### ING-02 — Replay vẫn starvation khi trạm liên tục đầy batch

- **Severity:** High / P1.
- **Confidence:** High; runtime reproduction.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/TestResultScanner.cs:110-177`.
- **Symbol:** `CollectBatch`.
- **Evidence:** Fresh lấy đủ `EffectiveBatchSize`; overlap chỉ chạy khi `remaining > 0`. Cutoff mỗi cycle lại bằng main cursor trừ OverlapMinutes. Probe batch=2, late file cùng timestamp nằm trước cursor, bốn cycle mỗi cycle hai fresh files cách nhau 5 phút: late file không được gửi; đến idle cycle đã nằm ngoài cutoff.
- **Finding:** Replay cursor bền vững chỉ đảm bảo quay vòng khi có capacity; main cursor có thể làm file chưa replay hết hạn trước khi có capacity.
- **Realistic failure scenario:** File copy đến muộn với mtime giữ nguyên trong lúc station đang drain backlog hoặc có lượng file mới đủ lấp batch suốt hơn 15 phút.
- **Operational impact:** Mất kết quả khỏi monitoring bình thường; restart với cursor lưu thành công không tự khôi phục. Có thể thiếu FAIL và giảm tỷ lệ lỗi.
- **Root cause:** Deadline overlap gắn với tiến độ fresh, trong khi replay không được cấp throughput tối thiểu hay giữ pending durable.
- **Recommended direction:** Bảo đảm capacity cho cả fresh/replay hoặc lưu tập pending trước khi cutoff tiến qua nó. Giữ cursor ổn định và tránh tái tạo lỗi fresh backlog trước đây; cần cơ chế reconciliation/backfill có giới hạn.
- **Required regression test:** Continuous full batches vượt overlap duration, insert late file phía trước cursor, restart giữa chừng; assert mọi file cuối cùng được accepted đúng một lần về nghiệp vụ.
- **Estimated change scope:** Medium.

### ING-03 — Kết quả filter cũ ghi đè filter mới

- **Severity:** Medium / P2.
- **Confidence:** High.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx:95-129`.
- **Symbol:** `load`, effect.
- **Evidence:** Mỗi completion gọi setters trực tiếp; không AbortController, generation ID hoặc cleanup bỏ kết quả request cũ. Panel rejected giữ nguyên state từ lần tải trước.
- **Finding:** Thứ tự response quyết định nội dung, thay vì filter đang được chọn. Panel lỗi còn có thể trộn dữ liệu của filter trước với panel mới.
- **Realistic failure scenario:** Request Sfis chậm; operator chọn Golden; Golden trả trước rồi Sfis trả sau. Dropdown vẫn Golden nhưng counts/failures là Sfis. Probe dùng deferred promise tái hiện đúng lịch này.
- **Operational impact:** Đọc nhầm nguồn, ngày hoặc station; có thể che cảnh báo thật hoặc gây cảnh báo sai.
- **Root cause:** State fetch không gắn với query identity.
- **Recommended direction:** Chỉ apply generation mới nhất; hủy/ignore request cũ. Với panel thất bại, đánh dấu stale kèm query gốc hoặc thay panel bằng error state.
- **Required regression test:** Đảo thứ tự hai response; test một panel của filter mới reject; assert mọi panel hiển thị khớp filter hiện hành hoặc được ghi rõ unavailable.
- **Estimated change scope:** Small.

### ING-04 — Basename dedup không phân biệt source

- **Severity:** Medium / P2.
- **Confidence:** High về hành vi; Medium về tần suất/vai trò copy giữa nguồn.
- **Category:** design risk.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:957-976`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:240`.
- **Symbol:** `ReportTestResultsAsync`; unique index TestResultRecord.
- **Evidence:** GroupBy và lookup dùng FileName; unique key chỉ có ComputerId + FileName. Probe gửi NoSfc trước, cùng filename Sfis sau: Sfis accepted=0, duplicates=1, chỉ còn row NoSfc.
- **Finding:** Hai nguồn/context chia sẻ basename sẽ có một bản ghi; nguồn tới trước quyết định source và metadata lâu dài. Không có luật ưu tiên/promote Sfis được biểu diễn trong contract.
- **Realistic failure scenario:** Log NoSfc được copy vào root Sfis khi phục hồi đồng bộ, hoặc ordinary format drift tạo tên chung ở nhiều folder.
- **Operational impact:** Current monitor mặc định Sfis có thể không thấy file; support link chỉ lookup Sfis nên cũng có thể null.
- **Root cause:** Dùng basename như immutable event identity toàn computer, nhưng Source là thuộc tính bất biến của bản ghi đầu tiên.
- **Recommended direction:** Xác định copy là cùng event hay event độc lập. Nếu cùng event, có canonicalization/source provenance; nếu độc lập, mở rộng identity và migration. Không chỉ thêm Source vào index nếu việc copy sẽ gây double count.
- **Required regression test:** Cùng basename ở ba source theo mọi thứ tự, khác context, retry và support link; assert business identity/canonical source đã thống nhất.
- **Estimated change scope:** Medium.

### ING-05 — Snapshot metrics cũ có thể rollback counters

- **Severity:** Medium / P2.
- **Confidence:** High; runtime reproduction.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:905-932`.
- **Symbol:** `ReportTestMetricsAsync`.
- **Evidence:** Ghi đè mọi counter và CollectedAt không so với snapshot đang lưu. Probe gửi PassCount=200 tại T rồi 100 tại T−5 phút; stored count trở thành 100, CollectedAt lùi lại.
- **Finding:** Endpoint không bảo vệ ordering của snapshot; trạng thái cuối phụ thuộc completion order.
- **Realistic failure scenario:** Request cũ đã đến server nhưng bị chậm; Agent timeout/restart rồi snapshot mới được commit trước request cũ. Một Agent loop bình thường là tuần tự, nên không khẳng định mọi cycle đều gặp race.
- **Operational impact:** Counter detail hiển thị lùi số liệu, hoặc phục hồi giá trị trước reset trong cửa sổ đến snapshot tiếp theo.
- **Root cause:** Upsert thiếu version/ordering guard và concurrency condition.
- **Recommended direction:** Có snapshot sequence/epoch hoặc quy tắc freshness đủ rõ, compare-and-update atomic; không dùng max(counter) vì reset hợp lệ phải giảm số đếm.
- **Required regression test:** Snapshot mới rồi cũ, concurrent completion ngược, equal version retry và reset mới hơn; chỉ snapshot hợp lệ mới thắng.
- **Estimated change scope:** Medium.

### ING-06 — Mtime thay đổi ý nghĩa thời điểm test

- **Severity:** Medium / P2.
- **Confidence:** High về mapping; Medium về hợp đồng timestamp của mọi station writer.
- **Category:** design risk.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:991`; `MProject.TestResults/TestResultParser.cs:30-40,148-154`.
- **Symbol:** `ReportTestResultsAsync`; `TestResultParseOutcome` / timestamp validation.
- **Evidence:** TestedAt gán FileModifiedAt UTC. Parser kiểm tra timestamp trong tên nhưng không trả event time. Probe filename 08:00 vẫn Full và lưu TestedAt bằng mtime lúc probe chạy.
- **Finding:** File event time và filesystem write time không được phân biệt; Full không bảo đảm độ chính xác thời điểm test. Retry basename cũng không cập nhật TestedAt khi delayed writes thay mtime.
- **Realistic failure scenario:** Copy/tool chạm mtime hoặc ghi bổ sung log qua mốc 19:00 trước lần scan đầu; test buổi sáng vào ca tối. Nếu preserve mtime quá cũ, cutoff lại có thể bỏ file.
- **Operational impact:** Shift/day totals, thứ tự streak và LastTestedAt có thể sai so với lần test thực.
- **Root cause:** Một timestamp đảm nhiệm cả cursor scan và thời gian nghiệp vụ, không lưu provenance/timezone nguồn.
- **Recommended direction:** Giữ mtime phục vụ discovery; nếu writer contract cho phép, thêm event time + timezone/provenance, fallback rõ cho Partial/Unstructured. Không reject log chỉ vì không parse được timestamp.
- **Required regression test:** Copy preserve/change mtime, append qua boundary, DST ambiguous time và timezone đổi; totals/streak theo contract thời gian đã xác định.
- **Estimated change scope:** Medium.

### ING-07 — Serial search bị cắt ở trang đầu

- **Severity:** Medium / P2.
- **Confidence:** High; source trace.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx:104,116,399-465`; `MProjectFrontend/src/api/testResults.ts:33-47`.
- **Symbol:** `load`, records table, `testResultsApi.search`.
- **Evidence:** Search luôn gọi page=1/pageSize=20; component chỉ giữ `page.value.items`, bỏ total/page metadata, không có page state hoặc navigation.
- **Finding:** Với serial có trên 20 kết quả, người dùng không thể xem lịch sử còn lại từ màn hình này, dù backend hỗ trợ pagination.
- **Realistic failure scenario:** Serial được retest nhiều lần hoặc tìm substring khớp nhiều units; failure cũ hơn 20 records không truy cập được.
- **Operational impact:** Tra cứu lịch sử test thiếu dữ liệu mà không có dấu hiệu đã cắt trang. Danh sách recent mặc định giới hạn 20 có thể hợp lý; lỗi nằm ở search history không có đường xem tiếp.
- **Root cause:** Paged response bị hạ thành array ở component.
- **Recommended direction:** Giữ paging metadata, thêm pagination/load-more cho search, reset page khi đổi query; backend thêm tie-breaker ổn định sau TestedAt để bảo đảm pagination có thứ tự rõ.
- **Required regression test:** Serial có 21+ records và timestamps trùng; truy cập hết mọi ID qua các trang, không mất/nhân đôi trên dataset cố định, filter mới về trang 1.
- **Estimated change scope:** Small.

## Good decisions cần giữ

- Shared parser độc lập filesystem/EF, corpus 17 case dùng chung hai consumer, enum values được test để không phá persisted schema.
- Partial/Unstructured vẫn được lưu và đếm; tên FAIL vẫn giữ failure khi metadata chỉ parse một phần. Unknown không bị coi là Pass; frontend có unparsed warning.
- Không đọc body log mutable: scanner chỉ cần metadata để quan sát production; không nên mở rộng thành parser nội dung nghiêm ngặt gây chặn ingestion.
- Cursor `(mtime, relativePath)` có thứ tự ordinal, replay cursor lưu qua restart, legacy watermark được đọc; save sau acknowledgement bằng temp+replace. Các test backlog cùng timestamp đang pass.
- Batch clamp 1..5000 khớp server, không silently Take(N) rồi acknowledge toàn batch quá cỡ. Server chỉ trả thành công sau SaveChanges.
- Ingestion ownership lấy từ authenticated Agent; monitor endpoints có permission. Ngoại lệ shared install token được giữ nguyên.
- Mặc định loại Golden/NoSfc khỏi production yield, calendar-date request không gửi UTC midnight từ browser, interval `[from,to)`, streak cố ý không reset theo ca, DB aggregation tránh tải toàn bộ logs cho summary.
- Registry reset allowlist không đụng các interlock/fail-streak keys và không tự tạo counter không có trên station.

## Rejected suspicions và giới hạn bằng chứng

- **Network mất sau commit / restart gây double count:** sequential resend được dedup; backend test overlap pass. Crash trước cursor replace có thể resend nhưng không tự tạo event thứ hai. Chưa fault-inject crash thật.
- **Agent có thể chọn PC khác bằng path PcName:** PcName chỉ là metadata; ComputerId lấy từ Agent. Không xác nhận ownership bypass ở hai endpoint này.
- **Partial bắt buộc đồng nghĩa FAIL bị mất:** sai; recoverable FAIL vẫn được lưu Fail, unparsed percentage phản ánh chất lượng metadata. Full thiếu serial là hành vi corpus đã đóng băng, không tự coi là defect.
- **Search serial bỏ ngày/shift hoặc streak vượt ca:** đây là hành vi có test và mục đích rõ; không ghi thành lỗi. Source/model/station vẫn áp dụng.
- **Exact-depth và tail window:** phù hợp layout cấu hình. File ở folder quá tail hoặc mtime trước overlap không được tự backfill, kể cả outage dài; là giới hạn vận hành cần biết, không hứa at-least-once vô hạn. Root source khác casing được scanner nhận, server enum converter xử lý string.
- **Enumeration khi rotate/xóa folder:** wrappers trả lazy enumerable, một số lỗi lúc MoveNext có thể thoát ra root scan; RunLoopAsync log và retry. Chưa fault-inject ACL/rotation trong lúc enumerate; không khẳng định mất file vĩnh viễn riêng từ nhánh này. Không chạy thử lên root production.
- **High volume:** scanner materialize/sort toàn bộ candidate trong tail rồi mới Take; một root chỉ gửi một batch/tick, không drain ngay nhiều batch. Default ceiling lý thuyết 12.000 fresh files/giờ/root còn thấp hơn khi scan/network chậm. Không có benchmark filesystem/SQL production nên không kết luận latency hay memory cụ thể. Streak query còn materialize mọi FAIL trong lookback cho các máy đang failing.
- **Concurrent duplicate insert:** unique index ngăn duplicate persisted nhưng lookup-then-insert có thể nhận unique violation khi request overlap concurrent. Next sequential retry có thể hồi phục; chưa chạy PostgreSQL race/transaction test, không ghi thành lỗi mất dữ liệu xác nhận.
- **DST/timezone:** default SE Asia không DST. Calculator dùng GetUtcOffset(localTime), không quy định explicit invalid/ambiguous local time; invalid zone fallback TimeZoneInfo.Local và thay config sẽ re-bucket timestamps đã lưu. Tests hiện có chủ yếu UTC+7; chưa kiểm chứng DST runtime. Browser format theo timezone browser trong khi query theo station timezone, nên giờ hiển thị trên browser khác vùng có thể gây hiểu nhầm; chưa có yêu cầu UX rõ để xếp defect độc lập.
- **Streak timestamp trùng:** logic `LastAt > LastPassAt` không có thứ tự event thứ hai; FAIL cùng timestamp với PASS không được coi là trailing fail. Do chưa có ordering contract cho các event đồng thời, cần quyết định nghiệp vụ và regression test thay vì tự chọn thứ tự ID.
- **Metrics multi-user/registry view:** chọn loaded hive đầu có counters, không chọn operator active; không có test thực cho nhiều hive hoặc 32/64-bit views. Chưa kết luận trạm hiện tại đọc sai. CollectedAt không normalize UTC tại service; Agent hiện gửi UTC nên không xác nhận lỗi provider ở luồng chuẩn.
- **Storage/format drift:** fields dài được truncate để giữ ingestion; filename >512 bị lọc mà response không có rejected count, nhưng Windows component thường giới hạn ngắn hơn nên chưa chứng minh file scanner hợp lệ gặp nhánh này. Request source enum số lạ không có validation ở service; normal scanner chỉ gửi ba nguồn đã biết. Không suy diễn thành cross-PC authorization bug.
- **Support integrity:** missing result chỉ khiến nullable link; không có ingestion code sửa request khác. Không khẳng định missing link được tự backfill sau này; đó là ngoài phạm vi liên kết tối thiểu được yêu cầu.

## Hướng xử lý

Ưu tiên ING-01 và ING-02, sau đó ING-03/05/07. ING-04 và ING-06 cần thống nhất event identity/time semantics trước khi đổi schema hoặc dedup để giữ đúng duplicate behavior và khả năng quan sát log bị lệch format. Báo cáo này chỉ đánh giá, chưa áp dụng các hướng sửa.
