# 11 — Tổng hợp code review và backlog khắc phục MProject

**Phạm vi/completeness:** Đã đọc mười một báo cáo nguồn **00–10** được liệt kê trong [reports/README.md](README.md), gồm [09 Identity/RBAC/approvals][r09], và đối chiếu lại bản tổng hợp 11 này. Đây là bản tổng hợp có giới hạn phạm vi, dùng để quyết định công việc trong các vùng đã có bằng chứng; **không phải chứng nhận hoàn tất toàn bộ review hoặc cho phép rollout**. Các finding identity/RBAC/generic approval/browser auth của report 09 được giữ nguyên giới hạn provider/runtime của báo cáo nguồn.

Ngày tổng hợp gốc: **2026-09-10, Asia/Saigon**. Repository HEAD gốc: `21c8cb3b5cb12217d8fba4581e33f12e43f635b0`. Prompt: [11-final-synthesis.md](../prompts/11-final-synthesis.md), SHA-256 `CA59CD497610229C07085651DB83D983CBE7720F399C7E670786EA28135DCAD5`. META-00 đối chiếu ngày **2026-09-13, Asia/Saigon** tại HEAD `d965c33e5b2e31bf26e8c6dc23ef71d9c59c18ca`; chỉ cập nhật tài liệu và không dùng code remediation sau report để xóa finding lịch sử. Đã đọc [AGENTS.md](../../../AGENTS.md), [review plan](../00-REVIEW-PLAN.md), toàn bộ [checklist lịch sử](../../PROJECT_REVIEW_CHECKLIST.md) và mười một báo cáo.

Plan yêu cầu đủ các intended reports trước final synthesis và chạy 09 trước quyết định đổi permissions/approval routes. Report 09 hiện có và được tích hợp theo đúng evidence, category, priority và giới hạn đã ghi trong báo cáo nguồn; META-00 không thực hiện review sâu mới hoặc nâng diagnostic probe thành acceptance evidence.

## 1. Đầu vào, giới hạn và kết luận để ra quyết định

| Đầu vào đã đọc | Revision khai báo | Finding gốc | Giới hạn cần giữ |
| --- | --- | ---: | --- |
| [00 Architecture][r00] | `0df8a0b` | 4 | 41 focused tests và serializer probe; chưa full-host/IIS lifecycle |
| [01 Enrollment/control plane][r01] | `0df8a0b` | 5 | Credential barriers SQLite, nhiều Agent ACL/TLS tests không chạy đạt do môi trường; chưa PostgreSQL/LocalSystem matrix |
| [02 Deployment jobs][r02] | **Không ghi revision/hash prompt** | 2 | Chỉ design risks; test run bị DLL lock, không có kết quả test hợp lệ |
| [03 Content/uploads/blobs][r03] | **Không ghi revision/hash prompt** | 10 | Có PostgreSQL và Local filesystem repro; MinIO replay chưa E2E |
| [04 Configuration][r04] | `09b66bd` | 16 | Repros dùng InMemory/renderer thật và React; chưa PostgreSQL concurrency |
| [05 Agent OTA][r05] | `439ce70` | 11 | Filesystem/subprocess/signing probes; full package bị thiếu runtime cache, chưa SCM/LocalSystem/net48 bundle E2E |
| [06 Launcher/process][r06] | `db480df` | 9 | 4 runtime probes, 5 source traces; net48 build pass, chưa real desktop/multi-session |
| [07 Production support][r07] | `db480df` | 9 | SQLite/React probes; 9 PostgreSQL cases trả về sớm khi thiếu test connection string |
| [08 Test monitoring][r08] | `21c8cb3` | 7 | Filesystem/InMemory/React; có revision nhưng không ghi hash prompt; chưa PostgreSQL/browser thật |
| [09 Identity/RBAC/approvals][r09] | `21c8cb3` | 13 | 12 confirmed defects, 1 design risk; probes có InMemory/SQLite/HTTP loopback/React, chưa PostgreSQL concurrency/browser thật/IIS |
| [10 Server deployment][r10] | `21c8cb3` | 13 | PostgreSQL/API Release và function probes; chưa IIS/two-host/TLS fleet drills |

Các reports/repro artifacts 08 và 10 đã có dưới dạng untracked trước lượt tổng hợp này, vẫn là supplied evidence. Git diff từ revision cũ nhất được khai báo `0df8a0b` tới HEAD chỉ đổi review files/.gitignore và class chiều rộng của `ComputersTable.tsx`; không đổi implementation được dẫn trong các findings. Điều này giảm rủi ro source drift cho reports có revision, nhưng **không bổ sung provenance còn thiếu của 02/03** và không xác nhận binary đang chạy ngoài workspace.

Hai phụ lục mà R06/R07 dẫn — [06-launcher-ipc-and-process-supervision-probes.md](06-launcher-ipc-and-process-supervision-probes.md) và [07-production-support-durable-workflow-probes.md](07-production-support-durable-workflow-probes.md) — hiện có trong thư mục báo cáo và đã được đối chiếu với scenario mà hai report dẫn. Chúng là mã/hướng dẫn tái hiện lỗi, không phải bằng chứng đã chạy lại ở META-00 hoặc acceptance test sau sửa. `PROJECT_MEMORY.md` không tồn tại ở HEAD META-00; các `.tmp` artifact được report nhắc cũng không phải evidence bền vững. Các artifacts 08/10 có sẵn được đối chiếu, không dùng log production hay secret.

**99 finding gốc → 96 mục canonical:** 86 finding gốc của bản tổng hợp trước cộng 13 finding R09; giữ ba cặp gộp có chung nguyên nhân/contract và không gộp thêm finding R09 nào. Không bỏ một finding gốc nào. Có **81 confirmed defects, 14 design risks, 1 maintainability issue**. Không nâng rủi ro thành confirmed defect chỉ vì thiếu test hoặc thiếu tài liệu. Không có nhóm “likely defect” riêng được tự suy ra; các giả thuyết chưa đủ bằng chứng nằm trong risk/verification hoặc câu hỏi ở phần 6.

| Ưu tiên tổng hợp | Số mục | Ý nghĩa |
| --- | ---: | --- |
| P0 | 0 | Chưa có evidence buộc kết luận sự cố khẩn cấp đang xảy ra hoặc đường tác động nghiêm trọng không cần các preconditions đã nêu |
| P1 | 43 | Cần xử lý/kiểm chứng trước lần rollout liên quan: chạy sai cấu hình/process, mất control plane/recovery, corrupt artifacts hoặc trust boundary quan trọng |
| P2 | 49 | Lỗi/rủi ro có điều kiện hẹp hơn, có đường phục hồi hoặc cần chốt hợp đồng trước; không đồng nghĩa đã xác minh runtime |
| P3 | 4 | Verification chưa có defect tái hiện, caller phụ, hoặc semantics/tài liệu; không tự sinh dự án production |

Ưu tiên tuân theo plan: **correctness → production safety → recoverability → security → state/data consistency → observability → maintainability → performance → elegance**. P trong báo cáo này độc lập với severity của report gốc và với kích thước code. P1 có thể là design risk có điều kiện, điển hình SYN-80; không có P0 không đồng nghĩa hệ thống đã đủ an toàn để rollout.

Điều kiện nâng khẩn cấp phải có evidence vận hành: private key thực sự bị phân phối (SYN-74), binary/config LocalSystem thực sự writable bởi user không được phép sau OTA (SYN-33), credential bị lấy qua HTTP (SYN-73), hoặc restore/deploy đang tác động nhầm DB (SYN-69/70). Khi xác nhận, containment/incident response đi trước backlog; không mặc định các sự cố đó đã xảy ra.

Quyết định trước mắt: sửa các guard nhỏ gây sai trạm/bytes/monitoring; hoàn thiện recovery server trước khi dùng đường deploy đó cho remediation; đồng thời chuẩn bị các fixes lifecycle trạm nhưng chỉ rollout sau LocalSystem canary có rollback. Báo cáo này không yêu cầu triển khai tất cả P1 trong một release.

### 1.1 Gộp root cause và giải quyết các chỗ giao nhau

| Evidence giao nhau | Kết luận canonical / ownership | Vì sao |
| --- | --- | --- |
| CONTENT03-01 + CONTENT03-02 | [SYN-08](#syn-08), owner 03 | Cùng publish trực tiếp vào canonical CAS thiếu verified immutable commit. Giữ riêng subpath MinIO authorization/repair và Local atomic-copy; phải test cả hai mới đóng |
| OTA05-05 + OTA05-06 | [SYN-37](#syn-37), owner 05 | Một shipping contract thiếu chung ở server/Agent: publisher nhận archive Agent reject, hoặc cả hai nhận inventory thiếu Launcher. Không đóng chỉ bằng kiểm “có manifest” |
| L06-04 + L06-07 | [SYN-46](#syn-46), owner 06 | Cùng thiếu actual process identity xuyên attach/status/recovery; path dự kiến không chứng minh ownership hoặc PID wrapper đang supervise |
| AGT01-04 ↔ config trace R04 ↔ job trace R02 | [SYN-04](#syn-04), owner 01; 04 cấp effective artifact, 02 tiêu thụ | Trace source hẹp xác nhận A→whole-file B→render C; authorizer chỉ nhận rendered-from-A/raw-B. Không đếm thành thêm lỗi render/TUS, không nới capability scope |
| R02 “chưa thấy defect” ↔ R06 process findings | Giữ SYN-06/07 là verification; SYN-43/44/46 là confirmed ở 06 | R02 không có test run hợp lệ và không sở hữu lifecycle concurrency sâu. Không dùng kết luận giới hạn của R02 phủ nhận repro của R06 |
| DEP02-01 ↔ executor stages | [SYN-06](#syn-06) giữ rủi ro có điều kiện | `ExecuteInstallJobAsync` resolve lại khi trước Deployed, có registry checks và conflict/gone handling. Không khẳng định mất registry sau restart tự động làm job tiếp tục; không thể đảm bảo dừng tức thì trước khi nhận được signal qua mạng |
| DEP02-02 ↔ R06 catalog observations | [SYN-07](#syn-07), chỉ fault validation | Source hẹp xác nhận catalog temp/replace/backup và ServerCompleted→CatalogCommitted→journal delete. Thiếu metadata generation chưa chứng minh các guards này hỏng |
| ARC00-01 ↔ R07 contract tests | [SYN-60](#syn-60), owner 07, 00 composition | R07 không có production MVC enum-negative matrix phản bác probe R00; API/Agent broad converters vẫn có trong source. Không coi contract tests riêng lẻ là HTTP proof |
| ARC00-02 ↔ R10 health/runtime probes | [SYN-82](#syn-82), owner 10 | R10 chưa idle/cold-boot IIS và runtime probe tắt workers; DB-ready không chứng minh worker lifecycle. Không gộp với schema-ready SYN-71 |
| R10-03 ↔ R10-10/11 | SYN-71/78/79 phối hợp qua artifact head/hash, vẫn ba mục | Readiness thiếu schema check, test gate hard-code head, classifier fail-open là các failure boundaries khác; sửa một không tự đóng hai mục kia |
| AGT01-02 ↔ L06-01/L06-06 | SYN-02/43/48 giữ riêng | Mất remote ACK, concurrent supervisor lifecycle và UI double-click đều có thể restart trùng, nhưng root/caller/recovery khác nhau |
| CFG04-12 ↔ ING-03 ↔ R07-02/07 | SYN-28/64/52/57 giữ riêng | Stale mutation target, stale read filter, thiếu IPC request ID và auth-scope observer isolation không phải một shared implementation bug |
| R07-04/05/06 ↔ OTA05-09 | SYN-54/55/56/40 giữ riêng | Retention clock, closed graph retry, InFlight reclaim, result consume-before-ACK là các durable transitions khác; không giải bằng một “outbox rewrite” chung |
| R09 identity/approval findings ↔ các handoff 01/04/07/10 | [SYN-84–96](#syn-84) giữ riêng | Report 09 xác minh các caller/boundary mới: station ACL create, delegated credential reset, session issuance, browser epoch/cache và generic approval. Chúng phối hợp với finding cũ nhưng không có cùng root cause hoặc close criteria |

Các trace bổ sung chỉ phục vụ đối chiếu disputed handoffs, không là một source review sâu mới. Không chạy thêm product tests trong lượt synthesis; các regression dưới đây là **điều kiện cần cho bản sửa**, không phải test đã pass ở lượt này.

### 1.2 Mapping report 09 vào canonical backlog

| Finding gốc | Canonical | Boundary giữ riêng |
| --- | --- | --- |
| R09-01 | [SYN-84](#syn-84) | Authorization tại target station khi tạo assignment |
| R09-02 | [SYN-85](#syn-85) | Chính sách reset credential theo privilege của target |
| R09-03 | [SYN-86](#syn-86) | Security generation tại login/refresh/password mutation |
| R09-04 | [SYN-87](#syn-87) | Một refresh family khi SingleDeviceLogin bật |
| R09-05 | [SYN-88](#syn-88) | Auth epoch cho response/replay đang bay trong browser |
| R09-06 | [SYN-89](#syn-89) | Cô lập React Query data riêng tư giữa identities |
| R09-07 | [SYN-90](#syn-90) | Selection/generation của approval drawer |
| R09-08 | [SYN-91](#syn-91) | Eligibility trước pagination/count của approval inbox |
| R09-09 | [SYN-92](#syn-92) | Effective action hints có xét target ACL |
| R09-10 | [SYN-93](#syn-93) | Immutable before/after authorization audit snapshot |
| R09-11 | [SYN-94](#syn-94) | Cache TTL tại scheduled grant/membership boundaries |
| R09-12 | [SYN-95](#syn-95) | Scope strategy riêng của từng approval step |
| R09-13 | [SYN-96](#syn-96) | Expected version/step và idempotency của approval action |

## 2. Bảng issue canonical

Ký hiệu category: **D** = confirmed defect; **R** = design risk; **M** = maintainability issue. Cột severity/confidence là thông tin **của report gốc**; các qualifiers về provider/source/probe được giữ trong phần chi tiết. Workflow bắt đầu bằng unit sở hữu theo plan. **S/M/L** trong chi tiết là phạm vi thay đổi tương đối, không phải cam kết thời gian: S thường một vài hàm/component; M một boundary/state/contract có integration tests; L thay đổi phối hợp nhiều services/state. Code nhỏ vẫn có thể cần lab rủi ro cao.

| Canonical ID / vấn đề | P tổng hợp | Severity / confidence gốc | Category | Workflow / owner | Evidence | Tác động thực tế | Disposition |
| --- | --- | --- | --- | --- | --- | --- | --- |
| [SYN-01](#syn-01) — Credential transition có thể hồi sinh Agent đã revoke | P1 | High / High | D | 01 · Enrollment/auth | [AGT01-01][r01] | Revoke mất hiệu lực hoặc máy vừa re-enroll bị 401 kéo dài. | Sửa |
| [SYN-02](#syn-02) — Remote command thực thi lại sau mất ACK | P1 | High / High (source) | D | 01 → 06 · Command/process | [AGT01-02][r01] | Restart/Stop cũ có thể cắt ngang chu kỳ test mới. | Sửa |
| [SYN-03](#syn-03) — Command hết TTL vẫn chặn dedup và quota | P2 | Medium / High | D | 01 · Command queue | [AGT01-03][r01] | Lệnh mới báo thành công nhưng không được giao; đủ 100 row có thể khóa queue. | Sửa nhanh |
| [SYN-04](#syn-04) — Authorizer từ chối config render từ whole-file override | P1 | Medium / High | D | 01 sở hữu auth; 04 → 02 cấp artifact | [AGT01-04][r01] | Tổ hợp file/value override hợp lệ làm cold-cache deployment nhận 410 liên tục. | Sửa |
| [SYN-05](#syn-05) — Redirect có thể mang credential sang origin khác | P2 | Medium / High cơ chế; production chưa rõ | R | 01 · HTTP transport | [AGT01-05][r01] | Lộ Agent header hoặc provisioning body nếu trusted origin phát cross-origin redirect. | Kiểm chứng + hardening |
| [SYN-06](#syn-06) — Cửa sổ cancellation khi mất mạng chưa được đo | P2 | Medium / Medium | R | 02; phụ thuộc 01 | [DEP02-01][r02] | Có thể còn filesystem/process side effect sau khi server hiển thị Cancelled. | Chỉ xác minh trước khi thiết kế |
| [SYN-07](#syn-07) — Catalog mất/stale cần crash matrix trước khi thêm schema | P3 | Medium / Medium | R | 02; 06 tiêu thụ catalog | [DEP02-02][r02] | Rủi ro metadata Launcher không khớp payload; chưa có defect tái hiện. | Chỉ xác minh |
| [SYN-08](#syn-08) — Publish vào CAS thiếu commit bảo toàn bytes đã xác minh | P1 | High / High; MinIO replay chưa E2E | D | 03 · Local/MinIO uploads | [CONTENT03-01][r03] + [CONTENT03-02][r03] | Upload sau làm hỏng blob đang dùng chung; Agent reject hash, browser/archive có thể trả bytes sai. | Sửa theo provider |
| [SYN-09](#syn-09) — DELETE file vượt release generation boundary | P1 | High / High (PostgreSQL) | D | 03 · Release | [CONTENT03-03][r03] | Cùng VersionId phát hành có manifest khác nhau, thiếu dependency. | Sửa nhanh |
| [SYN-10](#syn-10) — GC vướng FK lịch sử sau khi đã xóa object | P1 | High / High (PostgreSQL) | D | 03 · Blob GC/recovery | [CONTENT03-04][r03] | SHA bị kẹt tombstone, không reupload được; lỗi một candidate làm dừng sweep. | Quyết định retention rồi sửa |
| [SYN-11](#syn-11) — Hash cache tái dùng nội dung khác cùng metadata | P1 | High / High (frontend repro) | D | 03 · Browser → release | [CONTENT03-05][r03] | Version hợp lệ chứa bytes cũ khác file người dùng chọn; hash server/Agent vẫn khớp. | Sửa nhanh |
| [SYN-12](#syn-12) — Upload-complete tự conflict khi prune metadata | P2 | Medium / High (PostgreSQL) | D | 03 · Draft revision | [CONTENT03-06][r03] | Rename/bỏ entry, icon, watch khiến finalize rollback dù không có request cạnh tranh. | Sửa nhanh |
| [SYN-13](#syn-13) — Ingest/release bỏ sót file là ancestor của file khác | P2 | Medium / High (PostgreSQL) | D | 03 → 02 · File tree | [CONTENT03-07][r03] | Released tree không materialize được trên Windows, deployment bị chặn. | Sửa nhanh |
| [SYN-14](#syn-14) — Mất final TUS PATCH response không tự reconcile | P2 | Medium / High | D | 03 · TUS HTTP/browser | [CONTENT03-08][r03] | Upload báo lỗi dù reservation Uploaded; cần thao tác lại, chưa phải mất bytes vĩnh viễn. | Sửa nhanh |
| [SYN-15](#syn-15) — Human download-url Local còn dùng Agent route cũ | P3 | Low / High (source) | D | 03; phối hợp 01/09 | [CONTENT03-09][r03] | Caller hai API URL không tải được; browser hiện dùng stream nên phạm vi hẹp. | Sửa hoặc deprecate có kiểm caller |
| [SYN-16](#syn-16) — Released path policies có mutation chưa được định nghĩa rõ | P2 | Medium / High code, Medium contract | R | 03; 04/02/09 | [CONTENT03-10][r03] | Cùng VersionId có Overridable/PreserveLocal policy khác giữa hai lần resolve. | Quyết định sản phẩm |
| [SYN-17](#syn-17) — Baseline effective khác pipeline cấu hình thực tế | P1 | High / High | D | 04 · Baseline → pin → manifest | [CFG04-01][r04] | Pin pass nhưng bytes mirror/whole-file gửi đến trạm vi phạm baseline. | Sửa có cấu trúc |
| [SYN-18](#syn-18) — Render fallback/partial được xuất bản như success | P1 | High / High | D | 04 → 02 · Config delivery | [CFG04-02][r04] | Trạm nhận tổ hợp giá trị chưa áp đủ nhưng deployment có thể Healthy. | Sửa có cấu trúc |
| [SYN-19](#syn-19) — INI mirror nhận newline tạo key ngoài declaration | P1 | High / High | D | 04 · Value/approval → format writer | [CFG04-03][r04] | Value có thể thêm setting không được khai báo hoặc làm hỏng INI. | Sửa |
| [SYN-20](#syn-20) — Một snapshot primary không đủ cho nhiều mirror | P1 | High / High | D | 04 · Targets/stale/render | [CFG04-04][r04] | Primary/mirror phân kỳ; một mirror làm Stale cả override, lần resolve sau đổi toàn output. | Quyết định invariant rồi sửa |
| [SYN-21](#syn-21) — Render cũ đánh Stale revision override vừa cập nhật | P1 | High / High | D | 04 · Concurrent render/edit | [CFG04-05][r04] | Value mới hợp lệ bị loại khỏi runtime, có thể ảnh hưởng cả Station. | Sửa |
| [SYN-22](#syn-22) — Migrate legacy whole-file có thể kích hoạt payload chưa duyệt | P1 | High / High | D | 04 target; 09 approval handoff | [CFG04-06][r04] | Draft/Pending thành Active values và approval cũ mồ côi. | Sửa; policy gate 09 |
| [SYN-23](#syn-23) — Migration không giữ effective config trước/sau | P1 | High / High | D | 04 · Legacy conversion | [CFG04-07][r04] | Key đã bỏ quay lại hoặc Computer value mất hiệu lực về Station. | Sửa |
| [SYN-24](#syn-24) — Migration values/retire/reference không atomic | P2 | Medium / High; PostgreSQL chưa chạy | D | 04 · Migration transaction | [CFG04-08][r04] | API lỗi nhưng Active values và file cũ cùng tồn tại, poll có thể ghi stale side effects. | Sửa cùng SYN-22/23 |
| [SYN-25](#syn-25) — Apply all programs commit một phần rồi báo lỗi | P1 | Medium / High | D | 04 · Multi-program config | [CFG04-09][r04] | Cùng PC có cấu hình các chương trình không đồng bộ dù người dùng nhận fail. | Chốt atomicity rồi sửa |
| [SYN-26](#syn-26) — Config mutation thiếu redeploy invalidation | P2 | Medium / High | D | 04 → 02 · Same-version delivery | [CFG04-10][r04] | Máy đã cài Healthy giữ config cũ trong khi máy mới nhận config mới. | Sửa |
| [SYN-27](#syn-27) — Approval activate value không còn hợp lệ với declaration | P2 | Medium / High | D | 04 target validation; 09 engine | [CFG04-11][r04] | Approved/Active có thể không render được hoặc trái type/range mới. | Sửa target; giữ approval boundary SYN-95/96 |
| [SYN-28](#syn-28) — Config modal Save nhắm PC cũ dưới tiêu đề PC mới | P1 | High / High (React repro) | D | 04 · Browser mutation target | [CFG04-12][r04] | Operator đổi cấu hình nhầm trạm với quyền hợp lệ. | Sửa nhanh |
| [SYN-29](#syn-29) — Read DTO che Pending replacement | P2 | Medium / High | D | 04 · API/UI approval state | [CFG04-13][r04] | Proposal biến khỏi màn config, pending count=0 gây nhập lại/cancel nhầm. | Sửa |
| [SYN-30](#syn-30) — Computed stale, stored stale và cache lệch nhau | P2 | Medium / High | D | 04 · Preview/reconfirm/render | [CFG04-14][r04] | UI cho Reconfirm nhưng API reject; cache che stale status/count. | Sửa |
| [SYN-31](#syn-31) — Non-finite Decimal vượt range validation | P2 | Medium / High (.NET repro) | D | 04 · Validation → manifest | [CFG04-15][r04] | NaN lưu hợp lệ nhưng JSON render ném lỗi, chặn job đến khi sửa value. | Sửa nhanh |
| [SYN-32](#syn-32) — Acknowledgement và edit không bind revision đã xem | P2 | Medium / High code, Medium impact | R | 04 pin/config; 09 decision semantics | [CFG04-16][r04] | Count bằng nhau nhưng tập stale khác vẫn pin; delayed retry có thể ghi đè intent mới. | Quyết định contract |
| [SYN-33](#syn-33) — OTA exact replacement làm mất ACL hardened | P1 | High / High | D | 05 · Station update/rollback | [OTA05-01][r05] | Config nhận Users read; nếu parent cho Modify, binaries service cũng có thể bị ghi. | Sửa trước OTA rộng |
| [SYN-34](#syn-34) — OTA cleanup lỗi phá active và dùng old snapshot đã xóa dở | P1 | High / High (Windows fixture) | D | 05 · Exact switch/recovery | [OTA05-02][r05] | Mất install hoặc thiếu exe dù intact backup còn trên đĩa; cần can thiệp tại PC. | Sửa trước OTA rộng |
| [SYN-35](#syn-35) — Crash trước OTA readiness mất owner recovery và backup tốt | P1 | High / High (subprocess + helpers) | D | 05 · Bootstrap transaction | [OTA05-03][r05] | Agent mới lỗi có thể mắc vĩnh viễn; retry overwrite backup OLD bằng NEW chưa commit. | Sửa trước OTA rộng |
| [SYN-36](#syn-36) — Apply latch tồn tại sau launch failure/helper exit | P1 | High / High latch, Medium race | D | 05 · OTA single-flight | [OTA05-04][r05] | Release kế tiếp chỉ stage, không apply cho đến restart Agent. | Sửa |
| [SYN-37](#syn-37) — Server và Agent thiếu chung contract cho bundle OTA hoàn chỉnh | P1 | Medium + High / High | D | 05 · Publish → stage → readiness | [OTA05-05][r05] + [OTA05-06][r05] | Release sai version được activate rồi reject; signed exe-only bundle có thể xóa Launcher nhưng Agent vẫn Online. | Sửa parity + minimum bundle |
| [SYN-38](#syn-38) — Packager đưa stale Launcher DLL vào manifest mới | P2 | Medium / High | D | 05 · Build/package | [OTA05-07][r05] | Ship dependency thừa/lỗi thời; source không chứng minh bundle production đang có file đó. | Sửa nhanh |
| [SYN-39](#syn-39) — OTA download body treo giữ heartbeat | P1 | Medium / High | D | 05 → 01 · Liveness/commands | [OTA05-08][r05] | Heartbeat/commands dừng vô hạn khi body stall; poll/inventory khác có thể còn chạy. | Sửa |
| [SYN-40](#syn-40) — Update result bị consume trước server ACK | P2 | Medium / High | D | 05 · Outcome/adoption | [OTA05-09][r05] | Applied/rolled_back biến mất sau heartbeat lỗi; server không biết máy đã thử và thất bại. | Sửa nhanh |
| [SYN-41](#syn-41) — OTA staging chưa có retention theo reference | P2 | Medium / High code, Medium capacity | R | 05 · Station disk/recovery | [OTA05-10][r05] | Nhiều releases/crashes tích lũy bytes, có thể hết đĩa cùng volume state/outbox. | Đo dung lượng rồi harden |
| [SYN-42](#syn-42) — OTA key rotation/revocation chưa có protocol/runbook | P2 | Medium / High | R | 05; 10 phân phối trust | [OTA05-11][r05] | Mất/thu hồi key có thể làm kênh OTA không dùng được nếu chưa có recovery ngoài OTA. | Quyết định + runbook |
| [SYN-43](#syn-43) — ProcessSupervisor thiếu serialization toàn lifecycle | P1 | P1–High / High (repro) | D | 06; 01/02 cùng caller | [L06-01][r06] | Run A/B có thể cùng chạy B; stop/callback cũ tác động generation mới. | Sửa nền lifecycle |
| [SYN-44](#syn-44) — Hủy request trong attach để lại process không được quản lý | P1 | P1–High / High (repro) | D | 06 · IPC deadline → OS process | [L06-02][r06] | Wrapper sống nhưng thiếu watcher/durable PID; Agent restart có thể chạy thêm. | Sửa cùng lifecycle |
| [SYN-45](#syn-45) — HTTP health timeout kết thúc toàn bộ probe loop | P1 | P1–High / High (repro) | D | 06 · Process health | [L06-03][r06] | Ứng dụng treo vẫn Running; threshold/auto-restart ngừng hoạt động. | Sửa nhanh có station test |
| [SYN-46](#syn-46) — Supervised process identity không gắn với actual launch | P1 | P1–High + P2–Medium / High | D | 06 · Attach/status/recovery | [L06-04][r06] + [L06-07][r06] | Attach/Stop nhầm process có trước; fallback wrapper bị coi chết và có thể bị launch trùng. | Sửa identity chung |
| [SYN-47](#syn-47) — Launcher session cũ ngăn UI của console mới | P2 | P2–Medium / High (source) | D | 06 · Interactive session bootstrap | [L06-05][r06] | User B không có Launcher khi process user A còn ở session cũ. | Sửa |
| [SYN-48](#syn-48) — Launcher queue nhiều Run cho một thao tác lặp | P2 | P2–Medium / High (source) | D | 06 · WPF → IPC intent | [L06-06][r06] | Double-click khi chờ response có thể restart app vừa mở. | Sửa nhanh |
| [SYN-49](#syn-49) — Icon lớn làm mất core status và mutation result | P2 | P2–Medium / High (source) | D | 06 · Status/IPC/UI | [L06-08][r06] | Optional image vượt 4 MiB envelope, UI giữ card cũ và có thể retry action đã chạy. | Sửa |
| [SYN-50](#syn-50) — IPC Stop/Restart bỏ outcome false và báo success | P2 | P2–Medium / High (source) | D | 06 · Operator outcome | [L06-09][r06] | Operator tưởng process đã dừng dù kill lỗi và PID còn sống. | Sửa nhanh |
| [SYN-51](#syn-51) — Support storage lỗi có thể dừng core Agent | P1 | P1 / High | D | 07 → Agent host/06 status | [R07-01][r07] | Outbox schema/corruption riêng support làm heartbeat/poll/IPC không hoạt động. | Sửa trước rollout support/OTA |
| [SYN-52](#syn-52) — Support update từ dialog cũ nhắm request mới | P1 | P1 / High service; WPF chưa runtime | D | 07 · Launcher IPC → outbox | [R07-02][r07] | Có thể resolve incident B bằng nội dung người dùng chuẩn bị cho A. | Sửa identity contract |
| [SYN-53](#syn-53) — Correction bỏ sót failed Resolved và failed predecessor | P2 | P1 / High (SQLite probes) | D | 07 · Operator correction | [R07-03][r07] | Sửa employee không giải phóng queue; tạo thêm successors vẫn mắc dependency. | Sửa |
| [SYN-54](#syn-54) — Reconcile reset ClosedAt làm retention không bao giờ hết | P2 | P1 / High (SQLite + clock) | D | 07 · Outbox retention | [R07-04][r07] | Lịch sử chạm 1000 operations/20MiB và chặn report/update mới. | Sửa nhanh |
| [SYN-55](#syn-55) — Update của request đã đóng retry không hội tụ | P2 | P2 / High | D | 07 · Server closure → outbox | [R07-05][r07] | Queue/badge tồn đọng, retries và capacity tăng dù không còn current để sửa. | Sửa |
| [SYN-56](#syn-56) — InFlight kẹt nếu delivery và ghi retry cùng lỗi | P2 | P2 / High (SQLite fault) | D | 07 · Durable claim recovery | [R07-06][r07] | Operation/descendants đứng đến restart dù storage đã phục hồi. | Sửa |
| [SYN-57](#syn-57) — Browser giữ dữ liệu support của scope cũ sau purge | P2 | P2 / High (React observer probes) | D | 07 UI; 09 auth/scope contract | [R07-07][r07] | Dữ liệu team cũ còn hiển thị tại boundary đổi quyền; không chứng minh server bypass. | Sửa; phối hợp 09 |
| [SYN-58](#syn-58) — Support list load lỗi được hiển thị như empty success | P2 | P2 / High (React repro) | D | 07 · Web triage | [R07-08][r07] | Operator tưởng không có yêu cầu khi API 503/timeout. | Sửa nhanh |
| [SYN-59](#syn-59) — Sync status phản ánh từng thành công thay vì freshness hiện tại | P2 | P2 / High | D | 07 · Reconcile → heartbeat/Launcher | [R07-09][r07] | Queue rỗng báo Synced dù support-only API lỗi và current snapshot cũ. | Sửa nhanh |
| [SYN-60](#syn-60) — Global JSON converter vô hiệu strict support enum contract | P2 | Medium / High | D | 07 sở hữu contract; 00 composition | [ARC00-01][r00] | API/Agent chấp nhận wire values trái fixtures, dễ lệch compatibility; chưa chứng minh bỏ state/permission checks. | Sửa contract có HTTP test |
| [SYN-61](#syn-61) — Support docs còn ghi current Stage-0/API v1 | P3 | Low / High | M | 07 · Compatibility documentation | [ARC00-04][r00] | Người đọc đánh giá sai capabilities/rollout; chưa có bằng chứng deployment sai. | Cập nhật tài liệu |
| [SYN-62](#syn-62) — Test Monitor không tự lấy dữ liệu mới | P1 | High/P1 / High | D | 08 · Production monitoring | [ING-01][r08] | Màn mở cả ca có thể không thấy FAIL mới hoặc chuyển ca cho đến tương tác/reload. | Sửa nhanh |
| [SYN-63](#syn-63) — Late logs hết overlap trước khi replay có capacity | P1 | High/P1 / High (filesystem probe) | D | 08 · Agent scan/cursor | [ING-02][r08] | Có thể mất FAIL khỏi monitoring khi fresh batches liên tục đầy. | Sửa |
| [SYN-64](#syn-64) — Response filter cũ ghi đè Test Monitor mới | P2 | Medium/P2 / High (React repro) | D | 08 · Browser query identity | [ING-03][r08] | Golden/Sfis hoặc station filter một đằng nhưng panels hiển thị dữ liệu khác/trộn. | Sửa nhanh cùng polling |
| [SYN-65](#syn-65) — Basename dedup gộp các source chưa có business identity contract | P2 | Medium/P2 / High code, Medium semantics | R | 08; 07 Sfis linkage | [ING-04][r08] | NoSfc đến trước có thể làm Sfis mất row và support link null. | Quyết định identity trước schema |
| [SYN-66](#syn-66) — Metrics snapshot cũ rollback counters mới | P2 | Medium/P2 / High (service probe) | D | 08 · Registry snapshot ingestion | [ING-05][r08] | Computer detail số đếm lùi hoặc lấy lại value trước reset; không đổi Test Monitor log totals. | Sửa |
| [SYN-67](#syn-67) — TestedAt dùng mtime nhưng chưa rõ event-time contract | P2 | Medium/P2 / High mapping, Medium semantics | R | 08 · Shift/time provenance | [ING-06][r08] | Copy/append mtime có thể đưa test vào sai ca hoặc đổi streak ordering. | Quyết định nguồn thời gian |
| [SYN-68](#syn-68) — Serial search không có trang sau 20 records | P2 | Medium/P2 / High (source) | D | 08 · Test history UI/API | [ING-07][r08] | Retest/failure cũ ngoài trang1 không truy cập được dù backend có pagination. | Sửa nhanh |
| [SYN-69](#syn-69) — Server rollback lỗi vẫn mở pool và gỡ offline | P1 | P1 / High (function probe) | D | 10 · Server recovery | [R10-01][r10] | Requests/workers chạy trên code hoặc database restore dở. | Sửa trước deploy |
| [SYN-70](#syn-70) — Deploy target DB có thể khác effective API DB | P1 | P1 / High | D | 10 · Backup/schema/restore identity | [R10-02][r10] | Dump/migrate/drop-restore nhầm DB; backup không cứu được API thật. | Sửa trước deploy |
| [SYN-71](#syn-71) — Deployment thiếu schema vẫn báo healthy | P1 | P1 / High (API Release + PostgreSQL) | D | 10 · Artifact/schema/readiness | [R10-03][r10] | Script DONE nhưng feature mới lỗi500 vì thiếu cột/migration. | Sửa trước deploy |
| [SYN-72](#syn-72) — Code update tự thay TLS certificate theo expiry | P1 | P1 / High source/probe; trust chưa live | D | 10 → 01 · TLS fleet | [R10-04][r10] | Server local health pass nhưng fleet chưa trust certificate mới mất kết nối. | Sửa trước deploy |
| [SYN-73](#syn-73) — Console mặc định đăng nhập qua HTTP | P1 | P1 / High (source trace) | D | 10 transport; 09 human auth | [R10-05][r10] | Login/Bearer/refresh có thể đi cleartext trên LAN nếu dùng URL installer quảng bá. | Sửa transport; giữ auth boundary SYN-88/89 |
| [SYN-74](#syn-74) — Public-key input phân phối được private key | P1 | P1 / High input flow; probe chỉ labels | D | 10 build input; 05 station signing trust | [R10-06][r10] | Nếu chọn nhầm PEM, signing private material có thể đi vào server/Agent distributables. | Sửa nhanh trước build phát hành |
| [SYN-75](#syn-75) — DB password không round-trip connection-string escaping | P2 | P2 / High (parser probe) | D | 10 · Config generation | [R10-07][r10] | psql dùng đúng password nhưng API DB connection sai với semicolon/quotes. | Sửa nhanh |
| [SYN-76](#syn-76) — Rollback giữ config/key của deployment thất bại | P2 | P2 / High (function probe) | D | 10; 05 key configuration | [R10-08][r10] | Old code/DB quay lại nhưng signing key/config vẫn từ artifact lỗi. | Sửa |
| [SYN-77](#syn-77) — KeepBackups=0 xóa cả recovery pair đang dùng | P1 | P2 / High (filesystem probe) | D | 10 · Backup retention/recovery | [R10-09][r10] | Health fail sau migration không còn archive/dump để rollback. | Sửa nhanh trước deploy |
| [SYN-78](#syn-78) — Release gates hard-code migration head cũ | P2 | P2 / High | D | 10 · Release validation/provenance | [R10-10][r10] | Artifact mới hợp lệ bị reject hoặc report head sai, gate không còn đáng tin. | Sửa nhanh |
| [SYN-79](#syn-79) — Migration classifier fail-open với syntax/SQL không hiểu | P2 | P2 / High mechanism; impact chưa xảy ra | R | 10 · Schema safety classification | [R10-11][r10] | Migration mới destructive có thể bị phân loại additive và bỏ preparation gate. | Harden classifier |
| [SYN-80](#syn-80) — Maintenance/backup boundary chưa bao phủ mọi writer và storage | P1 | P1 / High scope; topology chưa kiểm | R | 10 xuyên 01–08 · Recovery admission | [R10-12][r10] | DB rewind có thể mất writes đã ACK hoặc lệch object/git/Agent side effects. | Chốt topology/RPO rồi drill |
| [SYN-81](#syn-81) — Local deploy health bỏ TLS validation | P2 | P2 / High (source) | D | 10 · IIS staging verification | [R10-13][r10] | Staging báo pass certificate sai trong khi Agent strict client sẽ fail. | Sửa nhanh |
| [SYN-82](#syn-82) — IIS workers phụ thuộc traffic/inherited pool lifecycle | P2 | Medium / High config gap; live settings chưa rõ | R | 10 sở hữu host; 00 composition | [ARC00-02][r00] | Watchdog/GC/recovery sweeps có thể chưa chạy sau reboot hoặc ngừng khi idle. | Xác minh effective IIS rồi cấu hình |
| [SYN-83](#syn-83) — DomainEventDispatcher logging-only chưa có delivery semantics | P3 | Medium / High | R | 00 architecture · Domain events | [ARC00-03][r00] | Consumer tương lai có thể hiểu nhầm ProcessedAt; chưa chứng minh workflow hiện tại mất side effect. | Quyết định/document trước; không build outbox mặc định |
| [SYN-84](#syn-84) — Assignment creation bỏ qua ACL Deny tại station đích | P1 | P1 / High (HTTP + persisted state) | D | 09 · Assignment authority | [R09-01][r09] | Tạo deployment intent Active tại station actor bị cấm. | Sửa tại application mutation boundary |
| [SYN-85](#syn-85) — Delegated user manager reset được tài khoản đặc quyền | P1 | P1 / High (HTTP + login) | D | 09 · Credential administration | [R09-02][r09] | `user.manage` có thể trở thành đường chiếm quyền của human account cao hơn. | Thêm target-privilege policy |
| [SYN-86](#syn-86) — Login cũ phát refresh sau security mutation | P1 | P1 / High; chưa PostgreSQL barrier | D | 09 · Authentication issuance | [R09-03][r09] | Lần xác thực bằng mật khẩu cũ tiếp tục mint JWT hợp lệ sau đổi/reset mật khẩu. | DB-enforced security generation |
| [SYN-87](#syn-87) — SingleDeviceLogin vẫn để hai refresh families hợp lệ | P2 | P2 / High (deterministic two-context) | D | 09 · Session replacement | [R09-04][r09] | Hai session tiếp tục refresh dù cấu hình quảng bá single-device. | Sửa cùng security generation |
| [SYN-88](#syn-88) — Browser nhận response auth cũ sau khi đổi phiên | P1 | P1 / High (deferred frontend probes) | D | 09 · Browser auth lifecycle | [R09-05][r09] | Phiên A có thể ghi token/profile hoặc replay request sau khi user B đăng nhập. | Một auth epoch/coordinator |
| [SYN-89](#syn-89) — Cache riêng tư không cô lập giữa users cùng tab | P1 | P1 / High (real query hook/cache) | D | 09 · Browser private data | [R09-06][r09] | User B có thể thấy repository/private query data đã cache của user A. | Identity-scope protected queries |
| [SYN-90](#syn-90) — Approval drawer có thể thao tác request từ response cũ | P2 | P2 / High (component action probe) | D | 09 · Approval browser intent | [R09-07][r09] | Approve/reject/cancel có thể gửi ID khác selection hiện tại. | Sửa nhanh bằng generation guard |
| [SYN-91](#syn-91) — Approval inbox cắt candidates trước eligibility | P2 | P2 / High (501-request probe) | D | 09 · Approval query | [R09-08][r09] | Request hợp lệ có thể biến mất khỏi mọi trang và total/badge. | Eligibility trước paginate/count |
| [SYN-92](#syn-92) — Mutation UI dùng navigation permissions, bỏ qua ACL | P2 | P2 / High (backend probe + consumers) | D | 09 · Effective action hints | [R09-09][r09] | Scoped Allow không mở action hợp lệ; Deny có thể chỉ bị phát hiện sau submit. | Target-aware capability hints |
| [SYN-93](#syn-93) — Authorization revoke audit làm hỏng before snapshot | P2 | P2 / High (service/audit probe) | D | 09 · Authorization audit | [R09-10][r09] | Before và after đều `IsDeleted=true`, làm mất bằng chứng thay đổi. | Sửa nhanh bằng immutable projection |
| [SYN-94](#syn-94) — Scheduled ACL boundary không hết hạn decision cache đúng lúc | P2 | P2 / High (cached/uncached probe) | D | 09 · Authorization cache | [R09-11][r09] | Actor có thể tiếp tục mutation sau lúc Deny bắt đầu có hiệu lực. | Tính next transition bằng một clock |
| [SYN-95](#syn-95) — Approval multi-step dùng scope của bước đầu cho mọi bước | P2 | P2 / High; deployment usage chưa rõ | D | 09 · Approval policy snapshot | [R09-12][r09] | Approver selection lệch policy hoặc workflow kẹt ở bước sau. | Chốt support rồi snapshot/reject rõ |
| [SYN-96](#syn-96) — Approval action không bind version/step đã xem | P2 | P2 / High replay; impact phụ thuộc multi-step | R | 09 · Approval action protocol | [R09-13][r09] | Retry của bước trước có thể duyệt luôn bước tiếp theo. | Expected version/step + idempotency |

## 3. Chi tiết từng issue canonical

<a id="syn-01"></a>

### SYN-01 — Credential transition có thể hồi sinh Agent đã revoke

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [AGT01-01][r01]; severity/confidence gốc: High / High.

- **Root cause:** Agent không có CAS/version chung; Computer.Version không bao phủ promotion/revoke.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:334](../../../MProjectBackend/MProject.Application/Services/Assets/AgentService.cs#L334) — EnrollAsync, AuthenticateAsync, PromotePendingToken, RevokeAsync; [MProjectBackend/MProject.Domain/Entities/Assets/Agent.cs:6](../../../MProjectBackend/MProject.Domain/Entities/Assets/Agent.cs#L6) — Agent; [MProjectBackend/MProject.Infrastructure/DBContext.cs:249](../../../MProjectBackend/MProject.Infrastructure/DBContext.cs#L249) — Agent mapping / concurrency.
- **Bằng chứng và scenario:** Hai barrier SQLite: revoke thắng trước enroll-save nhưng Agent trở lại Active; promotion cũ ghi sau re-enroll làm replacement token mất hiệu lực. **Impact:** Revoke mất hiệu lực hoặc máy vừa re-enroll bị 401 kéo dài.
- **Hướng sửa:** Version/CAS hoặc row lock cho toàn bộ credential transitions; conflict phải reload và xác minh lại, giữ shared provisioning.
- **Rủi ro thay đổi / rollback:** Có migration; triển khai server trước, giữ old/new schema tương thích. Không phục hồi credential đã revoke khi rollback; canary enroll/rotate/revoke trước mở rộng.
- **Regression bắt buộc:** PostgreSQL hai DbContext: revoke/enroll, re-enroll/promotion, hai promotion, ticket replacement; assert authentication sau commit, không chỉ response đang bay.

<a id="syn-02"></a>

### SYN-02 — Remote command thực thi lại sau mất ACK

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [AGT01-02][r01]; severity/confidence gốc: High / High (source).

- **Root cause:** Retry delivery gọi lại side effect; Agent thiếu durable receipt theo CommandId.
- **Files/symbols:** [MProjectAgent/Services/AgentCommandHandler.cs:39](../../../MProjectAgent/Services/AgentCommandHandler.cs#L39) — DispatchAsync, HandleAsync; [MProjectBackend/MProject.Application/Services/Assets/AgentCommandService.cs:72](../../../MProjectBackend/MProject.Application/Services/Assets/AgentCommandService.cs#L72) — ClaimPendingForDeliveryAsync; [MProjectAgent/Services/ProcessSupervisor.cs:452](../../../MProjectAgent/Services/ProcessSupervisor.cs#L452) — RequestRestartAsync.
- **Bằng chứng và scenario:** KillAndRestart thành công, ACK mất trước commit hoặc Agent chết trước ACK; heartbeat giao lại cùng ID. Chưa chạy kill/restart thật trong R01. **Impact:** Restart/Stop cũ có thể cắt ngang chu kỳ test mới.
- **Hướng sửa:** Lưu receipt/result có giới hạn và ACK lại; bind target process/deployment generation. Quy định crash giữa side effect và receipt, không hứa exactly-once chỉ từ một cờ.
- **Rủi ro thay đổi / rollback:** Phối hợp SYN-43/46; reader cũ phải hiểu hoặc bỏ qua receipt an toàn. Trước downgrade drain lệnh và lưu journal; thử khôi phục Agent cũ với payload đang chạy.
- **Regression bắt buộc:** LocalSystem + app fixture: mất ACK trước/sau server commit, restart Agent giữa action/receipt/ACK; không tác động generation mới, CancelJob vẫn hoạt động.

<a id="syn-03"></a>

### SYN-03 — Command hết TTL vẫn chặn dedup và quota

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [AGT01-03][r01]; severity/confidence gốc: Medium / High.

- **Root cause:** Enqueue chỉ xét chưa ACK, delivery còn xét TTL 24 giờ.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentCommandService.cs:38](../../../MProjectBackend/MProject.Application/Services/Assets/AgentCommandService.cs#L38) — EnqueueAsync, ClaimPendingForDeliveryAsync.
- **Bằng chứng và scenario:** Probe tạo Restart 25 giờ tuổi: enqueue trả ID cũ, claim trả zero. Máy offline cuối tuần là điều kiện thực tế. **Impact:** Lệnh mới báo thành công nhưng không được giao; đủ 100 row có thể khóa queue.
- **Hướng sửa:** Một predicate active/expiry cho dedup, quota, delivery hoặc terminalize expiry; giữ lịch sử và ID mới cho intent mới.
- **Rủi ro thay đổi / rollback:** Thay predicate nhỏ; nếu thêm status phải tương thích reader cũ. Không xóa lịch sử hoặc ACK hộ lệnh chưa chạy.
- **Regression bắt buộc:** TTL đúng biên, 100 row hết hạn, dedup trong TTL, ACK cũ không kết thúc ID mới; API/DB queue fixture.

<a id="syn-04"></a>

### SYN-04 — Authorizer từ chối config render từ whole-file override

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [AGT01-04][r01]; severity/confidence gốc: Medium / High.

- **Root cause:** Issuer phát output C từ override B nhưng authorization graph chỉ nối rendered base với SoftwareFile A.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentBlobDownloadAuthorizer.cs:68](../../../MProjectBackend/MProject.Application/Services/Assets/AgentBlobDownloadAuthorizer.cs#L68) — IsAuthorizedAsync; [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:752](../../../MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L752) — ResolveOverridesAsync, BuildManifestJobsAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:252](../../../MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs#L252) — PersistAsync.
- **Bằng chứng và scenario:** Fixture HMAC hợp lệ A→B→C cho A/B tải được, C bị từ chối. Trace R04 xác nhận precedence này là hợp lệ; chưa có full provider E2E. **Impact:** Tổ hợp file/value override hợp lệ làm cold-cache deployment nhận 410 liên tục.
- **Hướng sửa:** Authorize đúng effective artifacts của job/revision hoặc mở graph có scope/path/generation; không cho mọi rendered blob theo package/hash.
- **Rủi ro thay đổi / rollback:** Thay trust boundary, cần negative matrix trước release; rollback validator giữ fail-closed, có thể tạm ngừng tổ hợp override bị ảnh hưởng.
- **Regression bắt buộc:** Service thật resolve rồi dùng chính capability tải qua HTTP Local/MinIO; wrong Agent/scope, cancelled/completed job, expiry/tamper phải deny.

<a id="syn-05"></a>

### SYN-05 — Redirect có thể mang credential sang origin khác

**P2 · design risk · Kiểm chứng + hardening · Scope S.** Evidence: [AGT01-05][r01]; severity/confidence gốc: Medium / High cơ chế; production chưa rõ.

- **Root cause:** Origin validation chỉ kiểm URL đầu, handler tự follow redirect với custom credential header.
- **Files/symbols:** [MProjectAgent/Program.cs:113](../../../MProjectAgent/Program.cs#L113) — HTTP client server; [MProjectAgent/Services/ServerClient.cs:143](../../../MProjectAgent/Services/ServerClient.cs#L143) — PostCoreAsync, AttachCredential; [MProjectAgent/Commands/EnrollCommand.cs:178](../../../MProjectAgent/Commands/EnrollCommand.cs#L178) — RunAsync.
- **Bằng chứng và scenario:** Hai loopback origins tái hiện header đi theo 307. Chưa có bằng chứng IIS/proxy hiện tại phát redirect này; không phải TLS trust bypass. **Impact:** Lộ Agent header hoặc provisioning body nếu trusted origin phát cross-origin redirect.
- **Hướng sửa:** Tắt auto-redirect hoặc kiểm origin trước từng hop; chốt same-origin behavior cho worker và CLI.
- **Rủi ro thay đổi / rollback:** Kiểm rule canonical/migration trước đổi client để tránh mất kết nối; canary enroll/heartbeat và phục hồi binary/config với origin cũ tin cậy.
- **Regression bắt buộc:** Hai HTTPS origins có trust fixture: 307/308 không gửi header/body ra ngoài; same-origin theo contract, giữ invalid-cert matrix.

<a id="syn-06"></a>

### SYN-06 — Cửa sổ cancellation khi mất mạng chưa được đo

**P2 · design risk · Chỉ xác minh trước khi thiết kế · Scope M cho lab; code TBD.** Evidence: [DEP02-01][r02]; severity/confidence gốc: Medium / Medium.

- **Root cause:** Signal remote bị trễ; registry local trong memory. Không có chứng cứ mọi restart đều tiếp tục deploy.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs](../../../MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs) — CancelActiveJobsForPackageAsync; [MProjectAgent/Services/JobCancellationRegistry.cs](../../../MProjectAgent/Services/JobCancellationRegistry.cs) — CancelJob, IsCancelled; [MProjectAgent/Services/JobExecutor.cs:92](../../../MProjectAgent/Services/JobExecutor.cs#L92) — ExecuteInstallJobAsync, ResumePendingAsync.
- **Bằng chứng và scenario:** R02 chưa chạy test do DLL bị khóa. Đối chiếu hẹp: stage trước Deployed resolve lại server; Complete nhận conflict/gone có retire journal. Không thể suy từ registry in-memory thành mất cancellation vô điều kiện. **Impact:** Có thể còn filesystem/process side effect sau khi server hiển thị Cancelled.
- **Hướng sửa:** Đo boundary đã resolve/download rồi offline; chốt thời hạn cancellation và offline policy. Chỉ thêm marker/lease nếu fixture chứng minh phần thiếu.
- **Rủi ro thay đổi / rollback:** Không hứa dừng tức thì khi không liên lạc được server. Giữ execution journal/AutoRemove policy; thử reconnect và phục hồi payload trên trạm lab.
- **Regression bắt buộc:** LocalSystem: barrier sau resolve/download, cancel server, ngắt mạng, restart ở từng stage, reconnect; đo side effect/stop, không yêu cầu invariant bất khả thi khi Agent chưa biết cancel.

<a id="syn-07"></a>

### SYN-07 — Catalog mất/stale cần crash matrix trước khi thêm schema

**P3 · design risk · Chỉ xác minh · Scope M cho lab; code TBD.** Evidence: [DEP02-02][r02]; severity/confidence gốc: Medium / Medium.

- **Root cause:** R02 thiếu bằng chứng catalog generation trong inventory; đó chưa chứng minh atomic write/recovery hiện tại hỏng.
- **Files/symbols:** [MProjectAgent/Services/InventoryReporter.cs](../../../MProjectAgent/Services/InventoryReporter.cs) — inventory report; [MProjectAgent/Storage/AppCatalogStore.cs:66](../../../MProjectAgent/Storage/AppCatalogStore.cs#L66) — ReadUnlockedAsync, WriteUnlockedAsync; [MProjectBackend/MProject.Application/Services/Software/PcInventoryService.cs](../../../MProjectBackend/MProject.Application/Services/Software/PcInventoryService.cs) — inventory reconciliation; [MProjectAgent/Services/JobExecutor.cs:204](../../../MProjectAgent/Services/JobExecutor.cs#L204) — ServerCompleted → CatalogCommitted.
- **Bằng chứng và scenario:** Đối chiếu R06 và source: catalog có temp+File.Replace+backup; job giữ journal đến CatalogCommitted. Restore catalog cũ/hỏng cả primary và backup vẫn chưa được kiểm. **Impact:** Rủi ro metadata Launcher không khớp payload; chưa có defect tái hiện.
- **Hướng sửa:** Chạy fault matrix, chỉ thêm checksum/generation/rebuild khi có mismatch sống sót qua các guard hiện tại. Không mở lại JOB-002 chỉ từ thiếu field.
- **Rủi ro thay đổi / rollback:** Bảo toàn catalog, backup, journal, cache index và payload trong lab; không xóa để ép rebuild. Các lỗi process identity đã xác nhận thuộc SYN-43/46.
- **Regression bắt buộc:** Kill ở catalog write/replace/journal delete; mất/stale primary, backup và metadata-only drift; kiểm launch đúng PackageId/version và inventory hội tụ.

<a id="syn-08"></a>

### SYN-08 — Publish vào CAS thiếu commit bảo toàn bytes đã xác minh

**P1 · confirmed defect · Sửa theo provider · Scope M.** Evidence: [CONTENT03-01][r03] + [CONTENT03-02][r03]; severity/confidence gốc: High / High; MinIO replay chưa E2E.

- **Root cause:** Client/copy ghi thẳng canonical CAS đang sống; một lần verify trước đó không bảo đảm bytes bất biến qua lần ghi mới.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:204](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L204) — InitializeUploadAsync, PreverifyNewUploadedBlobsAsync, CompleteUploadAsync; [MProjectBackend/MProject.Infrastructure/Storage/MinioStorageService.cs:132](../../../MProjectBackend/MProject.Infrastructure/Storage/MinioStorageService.cs#L132) — GetPresignedUploadUrlAsync; [MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs:30](../../../MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs#L30) — UploadAsync; [MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:309](../../../MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs#L309) — CompleteBlobUploadAsync; [MProjectBackend/MProject.Application/Services/Uploads/UploadReservationService.cs:83](../../../MProjectBackend/MProject.Application/Services/Uploads/UploadReservationService.cs#L83) — ReserveAsync; [MProjectFrontend/src/api/software.ts:415](../../../MProjectFrontend/src/api/software.ts#L415) — uploadRemoteBlob.
- **Bằng chứng và scenario:** R03: PostgreSQL+fake storage chấp nhận repair sai bytes cùng size; frontend dùng raw PUT. Windows fixture truncate object hợp lệ khi copy sau lỗi. Replay presigned URL trên MinIO thật còn cần test. **Impact:** Upload sau làm hỏng blob đang dùng chung; Agent reject hash, browser/archive có thể trả bytes sai.
- **Hướng sửa:** Staging riêng theo reservation, verify mọi publish/repair, commit atomic/serialized với GC; URL client không được đổi canonical sau finalize. Local dùng temp cùng volume rồi reuse/replace có kiểm chứng.
- **Rủi ro thay đổi / rollback:** Không đổi namespace hàng loạt. Triển khai provider từng bước, giữ old reader; backup object/versioning trước repair. Rollback code không tự khôi phục bytes hỏng: cần source/hash đã xác minh.
- **Regression bắt buộc:** MinIO thật: repair sai cùng size, replay sau finalize/release, hai reservation/late PUT. Local thật: cancel/I/O giữa copy; GC-vs-promote. Assert live bytes/hash/ref/reservation giữ đúng.

<a id="syn-09"></a>

### SYN-09 — DELETE file vượt release generation boundary

**P1 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CONTENT03-03][r03]; severity/confidence gốc: High / High (PostgreSQL).

- **Root cause:** Delete non-entry chỉ kiểm Draft trước save, không touch version trong cùng transaction.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:479](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L479) — DeleteFileAsync; [MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:451](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs#L451) — ReleaseVersionAsync; [MProjectBackend/MProject.Infrastructure/DBContext.cs:1131](../../../MProjectBackend/MProject.Infrastructure/DBContext.cs#L1131) — SaveChangesAsync; [MProjectBackend/MProject.Domain/Entities/Software/SoftwareFile.cs:5](../../../MProjectBackend/MProject.Domain/Entities/Software/SoftwareFile.cs#L5) — SoftwareFile.
- **Bằng chứng và scenario:** Barrier PostgreSQL pause delete DLL, release commit, delete tiếp tục; Released/generation giữ nguyên nhưng file set giảm. **Impact:** Cùng VersionId phát hành có manifest khác nhau, thiếu dependency.
- **Hướng sửa:** Conditional Draft touch, delete, metadata cleanup và refcount cùng transaction; giữ guard release hiện có.
- **Rủi ro thay đổi / rollback:** Thay service nhỏ; rollback không sửa artifact từng bị mutate. Đối chiếu manifest/version trước khi tái triển khai, không sửa trực tiếp Released để chữa lịch sử.
- **Regression bắt buộc:** PostgreSQL hai thứ tự delete/release, entry và non-entry; chỉ một transition thắng, file set Released giữ nguyên.

<a id="syn-10"></a>

### SYN-10 — GC vướng FK lịch sử sau khi đã xóa object

**P1 · confirmed defect · Quyết định retention rồi sửa · Scope M.** Evidence: [CONTENT03-04][r03]; severity/confidence gốc: High / High (PostgreSQL).

- **Root cause:** GC xét live refs nhưng FK Restrict vẫn giữ soft-deleted rows; eligibility và history retention khác nhau.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs:168](../../../MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs#L168) — SweepAsync, DeleteClaimedIfUnreferencedAsync, TryFinishClaimedBlobAsync; [MProjectBackend/MProject.Infrastructure/DBContext.cs:725](../../../MProjectBackend/MProject.Infrastructure/DBContext.cs#L725) — SoftwareFile → Blob FK; [MProjectBackend/MProject.Application/Services/Common/IntervalSweepService.cs:47](../../../MProjectBackend/MProject.Application/Services/Common/IntervalSweepService.cs#L47) — sweep error boundary.
- **Bằng chứng và scenario:** R03 chạy upload/delete/grace: object mất, FK chặn DB delete qua hai sweep, init cùng SHA tiếp tục thất bại. Không chứng minh đã xóa live-reference bytes. **Impact:** SHA bị kẹt tombstone, không reupload được; lỗi một candidate làm dừng sweep.
- **Hướng sửa:** Chốt giữ/tách/purge historical refs theo retention; kiểm trước object delete, isolate lỗi từng candidate. Không tự purge history để giải phóng SHA.
- **Rủi ro thay đổi / rollback:** Migration/data fix rủi ro cao; snapshot DB+object và kiểm restore trước thao tác. Rollback DB riêng không hồi sinh object đã xóa; có thể pause GC trong canary.
- **Regression bắt buộc:** PostgreSQL lifecycle và reupload, software/document/override/release history, poison candidate không chặn phần còn lại, GC/write barriers.

<a id="syn-11"></a>

### SYN-11 — Hash cache tái dùng nội dung khác cùng metadata

**P1 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CONTENT03-05][r03]; severity/confidence gốc: High / High (frontend repro).

- **Root cause:** path/size/mtime được coi là content identity và cache hit bỏ đọc File hiện tại.
- **Files/symbols:** [MProjectFrontend/src/utils/fileHash.ts:43](../../../MProjectFrontend/src/utils/fileHash.ts#L43) — calculateSha256, getCachedHash, buildHashCacheKey; [MProjectFrontend/src/api/software.ts:335](../../../MProjectFrontend/src/api/software.ts#L335) — reviseVersionFiles; [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:204](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L204) — InitializeUploadAsync existing blob.
- **Bằng chứng và scenario:** Hai File AAAA/BBBB cùng path,size,mtime trả cùng SHA; server có blob cũ khiến lần hai không upload bytes. **Impact:** Version hợp lệ chứa bytes cũ khác file người dùng chọn; hash server/Agent vẫn khớp.
- **Hướng sửa:** Hash File của selection hiện tại hoặc cache chỉ theo immutable identity đã chứng minh. Thêm package/version vào key không chữa được collision này.
- **Rủi ro thay đổi / rollback:** Đổi frontend nhỏ, vô hiệu cache cũ. Không tự sửa version đã phát hành; đối chiếu file gốc nếu cần tạo Draft thay thế.
- **Regression bắt buộc:** Browser/API flow hai bộ files cùng metadata khác bytes, server có blob đầu; lần hai truyền đúng SHA/bytes, cả folder strip-root.

<a id="syn-12"></a>

### SYN-12 — Upload-complete tự conflict khi prune metadata

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CONTENT03-06][r03]; severity/confidence gốc: Medium / High (PostgreSQL).

- **Root cause:** ExecuteUpdate tăng generation nhưng tracked original version vẫn cũ khi SaveChanges metadata.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:286](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L286) — CompleteUploadAsync, TouchEditableVersionAsync; [MProjectBackend/MProject.Infrastructure/DBContext.cs:1095](../../../MProjectBackend/MProject.Infrastructure/DBContext.cs#L1095) — concurrency tracking; [MProjectFrontend/src/pages/Software/components/ManifestModal.tsx:984](../../../MProjectFrontend/src/pages/Software/components/ManifestModal.tsx#L984) — runRevision.
- **Bằng chứng và scenario:** PostgreSQL fixture loại entry point tự ném concurrency exception; old file/refcount rollback nguyên vẹn, bytes mới có thể còn Uploaded. **Impact:** Rename/bỏ entry, icon, watch khiến finalize rollback dù không có request cạnh tranh.
- **Hướng sửa:** Đồng bộ tracker sau conditional touch hoặc một mutation có điều kiện cho metadata; giữ version guard.
- **Rủi ro thay đổi / rollback:** Không bỏ concurrency token. Rollback service bảo toàn old Draft; retry Uploaded qua reservation hiện có.
- **Regression bắt buộc:** PostgreSQL rename folder, remove icon/watch và policy prune commit khi đơn lẻ; release-winning race vẫn conflict.

<a id="syn-13"></a>

### SYN-13 — Ingest/release bỏ sót file là ancestor của file khác

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CONTENT03-07][r03]; severity/confidence gốc: Medium / High (PostgreSQL).

- **Root cause:** Validation từng path/full equality không kiểm ancestor theo segment và Windows casing.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:961](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L961) — ValidateAndNormalizeFiles; [MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:473](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs#L473) — ReleaseVersionAsync; [MProjectBackend/MProject.Application/Utils/WindowsPathRules.cs:14](../../../MProjectBackend/MProject.Application/Utils/WindowsPathRules.cs#L14) — NormalizeRelativeFilePath.
- **Bằng chứng và scenario:** Fixture bin và bin/app.exe được upload và Released. Đây là tree collision, không phải traversal bypass. **Impact:** Released tree không materialize được trên Windows, deployment bị chặn.
- **Hướng sửa:** Kiểm cây ở ingest/release, bao gồm clone/propagate; case-insensitive theo segment.
- **Rủi ro thay đổi / rollback:** Validation mới có thể chặn Draft cũ: báo path xung đột để sửa; không mutate Released hoặc nới containment Agent.
- **Regression bắt buộc:** bin + bin/app.exe, Bin variant, ancestor nhiều cấp reject; bin/a + bin2/b pass; derived versions cùng invariant.

<a id="syn-14"></a>

### SYN-14 — Mất final TUS PATCH response không tự reconcile

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CONTENT03-08][r03]; severity/confidence gốc: Medium / High.

- **Root cause:** Server xóa session sau MarkUploaded; client retry chỉ HEAD session cũ, không hỏi reservation.
- **Files/symbols:** [MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs:333](../../../MProjectBackend/MProject.Api/Infrastructure/TusUploadHandler.cs#L333) — CompleteBlobUploadAsync; [MProjectFrontend/src/api/software.ts:540](../../../MProjectFrontend/src/api/software.ts#L540) — uploadBlobWithTus, getTusOffset, withUploadRetry; [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:262](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L262) — alreadyUploaded init.
- **Bằng chứng và scenario:** Frontend mock lost final PATCH rồi HEAD 404 reject; backend ordering tương ứng. Init mới có alreadyUploaded nhưng client chưa dùng ở recovery này. **Impact:** Upload báo lỗi dù reservation Uploaded; cần thao tác lại, chưa phải mất bytes vĩnh viễn.
- **Hướng sửa:** Kết quả final PATCH mơ hồ + HEAD not-found phải reconcile đúng actor/target/hash/reservation; chỉ finalize khi server xác nhận Uploaded.
- **Rủi ro thay đổi / rollback:** Backward-compatible client flow; giữ expiry/permission và không coi mọi 404 là thành công. Không xóa staging để ép retry.
- **Regression bắt buộc:** TUS HTTP thật commit/teardown rồi drop response, browser finalize một lần; controls session expired/chưa upload phải fail.

<a id="syn-15"></a>

### SYN-15 — Human download-url Local còn dùng Agent route cũ

**P3 · confirmed defect · Sửa hoặc deprecate có kiểm caller · Scope S.** Evidence: [CONTENT03-09][r03]; severity/confidence gốc: Low / High (source).

- **Root cause:** LocalStorage trả path URL cũ trong khi route Agent yêu cầu cap + Agent auth.
- **Files/symbols:** [MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs:72](../../../MProjectBackend/MProject.Infrastructure/Storage/LocalStorageService.cs#L72) — GetPresignedDownloadUrlAsync; [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:507](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L507) — GetDownloadUrlAsync, GetAdminDownloadUrlAsync; [MProjectBackend/MProject.Api/Controllers/Software/SoftwareFilesController.cs:36](../../../MProjectBackend/MProject.Api/Controllers/Software/SoftwareFilesController.cs#L36) — download-url endpoints; [MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs:138](../../../MProjectBackend/MProject.Api/Controllers/Assets/AgentController.cs#L138) — DownloadLocalBlob.
- **Bằng chứng và scenario:** Human Bearer gọi URL được cấp thành công rồi bị Agent endpoint từ chối. Không có authorization bypass. **Impact:** Caller hai API URL không tải được; browser hiện dùng stream nên phạm vi hẹp.
- **Hướng sửa:** Trả human streaming route phù hợp hoặc deprecate sau inventory caller; capability human mới phải theo authority matrix SYN-84/85/92.
- **Rủi ro thay đổi / rollback:** Giữ API compatibility/deprecation window. Không nới Agent auth để chữa URL; rollback chỉ ảnh hưởng caller phụ này.
- **Regression bắt buộc:** HTTP cả hai URL theo permission, Local/MinIO, đúng bytes; thiếu quyền deny và Agent capability negatives vẫn pass.

<a id="syn-16"></a>

### SYN-16 — Released path policies có mutation chưa được định nghĩa rõ

**P2 · design risk · Quyết định sản phẩm · Scope M.** Evidence: [CONTENT03-10][r03]; severity/confidence gốc: Medium / High code, Medium contract.

- **Root cause:** Hai policy được sửa trên Released, nhưng không có policy revision/immutability boundary được ghi rõ.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:651](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs#L651) — SetOverridablePathsAsync; [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:756](../../../MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L756) — effective blob / PreserveLocal mapping; [MProjectBackend/MProject.Api/Controllers/Software/SoftwareVersionsController.cs:90](../../../MProjectBackend/MProject.Api/Controllers/Software/SoftwareVersionsController.cs#L90) — policy endpoint.
- **Bằng chứng và scenario:** PostgreSQL release rồi thêm PreserveLocal thành công; CRUD đủ gọi setter. Config customization là tính năng chủ ý, chưa có bằng chứng ngoại lệ này bị cấm. **Impact:** Cùng VersionId có Overridable/PreserveLocal policy khác giữa hai lần resolve.
- **Hướng sửa:** Chọn policy chỉ đổi qua Draft hoặc revision/audit riêng; giữ authority và stale-action contracts SYN-84–96. Không âm thầm bỏ feature.
- **Rủi ro thay đổi / rollback:** Nếu đóng băng, giữ history policy đã dùng cho job và đường revert revision. Canary preserve-local phải kiểm bytes thực, không chỉ desired manifest.
- **Regression bắt buộc:** Resolve quanh policy change, CRUD-vs-publish actor, history/generation và installed local file theo contract được chọn.

<a id="syn-17"></a>

### SYN-17 — Baseline effective khác pipeline cấu hình thực tế

**P1 · confirmed defect · Sửa có cấu trúc · Scope L.** Evidence: [CFG04-01][r04]; severity/confidence gốc: High / High.

- **Root cause:** Validator dựng một phần effective config, bỏ file precedence, secondary targets/MaxScope; API bỏ station scope.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigBaselineValidator.cs:78](../../../MProjectBackend/MProject.Application/Services/Software/ConfigBaselineValidator.cs#L78) — ValidateAsync, LoadLayersAsync, EvaluateEffective; [MProjectBackend/MProject.Application/Services/Software/ConfigBaselineService.cs:275](../../../MProjectBackend/MProject.Application/Services/Software/ConfigBaselineService.cs#L275) — ValidateAsync; [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:735](../../../MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L735) — ResolveOverridesAsync.
- **Bằng chứng và scenario:** Diagnostic primary/mirror base=1, override=2: baseline mirror=1 Passed nhưng runtime mirror=2. InMemory/renderer thật, chưa PostgreSQL E2E. **Impact:** Pin pass nhưng bytes mirror/whole-file gửi đến trạm vi phạm baseline.
- **Hướng sửa:** Một resolve plan kiểm chứng dùng chung baseline/preview/manifest, gồm scope, whole-file, mirror/glob; giữ packaged-only mode có nhãn rõ.
- **Rủi ro thay đổi / rollback:** L: chia nhỏ bằng characterization/parity tests, chưa đổi mọi resolver một lần. Canary so bytes trước/sau, lưu pin/config revision để phục hồi trạm; không tự rebase overrides.
- **Regression bắt buộc:** Baseline đối chiếu bytes thật cho whole-file, mirror, glob, MaxScope, Computer; API station scope phải có hiệu lực; pin transaction PostgreSQL và Agent nhận đúng file.

<a id="syn-18"></a>

### SYN-18 — Render fallback/partial được xuất bản như success

**P1 · confirmed defect · Sửa có cấu trúc · Scope L.** Evidence: [CFG04-02][r04]; severity/confidence gốc: High / High.

- **Root cause:** Nullable result gộp no-change/failure; writer best-effort và cache cả output áp một phần.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:71](../../../MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs#L71) — RenderAsync, ApplyValues, PersistAsync; [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:764](../../../MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L764) — ResolveOverridesAsync / success record.
- **Bằng chứng và scenario:** Requested valid=3, typed=not-number tạo/cache JSON valid=3, typed=2; read/persist lỗi có thể trả null rồi caller dùng base. R00 handoff được R04 xác nhận. **Impact:** Trạm nhận tổ hợp giá trị chưa áp đủ nhưng deployment có thể Healthy.
- **Hướng sửa:** Typed success/no-change/stale/failure; chỉ publish output hoàn chỉnh theo plan. Failure giữ revision trước hoặc chặn/retry job có trạng thái rõ, không cache partial success.
- **Rủi ro thay đổi / rollback:** Thay semantics có thể biến silent success thành blocked job; canary inventory lỗi hiện có trước rollout. Giữ bản config/pin tốt và thao tác phục hồi trong maintenance window.
- **Regression bắt buộc:** Inject key/type/storage/persist failure nhiều file, retry sau phục hồi; không manifest success partial, cache không che lỗi. Thử Local/MinIO và installed config thực.

<a id="syn-19"></a>

### SYN-19 — INI mirror nhận newline tạo key ngoài declaration

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [CFG04-03][r04]; severity/confidence gốc: High / High.

- **Root cause:** Validation theo primary format, không kiểm output sau template của destination; INI writer ghép raw newline.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigValueValidator.cs:25](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueValidator.cs#L25) — Validate; [MProjectBackend/MProject.Application/Services/Software/OverrideResolver.cs:206](../../../MProjectBackend/MProject.Application/Services/Software/OverrideResolver.cs#L206) — ExpandTargetsAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigFileWriter.cs:57](../../../MProjectBackend/MProject.Application/Services/Software/ConfigFileWriter.cs#L57) — SetIni; [MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:856](../../../MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs#L856) — NormalizeTargets.
- **Bằng chứng và scenario:** JSON String nhiều dòng qua validator, INI mirror sinh key UnexpectedSetting trong diagnostic thật; template/separator có cùng boundary. **Impact:** Value có thể thêm setting không được khai báo hoặc làm hỏng INI.
- **Hướng sửa:** Chặn CR/LF ở INI writer và validate tất cả destination outputs trước persist/submit. Phối hợp SYN-18 tránh chỉ bỏ một target rồi báo success.
- **Rủi ro thay đổi / rollback:** Validation chặt hơn có thể làm dữ liệu cũ bị từ chối; liệt kê row cần sửa, giữ Active/config tốt. Không làm sạch value âm thầm.
- **Regression bắt buộc:** JSON→INI LF/CRLF, template/separator, Set/Bulk/Apply/Approve; không thêm undeclared key hoặc publish partial file.

<a id="syn-20"></a>

### SYN-20 — Một snapshot primary không đủ cho nhiều mirror

**P1 · confirmed defect · Quyết định invariant rồi sửa · Scope L.** Evidence: [CFG04-04][r04]; severity/confidence gốc: High / High.

- **Root cause:** Base snapshot của từng mirror được suy từ primary dù base thực khác; dùng chung OverrideId/status.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:242](../../../MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs#L242) — SetTargetsAsync, SimulateAsync; [MProjectBackend/MProject.Application/Services/Software/OverrideResolver.cs:206](../../../MProjectBackend/MProject.Application/Services/Software/OverrideResolver.cs#L206) — ExpandTargetsAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:116](../../../MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs#L116) — ApplyValues, MarkStaleAsync.
- **Bằng chứng và scenario:** COM20 primary, COM40 mirror được khai báo hợp lệ; override COM9 render mirror stale rồi vô hiệu cả parameter. R04 có repro; chưa biết số legacy rows. **Impact:** Primary/mirror phân kỳ; một mirror làm Stale cả override, lần resolve sau đổi toàn output.
- **Hướng sửa:** Enforce base compatibility của tất cả mirrors hoặc snapshot theo destination/context; đánh giá toàn parameter trước publish.
- **Rủi ro thay đổi / rollback:** Có thể đổi data shape và stale policy; snapshot declarations/values, migration có dry-run. Restore revision tốt và kiểm cả mirrors trên trạm trước mở rộng.
- **Regression bắt buộc:** Mirror khác base, release đổi riêng mirror, đảo thứ tự files; preview/pin/render phải thống nhất và hai resolve cùng revision có cùng output.

<a id="syn-21"></a>

### SYN-21 — Render cũ đánh Stale revision override vừa cập nhật

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [CFG04-05][r04]; severity/confidence gốc: High / High.

- **Root cause:** ResolvedConfigValue chỉ mang ID, MarkStale load row mới rồi áp quyết định của snapshot cũ.
- **Files/symbols:** [MProjectBackend/MProject.Application/Interface/Software/IOverrideResolver.cs:27](../../../MProjectBackend/MProject.Application/Interface/Software/IOverrideResolver.cs#L27) — ResolvedConfigValue; [MProjectBackend/MProject.Application/Services/Software/OverrideResolver.cs:94](../../../MProjectBackend/MProject.Application/Services/Software/OverrideResolver.cs#L94) — ResolveValuesAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:168](../../../MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs#L168) — MarkStaleAsync.
- **Bằng chứng và scenario:** R04 resolve COM3/base COM7, Set COM8/base COM20 rồi render cũ làm COM8 Stale. EF token không bảo vệ vì MarkStale load sau update. **Impact:** Value mới hợp lệ bị loại khỏi runtime, có thể ảnh hưởng cả Station.
- **Hướng sửa:** Carry expected row/base revision, conditional stale update; context đổi thì bỏ/re-resolve kết quả cũ.
- **Rủi ro thay đổi / rollback:** Additive revision contract; giữ reader cũ tương thích. Không auto-reactivate mọi stale row khi rollback; chỉ sửa row có evidence và kiểm rendered config.
- **Regression bắt buộc:** PostgreSQL barrier resolve-old → Set/Reconfirm/Approve-new → stale-old; value mới Active, audit không ghi sai revision.

<a id="syn-22"></a>

### SYN-22 — Migrate legacy whole-file có thể kích hoạt payload chưa duyệt

**P1 · confirmed defect · Sửa; policy gate 09 · Scope M.** Evidence: [CFG04-06][r04]; severity/confidence gốc: High / High.

- **Root cause:** Migration giả định source đã Active; Upsert đặt Active, retire không chuyển pending approval.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs:122](../../../MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs#L122) — MigrateAsync, LoadAsync, UpsertValueAsync, RetireOverrideFileAsync; [MProjectBackend/MProject.Application/Services/Approvals/OverrideFileApprovalHandler.cs:50](../../../MProjectBackend/MProject.Application/Services/Approvals/OverrideFileApprovalHandler.cs#L50) — OnApprovedAsync; [MProjectFrontend/src/pages/OverrideFiles/OverrideFiles.tsx:499](../../../MProjectFrontend/src/pages/OverrideFiles/OverrideFiles.tsx#L499) — migration action.
- **Bằng chứng và scenario:** Diagnostic Pending file migrate thành values Active, file deleted nhưng request Pending. Điều kiện là legacy rows và actor có manage override + package CRUD. **Impact:** Draft/Pending thành Active values và approval cũ mồ côi.
- **Hướng sửa:** Chỉ migrate Active hoặc bảo toàn approval proposal/state theo quyết định nghiệp vụ; cancel/replace request trong transaction. Không đổi generic approval ngoài contracts SYN-95/96.
- **Rủi ro thay đổi / rollback:** Chặn entry này có thể là containment nhỏ sau khi đối chiếu policy; snapshot rows/approvals trước conversion. Không xóa pending để bỏ qua review.
- **Regression bắt buộc:** Draft/Pending/Active/rejected/cancelled và PostgreSQL migrate-vs-approve/delete; không Active từ payload chưa duyệt, không dangling request.

<a id="syn-23"></a>

### SYN-23 — Migration không giữ effective config trước/sau

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [CFG04-07][r04]; severity/confidence gốc: High / High.

- **Root cause:** Diff một chiều chỉ đọc keys trong override; Computer base snapshot dùng packaged thay vì lower effective layer.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs:96](../../../MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs#L96) — Diff, UpsertValueAsync, ResolveComparisonVersionAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigValueLayering.cs:26](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueLayering.cs#L26) — Fold.
- **Bằng chứng và scenario:** Hai repro: packaged A/B, override chỉ A, migrate phục hồi B; Computer COM3 snapshot packaged COM20 nhưng Station COM5 làm Computer stale. **Impact:** Key đã bỏ quay lại hoặc Computer value mất hiệu lực về Station.
- **Hướng sửa:** Render candidate theo lower layers sau retire và kiểm tương đương semantic; chặn deletion/type/null/structure chưa biểu diễn được.
- **Rủi ro thay đổi / rollback:** Data conversion rủi ro cao; dry-run diff đầy đủ, giữ file/row gốc và id mapping cho reverse. Chưa retire nếu không chứng minh tương đương.
- **Regression bắt buộc:** Deleted keys, JSON null/type/array, model/station file precedence, Station value + Computer file, comparison-version đổi; kiểm actual output trước/sau.

<a id="syn-24"></a>

### SYN-24 — Migration values/retire/reference không atomic

**P2 · confirmed defect · Sửa cùng SYN-22/23 · Scope M.** Evidence: [CFG04-08][r04]; severity/confidence gốc: Medium / High; PostgreSQL chưa chạy.

- **Root cause:** Hai SaveChanges và ref bookkeeping không có transaction toàn conversion.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs:246](../../../MProjectBackend/MProject.Application/Services/Software/OverrideFileMigrationService.cs#L246) — MigrateAsync, RetireOverrideFileAsync.
- **Bằng chứng và scenario:** Inject lỗi save thứ hai trong R04, reload vẫn thấy values commit và source file live. Không dùng InMemory như bằng chứng isolation PostgreSQL. **Impact:** API lỗi nhưng Active values và file cũ cùng tồn tại, poll có thể ghi stale side effects.
- **Hướng sửa:** Một transaction cho parameters/values/file/approval/refs; preflight trước mutation, source revision guard và retry identity.
- **Rủi ro thay đổi / rollback:** Migration additive, giữ snapshot cùng source; thất bại phải rollback toàn domain. Không retry mù khi conversion trước đã commit một phần.
- **Regression bắt buộc:** PostgreSQL fault từng save, concurrent resolve, duplicate migrate; toàn old hoặc toàn new, refs và approvals consistent.

<a id="syn-25"></a>

### SYN-25 — Apply all programs commit một phần rồi báo lỗi

**P1 · confirmed defect · Chốt atomicity rồi sửa · Scope M.** Evidence: [CFG04-09][r04]; severity/confidence gốc: Medium / High.

- **Root cause:** Main Set commit trước sibling preflight; mỗi sibling có transaction riêng.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs:911](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs#L911) — SetAsync, ApplyToSiblingProgramsAsync; [MProjectBackend/MProject.Tests/ConfigValueOverrideServiceTests.cs:729](../../../MProjectBackend/MProject.Tests/ConfigValueOverrideServiceTests.cs#L729) — RefusesBeforeWritingWhenASiblingLocksTheScope; [MProjectFrontend/src/components/files/ConfigValuesModal.tsx:156](../../../MProjectFrontend/src/components/files/ConfigValuesModal.tsx#L156) — save.
- **Bằng chứng và scenario:** R04 bổ sung assertion main đã COM9 khi sibling khóa scope; test cũ chỉ nhìn sibling. Tăng ưu tiên so với Medium vì có thể đổi port trạm trong một operation báo thất bại. **Impact:** Cùng PC có cấu hình các chương trình không đồng bộ dù người dùng nhận fail.
- **Hướng sửa:** Resolve/preflight tất cả rồi atomic commit, hoặc product chọn partial semantics với per-target result/retry rõ. Không để UI hiểu all-or-nothing sai.
- **Rủi ro thay đổi / rollback:** Phối hợp approvals; snapshot main/sibling và requests. Revert bằng revision/approval hợp lệ, không ghi đè proposal khác phát sinh sau operation.
- **Regression bắt buộc:** PostgreSQL sibling cuối fail type/scope/base/submit; assert cả main và mọi sibling/request giữ nguyên nếu contract atomic.

<a id="syn-26"></a>

### SYN-26 — Config mutation thiếu redeploy invalidation

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [CFG04-10][r04]; severity/confidence gốc: Medium / High.

- **Root cause:** Flag gắn value CRUD, chưa gắn metadata/targets/whole-file approval-delete.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:158](../../../MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs#L158) — SetForFileAsync, SetTargetsAsync; [MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs:182](../../../MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs#L182) — DeleteOverrideFileAsync; [MProjectBackend/MProject.Application/Services/Approvals/OverrideFileApprovalHandler.cs:50](../../../MProjectBackend/MProject.Application/Services/Approvals/OverrideFileApprovalHandler.cs#L50) — OnApprovedAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigValueRedeploy.cs:27](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueRedeploy.cs#L27) — FlagAsync; [MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:210](../../../MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L210) — ConfigChanged poll trigger.
- **Bằng chứng và scenario:** R04 thêm target với Active override nhưng installation vẫn Healthy. AutoHeal off làm khác biệt không tự dẫn tới job. **Impact:** Máy đã cài Healthy giữ config cũ trong khi máy mới nhận config mới.
- **Hướng sửa:** Cùng commit đánh dấu affected config revision/installations cho mọi mutation làm đổi effective output; giới hạn package/scope.
- **Rủi ro thay đổi / rollback:** Có thể tạo nhiều jobs: preview fan-out và canary trong update window. Giữ old revision/pin và xác minh preserve-local khi phục hồi.
- **Regression bắt buộc:** Healthy same-version, AutoHeal off; targets/MaxScope/file approve/delete tạo ConfigChanged và job; no-op không tạo redeploy thừa.

<a id="syn-27"></a>

### SYN-27 — Approval activate value không còn hợp lệ với declaration

**P2 · confirmed defect · Sửa target; giữ approval boundary SYN-95/96 · Scope M.** Evidence: [CFG04-11][r04]; severity/confidence gốc: Medium / High.

- **Root cause:** Set validate lúc submit; handler không revalidate declaration/targets hiện hành khi promote.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Approvals/ConfigValueOverrideApprovalHandler.cs:57](../../../MProjectBackend/MProject.Application/Services/Approvals/ConfigValueOverrideApprovalHandler.cs#L57) — OnApprovedAsync / missing ValidateApprovalAsync override; [MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:170](../../../MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs#L170) — ApproveAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs:158](../../../MProjectBackend/MProject.Application/Services/Software/ConfigParameterService.cs#L158) — SetForFileAsync.
- **Bằng chứng và scenario:** Submit COM8, đổi declaration sang Int, approve vẫn Active trong diagnostic. Không phải evidence generic approval concurrency bypass. **Impact:** Approved/Active có thể không render được hoặc trái type/range mới.
- **Hướng sửa:** Bind proposal với declaration/plan revision và validate trong final commit; invalid thì resubmit, giữ Active cũ.
- **Rủi ro thay đổi / rollback:** Không tự hủy mọi pending request; chỉ conflict với revision đã đổi. Snapshot proposal/context, giữ contract snapshot UI và backward compatibility.
- **Regression bắt buộc:** Type/range/allowed values/MaxScope/target/pin/station đổi lúc Pending; PostgreSQL approve race không promote invalid, Active trước còn nguyên.

<a id="syn-28"></a>

### SYN-28 — Config modal Save nhắm PC cũ dưới tiêu đề PC mới

**P1 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CFG04-12][r04]; severity/confidence gốc: High / High (React repro).

- **Root cause:** Late load ghi view không kiểm generation; title lấy props B, mutation lấy view.targetId A.
- **Files/symbols:** [MProjectFrontend/src/components/files/ConfigValuesModal.tsx:98](../../../MProjectFrontend/src/components/files/ConfigValuesModal.tsx#L98) — load, effect, save, modal title; [MProjectFrontend/src/pages/Computer/components/ComputerDetailDrawer.tsx:615](../../../MProjectFrontend/src/pages/Computer/components/ComputerDetailDrawer.tsx#L615) — ConfigValuesModal target props.
- **Bằng chứng và scenario:** Deferred responses B trước A: title B, data A, Save API target A. Chỉ modal này được repro; chưa gán lỗi cho mọi modal tương tự. **Impact:** Operator đổi cấu hình nhầm trạm với quyền hợp lệ.
- **Hướng sửa:** Ignore/abort old generation, clear view khi target/scope đổi; gate mọi write bằng current view identity.
- **Rủi ro thay đổi / rollback:** UI-only nhỏ, không migration. Rollback bản sửa không hoàn tác cấu hình đã ghi sai: cần audit target và correction theo approval hiện hành.
- **Regression bắt buộc:** React/browser A→B đảo response, close/reopen, scope/byResource; Set/Delete/Reconfirm luôn đúng current target, không mutate stale view.

<a id="syn-29"></a>

### SYN-29 — Read DTO che Pending replacement

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [CFG04-13][r04]; severity/confidence gốc: Medium / High.

- **Root cause:** Domain tách Active/Pending nhưng ToLayer/DTO/counters/frontend chỉ đọc Active.
- **Files/symbols:** [MProjectBackend/MProject.Application/Models/ConfigValueOverrideModels.cs:8](../../../MProjectBackend/MProject.Application/Models/ConfigValueOverrideModels.cs#L8) — ConfigValueLayerDto; [MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs:1312](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs#L1312) — ToLayer, GetDriftAsync; [MProjectFrontend/src/types/configValueOverrides.ts:16](../../../MProjectFrontend/src/types/configValueOverrides.ts#L16) — layer contract; [MProjectFrontend/src/components/files/ConfigValuesModal.tsx:140](../../../MProjectFrontend/src/components/files/ConfigValuesModal.tsx#L140) — startEdit, renderLayer.
- **Bằng chứng và scenario:** R04 row Active + PendingApproval replacement nhưng scope view chỉ Active và zero pending; không phủ nhận CFG-001 giữ Active đúng. **Impact:** Proposal biến khỏi màn config, pending count=0 gây nhập lại/cancel nhầm.
- **Hướng sửa:** Expose hai revisions/request identity, counters và action meaning riêng; field additions backward-compatible.
- **Rủi ro thay đổi / rollback:** Server DTO trước UI; không đổi state vì chỉ thiếu read projection. Rollback UI vẫn đọc Active, không được delete proposal để khớp màn.
- **Regression bắt buộc:** Active+Draft/Pending, reject/cancel/approve/reopen; value/proposal/badge/count/edit defaults đúng trên API và component.

<a id="syn-30"></a>

### SYN-30 — Computed stale, stored stale và cache lệch nhau

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [CFG04-14][r04]; severity/confidence gốc: Medium / High.

- **Root cause:** Read Fold tính stale, endpoint đòi stored Stale, render mới materialize nhưng cache hit bỏ side effect.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigValueLayering.cs:26](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueLayering.cs#L26) — Fold; [MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs:1257](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs#L1257) — ReconfirmAsync; [MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:61](../../../MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs#L61) — RenderAsync cache hit; [MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs:711](../../../MProjectBackend/MProject.Application/Services/Software/SoftwareFileService.cs#L711) — ResolveEffectiveBlobsAsync; [MProjectFrontend/src/components/files/ConfigValuesModal.tsx:512](../../../MProjectFrontend/src/components/files/ConfigValuesModal.tsx#L512) — Reconfirm action.
- **Bằng chứng và scenario:** Hai R04 probes: computed stale không reconfirm được; preview markStale=false tạo cache khiến deploy markStale=true không ghi Stale. **Impact:** UI cho Reconfirm nhưng API reject; cache che stale status/count.
- **Hướng sửa:** Một revision-aware stale definition cho action/lifecycle, tách lifecycle evaluation khỏi bytes cache; phối hợp SYN-21.
- **Rủi ro thay đổi / rollback:** Không dựa vào purge cache như fix lâu dài. Giữ old values/snapshots và thử reconfirm trước poll; không auto-rebase.
- **Regression bắt buộc:** Preview→deploy hit, stale+valid values, hai PC cùng bytes key khác OverrideId, reconfirm trước poll; status/count/action hội tụ đúng revision.

<a id="syn-31"></a>

### SYN-31 — Non-finite Decimal vượt range validation

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [CFG04-15][r04]; severity/confidence gốc: Medium / High (.NET repro).

- **Root cause:** Double parse chấp nhận non-finite; comparisons Min/Max không loại NaN, writer không bảo vệ numeric serialization.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/ConfigValueValidator.cs:44](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueValidator.cs#L44) — Validate Decimal, EnsureInRange; [MProjectBackend/MProject.Application/Services/Software/ConfigFileWriter.cs:282](../../../MProjectBackend/MProject.Application/Services/Software/ConfigFileWriter.cs#L282) — BuildNodeMatchingType; [MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:141](../../../MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs#L141) — ApplyValues.
- **Bằng chứng và scenario:** R04 Min=0/Max=10 Validate(NaN) pass rồi ArgumentException khi render JSON; không phải lỗi mọi Decimal. **Impact:** NaN lưu hợp lệ nhưng JSON render ném lỗi, chặn job đến khi sửa value.
- **Hướng sửa:** Require finite ở validator/writer/bounds; dùng typed render failure của SYN-18.
- **Rủi ro thay đổi / rollback:** Chặn input mới, kiểm dữ liệu hiện có trước rollout để tránh job bất ngờ fail. Không tự clamp/sửa số đo vận hành.
- **Regression bắt buộc:** NaN, ±Infinity, exponent overflow có/không bounds, JSON/INI, Set/Bulk/Approve; không persist/activate non-finite hoặc unhandled manifest error.

<a id="syn-32"></a>

### SYN-32 — Acknowledgement và edit không bind revision đã xem

**P2 · design risk · Quyết định contract · Scope M.** Evidence: [CFG04-16][r04]; severity/confidence gốc: Medium / High code, Medium impact.

- **Root cause:** Preview chỉ count, edit không expected revision/operation ID; concurrency sau server load không bảo vệ stale client view.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:330](../../../MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs#L330) — EnsureImpactAcknowledged; [MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs:826](../../../MProjectBackend/MProject.Application/Services/Software/ConfigValueOverrideService.cs#L826) — PreviewAssignmentImpactAsync, SetAsync, BulkSetAsync; [MProjectBackend/MProject.Application/Models/ConfigValueOverrideModels.cs](../../../MProjectBackend/MProject.Application/Models/ConfigValueOverrideModels.cs) — SetConfigValueOverrideRequest, BulkSetConfigValuesRequest; [MProjectFrontend/src/utils/pin-impact.tsx](../../../MProjectFrontend/src/utils/pin-impact.tsx) — confirmPinImpact.
- **Bằng chứng và scenario:** R04 source: A bị bỏ, B thêm, count=1 vẫn chấp nhận acknowledgement cũ; chưa có PostgreSQL/UI concurrent fixture xác nhận incident. **Impact:** Count bằng nhau nhưng tập stale khác vẫn pin; delayed retry có thể ghi đè intent mới.
- **Hướng sửa:** Nếu cần giữ intent đã xem, impact digest/revision ở commit, expected edit revision và operation identity. Chốt semantics trước đổi API.
- **Rủi ro thay đổi / rollback:** Additive contract với compatibility fleet/UI; tránh bắt mọi legacy caller gửi field ngay. Lưu config/pin revision để restore có kiểm scope.
- **Regression bắt buộc:** A→B cùng count, edit-new rồi delayed duplicate Set, declaration/pin đổi trong preview; PostgreSQL/browser conflict-vs-idempotent retry theo contract.

<a id="syn-33"></a>

### SYN-33 — OTA exact replacement làm mất ACL hardened

**P1 · confirmed defect · Sửa trước OTA rộng · Scope M.** Evidence: [OTA05-01][r05]; severity/confidence gốc: High / High.

- **Root cause:** Copy/rename bảo toàn bytes nhưng candidate/rollback không giữ security descriptor của install/config.
- **Files/symbols:** [MProjectAgent/Commands/ApplyUpdateCommand.cs:146](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L146) — CopyStagedOverInstall, ReplaceWithExactSnapshot, CopyDirectory, CopyStationOwnedConfig; [scripts/install-agent.ps1:126](../../../scripts/install-agent.ps1#L126) — Protect-AgentInstallation.
- **Bằng chứng và scenario:** Windows fixture parent Users RX làm root/config mất protected ACL sau helper. Parent writable tăng tác động; không khẳng định mọi PC hiện có ACL này. **Impact:** Config nhận Users read; nếu parent cho Modify, binaries service cũng có thể bị ghi.
- **Hướng sửa:** Harden candidate/backup trước có thể sử dụng hoặc switch; bảo vệ config riêng, giữ quyền đọc Launcher hợp lệ, kiểm parent/switch metadata.
- **Rủi ro thay đổi / rollback:** Cả update và restore phải reapply ACL tin cậy. Kiểm bootstrap đang cài; backup ngoài install chỉ SYSTEM/Admin, không đưa credential vào report.
- **Regression bắt buộc:** LocalSystem update/rollback trên parent RX/Modify; standard-user effective access trước/trong/sau rename, exe/config/backup; core service và Launcher còn chạy.

<a id="syn-34"></a>

### SYN-34 — OTA cleanup lỗi phá active và dùng old snapshot đã xóa dở

**P1 · confirmed defect · Sửa trước OTA rộng · Scope M.** Evidence: [OTA05-02][r05]; severity/confidence gốc: High / High (Windows fixture).

- **Root cause:** Recursive cleanup nằm trong switch catch; sự tồn tại parked directory được coi là nguồn rollback exact.
- **Files/symbols:** [MProjectAgent/Commands/ApplyUpdateCommand.cs:156](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L156) — CopyStagedOverInstall, ReplaceWithExactSnapshot, RunAsync; [MProjectAgent/Commands/BootstrapCommand.cs:61](../../../MProjectAgent/Commands/BootstrapCommand.cs#L61) — Recover.
- **Bằng chứng và scenario:** Khóa file cuối parked sau new-active; delete một phần, catch xóa new/restore lỗi. Sau bỏ lock, recovery chọn partial old thiếu exe. **Impact:** Mất install hoặc thiếu exe dù intact backup còn trên đĩa; cần can thiệp tại PC.
- **Hướng sửa:** Commit switch/readiness tách khỏi retry cleanup; giữ snapshot verified nguyên vẹn và provenance, không xóa active tốt vì cleanup failed.
- **Rủi ro thay đổi / rollback:** Rủi ro cao dù code M; phải có helper recovery ngoài install đã kiểm phiên bản, intact backup và đường manual restore. Dừng đúng Launcher khi rollback.
- **Regression bắt buộc:** LocalSystem/SCM: locked exe/AV-like handle, lỗi giữa recursive delete, crash cleanup ở update và rollback; luôn có bootable install và known-good source.

<a id="syn-35"></a>

### SYN-35 — Crash trước OTA readiness mất owner recovery và backup tốt

**P1 · confirmed defect · Sửa trước OTA rộng · Scope M.** Evidence: [OTA05-03][r05]; severity/confidence gốc: High / High (subprocess + helpers).

- **Root cause:** Switch marker chỉ biết directory; readiness/known-good backup không nằm trong durable attempt do external bootstrap sở hữu.
- **Files/symbols:** [MProjectAgent/Commands/ApplyUpdateCommand.cs:81](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L81) — RunAsync backup/switch/readiness; [MProjectAgent/Commands/BootstrapCommand.cs:61](../../../MProjectAgent/Commands/BootstrapCommand.cs#L61) — Recover, BootstrapService.OnStart; [MProjectAgent/Services/AgentWorker.cs:233](../../../MProjectAgent/Services/AgentWorker.cs#L233) — HeartbeatTickAsync; [MProjectAgent/Services/AgentUpdater.cs:77](../../../MProjectAgent/Services/AgentUpdater.cs#L77) — MaybeStageUpdateAsync.
- **Bằng chứng và scenario:** Crash new-active, bootstrap chọn NEW; lần apply sau ResetDir/copy làm mất OLD. Nếu NEW không heartbeat, resume updater không chạy. **Impact:** Agent mới lỗi có thể mắc vĩnh viễn; retry overwrite backup OLD bằng NEW chưa commit.
- **Hướng sửa:** Journal phases backup/switch/start/readiness/commit, immutable backup theo attempt; bootstrap/updater ngoài install quản recovery đến commit, không reset unfinished backup.
- **Rủi ro thay đổi / rollback:** Phải nâng/kiểm bootstrap thực, vì bootstrap/v1 có thể reuse binary cũ. Diễn tập manual recovery với old bundle/state/journal, không chỉ deploy ApplyUpdateCommand mới.
- **Regression bắt buộc:** SCM/reboot/kill updater sau switch/start/trước-sau heartbeat, NEW crash/no-network; tự rollback OLD qua nhiều restart, null/same-version/force offer.

<a id="syn-36"></a>

### SYN-36 — Apply latch tồn tại sau launch failure/helper exit

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [OTA05-04][r05]; severity/confidence gốc: High / High latch, Medium race.

- **Root cause:** Interlocked flag biểu diễn từng thử launch, không lifecycle của updater đang sở hữu lease.
- **Files/symbols:** [MProjectAgent/Services/AgentUpdater.cs:282](../../../MProjectAgent/Services/AgentUpdater.cs#L282) — LaunchApply, MaybeStageUpdateAsync; [MProjectAgent/Commands/ApplyUpdateCommand.cs:43](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L43) — RunAsync apply lease; [MProjectAgent/Services/AgentWorker.cs:255](../../../MProjectAgent/Services/AgentWorker.cs#L255) — HeartbeatTickAsync.
- **Bằng chứng và scenario:** Process.Start throw vẫn latch=1. NEW heartbeat trước staged marker clear có thể launch contender thoát lease nhưng giữ latch trong Agent mới. **Impact:** Release kế tiếp chỉ stage, không apply cho đến restart Agent.
- **Hướng sửa:** Theo dõi attempt/child terminal paths, reset/reconcile khi launch lỗi/lease contention/completion; giữ lease và recovery-before-version guard.
- **Rủi ro thay đổi / rollback:** Không bỏ lease để tránh deadlock giả. Canary A→B→C trong cùng process generation, giữ khả năng restart service có kiểm state nếu cần phục hồi.
- **Regression bắt buộc:** Start fail rồi success, child exit/lease contention, updater crash, hai releases liên tiếp không restart thủ công; real bootstrap/SCM timing.

<a id="syn-37"></a>

### SYN-37 — Server và Agent thiếu chung contract cho bundle OTA hoàn chỉnh

**P1 · confirmed defect · Sửa parity + minimum bundle · Scope M.** Evidence: [OTA05-05][r05] + [OTA05-06][r05]; severity/confidence gốc: Medium + High / High.

- **Root cause:** Không có shipping contract chung enforce manifest metadata và minimum components; exact inventory tự khai không chứng minh bundle đủ.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:179](../../../MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs#L179) — PublishAsync, EnsureAgentArchive; [MProjectAgent/Services/AgentUpdater.cs:230](../../../MProjectAgent/Services/AgentUpdater.cs#L230) — ValidateBundleManifestAsync; [MProjectAgent/Commands/ApplyUpdateCommand.cs:150](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L150) — CopyStagedOverInstall, WaitForHeartbeatReadinessAsync; [MProjectAgent/Services/LauncherBootstrapper.cs:35](../../../MProjectAgent/Services/LauncherBootstrapper.cs#L35) — EnsureRunning; [scripts/package-agent.ps1:219](../../../scripts/package-agent.ps1#L219) — Launcher files/version gate.
- **Bằng chứng và scenario:** R05 signed manifest version khác form được server activate nhưng Agent reject; signed exe-only đúng manifest stage được và exact replace xóa Launcher. Hai biểu hiện cùng contract thiếu, không đếm hai backlog. **Impact:** Release sai version được activate rồi reject; signed exe-only bundle có thể xóa Launcher nhưng Agent vẫn Online.
- **Hướng sửa:** Validate archive/schema/protocol/form version/file hashes và required Agent/Launcher/IPC dependencies ở server lẫn Agent trước activate/switch; parity tests, không chạy uploaded exe để inspect.
- **Rủi ro thay đổi / rollback:** Server validation trước Agent rollout; kiểm binary lịch sử/bundle types trước siết rules. Backup known-good complete bundle và state; readiness Launcher không bắt buộc có desktop user.
- **Regression bắt buộc:** Signed missing/wrong manifest/version/protocol, từng dependency thiếu, binary version mismatch, duplicate/path/hash; active release/refs không đổi khi reject, complete bundle/rollback pass trên net48.

<a id="syn-38"></a>

### SYN-38 — Packager đưa stale Launcher DLL vào manifest mới

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [OTA05-07][r05]; severity/confidence gốc: Medium / High.

- **Root cause:** Copy theo extension từ mutable incremental Launcher output; manifest chỉ hash những gì được copy.
- **Files/symbols:** [scripts/package-agent.ps1:179](../../../scripts/package-agent.ps1#L179) — Launcher MSBuild/copy, releaseFiles; [MProjectLauncher/MProjectLauncher.csproj:27](../../../MProjectLauncher/MProjectLauncher.csproj#L27) — output configuration.
- **Bằng chứng và scenario:** AST smoke seed removed-dependency.dll trong output, copy và manifest đều nhận. Full package lượt R05 chưa build được do cache runtime. **Impact:** Ship dependency thừa/lỗi thời; source không chứng minh bundle production đang có file đó.
- **Hướng sửa:** Launcher build vào fresh isolated output hoặc clean đúng output, đóng gói declared dependencies; giữ version/hash gates.
- **Rủi ro thay đổi / rollback:** Chỉ build path; đối chiếu inventory và test net48 trước ship. Không xóa DLL production theo suy đoán; bundle rollback phải đầy đủ.
- **Regression bắt buộc:** Seed extra dll/config/exe, remove/upgrade reference rồi full clean package; extras không ship, QRCoder/Newtonsoft/IPC đủ và Launcher load.

<a id="syn-39"></a>

### SYN-39 — OTA download body treo giữ heartbeat

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [OTA05-08][r05]; severity/confidence gốc: Medium / High.

- **Root cause:** ResponseHeadersRead body copy không deadline/byte bound riêng, updater được await trong heartbeat tick.
- **Files/symbols:** [MProjectAgent/Services/ServerClient.cs:113](../../../MProjectAgent/Services/ServerClient.cs#L113) — OpenDownloadStreamAsync; [MProjectAgent/Services/AgentUpdater.cs:104](../../../MProjectAgent/Services/AgentUpdater.cs#L104) — DownloadToFileAsync, MaybeStageUpdateAsync; [MProjectAgent/Services/AgentWorker.cs:283](../../../MProjectAgent/Services/AgentWorker.cs#L283) — HeartbeatTickAsync; [MProjectAgent/Program.cs:117](../../../MProjectAgent/Program.cs#L117) — server client timeout.
- **Bằng chứng và scenario:** R05 100ms HttpClient timeout nhưng body vẫn chờ sau 500ms, chỉ service token hủy được. Nâng P1 vì outage control plane không bị giới hạn thời gian khi transport stall. **Impact:** Heartbeat/commands dừng vô hạn khi body stall; poll/inventory khác có thể còn chạy.
- **Hướng sửa:** Deadline/idle timeout và size bound sau headers, cleanup partial/retry; nếu cần worker update single-flight tách heartbeat.
- **Rủi ro thay đổi / rollback:** Giữ jitter/backoff, không parallel apply. Canary slow-network qua proxy; rollback client/config và xác nhận heartbeat/commands phục hồi mà journal không mất.
- **Regression bắt buộc:** Real HTTP headers-fast/body-black-hole, slow/stall/EOF/oversize/cancel; heartbeat hữu hạn/tiếp tục, partial dọn và retry đúng.

<a id="syn-40"></a>

### SYN-40 — Update result bị consume trước server ACK

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [OTA05-09][r05]; severity/confidence gốc: Medium / High.

- **Root cause:** Read-delete trước HTTP, file result ghi trực tiếp không atomic/generation acknowledgement.
- **Files/symbols:** [MProjectAgent/Services/AgentUpdater.cs:39](../../../MProjectAgent/Services/AgentUpdater.cs#L39) — WriteResultFile, ConsumeResult; [MProjectAgent/Services/AgentWorker.cs:238](../../../MProjectAgent/Services/AgentWorker.cs#L238) — HeartbeatTickAsync; [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:651](../../../MProjectBackend/MProject.Application/Services/Assets/AgentService.cs#L651) — UpdateAgentTelemetryAsync; [MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:105](../../../MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs#L105) — GetAdoptionAsync.
- **Bằng chứng và scenario:** R05 consume rolled_back rồi HTTP fail, lần đọc kế tiếp null; JSON partial cũng bị xóa. **Impact:** Applied/rolled_back biến mất sau heartbeat lỗi; server không biết máy đã thử và thất bại.
- **Hướng sửa:** Atomic peek/write và ACK đúng attempt/result generation sau heartbeat accepted; không clear result mới từ response cũ.
- **Rủi ro thay đổi / rollback:** Additive telemetry ID compatible Agent cũ; giữ file pending qua update/downgrade, không xóa để làm adoption xanh.
- **Regression bắt buộc:** Timeout/401/500 trước/sau server commit, restart/partial write, result mới khi request cũ bay; retry idempotent đến ACK.

<a id="syn-41"></a>

### SYN-41 — OTA staging chưa có retention theo reference

**P2 · design risk · Đo dung lượng rồi harden · Scope M.** Evidence: [OTA05-10][r05]; severity/confidence gốc: Medium / High code, Medium capacity.

- **Root cause:** Cleanup cục bộ không reclaim version cũ/orphans theo active attempt và known-good references.
- **Files/symbols:** [MProjectAgent/Services/AgentUpdater.cs:164](../../../MProjectAgent/Services/AgentUpdater.cs#L164) — StageFromFileAsync, GetVersionedStagingDir; [MProjectAgent/Commands/ApplyUpdateCommand.cs:106](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L106) — completion/sibling snapshots; [MProjectAgent/Commands/BootstrapCommand.cs:19](../../../MProjectAgent/Commands/BootstrapCommand.cs#L19) — Prepare, Recover.
- **Bằng chứng và scenario:** Source giữ versions và crash có thể để prepare/next/parked; chưa có fleet capacity/số lần rollout để dự báo thời điểm full. **Impact:** Nhiều releases/crashes tích lũy bytes, có thể hết đĩa cùng volume state/outbox.
- **Hướng sửa:** Retention hữu hạn và headroom preflight, dọn chỉ sau commit theo attempt/reference. Làm sau SYN-35 để biết snapshot nào cần giữ.
- **Rủi ro thay đổi / rollback:** Không xóa staged exe đang chạy hoặc backup unfinished. Reversible retention config; kiểm khôi phục known-good trước bật cleanup.
- **Regression bắt buộc:** Nhiều update/rollback/crash cycles, disk-pressure fixture; directory/bytes về budget, active/bootstrap/rollback source còn đủ.

<a id="syn-42"></a>

### SYN-42 — OTA key rotation/revocation chưa có protocol/runbook

**P2 · design risk · Quyết định + runbook · Scope M.** Evidence: [OTA05-11][r05]; severity/confidence gốc: Medium / High.

- **Root cause:** Một public PEM tĩnh; không KeyId/trust-set transition, OTA preserve config nên key mới trong bundle không cập nhật trust.
- **Files/symbols:** [MProjectAgent/Configuration/AgentOptions.cs:22](../../../MProjectAgent/Configuration/AgentOptions.cs#L22) — ReleasePublicKeyPem; [MProjectAgent/Services/ReleaseSignatureVerifier.cs:7](../../../MProjectAgent/Services/ReleaseSignatureVerifier.cs#L7) — Verify; [MProjectAgent/Commands/ApplyUpdateCommand.cs:248](../../../MProjectAgent/Commands/ApplyUpdateCommand.cs#L248) — config preservation; [MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:337](../../../MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs#L337) — EnsureSignatureTrusted.
- **Bằng chứng và scenario:** R05 xác nhận OTA-002 còn hiện hành. Không có bằng chứng key lộ hoặc RSA verification bypass; tách khỏi SYN-74 private-key distribution defect. **Impact:** Mất/thu hồi key có thể làm kênh OTA không dùng được nếu chưa có recovery ngoài OTA.
- **Hướng sửa:** Chốt owner key/runbook khi key mất hoặc compromised; nếu làm rotation, K1→K1/K2→K2 và offline-station recovery theo acceptance criteria.
- **Rủi ro thay đổi / rollback:** Không thay live key tự động. Chuẩn bị kênh deploy tin cậy ngoài OTA; giữ trust cũ chỉ theo quyết định incident, không rollback về key đã bị thu hồi.
- **Regression bắt buộc:** Transition K1/K2, K2-only, reject K3, station offline trong transition, key cũ không dùng được và manual recovery có kiểm trust.

<a id="syn-43"></a>

### SYN-43 — ProcessSupervisor thiếu serialization toàn lifecycle

**P1 · confirmed defect · Sửa nền lifecycle · Scope M.** Evidence: [L06-01][r06]; severity/confidence gốc: P1–High / High (repro).

- **Root cause:** Lock snapshot/I/O không bao quanh check→stop→state→start→watch; start đọc global state sau await.
- **Files/symbols:** [MProjectAgent/Services/ProcessSupervisor.cs:119](../../../MProjectAgent/Services/ProcessSupervisor.cs#L119) — LaunchAndSuperviseAsync, StartProcessAsync; [MProjectAgent/Services/IpcRequestHandler.cs:177](../../../MProjectAgent/Services/IpcRequestHandler.cs#L177) — GuardActive; [MProjectAgent/Services/IpcServer.cs:83](../../../MProjectAgent/Services/IpcServer.cs#L83) — ExecuteAsync listeners.
- **Bằng chứng và scenario:** R06 barrier store cho Run A/B: marker A không có, B hai lần. Bốn pipe listeners và caller post-deploy đều vào cùng supervisor. **Impact:** Run A/B có thể cùng chạy B; stop/callback cũ tác động generation mới.
- **Hướng sửa:** Một lifecycle gate cho mọi entry point, fixed launch context, expected package/generation cho Stop/Restart, callback guard; thiết kế cancel/join tránh tự deadlock.
- **Rủi ro thay đổi / rollback:** Thay scheduler trạm, phải canary cùng SYN-44/46. Giữ state reader compatible và kiểm stop owned process, restart Agent/recover không duplicate trước downgrade.
- **Regression bắt buộc:** LocalSystem real pipe: Run/Stop/Restart chéo package, old exit callback, post-deploy/IPC/recovery concurrent; một owned process, RAM/disk cùng generation.

<a id="syn-44"></a>

### SYN-44 — Hủy request trong attach để lại process không được quản lý

**P1 · confirmed defect · Sửa cùng lifecycle · Scope M.** Evidence: [L06-02][r06]; severity/confidence gốc: P1–High / High (repro).

- **Root cause:** Process tạo trước attach; canceled token dùng tiếp cho ownership Save, không cleanup/commit độc lập sau OS side effect.
- **Files/symbols:** [MProjectAgent/Services/ProcessSupervisor.cs:198](../../../MProjectAgent/Services/ProcessSupervisor.cs#L198) — StartProcessAsync, TryAttachWatchProcessAsync; [MProjectAgent/Configuration/AgentOptions.cs:224](../../../MProjectAgent/Configuration/AgentOptions.cs#L224) — attach timeout; [MProjectAgent.Ipc.Contracts/IpcContracts.cs:24](../../../MProjectAgent.Ipc.Contracts/IpcContracts.cs#L24) — GetServerOperationTimeout.
- **Bằng chứng và scenario:** R06 cancel 1s tái hiện process sống, watcher complete, disk không PID; core IPC 7s ngắn hơn attach mặc định 30s. **Impact:** Wrapper sống nhưng thiếu watcher/durable PID; Agent restart có thể chạy thêm.
- **Hướng sửa:** Sau launch nếu request cancel phải thu hồi process hoặc commit ownership bằng cleanup budget riêng và trả typed pending/outcome; bảo vệ cả wrapper/child.
- **Rủi ro thay đổi / rollback:** Không chỉ tăng timeout. Test cleanup bảo đảm không kill process ngoài ownership; backup runtime state, canary wrapper chậm và recovery của Agent cũ.
- **Regression bắt buộc:** Cancel trước create/trong attach/trước save, persist fail; real pipe production timeout, wrapper sinh child chậm; không orphan trong RAM/disk/watch.

<a id="syn-45"></a>

### SYN-45 — HTTP health timeout kết thúc toàn bộ probe loop

**P1 · confirmed defect · Sửa nhanh có station test · Scope S.** Evidence: [L06-03][r06]; severity/confidence gốc: P1–High / High (repro).

- **Root cause:** OperationCanceledException từ per-request timeout bị coi là supervisor cancellation.
- **Files/symbols:** [MProjectAgent/Services/ProcessSupervisor.cs:283](../../../MProjectAgent/Services/ProcessSupervisor.cs#L283) — ProbeAsync.
- **Bằng chứng và scenario:** R06 HttpClient timeout 1s, threshold 3: loop thoát sau một request dù supervisor token chưa hủy. **Impact:** Ứng dụng treo vẫn Running; threshold/auto-restart ngừng hoạt động.
- **Hướng sửa:** Chỉ exit khi lifetime token hủy; timeout request là failure để áp interval/threshold.
- **Rủi ro thay đổi / rollback:** Có thể kích hoạt auto-restart trước đây bị bỏ sót; canary threshold với app fixture và điều kiện ca sản xuất, không thử kill trên payload thật.
- **Regression bắt buộc:** HTTP black-hole đủ threshold HealthCheckFailed; timeout rồi success reset; supervisor cancel không tăng lỗi/kill; LocalSystem process fixture.

<a id="syn-46"></a>

### SYN-46 — Supervised process identity không gắn với actual launch

**P1 · confirmed defect · Sửa identity chung · Scope M.** Evidence: [L06-04][r06] + [L06-07][r06]; severity/confidence gốc: P1–High + P2–Medium / High.

- **Root cause:** Desired watch path được dùng thay actual owned PID/exe/session/start-time/generation; không có identity contract xuyên launch→recovery.
- **Files/symbols:** [MProjectAgent/Services/ProcessSupervisor.cs:218](../../../MProjectAgent/Services/ProcessSupervisor.cs#L218) — TryAttachWatchProcessAsync, FindProcessByMainModule, WatchTargetPath, IsAlive, RecoverAsync; [MProjectAgent/Models/ProcessSupervisorModels.cs:28](../../../MProjectAgent/Models/ProcessSupervisorModels.cs#L28) — SupervisedProcessState.
- **Bằng chứng và scenario:** R06 repro attach và kill PID có trước cùng path; source fallback supervise wrapper nhưng expected path vẫn child. Gộp hai biểu hiện cùng thiếu actual process identity; cross-session chưa runtime. **Impact:** Attach/Stop nhầm process có trước; fallback wrapper bị coi chết và có thể bị launch trùng.
- **Hướng sửa:** Lưu actual identity và desired watch riêng, bind session/start/generation/ownership; reject ambiguity, chốt policy adopt-existing rõ.
- **Rủi ro thay đổi / rollback:** Runtime schema cần đọc được legacy state. Không kill theo basename hoặc tự adopt PID cũ khi thiếu evidence; canary recovery/stop trước đổi fleet.
- **Regression bắt buộc:** LocalSystem multi-session cùng path, preexisting/two matches, slow child, fallback wrapper/restart Agent, PID reuse; chỉ owned process bị stop, status/recovery đúng.

<a id="syn-47"></a>

### SYN-47 — Launcher session cũ ngăn UI của console mới

**P2 · confirmed defect · Sửa · Scope S.** Evidence: [L06-05][r06]; severity/confidence gốc: P2–Medium / High (source).

- **Root cause:** Bất kỳ desktop process cùng basename được coi đủ; show flag theo service run, không active session/path.
- **Files/symbols:** [MProjectAgent/Services/LauncherBootstrapper.cs:38](../../../MProjectAgent/Services/LauncherBootstrapper.cs#L38) — EnsureRunning, IsRunningOnADesktop, _showRequestedForServiceRun.
- **Bằng chứng và scenario:** Fast user switching A→B; A còn process nhưng không đủ active-console IPC. Chưa diễn tập desktop thật trong R06. **Impact:** User B không có Launcher khi process user A còn ở session cũ.
- **Hướng sửa:** So active console + canonical executable, theo dõi show theo session; chỉ xử lý process verified của bundle.
- **Rủi ro thay đổi / rollback:** Giữ active-console authorization, không mở quyền cho session cũ. Canary switch users, quay lại old Agent/bundle phải còn đúng một Launcher ở console.
- **Regression bắt buộc:** LocalSystem A→B/logoff-logon/disconnected RDP/session0, basename khác path; đúng một Launcher active, session cũ không điều khiển.

<a id="syn-48"></a>

### SYN-48 — Launcher queue nhiều Run cho một thao tác lặp

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [L06-06][r06]; severity/confidence gốc: P2–Medium / High (source).

- **Root cause:** MarkRunRequested không disable ngay; semaphore chỉ xếp hàng, không dedup intent.
- **Files/symbols:** [MProjectLauncher/ViewModels.cs:92](../../../MProjectLauncher/ViewModels.cs#L92) — MarkRunRequested, MainViewModel.Run; [MProjectLauncher/IpcClient.cs:63](../../../MProjectLauncher/IpcClient.cs#L63) — SendAsync gate; [MProjectLauncher/MainWindow.xaml:189](../../../MProjectLauncher/MainWindow.xaml#L189) — Run action.
- **Bằng chứng và scenario:** Run thứ hai chờ first response rồi supervisor StopCurrent/start lại. Khác SYN-02 cùng CommandId được giao lại qua mạng. **Impact:** Double-click khi chờ response có thể restart app vừa mở.
- **Hướng sửa:** Pending/disable đồng bộ trước await, release finally/reconcile; bỏ duplicate Run trong queue, cân nhắc gộp refresh.
- **Rủi ro thay đổi / rollback:** UI-only nhưng net48 behavior phải kiểm. Timeout cho retry có chủ ý; không khóa vĩnh viễn khi pipe mất.
- **Regression bắt buộc:** WPF/fake delayed IPC double Run chỉ một request; error/timeout mở retry, refresh queue không đẩy action vào stale state.

<a id="syn-49"></a>

### SYN-49 — Icon lớn làm mất core status và mutation result

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [L06-08][r06]; severity/confidence gốc: P2–Medium / High (source).

- **Root cause:** Load/cache base64 unbounded trước wire bound; không degrade icon độc lập core status.
- **Files/symbols:** [MProjectAgent/Services/AgentStatusProvider.cs:105](../../../MProjectAgent/Services/AgentStatusProvider.cs#L105) — GetIcon, LoadIconBase64; [MProjectAgent/Services/IpcServer.cs:216](../../../MProjectAgent/Services/IpcServer.cs#L216) — WriteResponseAsync; [MProjectLauncher/ViewModels.cs:484](../../../MProjectLauncher/ViewModels.cs#L484) — Apply.
- **Bằng chứng và scenario:** Một BMP 4MiB sau base64 vượt frame budget; chưa có desktop fixture, source đủ chỉ ra size failure. **Impact:** Optional image vượt 4 MiB envelope, UI giữ card cũ và có thể retry action đã chạy.
- **Hướng sửa:** Input/dimensions/thumbnail và tổng byte/cache budget, bỏ icon khi hết budget; status lỗi đánh stale/action state, giữ frame limits.
- **Rủi ro thay đổi / rollback:** Không tăng frame cap để chữa. Bundle canary nhiều icons; cache clear được, core receipt không phụ thuộc image và rollback không cần data migration.
- **Regression bắt buộc:** Một ảnh lớn/tổng nhiều icons/nhiều versions, real pipe bound; core state và command result nhận được, UI không giữ availability giả.

<a id="syn-50"></a>

### SYN-50 — IPC Stop/Restart bỏ outcome false và báo success

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [L06-09][r06]; severity/confidence gốc: P2–Medium / High (source).

- **Root cause:** Bool result supervisor bị handler bỏ; success message không dựa postcondition.
- **Files/symbols:** [MProjectAgent/Services/IpcRequestHandler.cs:161](../../../MProjectAgent/Services/IpcRequestHandler.cs#L161) — StopAsync, RestartAsync; [MProjectAgent/Services/ProcessSupervisor.cs:417](../../../MProjectAgent/Services/ProcessSupervisor.cs#L417) — RequestStopAsync, RequestRestartAsync.
- **Bằng chứng và scenario:** OS kill denied, supervisor false; WithStatus trả Ok=true/stopped nếu status read thành công. Không khẳng định mọi timeout đều báo success. **Impact:** Operator tưởng process đã dừng dù kill lỗi và PID còn sống.
- **Hướng sửa:** Typed stopped/already-stopped/failed/canceled/unsupported; chỉ success khi postcondition đúng, thêm code compatible legacy ok/message.
- **Rủi ro thay đổi / rollback:** Contract additive, test Launcher cũ. Không tự tăng quyền kill hoặc nuốt lỗi để xanh; canary restore/restart kiểm process thật.
- **Regression bắt buộc:** Fault kill denied, already-stopped idempotent, disabled/canceled restart; actual PID/status và IPC outcome phải khớp.

<a id="syn-51"></a>

### SYN-51 — Support storage lỗi có thể dừng core Agent

**P1 · confirmed defect · Sửa trước rollout support/OTA · Scope M.** Evidence: [R07-01][r07]; severity/confidence gốc: P1 / High.

- **Root cause:** Initialize nằm ngoài worker catch; core status await support snapshot không có error boundary.
- **Files/symbols:** [MProjectAgent/Support/ProductionSupportDeliveryWorker.cs:27](../../../MProjectAgent/Support/ProductionSupportDeliveryWorker.cs#L27) — ExecuteAsync; [MProjectAgent/Support/ProductionSupportDeliveryService.cs:84](../../../MProjectAgent/Support/ProductionSupportDeliveryService.cs#L84) — InitializeAsync; [MProjectAgent/Support/SupportOutboxStore.cs:573](../../../MProjectAgent/Support/SupportOutboxStore.cs#L573) — EnsureInitializedLockedAsync; [MProjectAgent/Program.cs:141](../../../MProjectAgent/Program.cs#L141) — hosted workers; [MProjectAgent/Services/AgentStatusProvider.cs:72](../../../MProjectAgent/Services/AgentStatusProvider.cs#L72) — GetStatusAsync.
- **Bằng chứng và scenario:** R07 SQLite user_version=999 làm exception thoát worker. Chưa full-host LocalSystem nhưng composition xác nhận propagation; rollback về schema cũ là điều kiện thực tế. **Impact:** Outbox schema/corruption riêng support làm heartbeat/poll/IPC không hoạt động.
- **Hướng sửa:** Support init/runtime retry/pause riêng, core status trả support-unavailable; giữ DB nguyên và fail closed với schema không hiểu.
- **Rủi ro thay đổi / rollback:** Không delete/recreate/downgrade outbox. Snapshot WAL-aware bằng cơ chế backup phù hợp; thử binary rollback với schema mới, core vẫn healthy và queue không ACK sai.
- **Regression bắt buộc:** Host LocalSystem outbox corrupt/future schema/transient open và snapshot failure; heartbeat/core IPC sống, support không ACK khi chưa commit, phục hồi storage tự resume.

<a id="syn-52"></a>

### SYN-52 — Support update từ dialog cũ nhắm request mới

**P1 · confirmed defect · Sửa identity contract · Scope M.** Evidence: [R07-02][r07]; severity/confidence gốc: P1 / High service; WPF chưa runtime.

- **Root cause:** Dialog có snapshot A nhưng DTO chỉ employee/status/note; Agent chọn current ID ở thời điểm enqueue.
- **Files/symbols:** [MProjectAgent.Ipc.Contracts/ProductionSupportIpcContracts.cs:139](../../../MProjectAgent.Ipc.Contracts/ProductionSupportIpcContracts.cs#L139) — SupportUpdateRequestDto; [MProjectLauncher/SupportUpdateDialog.xaml.cs:39](../../../MProjectLauncher/SupportUpdateDialog.xaml.cs#L39) — Submit_Click; [MProjectLauncher/ViewModels.cs:706](../../../MProjectLauncher/ViewModels.cs#L706) — support update; [MProjectAgent/Support/ProductionSupportAgentService.cs:230](../../../MProjectAgent/Support/ProductionSupportAgentService.cs#L230) — UpdateAsync.
- **Bằng chứng và scenario:** R07 stale-modal reconcile A closed, B current rồi submit DTO cũ: receipt canonical B. Server kiểm B current đúng nên không bắt được sai intent. **Impact:** Có thể resolve incident B bằng nội dung người dùng chuẩn bị cho A.
- **Hướng sửa:** Expected local request ID từ dialog, atomically compare trong enqueue; typed stale-target error. Version/capability tương thích, canonical-only không đủ offline.
- **Rủi ro thay đổi / rollback:** Nâng receiver rồi Launcher hoặc feature capability gate; old callers không bị âm thầm redirect. Preserve graph IDs và thử downgrade core IPC/support pause an toàn.
- **Regression bắt buộc:** Real pipe/WPF hai callers: dialog A, force-resolve A, B current; submit A deny, B không đổi; cả local chưa map canonical.

<a id="syn-53"></a>

### SYN-53 — Correction bỏ sót failed Resolved và failed predecessor

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [R07-03][r07]; severity/confidence gốc: P1 / High (SQLite probes).

- **Root cause:** HasUnconfirmedResolved tính NeedsUserAction và service chỉ sửa tail; primitive supersede/relink không được gọi cho node đang chặn.
- **Files/symbols:** [MProjectAgent/Support/ProductionSupportAgentService.cs:239](../../../MProjectAgent/Support/ProductionSupportAgentService.cs#L239) — UpdateAsync; [MProjectAgent/Support/SupportOutboxContract.cs:327](../../../MProjectAgent/Support/SupportOutboxContract.cs#L327) — HasUnconfirmedResolved, FindActiveTail; [MProjectAgent/Support/SupportOutboxStore.cs:457](../../../MProjectAgent/Support/SupportOutboxStore.cs#L457) — SupersedeAndEnqueueReplacementAsync.
- **Bằng chứng và scenario:** R07 failed-resolved nhập đúng vẫn InvalidArgument; failed predecessor có Pending successor khiến correction thêm node mới. Hạ P2 vì support bị kẹt, chưa chứng minh core station dừng. **Impact:** Sửa employee không giải phóng queue; tạo thêm successors vẫn mắc dependency.
- **Hướng sửa:** Chọn đúng failed node gồm Resolved, supersede/relink transaction hiện có; giữ immutable old payload/ID và ID mới cho corrected data.
- **Rủi ro thay đổi / rollback:** Snapshot graph trước conversion; không sửa payload cũ tại chỗ hoặc xóa descendants. Compatibility/canary offline queue, restart kiểm graph trước rollback.
- **Regression bắt buộc:** SQLite real failed Resolved→correct→server resolve một lần; failed ancestor nhiều descendants→relink→restart; PostgreSQL replay giữ một logical update.

<a id="syn-54"></a>

### SYN-54 — Reconcile reset ClosedAt làm retention không bao giờ hết

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [R07-04][r07]; severity/confidence gốc: P1 / High (SQLite + clock).

- **Root cause:** ClosedAt được ghi lại mỗi absent/resolved reconcile thay vì chỉ ở transition closed; reconcile chạy trước prune.
- **Files/symbols:** [MProjectAgent/Support/SupportOutboxRuntime.cs:219](../../../MProjectAgent/Support/SupportOutboxRuntime.cs#L219) — ApplyReconcileAsync, CloseRequestAsync, PruneClosedTerminalGraphsAsync; [MProjectAgent/Support/ProductionSupportDeliveryService.cs:84](../../../MProjectAgent/Support/ProductionSupportDeliveryService.cs#L84) — InitializeAsync; [MProjectAgent/Support/SupportOutboxStore.cs:713](../../../MProjectAgent/Support/SupportOutboxStore.cs#L713) — EnsureCapacityAsync.
- **Bằng chứng và scenario:** R07 graph đóng 31 ngày vẫn còn sau initialize; control prune trực tiếp thì xóa. Hạ P2 vì tích lũy theo thời gian, không phải mọi startup mất core Agent. **Impact:** Lịch sử chạm 1000 operations/20MiB và chặn report/update mới.
- **Hướng sửa:** Giữ closure timestamp khi đã closed, chỉ clear khi reopen thật; bảo đảm queue full còn có đường prune terminal.
- **Rủi ro thay đổi / rollback:** Chỉ đổi lifecycle timestamp, không purge pending/NeedsUserAction. Dữ liệu closure lịch sử đã bị reset cần policy phục hồi tuổi, không bịa mốc để xóa.
- **Regression bắt buộc:** Chạy cycle qua 30 ngày, restart sau31 ngày, repeated absent/resolved; terminal prune đúng hạn, capacity reuse, pending graph nguyên.

<a id="syn-55"></a>

### SYN-55 — Update của request đã đóng retry không hội tụ

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [R07-05][r07]; severity/confidence gốc: P2 / High.

- **Root cause:** Reconcile đóng current không terminalize graph; RequestNotCurrent luôn retry, claim không loại closed graph.
- **Files/symbols:** [MProjectAgent/Support/SupportOutboxRuntime.cs:219](../../../MProjectAgent/Support/SupportOutboxRuntime.cs#L219) — ApplyReconcileAsync; [MProjectAgent/Support/ProductionSupportDeliveryService.cs:309](../../../MProjectAgent/Support/ProductionSupportDeliveryService.cs#L309) — HandleFailureAsync; [MProjectAgent/Support/SupportOutboxStore.cs:344](../../../MProjectAgent/Support/SupportOutboxStore.cs#L344) — TryClaimNextAsync; [MProjectBackend/MProject.Application/Services/Support/ProductionSupportService.cs:310](../../../MProjectBackend/MProject.Application/Services/Support/ProductionSupportService.cs#L310) — UpdateInTransactionAsync.
- **Bằng chứng và scenario:** Offline update, admin force-resolve trước POST; R07 hai cycles cùng RetryScheduled/current null. Khác retention clock của SYN-54. **Impact:** Queue/badge tồn đọng, retries và capacity tăng dù không còn current để sửa.
- **Hướng sửa:** Replay bằng operation ID trước để nhận diện đã commit; chỉ khi xác minh không có receipt và request không current mới terminalize/supersede descendants có lý do.
- **Rủi ro thay đổi / rollback:** Không coi GET absent là Delivered. Giữ exact payload/IDs và old receipts, schema/state changes phải compatible; snapshot queue trước data repair.
- **Regression bắt buộc:** Force-resolve offline update hội tụ; control Resolved commit rồi mất response phải replay đúng, A closed/B current không đổi target.

<a id="syn-56"></a>

### SYN-56 — InFlight kẹt nếu delivery và ghi retry cùng lỗi

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [R07-06][r07]; severity/confidence gốc: P2 / High (SQLite fault).

- **Root cause:** Claim không lease/owner recovery; retry-write fail chỉ log, InFlight reset chỉ ở initialization một lần.
- **Files/symbols:** [MProjectAgent/Support/ProductionSupportDeliveryService.cs:144](../../../MProjectAgent/Support/ProductionSupportDeliveryService.cs#L144) — RecoverUnexpectedFailureAsync; [MProjectAgent/Support/SupportOutboxStore.cs:344](../../../MProjectAgent/Support/SupportOutboxStore.cs#L344) — TryClaimNextAsync, EnsureInitializedLockedAsync; [MProjectAgent/Support/SupportOutboxContract.cs:202](../../../MProjectAgent/Support/SupportOutboxContract.cs#L202) — CanDispatch.
- **Bằng chứng và scenario:** R07 trigger chặn state update khi InFlight, network+recovery lỗi; bỏ trigger, tăng clock2 ngày vẫn không dispatch. **Impact:** Operation/descendants đứng đến restart dù storage đã phục hồi.
- **Hướng sửa:** Lease/reclaim có ownership hoặc retry local completion sau network call; giữ operation ID/payload, không reset tất cả InFlight mỗi cycle.
- **Rủi ro thay đổi / rollback:** Migration additive/reader compatibility; không reclaim operation còn gửi. Snapshot queue và chứng minh restart old/new không tạo duplicate logical record.
- **Regression bắt buộc:** SQLite disk/lock/commit fault + HTTP error, phục hồi không restart; auto retry/replay đúng; active owner không bị reclaim, restart giữ attempt count.

<a id="syn-57"></a>

### SYN-57 — Browser giữ dữ liệu support của scope cũ sau purge

**P2 · confirmed defect · Sửa; phối hợp 09 · Scope M.** Evidence: [R07-07][r07]; severity/confidence gốc: P2 / High (React observer probes).

- **Root cause:** Cache removal không clear active observer result; query identity chỉ userId, thiếu auth/scope generation.
- **Files/symbols:** [MProjectFrontend/src/components/auth/SupportQueryIsolation.tsx:17](../../../MProjectFrontend/src/components/auth/SupportQueryIsolation.tsx#L17) — purgeSupportQueries, currentIdentity; [MProjectFrontend/src/hooks/api/support/use-production-support.ts:12](../../../MProjectFrontend/src/hooks/api/support/use-production-support.ts#L12) — supportQueryKeys, useSupportList.
- **Bằng chứng và scenario:** Token event hoặc same user đổi teams: QueryCache rỗng nhưng DOM còn old rows, không refetch ngay. Timer có thể cập nhật sau, không khẳng định tồn tại vĩnh viễn. **Impact:** Dữ liệu team cũ còn hiển thị tại boundary đổi quyền; không chứng minh server bypass.
- **Hướng sửa:** Auth/scope generation hoặc remount/gate view, clear previous data tại boundary, cancel/ignore stale responses; giữ paging cùng scope.
- **Rủi ro thay đổi / rollback:** Không mở rộng backend permissions ngoài authority contract SYN-84/85/92. Server auth vẫn nguồn quyền; browser logout/team-switch canary và compatible query reset.
- **Regression bắt buộc:** Browser JWT refresh/scope đổi, logout-login khác user, mounted list/detail/facets/recovery; DOM clear ngay, late response bỏ, request mới dùng credential hiện tại.

<a id="syn-58"></a>

### SYN-58 — Support list load lỗi được hiển thị như empty success

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [R07-08][r07]; severity/confidence gốc: P2 / High (React repro).

- **Root cause:** Error alert chỉ khi rows.length>0, thiếu initial-error và empty-snapshot stale states.
- **Files/symbols:** [MProjectFrontend/src/pages/SupportRequests/SupportRequests.tsx:119](../../../MProjectFrontend/src/pages/SupportRequests/SupportRequests.tsx#L119) — SupportRequestsPage, stale, initialLoading, Table.locale.emptyText; [MProjectFrontend/src/locales/en.ts:1967](../../../MProjectFrontend/src/locales/en.ts#L1967) — empty/error messages.
- **Bằng chứng và scenario:** R07 first load error cho no matching requests, không alert; empty success rồi poll fail cũng vậy. **Impact:** Operator tưởng không có yêu cầu khi API 503/timeout.
- **Hướng sửa:** Error/retry riêng khi chưa load success; stale cả khi snapshot rỗng, empty chỉ cho successful zero; đồng bộ ba locales.
- **Rủi ro thay đổi / rollback:** UI-only, không data mutation; không đổi retry thành action recovery có side effect.
- **Regression bắt buộc:** Initial503/timeout, empty-success→fail, data→fail, retry→zero; DOM phân biệt rõ unavailable/empty/stale.

<a id="syn-59"></a>

### SYN-59 — Sync status phản ánh từng thành công thay vì freshness hiện tại

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [R07-09][r07]; severity/confidence gốc: P2 / High.

- **Root cause:** Có LastReconciledAt là đủ, không xét lỗi mới hơn/freshness; core reachability không đại diện support health.
- **Files/symbols:** [MProjectAgent/Support/ProductionSupportAgentService.cs:355](../../../MProjectAgent/Support/ProductionSupportAgentService.cs#L355) — GetStatusAsync; [MProjectAgent/Support/SupportOutboxRuntime.cs:402](../../../MProjectAgent/Support/SupportOutboxRuntime.cs#L402) — RecordReconcileFailureAsync; [MProjectAgent/Services/AgentWorker.cs:292](../../../MProjectAgent/Services/AgentWorker.cs#L292) — BuildProductionSupportTelemetryAsync; [MProjectAgent/Services/AgentStatusProvider.cs:82](../../../MProjectAgent/Services/AgentStatusProvider.cs#L82) — support state mapping; [MProjectLauncher/SupportUiPolicy.cs:39](../../../MProjectLauncher/SupportUiPolicy.cs#L39) — BuildState.
- **Bằng chứng và scenario:** R07 success rồi TemporarilyUnavailable: Synced + safe error. Heartbeat vẫn healthy nên Offline fallback không chữa. **Impact:** Queue rỗng báo Synced dù support-only API lỗi và current snapshot cũ.
- **Hướng sửa:** Latest reconcile result/freshness riêng delivery status, map typed Unknown/stale tương thích; không chặn durable enqueue chỉ vì remote unavailable.
- **Rủi ro thay đổi / rollback:** Ưu tiên enum/status đã compatible; nếu thêm wire value cần version/capability. Giữ pending graph và kiểm old/new Launcher render.
- **Regression bắt buộc:** Success→support503/401/version mismatch, core healthy/queue empty, current null/active; không Synced, recovery success phục hồi đúng.

<a id="syn-60"></a>

### SYN-60 — Global JSON converter vô hiệu strict support enum contract

**P2 · confirmed defect · Sửa contract có HTTP test · Scope S.** Evidence: [ARC00-01][r00]; severity/confidence gốc: Medium / High.

- **Root cause:** Broad converter ở options thắng type-level strict converter; strict reader Enum.TryParse cũng nhận numeric strings/whitespace.
- **Files/symbols:** [MProjectBackend/MProject.Api/Program.cs:408](../../../MProjectBackend/MProject.Api/Program.cs#L408) — AddControllers().AddJsonOptions; [MProject.ProductionSupport.Contracts/StrictStringEnumConverter.cs:13](../../../MProject.ProductionSupport.Contracts/StrictStringEnumConverter.cs#L13) — StrictStringEnumConverter<TEnum>.Read; [MProject.ProductionSupport.Contracts/ProductionSupportEnums.cs:5](../../../MProject.ProductionSupport.Contracts/ProductionSupportEnums.cs#L5) — type-level converters; [MProjectBackend/MProject.Tests/ProductionSupportContractTests.cs:27](../../../MProjectBackend/MProject.Tests/ProductionSupportContractTests.cs#L27) — WireEnums_AreExactStringsAndRejectNumericOrWrongCaseValues; [MProjectAgent/Support/ProductionSupportApiClient.cs:68](../../../MProjectAgent/Support/ProductionSupportApiClient.cs#L68) — JSON options.
- **Bằng chứng và scenario:** R00 net8 probe nhận 2/resolved ở API options; strict-only vẫn nhận chuỗi số/whitespace. R07 không có production MVC negative matrix để bác bỏ; đối chiếu hẹp source vẫn giữ converters đó. **Impact:** API/Agent chấp nhận wire values trái fixtures, dễ lệch compatibility; chưa chứng minh bỏ state/permission checks.
- **Hướng sửa:** Exact enum-name membership và strict registration precedence cho support; giữ semantics legacy enums ngoài support, Agent options tương ứng.
- **Rủi ro thay đổi / rollback:** Client lỗi có thể bị reject sau fix: test deployed compatibility, không global-change mọi enum. Rollback serializer không được làm mất state/permission validation.
- **Regression bắt buộc:** Production MVC HTTP create/update + Agent parsing: số/chuỗi số/wrong-case/whitespace/unknown reject, valid roundtrip; legacy APIs giữ contract.

<a id="syn-61"></a>

### SYN-61 — Support docs còn ghi current Stage-0/API v1

**P3 · maintainability issue · Cập nhật tài liệu · Scope S.** Evidence: [ARC00-04][r00]; severity/confidence gốc: Low / High.

- **Root cause:** Milestone lịch sử còn mô tả current build dù DI quảng bá support v2.
- **Files/symbols:** [MProject.ProductionSupport.Contracts/COMPATIBILITY_CONTRACT.txt:39](../../../MProject.ProductionSupport.Contracts/COMPATIBILITY_CONTRACT.txt#L39) — current Stage-0 wording; [MProject.ProductionSupport.Contracts/CONTRACT.txt:1](../../../MProject.ProductionSupport.Contracts/CONTRACT.txt#L1) — Agent API version label; [MProjectAgent/Program.cs:104](../../../MProjectAgent/Program.cs#L104) — RunAgentAsync support composition; [MProjectAgent/Services/IpcRequestHandler.cs:46](../../../MProjectAgent/Services/IpcRequestHandler.cs#L46) — GetCapabilities.
- **Bằng chứng và scenario:** R00/R07 cùng xác nhận implementation support thật và report.v2; matrix version v1 riêng vẫn đúng, không cần đổi theo API version. **Impact:** Người đọc đánh giá sai capabilities/rollout; chưa có bằng chứng deployment sai.
- **Hướng sửa:** Gắn historical baseline, ghi runtime capabilities/API v2 và gate pilot chưa được chứng nhận; phân biệt OTA protocol v1.
- **Rủi ro thay đổi / rollback:** Docs-only, không đổi capabilities để khớp prose cũ. Không cần production regression mới chỉ để sửa chữ.
- **Regression bắt buộc:** Đối chiếu composition/version manifest và compatibility tests sẵn có nếu contract đổi; link/doc validation cho sửa prose.

<a id="syn-62"></a>

### SYN-62 — Test Monitor không tự lấy dữ liệu mới

**P1 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [ING-01][r08]; severity/confidence gốc: High/P1 / High.

- **Root cause:** Effect theo load dependencies là trigger duy nhất, không polling/focus refresh.
- **Files/symbols:** [MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx:95](../../../MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx#L95) — load, load effect.
- **Bằng chứng và scenario:** R08 React probe10 phút không gọi lại API. Không phải lỗi parser/ingestion vì server có thể đã nhận đủ dữ liệu. **Impact:** Màn mở cả ca có thể không thấy FAIL mới hoặc chuyển ca cho đến tương tác/reload.
- **Hướng sửa:** Polling cleanup/single-flight và focus refresh, last-updated/stale/error; triển khai cùng SYN-64 để không tạo race mới.
- **Rủi ro thay đổi / rollback:** UI-only; polling có budget để tránh tải server, tắt được nếu lỗi. Giữ filter/shift semantics.
- **Regression bắt buộc:** Fake timer thêm FAIL và qua shift không tương tác, focus/unmount; browser smoke stale/error, không requests chồng và sau unmount.

<a id="syn-63"></a>

### SYN-63 — Late logs hết overlap trước khi replay có capacity

**P1 · confirmed defect · Sửa · Scope M.** Evidence: [ING-02][r08]; severity/confidence gốc: High/P1 / High (filesystem probe).

- **Root cause:** Replay chỉ dùng capacity dư, cutoff theo main cursor tiến lên khi replay chưa drain.
- **Files/symbols:** [MProjectAgent/Services/TestResultScanner.cs:110](../../../MProjectAgent/Services/TestResultScanner.cs#L110) — CollectBatch.
- **Bằng chứng và scenario:** R08 batch2 bốn cycles fresh mỗi5 phút, late file trước cursor không gửi và đã ngoài cutoff khi idle. Khác backlog cùng timestamp đã sửa ở LOG-001. **Impact:** Có thể mất FAIL khỏi monitoring khi fresh batches liên tục đầy.
- **Hướng sửa:** Fair capacity fresh/replay hoặc durable pending trước cutoff, bounded reconciliation/backfill; không làm fresh starvation trở lại.
- **Rủi ro thay đổi / rollback:** Giữ compatibility watermark/cursors, backup cursor trước canary; backfill đúng authenticated computer và dedup, không reset toàn fleet gây upload storm.
- **Regression bắt buộc:** Filesystem/Agent restart/network interruption: full batches vượt overlap, late arrival, restart giữa cycle; mọi file trong promised window accepted một lần nghiệp vụ.

<a id="syn-64"></a>

### SYN-64 — Response filter cũ ghi đè Test Monitor mới

**P2 · confirmed defect · Sửa nhanh cùng polling · Scope S.** Evidence: [ING-03][r08]; severity/confidence gốc: Medium/P2 / High (React repro).

- **Root cause:** Completion setters không bind query generation; panel reject giữ previous state không có query identity.
- **Files/symbols:** [MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx:95](../../../MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx#L95) — load, effect.
- **Bằng chứng và scenario:** R08 Sfis slow, Golden trả trước, Sfis ghi sau dưới dropdown Golden. Khác SYN-28 gây mutation nhầm target, ở đây là read-only. **Impact:** Golden/Sfis hoặc station filter một đằng nhưng panels hiển thị dữ liệu khác/trộn.
- **Hướng sửa:** Latest generation/abort cũ; panel failed của query mới phải unavailable hoặc stale gắn đúng query.
- **Rủi ro thay đổi / rollback:** UI-only, không đổi server filters. Khi thêm polling SYN-62 phải dùng cùng guard để khỏi nhân race.
- **Regression bắt buộc:** Deferred filters đảo response, một panel reject, đổi ca/unmount/focus; mọi panel cùng query hoặc unavailable rõ.

<a id="syn-65"></a>

### SYN-65 — Basename dedup gộp các source chưa có business identity contract

**P2 · design risk · Quyết định identity trước schema · Scope M.** Evidence: [ING-04][r08]; severity/confidence gốc: Medium/P2 / High code, Medium semantics.

- **Root cause:** ComputerId+FileName là unique identity mọi source, Source metadata bị cố định bởi lần insert đầu.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:957](../../../MProjectBackend/MProject.Application/Services/Assets/AgentService.cs#L957) — ReportTestResultsAsync; [MProjectBackend/MProject.Infrastructure/DBContext.cs:240](../../../MProjectBackend/MProject.Infrastructure/DBContext.cs#L240) — TestResultRecord unique index; [MProjectBackend/MProject.Application/Services/Support/ProductionSupportService.cs:702](../../../MProjectBackend/MProject.Application/Services/Support/ProductionSupportService.cs#L702) — Sfis basename lookup.
- **Bằng chứng và scenario:** R08 probe NoSfc rồi Sfis cùng filename: second duplicates, chỉ NoSfc. Chưa biết copy có nghĩa cùng event hay event độc lập ở factory. **Impact:** NoSfc đến trước có thể làm Sfis mất row và support link null.
- **Hướng sửa:** Nếu cùng event: canonical source/provenance; nếu độc lập: mở identity kèm migration. Không tự thêm Source làm double count copies.
- **Rủi ro thay đổi / rollback:** Data migration có backfill/duplicate policy, snapshot và reversible mapping; giữ IDs/link support, không xóa records mơ hồ.
- **Regression bắt buộc:** Ba sources mọi thứ tự, same basename khác context, sequential/concurrent retry trên PostgreSQL và support nullable link theo semantics.

<a id="syn-66"></a>

### SYN-66 — Metrics snapshot cũ rollback counters mới

**P2 · confirmed defect · Sửa · Scope M.** Evidence: [ING-05][r08]; severity/confidence gốc: Medium/P2 / High (service probe).

- **Root cause:** Upsert không compare snapshot ordering/version trong cùng update.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:905](../../../MProjectBackend/MProject.Application/Services/Assets/AgentService.cs#L905) — ReportTestMetricsAsync.
- **Bằng chứng và scenario:** R08 gửi count200 tại T rồi100 tại T−5phút, stored100 và timestamp lùi. Loop thường tuần tự nhưng request cũ delayed/restart tạo điều kiện. **Impact:** Computer detail số đếm lùi hoặc lấy lại value trước reset; không đổi Test Monitor log totals.
- **Hướng sửa:** Sequence/epoch hoặc freshness contract với atomic compare-update; không max(counter) vì reset mới hợp lệ có thể giảm.
- **Rủi ro thay đổi / rollback:** Schema/API additions compatible Agent cũ; giữ latest/source provenance, không tự sửa lịch sử counter bằng suy đoán.
- **Regression bắt buộc:** PostgreSQL two contexts mới→cũ, completion đảo, equal retry, reset epoch mới giảm count, Agent restart/clock behavior theo contract.

<a id="syn-67"></a>

### SYN-67 — TestedAt dùng mtime nhưng chưa rõ event-time contract

**P2 · design risk · Quyết định nguồn thời gian · Scope M.** Evidence: [ING-06][r08]; severity/confidence gốc: Medium/P2 / High mapping, Medium semantics.

- **Root cause:** Mtime vừa dùng discovery vừa làm business TestedAt; parser không trả timestamp tên file/provenance.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:991](../../../MProjectBackend/MProject.Application/Services/Assets/AgentService.cs#L991) — ReportTestResultsAsync TestedAt; [MProject.TestResults/TestResultParser.cs:30](../../../MProject.TestResults/TestResultParser.cs#L30) — TestResultParseOutcome / timestamp validation.
- **Bằng chứng và scenario:** R08 filename08:00 Full nhưng TestedAt=mtime lúc scan. Chưa xác nhận timestamp nào đáng tin với mọi writer. **Impact:** Copy/append mtime có thể đưa test vào sai ca hoặc đổi streak ordering.
- **Hướng sửa:** Giữ mtime discovery; nếu writer contract đủ tin cậy thì event time+timezone/provenance với fallback rõ Partial/Unstructured.
- **Rủi ro thay đổi / rollback:** Không rewrite lịch sử bằng timestamp suy đoán. Additive columns/read mode và so totals trước đổi query; giữ raw source timestamp để revert interpretation.
- **Regression bắt buộc:** Copy preserve/change mtime, append qua ca, timezone đổi/DST nếu hỗ trợ, parse partial; counts/streak đúng contract không reject log bị lệch format.

<a id="syn-68"></a>

### SYN-68 — Serial search không có trang sau 20 records

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [ING-07][r08]; severity/confidence gốc: Medium/P2 / High (source).

- **Root cause:** Component bỏ total/page metadata và luôn gọi page1,size20.
- **Files/symbols:** [MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx:104](../../../MProjectFrontend/src/pages/TestMonitor/TestMonitor.tsx#L104) — load, records table; [MProjectFrontend/src/api/testResults.ts:33](../../../MProjectFrontend/src/api/testResults.ts#L33) — testResultsApi.search.
- **Bằng chứng và scenario:** R08 source trace; recent20 mặc định có thể hợp lý, lỗi chỉ là search history không có navigation. **Impact:** Retest/failure cũ ngoài trang1 không truy cập được dù backend có pagination.
- **Hướng sửa:** Giữ metadata/page state, pagination/load-more, reset khi query đổi; server stable tie-breaker sau TestedAt.
- **Rủi ro thay đổi / rollback:** API-compatible UI, tie-breaker không tự thay business order của simultaneous PASS/FAIL streak. Không migration dữ liệu.
- **Regression bắt buộc:** 21+ rows và equal timestamps, fixed dataset truy cập mọi ID không thiếu/trùng, query đổi vềpage1; stale-response guard SYN-64.

<a id="syn-69"></a>

### SYN-69 — Server rollback lỗi vẫn mở pool và gỡ offline

**P1 · confirmed defect · Sửa trước deploy · Scope M.** Evidence: [R10-01][r10]; severity/confidence gốc: P1 / High (function probe).

- **Root cause:** Finally luôn start pool/remove app_offline dù restore chưa commit.
- **Files/symbols:** [scripts/update-server.ps1:353](../../../scripts/update-server.ps1#L353) — Restore-Backup, update catch.
- **Bằng chứng và scenario:** R10 stub Restore-Database throw vẫn start/remove offline. Tình huống thực: pg_restore lỗi sau recreate, locked DLL hoặc disk full; chưa IIS full drill. **Impact:** Requests/workers chạy trên code hoặc database restore dở.
- **Hướng sửa:** Chỉ admit traffic sau cả code/DB restore và validation; lỗi giữ stopped/offline, backup và phase diagnostics.
- **Rủi ro thay đổi / rollback:** Sửa chính đường cứu hộ; phải có manual retry cùng recovery pair trên IIS lab. Không thử destructive restore trên DB hiện hữu.
- **Regression bắt buộc:** IIS/PostgreSQL hai binaries: archive/locked-file/disk-full/pg_restore fault trước-sau recreate, không writer chạy khi fail; retry verified restore mới mở pool.

<a id="syn-70"></a>

### SYN-70 — Deploy target DB có thể khác effective API DB

**P1 · confirmed defect · Sửa trước deploy · Scope M.** Evidence: [R10-02][r10]; severity/confidence gốc: P1 / High.

- **Root cause:** CLI defaults cho psql/dump độc lập preserved appsettings/env; thiếu host/port/database identity handshake.
- **Files/symbols:** [scripts/update-server.ps1:34](../../../scripts/update-server.ps1#L34) — parameters, New-Backup, Assert-MigrationSafety, Restore-Database; [scripts/install-server.ps1:248](../../../scripts/install-server.ps1#L248) — Write-BackendAppSettings; [MProjectBackend/MProject.Api/Program.cs:39](../../../MProjectBackend/MProject.Api/Program.cs#L39) — effective connection configuration.
- **Bằng chứng và scenario:** R10 config kept dù CLI target khác; custom DB/port và default DB cùng tồn tại khiến nhiều bước vẫn thành công sai đối tượng. **Impact:** Dump/migrate/drop-restore nhầm DB; backup không cứu được API thật.
- **Hướng sửa:** Resolve/compare effective host/port/database/user trước mutation, bao gồm env/web.config; unresolved override cần explicit target contract, không log password.
- **Rủi ro thay đổi / rollback:** Fail-before-stop/dump/schema, không tự sửa DB identity. Verify recovery pair target/owner, restore rehearsal biệt lập trước bất kỳ DB mutation.
- **Regression bắt buộc:** Hai disposable DB, custom port và environment override; sai/thiếu target fail trước stop/SQL/DROP, đúng target backup→restore dùng cùng identity.

<a id="syn-71"></a>

### SYN-71 — Deployment thiếu schema vẫn báo healthy

**P1 · confirmed defect · Sửa trước deploy · Scope M.** Evidence: [R10-03][r10]; severity/confidence gốc: P1 / High (API Release + PostgreSQL).

- **Root cause:** Missing SQL/psql chỉ warning; ready chỉ CanConnect, artifact không có expected schema compatibility handshake.
- **Files/symbols:** [scripts/install-server.ps1:314](../../../scripts/install-server.ps1#L314) — Apply-Schema, schema branch; [scripts/update-server.ps1:388](../../../scripts/update-server.ps1#L388) — artifact preflight, health gate; [MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs:27](../../../MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs#L27) — Ready; [MProjectBackend/MProject.Api/Program.cs:444](../../../MProjectBackend/MProject.Api/Program.cs#L444) — AutoMigrate/seeder.
- **Bằng chứng và scenario:** R10 Release Production AutoMigrate=false trên DB thiếu BindTusSession/TusFileId vẫn200 Available. Không phải startup tự migrate; điều đó đã bị bác bỏ. **Impact:** Script DONE nhưng feature mới lỗi500 vì thiếu cột/migration.
- **Hướng sửa:** Thiếu SQL/tool fail preflight trừ intentional SkipSchema; artifact head/hash/compatibility và verify history trước traffic. Cho phép old binary trên compatible expand schema.
- **Rủi ro thay đổi / rollback:** Không ép Down migration để health khớp head. Gate phải hiểu compatibility cho code rollback và giữ maintenance khi thiếu schema.
- **Regression bắt buộc:** Release binary trên stale DB, SQL thiếu/cũ, psql thiếu, artifact mismatch; deploy fail đúng phase; control old binary/new expand schema được policy cho phép.

<a id="syn-72"></a>

### SYN-72 — Code update tự thay TLS certificate theo expiry

**P1 · confirmed defect · Sửa trước deploy · Scope M.** Evidence: [R10-04][r10]; severity/confidence gốc: P1 / High source/probe; trust chưa live.

- **Root cause:** RequireExistingCertificate chọn cert hợp lệ NotAfter lớn nhất thay vì current IIS binding thumbprint.
- **Files/symbols:** [scripts/install-server.ps1:115](../../../scripts/install-server.ps1#L115) — Get-UsableCertificate, Ensure-Certificate, Ensure-HttpsBinding; [scripts/update-server.ps1:423](../../../scripts/update-server.ps1#L423) — RequireExistingCertificate argument.
- **Bằng chứng và scenario:** Hai cert cùng SAN/CN A/B, B mới import chuẩn bị rotation, update bind B. R10 selection probe, chưa runtime store/trust drill. **Impact:** Server local health pass nhưng fleet chưa trust certificate mới mất kết nối.
- **Hướng sửa:** Ordinary update giữ và validate current thumbprint; rotation explicit với trust distribution, expired/mismatch fail trước mutation.
- **Rủi ro thay đổi / rollback:** Snapshot binding/cert identity trước update; code/DB rollback không tự restore binding hiện tại. Canary client chỉ trust A và thực hành phục hồi A.
- **Regression bắt buộc:** IIS thật two certs, update giữ A dù B expiry mới; wrong/missing/expired cert fail; station LocalSystem TLS/enroll/poll vẫn pass.

<a id="syn-73"></a>

### SYN-73 — Console mặc định đăng nhập qua HTTP

**P1 · confirmed defect · Sửa transport; giữ auth boundary SYN-88/89 · Scope M.** Evidence: [R10-05][r10]; severity/confidence gốc: P1 / High (source trace).

- **Root cause:** HTTP IIS site phục vụ SPA/API, frontend same-origin API URL rỗng, không redirect/reject boundary tương ứng.
- **Files/symbols:** [scripts/install-server.ps1:17](../../../scripts/install-server.ps1#L17) — HTTP bindings/firewall/advertised console URL; [scripts/prepare-deploy.ps1:278](../../../scripts/prepare-deploy.ps1#L278) — frontend API URL build; [MProjectFrontend/src/api/services/axios-client.ts:17](../../../MProjectFrontend/src/api/services/axios-client.ts#L17) — base URL, auth headers; [MProjectFrontend/src/api/auth.ts:18](../../../MProjectFrontend/src/api/auth.ts#L18) — login/refresh; [MProjectBackend/MProject.Api/Program.cs:455](../../../MProjectBackend/MProject.Api/Program.cs#L455) — middleware pipeline.
- **Bằng chứng và scenario:** R10 trace path mặc định http LAN-IP đến login/refresh; chưa chứng minh production traffic/credential đã bị thu thập. Agent strict TLS không bảo vệ browser này. **Impact:** Login/Bearer/refresh có thể đi cleartext trên LAN nếu dùng URL installer quảng bá.
- **Hướng sửa:** Canonical HTTPS/trust cho console, đổi entry/instructions và HTTP handling để không khởi tạo login cleartext; không chỉ redirect POST đã mang password.
- **Rủi ro thay đổi / rollback:** Rollout trust trước cutover, canary admin login/refresh và Agent cùng origin đúng. Không rollback bằng certificate bypass hoặc mở lại auth cleartext mặc định.
- **Regression bắt buộc:** IIS/browser/network: HTTP không trả working login SPA qua HTTP, sensitive API không xử lý cleartext, HTTPS same-origin/refresh/Agent vẫn tốt; giữ role/auth matrix SYN-84–96.

<a id="syn-74"></a>

### SYN-74 — Public-key input phân phối được private key

**P1 · confirmed defect · Sửa nhanh trước build phát hành · Scope S.** Evidence: [R10-06][r10]; severity/confidence gốc: P1 / High input flow; probe chỉ labels.

- **Root cause:** File existence/tên PublicKeyPath được tin như type check; raw PEM copy vào file/JSON.
- **Files/symbols:** [scripts/prepare-deploy.ps1:243](../../../scripts/prepare-deploy.ps1#L243) — PublicKeyPath copy; [scripts/package-agent.ps1:293](../../../scripts/package-agent.ps1#L293) — ReleasePublicKeyPem input; [scripts/install-server.ps1:281](../../../scripts/install-server.ps1#L281) — Set-AgentReleasePublicKey.
- **Bằng chứng và scenario:** R10 setter chấp nhận BEGIN PRIVATE KEY label; chưa có bằng chứng key thật từng ship. Đây là phân phối sai input, khác rotation design risk SYN-42. **Impact:** Nếu chọn nhầm PEM, signing private material có thể đi vào server/Agent distributables.
- **Hướng sửa:** Reject private/mixed blocks, parse public key và xuất public-only representation; scan final distributable kể cả JSON. Giữ private SignWith chỉ tại signer.
- **Rủi ro thay đổi / rollback:** Guard build nhỏ, không rotation live tự động. Nếu audit xác nhận đã lộ thì incident riêng dừng phát hành/thu hồi và thực hiện recovery key đã duyệt.
- **Regression bắt buộc:** Valid public PEM pass; PKCS1/8 private, mixed/malformed reject trước copy; server/Agent artifact không private material, signing path hợp lệ còn chạy.

<a id="syn-75"></a>

### SYN-75 — DB password không round-trip connection-string escaping

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [R10-07][r10]; severity/confidence gốc: P2 / High (parser probe).

- **Root cause:** String interpolation/JSON escaping không serialize đúng connection-string format.
- **Files/symbols:** [scripts/install-server.ps1:258](../../../scripts/install-server.ps1#L258) — Write-BackendAppSettings; [scripts/setup-iis-local.ps1:353](../../../scripts/setup-iis-local.ps1#L353) — Ensure-AppSettings.
- **Bằng chứng và scenario:** R10 synthetic password chứa semicolon parse lại khác input; không có credential thật trong evidence. **Impact:** psql dùng đúng password nhưng API DB connection sai với semicolon/quotes.
- **Hướng sửa:** Connection-string builder tương thích Npgsql cho các fields và drill parser; không giới hạn password để né escaping.
- **Rủi ro thay đổi / rollback:** Chỉ ghi config sau validation; backup config ACL đúng và compare fields không log secret. Preserve operator config khi không yêu cầu overwrite.
- **Regression bắt buộc:** ; = quotes leading/trailing spaces, parse exact; disposable PostgreSQL psql và Production API cùng credential hoạt động.

<a id="syn-76"></a>

### SYN-76 — Rollback giữ config/key của deployment thất bại

**P2 · confirmed defect · Sửa · Scope S.** Evidence: [R10-08][r10]; severity/confidence gốc: P2 / High (function probe).

- **Root cause:** Restore overlay live appsettings lên archive cũ, trong khi update tự sửa key ngay cả preserve-config mode.
- **Files/symbols:** [scripts/update-server.ps1:360](../../../scripts/update-server.ps1#L360) — Restore-Backup; [scripts/install-server.ps1:279](../../../scripts/install-server.ps1#L279) — Set-AgentReleasePublicKey.
- **Bằng chứng và scenario:** R10 old keyA/new keyB labels, rollback vẫnB. Khác TLS binding SYN-72: đây là file config ownership. **Impact:** Old code/DB quay lại nhưng signing key/config vẫn từ artifact lỗi.
- **Hướng sửa:** Restore predeploy config exact hoặc explicit field ownership merge; rotation key không tự xảy ra chỉ vì artifact có file.
- **Rủi ro thay đổi / rollback:** Backup config cùng code/DB, bảo vệ ACL. Không restore secret cũ nếu đã có rotation ngoài deployment trong window mà chưa chốt recovery policy.
- **Regression bắt buộc:** A→artifactB→health fail phải restoreA fingerprint; successful update và operator overrides theo contract, no secret output.

<a id="syn-77"></a>

### SYN-77 — KeepBackups=0 xóa cả recovery pair đang dùng

**P1 · confirmed defect · Sửa nhanh trước deploy · Scope S.** Evidence: [R10-09][r10]; severity/confidence gốc: P2 / High (filesystem probe).

- **Root cause:** Prune trước deploy và không bảo vệ active backup pair; zero chọn tất cả.
- **Files/symbols:** [scripts/update-server.ps1:30](../../../scripts/update-server.ps1#L30) — KeepBackups, Remove-OldBackups.
- **Bằng chứng và scenario:** R10 file fixture zero xóa pair mới. Nâng P1 dù cần option0: khi kích hoạt, phá recovery point ngay trong maintenance window. **Impact:** Health fail sau migration không còn archive/dump để rollback.
- **Hướng sửa:** Validate range, pin current pair theo attempt và prune chỉ sau commit/recovery; nếu zero được hỗ trợ thì nghĩa là không giữ lịch sử sau hoàn tất.
- **Rủi ro thay đổi / rollback:** Không thử cleanup trên backup thật. Protect recovery pair của đúng install/attempt; guard fail-safe và manual restore path còn dùng được.
- **Regression bắt buộc:** 0/1/negative, failure copy/health/restore; current pair còn đến success cuối, không xóa backup installation khác chung root.

<a id="syn-78"></a>

### SYN-78 — Release gates hard-code migration head cũ

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [R10-10][r10]; severity/confidence gốc: P2 / High.

- **Root cause:** Feature migration membership bị nhầm với current head, metadata literal không đọc artifact/DB.
- **Files/symbols:** [scripts/test-production-support-server-artifact-smoke.ps1:267](../../../scripts/test-production-support-server-artifact-smoke.ps1#L267) — migrationHeadBefore assertion; [scripts/test-production-support-release-db.ps1:122](../../../scripts/test-production-support-release-db.ps1#L122) — ProductionSupportMigrationApplied; [scripts/test-production-support-query-performance.ps1:481](../../../scripts/test-production-support-query-performance.ps1#L481) — MigrationHead report; [MProjectBackend/MProject.Infrastructure/Migrations/20260906090000_BindTusSession.cs:7](../../../MProjectBackend/MProject.Infrastructure/Migrations/20260906090000_BindTusSession.cs#L7) — new migration head.
- **Bằng chứng và scenario:** R10 runtime48 migrations headBindTusSession; gates vẫn AddProductionSupportReporter. Dùng chung artifact metadata với SYN-71 nhưng code lỗi/tool acceptance khác, giữ mục riêng. **Impact:** Artifact mới hợp lệ bị reject hoặc report head sai, gate không còn đáng tin.
- **Hướng sửa:** Expected head/hash từ artifact, feature bằng membership/schema; report actual head. Không chỉ đổi literal sang head hôm nay.
- **Rủi ro thay đổi / rollback:** Tool-only, sửa trước dùng làm gate cho fixes; giữ fixture old/new supported schemas, không bỏ assertion để pass artifact cũ.
- **Regression bắt buộc:** Migration mới sau feature, current artifact pass/actual head; thiếu required feature hoặc SQL/hash mismatch fail rõ.

<a id="syn-79"></a>

### SYN-79 — Migration classifier fail-open với syntax/SQL không hiểu

**P2 · design risk · Harden classifier · Scope M.** Evidence: [R10-11][r10]; severity/confidence gốc: P2 / High mechanism; impact chưa xảy ra.

- **Root cause:** Regex chỉ block-bodied Up và danh sách operations hẹp; không match thì return, SQL không được phân loại.
- **Files/symbols:** [scripts/prepare-deploy.ps1:186](../../../scripts/prepare-deploy.ps1#L186) — Up/destructive regex classifier; [scripts/update-server.ps1:182](../../../scripts/update-server.ps1#L182) — Assert-MigrationSafety; [MProjectBackend/MProject.Infrastructure/Migrations/20260818090000_RevokeViewerSoftwareMutationPermissions.cs:15](../../../MProjectBackend/MProject.Infrastructure/Migrations/20260818090000_RevokeViewerSoftwareMutationPermissions.cs#L15) — raw SQL example; [MProjectBackend/MProject.Infrastructure/Migrations/20260906090000_BindTusSession.cs:10](../../../MProjectBackend/MProject.Infrastructure/Migrations/20260906090000_BindTusSession.cs#L10) — expression-bodied Up example.
- **Bằng chứng và scenario:** R10 fake raw SQL DROP/expression DropTable bị bỏ, block DropTable nhận. Chưa chứng minh migration hiện hành đã làm mất business data qua gate. **Impact:** Migration mới destructive có thể bị phân loại additive và bỏ preparation gate.
- **Hướng sửa:** EF operations hoặc reviewed explicit metadata, unknown/Sql cần classification; bind toàn IDs/head/SQL hash. Annotate intentional security grant revoke, không sửa lịch sử.
- **Rủi ro thay đổi / rollback:** Metadata migration không phải chạy schema Down. Giữ backup gate và test classifier khi đổi toolchain; unknown fail rõ để chuẩn bị release.
- **Regression bắt buộc:** Block/expression/raw SQL/helper/generic/malformed, stale manifest/hash mismatch; unknown chặn hoặc explicit reviewed class.

<a id="syn-80"></a>

### SYN-80 — Maintenance/backup boundary chưa bao phủ mọi writer và storage

**P1 · design risk · Chốt topology/RPO rồi drill · Scope L.** Evidence: [R10-12][r10]; severity/confidence gốc: P1 / High scope; topology chưa kiểm.

- **Root cause:** Chỉ stop local pool, installer mở traffic trước verdict; SkipBackup bỏ stop trước schema; archive không chứa external storage.
- **Files/symbols:** [scripts/update-server.ps1:124](../../../scripts/update-server.ps1#L124) — Stop-BackendPool, New-Backup, fingerprint, health/rollback; [scripts/install-server.ps1:489](../../../scripts/install-server.ps1#L489) — schema/start ordering; [MProjectBackend/MProject.Api/Program.cs:172](../../../MProjectBackend/MProject.Api/Program.cs#L172) — mutation workers; [MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs:216](../../../MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs#L216) — object deletion.
- **Bằng chứng và scenario:** Host B còn write hoặc API mới nhận support/job ACK trước health fail rồi full-DB restore. Chưa có two-host/live-traffic evidence; conditional risk, không khẳng định mọi deploy mất dữ liệu. **Impact:** DB rewind có thể mất writes đã ACK hoặc lệch object/git/Agent side effects.
- **Hướng sửa:** Một host cũng giữ traffic/workers fenced đến commit, stop trước schema độc lập backup; nhiều hosts cần drain/fence chung. Chốt object/git snapshot và post-traffic forward recovery/reconciliation.
- **Rủi ro thay đổi / rollback:** Không tự DB rewind sau traffic đã ACK. Có recovery plan DB+objects+receipts/Agent state, phân biệt pretraffic rollback và posttraffic repair; RPO/RTO owner quyết định.
- **Regression bắt buộc:** IIS hai hosts/disposable PostgreSQL: writes từng phase, SkipBackup ordering, failed health, blob/git sentinel/job receipt; không mất acknowledged writes ngoài policy đã duyệt.

<a id="syn-81"></a>

### SYN-81 — Local deploy health bỏ TLS validation

**P2 · confirmed defect · Sửa nhanh · Scope S.** Evidence: [R10-13][r10]; severity/confidence gốc: P2 / High (source).

- **Root cause:** Node helper rejectUnauthorized=false trên canonical HTTPS; setup thật gọi helper này.
- **Files/symbols:** [scripts/deploy-local.ps1:63](../../../scripts/deploy-local.ps1#L63) — Test-HealthWithNode; [scripts/setup-iis-local.ps1:520](../../../scripts/setup-iis-local.ps1#L520) — local deploy call.
- **Bằng chứng và scenario:** R10 source explicit bypass, default URL không chỉ loopback. Main install/update curl strict không bác bỏ lỗi helper riêng. **Impact:** Staging báo pass certificate sai trong khi Agent strict client sẽ fail.
- **Hướng sửa:** Reuse strict probe, trust fixture và hostname hợp lệ; không tắt certificate check để chạy test.
- **Rủi ro thay đổi / rollback:** Tool-only; nếu lab chưa trust phải chuẩn bị cert thay vì revert bypass. Không đổi production trust store trong synthesis.
- **Regression bắt buộc:** Đúng helper setup gọi: trusted correct host pass, untrusted/wrong-host/expired trusted-chain fail; canary Agent token không gửi trước TLS success.

<a id="syn-82"></a>

### SYN-82 — IIS workers phụ thuộc traffic/inherited pool lifecycle

**P2 · design risk · Xác minh effective IIS rồi cấu hình · Scope M.** Evidence: [ARC00-02][r00]; severity/confidence gốc: Medium / High config gap; live settings chưa rõ.

- **Root cause:** Workers nằm trong API process nhưng installer không khai explicit idle/start/preload policy.
- **Files/symbols:** [scripts/install-server.ps1:200](../../../scripts/install-server.ps1#L200) — Ensure-AppPool, Ensure-Website; [MProjectBackend/MProject.Api/Program.cs:172](../../../MProjectBackend/MProject.Api/Program.cs#L172) — hosted-service registrations; [MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs:26](../../../MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs#L26) — Ready.
- **Bằng chứng và scenario:** R00 chỉ ra config gap; R10 chưa boot/idle/recycle drill, không bác bỏ. Heartbeat thường xuyên giảm xác suất nhưng không chứng minh idle/cold-start behavior. **Impact:** Watchdog/GC/recovery sweeps có thể chưa chạy sau reboot hoặc ngừng khi idle.
- **Hướng sửa:** Khai/verify lifecycle IIS phù hợp current host, đo last successful sweeps; không cần tách microservices/worker platform.
- **Rủi ro thay đổi / rollback:** Snapshot pool/site settings, lab idle không HTTP, restore setting được. Phối hợp SYN-80: tự-start không được phá maintenance fence.
- **Regression bắt buộc:** IIS cold boot/recycle không gửi HTTP và idle qua timeout cũ; quan sát sweep trực tiếp DB/log, không polling health làm che lỗi; update existing pool.

<a id="syn-83"></a>

### SYN-83 — DomainEventDispatcher logging-only chưa có delivery semantics

**P3 · design risk · Quyết định/document trước; không build outbox mặc định · Scope S tài liệu; M nếu có delivery requirement.** Evidence: [ARC00-03][r00]; severity/confidence gốc: Medium / High.

- **Root cause:** Persisted event được log rồi marked processed, không handler/ack/retry contract.
- **Files/symbols:** [MProjectBackend/MProject.Application/Services/Common/DomainEventDispatcherService.cs:43](../../../MProjectBackend/MProject.Application/Services/Common/DomainEventDispatcherService.cs#L43) — DispatchAsync; [MProjectBackend/MProject.Infrastructure/DBContext.cs:1168](../../../MProjectBackend/MProject.Infrastructure/DBContext.cs#L1168) — AddDomainEvent; [MProjectBackend/MProject.Api/Program.cs:177](../../../MProjectBackend/MProject.Api/Program.cs#L177) — dispatcher registration.
- **Bằng chứng và scenario:** R00 source/tests xác nhận logging-only và historical ARCH-001. Support SQLite outbox có durability riêng; không gộp thành support delivery defect. **Impact:** Consumer tương lai có thể hiểu nhầm ProcessedAt; chưa chứng minh workflow hiện tại mất side effect.
- **Hướng sửa:** Nếu chỉ audit/log, đổi tên/prose cho đúng; chỉ triển khai handler/retry/idempotency khi có consumer business được xác nhận.
- **Rủi ro thay đổi / rollback:** Docs/rename nhỏ; không replay tất cả DomainEvents vào side effects chưa định nghĩa. Nếu chọn delivery phải giữ audit và migration/poison/replay policy.
- **Regression bắt buộc:** Logging-only: contract/docs check đủ. Delivery có yêu cầu thật: PostgreSQL crash trước/sau side effect, duplicate/retry/concurrent owner và ack đúng lúc.

<a id="syn-84"></a>

### SYN-84 — Assignment creation bỏ qua ACL Deny tại station đích

**P1 · confirmed defect · Sửa tại application mutation boundary · Scope S.** Evidence: [R09-01][r09]; severity/confidence gốc: P1 / High (HTTP và persisted state).

- **Root cause:** Controller authorize với resource null và service không kiểm `software.assignment.manage` trên `StationResourceId` đã validate.
- **Files/symbols:** `SoftwareAssignmentsController.Assign`, `PermissionAuthorizationFilter.TryResolveResourceIdAsync`, `StationSoftwareAssignmentService.AssignAsync` tại revision của report 09.
- **Bằng chứng và scenario:** Global grant cùng explicit station Deny vẫn POST create 200/lưu Active, trong khi activate tại cùng station trả 403. **Impact:** tạo deployment intent tại station actor bị cấm; report không chạy Agent deployment.
- **Hướng sửa:** Kiểm effective permission trên station ở application mutation boundary; giữ Deny precedence và server là authority cuối.
- **Rủi ro thay đổi / rollback:** Có thể làm lộ caller đang dựa vào global-only check; rollback code không được mở lại Deny bypass. Audit/approval không được tạo khi request bị từ chối.
- **Regression bắt buộc:** HTTP global Allow + station/team Deny, scoped Allow đúng/sai station; denial không persist assignment, approval hoặc success audit.

<a id="syn-85"></a>

### SYN-85 — Delegated user manager reset được tài khoản đặc quyền

**P1 · confirmed defect · Thêm target-privilege policy · Scope M.** Evidence: [R09-02][r09]; severity/confidence gốc: P1 / High (HTTP và login bằng credential mới).

- **Root cause:** Reset password chỉ yêu cầu `user.manage`, không so privilege/grantability của actor và target hoặc bảo vệ system-role account.
- **Files/symbols:** `UsersController.ResetPassword`, `UserService.ResetUserPasswordAsync` tại revision của report 09.
- **Bằng chứng và scenario:** Actor chỉ có `user.manage` reset được account có `role.manage`, sau đó đăng nhập bằng target và gọi route đặc quyền. **Impact:** capability delegated support có thể chiếm mọi human account biết ID/username.
- **Hướng sửa:** Tách capability reset đặc quyền hoặc áp target policy dựa trên quyền actor có thể grant; đồng bộ UI/audit credential operation.
- **Rủi ro thay đổi / rollback:** Helpdesk flow hợp lệ phải giữ positive case cho user thông thường; rollback không được hạ target protection.
- **Regression bắt buộc:** HTTP actor chỉ `user.manage` được reset user thường nhưng bị deny với Admin/system-role/out-of-grantability; hash/AuthVersion/refresh rows không đổi khi deny.

<a id="syn-86"></a>

### SYN-86 — Login cũ phát refresh sau security mutation

**P1 · confirmed defect · DB-enforced security generation · Scope M.** Evidence: [R09-03][r09]; severity/confidence gốc: P1 / High tại service interleaving, chưa PostgreSQL barrier.

- **Root cause:** Password verification, AuthVersion mutation, revoke và refresh issuance không chung atomic boundary; refresh family không bind generation đã xác thực.
- **Files/symbols:** `AuthService.LoginAsync/ChangePasswordAsync/RefreshAsync`, `RefreshTokenService.IssueAsync`, `RefreshToken` tại revision của report 09.
- **Bằng chứng và scenario:** Login pause trước Issue; context khác đổi password/revoke; login cũ resume và tạo refresh có thể mint JWT theo AuthVersion mới. **Impact:** session bắt nguồn từ password cũ sống qua thao tác bảo vệ tài khoản.
- **Hướng sửa:** Bind issue/rotate vào database-enforced user security generation và conditional claim; không dùng in-process lock làm boundary.
- **Rủi ro thay đổi / rollback:** Migration và mixed-version issuance cần tương thích; access-token active/AuthVersion validation hiện có phải giữ nguyên.
- **Regression bắt buộc:** PostgreSQL hai context tại verify→issue và rotate→revoke cho change/reset/disable; generation cũ không mint JWT generation mới.

<a id="syn-87"></a>

### SYN-87 — SingleDeviceLogin vẫn để hai refresh families hợp lệ

**P2 · confirmed defect · Sửa cùng security generation · Scope M.** Evidence: [R09-04][r09]; severity/confidence gốc: P2 / High (deterministic two-context).

- **Root cause:** Hai login có thể cùng hoàn tất revoke trước khi một trong hai Issue; thiếu DB serialization/uniqueness theo user generation.
- **Files/symbols:** `AuthService.LoginAsync`, `RefreshTokenService.RevokeAllActiveForUserAsync`, `AuthTokenOptions.SingleDeviceLogin` tại revision của report 09.
- **Bằng chứng và scenario:** Hai context tạo hai active families khi option bật. **Impact:** hai browser tiếp tục refresh; report không diễn giải option là chỉ còn một access JWT.
- **Hướng sửa:** Dùng cùng generation/replace-session boundary với SYN-86 và định nghĩa rõ option quản lý refresh family.
- **Rủi ro thay đổi / rollback:** Lost login response không được làm user mất đường đăng nhập hợp lệ; mixed servers phải đồng ý generation.
- **Regression bắt buộc:** PostgreSQL concurrent login chỉ còn một family hợp lệ; family thua bị deny khi refresh, gồm logout/rotate/login xen kẽ.

<a id="syn-88"></a>

### SYN-88 — Browser nhận response auth cũ sau khi đổi phiên

**P1 · confirmed defect · Một auth epoch/coordinator · Scope M.** Evidence: [R09-05][r09]; severity/confidence gốc: P1 / High (deferred frontend probes).

- **Root cause:** Logout/login không tăng generation cho refresh, `/me`, token persistence và replay; AuthContext và interceptor có hai single-flight paths.
- **Files/symbols:** Axios `performRefresh`/response interceptor, `AuthContext.refreshMe/syncAuthState`, `UserDropDown` tại revision của report 09.
- **Bằng chứng và scenario:** Refresh hoặc `/me` của A trả sau logout/login B có thể ghi lại token/profile A hoặc replay request bằng actor cũ. **Impact:** UI/token/audit actor không khớp trong tab dùng chung.
- **Hướng sửa:** Một coordinator với auth epoch; mọi success/failure/persist/replay kiểm epoch và token đã dùng, hủy queue phiên cũ.
- **Rủi ro thay đổi / rollback:** Tránh refresh storm và logout giả do stale 401; giữ Axios interceptor contract thay vì thêm coordinator cạnh tranh.
- **Regression bắt buộc:** Deferred logout→login B, late refresh/`/me`/401, pending mutation và startup restore; stale work không ghi/xóa/replay vào phiên B.

<a id="syn-89"></a>

### SYN-89 — Cache riêng tư không cô lập giữa users cùng tab

**P1 · confirmed defect · Identity-scope protected queries · Scope M.** Evidence: [R09-06][r09]; severity/confidence gốc: P1 / High (real query hook/cache; server check source-traced).

- **Root cause:** QueryClient sống qua logout/login; chỉ support queries được purge và protected repository keys không mang identity.
- **Files/symbols:** App QueryClient, `SupportQueryIsolation`, repository overview/tree/content hooks tại revision của report 09.
- **Bằng chứng và scenario:** A cache private repository, B login cùng tab và hook trả cache A không gọi API dù B sẽ nhận 403. **Impact:** browser disclosure; không phải kết luận server bỏ authorization.
- **Hướng sửa:** Identity-scope hoặc cancel/remove toàn bộ protected queries tại auth boundary; stale response A không được populate cache B. Chỉ giữ public cache được phân loại rõ.
- **Rủi ro thay đổi / rollback:** Broad purge có thể tăng load; danh mục public/private phải explicit, mặc định dữ liệu authenticated là protected.
- **Regression bắt buộc:** A fetch overview/tree/blob → logout → B cùng URL không render A; bao phủ A request đang chờ, profile và scope/team change.

<a id="syn-90"></a>

### SYN-90 — Approval drawer có thể thao tác request từ response cũ

**P2 · confirmed defect · Sửa nhanh bằng generation guard · Scope S.** Evidence: [R09-07][r09]; severity/confidence gốc: P2 / High (component action probe).

- **Root cause:** Async detail/action state không bind selected ID/generation; footer có thể dùng mutable detail cũ khi loading.
- **Files/symbols:** `ApprovalActionDrawer` load effect và approve/reject/cancel handlers tại revision của report 09.
- **Bằng chứng và scenario:** Mở A rồi B, B trả trước A; drawer B hiển thị A và Cancel gửi ID A. **Impact:** mutation nhầm request mà actor vẫn có quyền.
- **Hướng sửa:** Clear state khi đổi/đóng, chỉ nhận response mới nhất, disable action khi detail chưa khớp selection và bind confirm vào snapshot mở.
- **Rủi ro thay đổi / rollback:** UI-only nhưng mutation đã gửi không tự hoàn tác; cần reload detail sau outcome không rõ.
- **Regression bắt buộc:** A trả sau B, close→reopen, B load fail khi A còn data; mọi action chỉ dùng selected ID hợp lệ.

<a id="syn-91"></a>

### SYN-91 — Approval inbox cắt candidates trước eligibility

**P2 · confirmed defect · Eligibility trước paginate/count · Scope M.** Evidence: [R09-08][r09]; severity/confidence gốc: P2 / High (501-request probe).

- **Root cause:** Cap 500 áp trước `CanAct` và không có continuation/truncation contract.
- **Files/symbols:** `ApprovalService.GetInboxAsync/MaxInboxScan`, frontend `Approvals/Inbox` tại revision của report 09.
- **Bằng chứng và scenario:** Một eligible request cũ hơn 500 request unrelated trả inbox rỗng/Total=0 dù detail nói actor có thể act. **Impact:** approval có thể chờ vô hạn.
- **Hướng sửa:** Đưa eligibility vào query trước pagination/count hoặc scan có continuation và total semantics chính xác; không chỉ tăng cap.
- **Rủi ro thay đổi / rollback:** Query phức tạp có thể ảnh hưởng performance và scope correctness; giữ server-side filtering, stable ordering và no-duplicate pages.
- **Regression bắt buộc:** 501+ records nhiều role/scope, eligible ngoài window; duyệt hết trang nhận đúng một lần, total/badge đúng, actor khác không thấy.

<a id="syn-92"></a>

### SYN-92 — Mutation UI dùng navigation permissions, bỏ qua ACL

**P2 · confirmed defect · Target-aware capability hints · Scope M.** Evidence: [R09-09][r09]; severity/confidence gốc: P2 / High (backend probe và consumer source).

- **Root cause:** Role-only `/me.permissions` dành cho navigation được dùng như effective action capability dù ACL phụ thuộc target.
- **Files/symbols:** `AuthService.GetMeAsync`, `DeploymentMatrix`, `SoftwarePackages`, `SupportRequests` tại revision của report 09.
- **Bằng chứng và scenario:** ACL Allow hợp lệ tại server không mở action trên UI; scoped Deny cũng không thể được diễn đạt bằng projection global. **Impact:** delegation cần API thủ công hoặc role rộng hơn.
- **Hướng sửa:** Giữ navigation projection rõ; cung cấp effective capability hints theo actor/target cho mutation UI, server vẫn là authority cuối.
- **Rủi ro thay đổi / rollback:** Hint stale không được biến thành security boundary; UI phải xử lý 403 và refresh sau grant/revoke.
- **Regression bắt buộc:** Base read role + scoped Allow mở đúng target; Deny không mở target bị cấm; actor không grant không có action.

<a id="syn-93"></a>

### SYN-93 — Authorization revoke audit làm hỏng before snapshot

**P2 · confirmed defect · Sửa nhanh bằng immutable projection · Scope S.** Evidence: [R09-10][r09]; severity/confidence gốc: P2 / High (service/audit probe).

- **Root cause:** `before` giữ cùng entity reference rồi serialize sau khi entity đã mutate/save.
- **Files/symbols:** `AuthorizationMutationService.DeleteRoleAssignmentAsync/DeleteAclEntryAsync`, `AuthorizationAuditLogger.LogAsync` tại revision của report 09.
- **Bằng chứng và scenario:** BeforeJson và AfterJson đều có `IsDeleted=true`. **Impact:** audit diff không chứng minh grant/Deny đã bị thu hồi.
- **Hướng sửa:** Chụp immutable allowlisted DTO trước mutation và projection after rõ ràng; giữ transaction mutation+audit hiện có.
- **Rủi ro thay đổi / rollback:** Không serialize navigation graph/secret; schema audit consumer phải giữ field contract.
- **Regression bắt buộc:** Role assignment và ACL delete có before false/after true cùng identity/scope/time/effect đúng.

<a id="syn-94"></a>

### SYN-94 — Scheduled ACL boundary không hết hạn decision cache đúng lúc

**P2 · confirmed defect · Tính next transition bằng một clock · Scope M.** Evidence: [R09-11][r09]; severity/confidence gốc: P2 / High (cached/uncached probe).

- **Root cause:** Query active loại future rules trước TTL calculation; cache chỉ xét EndTime và evaluator dùng clock khác batch path.
- **Files/symbols:** `AuthorizationService.EvaluateAsync/ComputeEffectiveCacheTtl`, `SubjectGrantQueryExtensions.WhereActiveForSubject` tại revision của report 09.
- **Bằng chứng và scenario:** Warm Allow trước `Deny.StartTime` vẫn true sau boundary trong cached path, batch path trả false. **Impact:** scheduled revoke áp muộn và API paths bất nhất.
- **Hướng sửa:** Tính next start/end của grants, Denies và memberships liên quan; dùng một `TimeProvider` semantic cho cached/batch evaluation.
- **Rủi ro thay đổi / rollback:** TTL quá ngắn tăng DB load; correctness tại boundary và invalidation vẫn ưu tiên hơn cache hit.
- **Regression bắt buộc:** Fake time trước/tại/sau Allow/Deny user/team global/scoped, owner shortcut; cached và uncached outcomes đồng nhất.

<a id="syn-95"></a>

### SYN-95 — Approval multi-step dùng scope của bước đầu cho mọi bước

**P2 · confirmed defect · Chốt support rồi snapshot/reject rõ · Scope M.** Evidence: [R09-12][r09]; severity/confidence gốc: P2 / High trong fixture multi-step; deployment usage chưa rõ.

- **Root cause:** Request chỉ lưu scope resolved từ bước đầu; `ApprovalStepSnapshot` không giữ strategy/context riêng từng step.
- **Files/symbols:** `ApprovalService.SubmitAsync`, `ApprovalStepSnapshot`, `ApprovalApproverResolver.GetApproversAsync` tại revision của report 09.
- **Bằng chứng và scenario:** Policy Global→RequesterTeam vẫn dùng approver global cho bước 2. **Impact:** lựa chọn sai người duyệt hoặc workflow kẹt; seeded one-step policies không đi qua nhánh này.
- **Hướng sửa:** Nếu mixed strategies không là requirement, reject policy rõ ràng; nếu có, snapshot/resolve scope cho từng step và giữ semantics sau policy edit.
- **Rủi ro thay đổi / rollback:** Có thể invalidate policy dữ liệu tùy chỉnh; cần inventory/dry validation trước enforcement và không reinterpret request đã submit.
- **Regression bắt buộc:** Global→Team, Team→Global, Resource→Department, membership/policy edit sau submit; eligible actor đúng từng snapshot.

<a id="syn-96"></a>

### SYN-96 — Approval action không bind version/step đã xem

**P2 · design risk · Expected version/step + idempotency · Scope M.** Evidence: [R09-13][r09]; severity/confidence gốc: P2 / High về replay, impact phụ thuộc multi-step.

- **Root cause:** Action request chỉ mang comment; mỗi call tải current step mới và không có expected version/step hoặc idempotency key.
- **Files/symbols:** `ApprovalActionRequest`, `ApprovalService.ApproveAsync`, `ApprovalActionDrawer.handleApprove` tại revision của report 09.
- **Bằng chứng và scenario:** Cùng action gửi tuần tự hai lần có thể approve hai bước cùng role; optimistic concurrency không chặn request thứ hai bắt đầu sau commit. **Impact:** mất một lần review độc lập trong policy multi-step.
- **Hướng sửa:** Bind action vào version/step đã hiển thị; stale trả conflict, retry cùng key trả kết quả action gốc thay vì áp vào current step.
- **Rủi ro thay đổi / rollback:** Contract client/server cần tương thích; one-step terminal retry phải giữ outcome rõ, không biến thành silent success mới.
- **Regression bắt buộc:** Hai tab, lost response sau step-1 commit, same/different-role steps và approve-vs-reject/cancel; một intent chỉ ghi đúng step đã xem.

## 4. Phân nhóm công việc để chọn release

### 4.1 Sửa nhanh

“Sửa nhanh” nói phạm vi code, vẫn phải đạt regression boundary và prerequisites ở phần 5.

- **Chặn sai intent/bytes ở browser:** SYN-11 (hash selection), SYN-28 (target modal), SYN-62/64 (monitor polling cùng generation guard), SYN-58 (error khác empty), SYN-68 (search pagination), SYN-90 (approval selection).
- **Guard nhỏ phía server/build:** SYN-03 (TTL), SYN-09 (DELETE/release), SYN-12 (tracker generation), SYN-13 (tree collision), SYN-31 (finite numbers), SYN-74 (public-only input), SYN-75 (connection-string builder), SYN-77 (active recovery pair), SYN-78 (migration gates), SYN-81 (strict local TLS), SYN-93 (immutable audit snapshot).
- **Nhỏ nhưng chạm runtime trạm:** SYN-45 (health timeout), SYN-47/48/50 (session/Run/outcome), SYN-40 (OTA result ACK), SYN-54 (retention timestamp), SYN-59 (sync freshness). Chỉ phát hành sau process/outbox compatibility tests và LocalSystem canary.
- **Tài liệu:** SYN-61. SYN-83 chỉ thành việc tài liệu/đổi tên nhỏ nếu owner xác nhận logging-only; chưa có lý do xây delivery platform.

### 4.2 Fix có rủi ro thay đổi cao

- **Credentials, sessions và authority:** SYN-01/04/05/84–89. CAS/revalidation phải bao phủ mọi writer; không chữa 410 bằng cách bỏ scope/capability check. Security generation phải được DB enforce; browser auth epoch và protected-query isolation phải dùng chung identity boundary.
- **Approval và timed authorization:** SYN-91/92/94–96. Eligibility phải đúng trước pagination; action hints không thay server authority; scheduled boundaries và multi-step intent cần provider/concurrency tests.
- **CAS và historical refs:** SYN-08/10. Cần real providers, PostgreSQL và restore bytes; DB rollback đơn lẻ không đủ. Retention/purge history cần quyết định trước migration.
- **Config rendering/activation:** SYN-17–27/30/32. Một Station value/target có thể đổi nhiều máy. Giữ immutable proposal và pin history, triển khai từng invariant.
- **Process và OTA:** SYN-02/33–37/39/43/44/46. Không đánh giá an toàn từ code size M; locks, deadlines, bootstrap cũ, executable handles và rollback quyết định khả năng cứu trạm.
- **Support graph:** SYN-51–56. Không xóa outbox để hết lỗi hoặc sửa exact payload đã có receipt. Schema downgrade không được làm core Agent chết.
- **Server recovery:** SYN-69–73/76/77/80/82. Phải fail trước mutation sai target, giữ maintenance khi recovery fail, không mở workers/traffic ngoài fence.

### 4.3 Refactor có căn cứ, giới hạn trong boundary hiện hữu

| Candidate | Mục đích cụ thể | Giới hạn |
| --- | --- | --- |
| Effective-config plan chung | SYN-17/18/20/21/26/27/30: baseline, render, stale và redeploy dùng cùng revision/targets | Bắt đầu từ pure plan + parity tests; không rewrite toàn application/config UI |
| Verified CAS publication | SYN-08 phối hợp SYN-10: staging, verify, atomic promote/GC | Sửa Local/MinIO adapters hiện có; FK history vẫn cần policy riêng |
| Supervisor lifecycle/identity | SYN-43/44/46/50: serialization, fixed context, actual identity, typed outcome | Một owner process trong Agent; không dựng orchestration platform |
| OTA attempt journal | SYN-34/35/36 phối hợp SYN-33/37: known-good backup đến readiness commit | Mở rộng external bootstrap/exact switch; không quay lại overlay |
| Support graph recovery | SYN-53–56: correction/closure/retention/reclaim hội tụ | Giữ SQLite graph + HTTP receipts và primitives đã có; không thay bằng broker |
| Artifact deployment contract | SYN-70/71/78/79 phối hợp SYN-80: DB identity, SQL hash/head, compatibility, maintenance | Một contract cho scripts hiện có; không microservices/CQRS/generic repositories |
| Browser authentication boundary | SYN-88/89: restore, refresh, login/logout, `/me`, replay và protected queries dùng cùng epoch | Giữ Axios/AuthContext hiện có thành một contract; không tạo hai single-flight paths |
| Approval scope/action contract | SYN-91/92/94/95/96: eligibility, capability hint, timed cache và step intent | Giữ server authority/snapshots; mixed step scope phải được support rõ hoặc reject rõ |

### 4.4 Cải tiến tùy chọn, chưa là blocker độc lập

- **Worker/storage health:** sau fixes lifecycle/schema, cân nhắc last-successful-sweep và storage diagnostics. Không biến dependency tạm lỗi thành fleet outage bằng readiness quá mạnh. Nguồn: ARC00-02 và giới hạn R10.
- **OTA readiness sâu hơn:** sau minimum bundle gate SYN-37, cân nhắc nhiều heartbeat/IPC readiness nếu pilot chứng minh cần. Không bắt WPF window ở trạm chưa login; R05 chưa xác định ngưỡng mới.
- **Performance:** benchmark scanner tail/materialize/sort, streak query và support retention volume trước đổi batch/parallelism/index. R08 chưa có workload factory; query drill R10 không đại diện tải API thực.
- **Build provenance/toolchain:** pin SDK/EF/inventory nếu cần reproducibility. Không đổi toolchain rộng chỉ để vượt cache thiếu runtime của R05; stale Launcher output SYN-38 vẫn là defect riêng.
- **Docs/test entrypoints:** hướng dẫn build solution phải phân biệt các test projects thực và local two-site setup với server same-origin bundle. Thiếu project trong solution chưa phải lỗi runtime.

### 4.5 Không hành động theo các kết luận sau

- Không coi shared installer-token reuse, không có token riêng mỗi PC hoặc không enrollment thủ công là lỗi. Giữ **installer/script → tự đăng ký PC → /computer**.
- Không bỏ TLS OS trust, Agent claims, scoped blob capabilities, TUS purpose/reservation, release concurrency, execution journal, install-root/reparse containment, pipe SID/session/bounds.
- Không mở lại “mọi delete pending kẹt approval”, “full upload luôn ghi sau release”, “GC chỉ dùng ReferenceCount”, “OTA vẫn overlay”, “scanner không xử lý timestamp bằng nhau”. Findings mới chỉ áp các paths đã dẫn.
- Không biến SYN-06/07 thành yêu cầu thêm cancellation/catalog schema ngay. Trước hết tái hiện failure còn tồn tại qua guards hiện tại và chốt offline contract.
- Không coi desired rendered SHA là bằng chứng file đang chạy đã đổi: PreserveLocal được giữ; phải đo bytes/process trạm.
- Không lấy checklist SEC-005 làm confirmed defect mới: live credential rotation thiếu evidence môi trường. AUTH-004 đã được report 09 tái hiện và map thành SYN-87; vẫn cần PostgreSQL acceptance gate khi sửa.
- Không đếm CFG-002 lần nữa: R01/R04 xác nhận ReloadConfig trả no-op/unimplemented, config-value delivery qua redeploy. Bỏ command hoặc làm reload thật cần chốt capability/outcome trước.
- Không lấy early-return “passed”, test source đã đọc, net48 build hoặc canary binary cũ làm runtime proof của binary hiện hành.
- Không xây DomainEvents outbox/broker, email/push, microservices, CQRS hoặc rewrite để khớp tên abstraction. SYN-83 chưa có business consumer bị mất delivery.

### 4.6 Quyết định cần người phụ trách/sản phẩm

Danh sách này dành cho remediation sau báo cáo, không phải yêu cầu xác nhận để hoàn tất lượt synthesis.

| Quyết định | Mục phụ thuộc | Kết quả cần ghi rõ |
| --- | --- | --- |
| Mixed-scope multi-step approval | SYN-95/96 | Hỗ trợ và snapshot scope từng step, hoặc reject policy khác strategy rõ ràng; không reinterpret request đã submit |
| Historical blob retention | SYN-10 | Giữ object theo history hay tách/purge; ai duyệt data repair, cách restore |
| Release policy immutability | SYN-16 | Draft-only hay policy revision/audit riêng; quyền đổi và history job |
| Baseline gate pin hay invariant liên tục | SYN-17/27/32, R04 questions | Effective sources/scopes; có revalidation ở value/approval/rule-edit hay không |
| Mirror/migration/fan-out semantics | SYN-20/23/25 | Mirror base compatible hay snapshot riêng; semantic equivalence; atomic hay explicit partial apply-all |
| Cancellation offline | SYN-06 | Độ trễ chấp nhận được, cached-manifest/network-loss behavior, điều kiện launch |
| Process adoption/session | SYN-46/47 | Có adopt process có trước không; ownership và active-session policy |
| Test identity/time | SYN-65/67 | Copy giữa sources là cùng/khác event; timestamp/timezone/provenance và backfill |
| Trust/bundle compatibility | SYN-37/42/72/74 | Required inventory, bootstrap deployed, key ownership, trust rollout/offline recovery |
| Server topology/RPO/RTO | SYN-70/80/82 | Writers/traffic admission, object/git backup, pretraffic rollback và posttraffic recovery |
| Domain-event semantics | SYN-83 | Consumer/side effect/SLA cụ thể; nếu không có, chỉ docs/logging semantics |

## 5. Thứ tự khắc phục và cổng kiểm chứng

Không gom mọi sửa đổi thành một release lớn. Có thể phát triển các nhánh độc lập trong tương lai, nhưng **rollout theo dependencies** dưới đây. Lượt hiện tại chỉ tạo báo cáo.

### Giai đoạn 0 — Khóa phạm vi, chuẩn bị evidence và recovery

- Report 09 và phụ lục 06/07 đã có trong canonical evidence. Bổ sung provenance 02/03 nếu có; khi sửa vẫn phải chạy acceptance regressions vì diagnostic probe chỉ tái hiện lỗi.
- Xác định topology/provider/effective DB target/IIS binding và Agent/Launcher/bootstrap versions, parent ACL, cấu hình canary. Chỉ lưu metadata/fingerprint an toàn.
- Chuẩn bị disposable PostgreSQL, Local/MinIO, TUS, IIS, LocalSystem/interactive lab. Lượt chứng minh TLS phải dùng MPROJECT_REQUIRE_TLS_FIXTURE=1; thiếu fixture phải fail gate.
- **Gate/recovery:** backup protected binaries/config/bootstrap, state/token, journal/catalog/cache, payload/PreserveLocal và support SQLite nhất quán; thử restore trên lab. Copy tùy tiện live SQLite/WAL không phải backup đã verified. Không chạy destructive tests trên hệ thống hiện hữu.

### Giai đoạn 1 — Đường deploy server dừng và khôi phục đúng

**Prerequisites:** giai đoạn0, DB/config ownership và target identity.

- SYN-69/70/71/75/76/77/78/81 và guard SYN-74. SYN-72 giữ cert; SYN-73 HTTPS sau trust preparation và đối chiếu auth boundary.
- SYN-79 trước migration chưa phân loại. **SYN-80 là prerequisite của production rollback full-DB**, kể cả một host nếu traffic/workers mở trước verdict; nhiều host phải fence tất cả writers.
- SYN-82 lifecycle chỉ sau khi auto-start/preload không phá maintenance fence.

**Gate:** IIS staging với **hai binary/schema versions thực**, head/hash/identity; fault archive/locked DLL/disk full/restore sau recreate giữ offline. Thử writes/receipts và external storage sentinel trong maintenance window. CanConnect, SCM RUNNING hoặc dump fixture nhỏ không đủ.

**Recovery thực dụng:** giữ archive+dump+predeploy config/binding đến commit, fence writers, retry restore đúng target rồi strict health mới mở traffic. Sau traffic đã ACK, không tự DB rewind mà bỏ object/job/support side effects.

### Giai đoạn 2 — Sửa nhỏ ngăn sai dữ liệu/intent và monitoring

**Prerequisites:** deployment gate giai đoạn1 cho release sử dụng scripts đó.

- Browser SYN-11/28/58/62/64/68; polling và generation guard cùng release.
- Approval drawer SYN-90 và audit snapshot SYN-93 là các guard nhỏ cùng phase, nhưng vẫn giữ test đúng target/projection.
- Server SYN-03/09/12/13/19/31. INI rejection phải không để partial success; phối hợp typed failure tối thiểu SYN-18 nếu cần.

**Gate:** browser/API target/filter/error tests, PostgreSQL DELETE/release và tracker barriers, INI/non-finite negatives. Test expected behavior phải fail trước fix, pass sau; repro assertions lỗi không phải acceptance.

**Recovery trạm:** UI revert không hoàn tác value đã gửi. Lưu config/pin revisions và audit target, thử revert hợp lệ/redeploy, so bytes/PreserveLocal và launch đúng app; không xóa Active/Pending để khớp UI.

### Giai đoạn 3 — CAS và GC đúng retention

**Prerequisites:** history policy SYN-10, DB/object restore rehearsal.

- SYN-08 cả Local/MinIO, SYN-10 GC/FK/candidate isolation. Không đóng canonical chỉ với một provider fix.
- Data repair theo SHA/reference evidence nếu tìm thấy bytes/tombstones hỏng; không repair từ size-only hoặc purge history tùy ý.

**Gate:** real MinIO repair/replay/late PUT, Local interruption, PostgreSQL GC/write lifecycle và reupload SHA. Published readers giữ đúng bytes/hash.

**Recovery trạm:** verified artifact cũ còn dùng được, cold-cache canary download/install; server rollback phải có object bytes phù hợp. Pause GC theo kế hoạch nếu cần giữ recovery sources.

### Giai đoạn 4 — Core Agent/process và control plane

**Prerequisites:** state/identity recovery canary và kênh phân phối đã verified.

- Server SYN-01/03/04/05/84/85 với full negative authority/target-privilege matrix. SYN-86/87 dùng một DB-enforced security generation cho issuance/session replacement.
- Browser SYN-88/89 dùng một auth epoch cho refresh, `/me`, replay và protected queries; SYN-91/92/94 giữ eligibility, target-aware action hints và timed authorization nhất quán.
- Agent SYN-51 isolation; SYN-43/44/46 lifecycle/ownership rồi SYN-45/47/48/49/50. Remote receipts SYN-02 dựa trên target generation/ownership đó.
- Không dựa duy nhất vào OTA đang có lỗi recovery để cứu fixes. Nếu bootstrap canary qua installer/thay binary đã kiểm chứng, giữ token/state và tự đăng ký; **không thêm mandatory manual enrollment**.

**Gate:** PostgreSQL credential/session races và auth sau race; HTTP actor/target scope, large approval inbox, fake-time cache boundaries, deferred browser identity tests; real LocalSystem pipe, concurrent operations, lost ACK/canceled attach, old callbacks/processes, multi-session và net48 Launcher. Không bypass TLS để pass.

**Recovery trạm:** stop chỉ owned payload, restore protected known-good binaries/bootstrap và compatible state; kiểm token/heartbeat/poll, catalog/cache, đúng một payload, Launcher console và support graph nguyên trước mở rộng cohort.

### Giai đoạn 5 — OTA đến readiness commit

**Prerequisites:** verified core bundle, SYN-74 public-only guard, SYN-72 stable TLS, recovery helper thực trên canary.

- SYN-33 ACL; SYN-34/35 switch-cleanup-journal; SYN-36 latch; SYN-37 shipping parity; SYN-38 clean output; SYN-39 download bounds; SYN-40 result ACK.
- Kiểm bootstrap/v1 deployed chứa fix; source mới mà reuse helper cũ chưa đủ.
- SYN-41 retention sau journal/reference correctness; SYN-42 rotation là rollout riêng.

**Gate:** signed A→B→C, SCM/LocalSystem/reboot/kill từng phase, locks/disk/network/no-heartbeat NEW, full Launcher dependencies, exact bytes/ACL. Không có user login khác thiếu required Launcher binary.

**Recovery trạm:** external helper restore OLD khi NEW không ready; backup OLD không overwritten qua retries. Manual restore khi helper hỏng giữ token/outbox/catalog/PreserveLocal. Chỉ mở rộng sau canary, theo dõi durable outcomes.

### Giai đoạn 6 — Config và migration theo revisions

**Prerequisites:** SYN-04 graph, CAS correctness, 09 cho approval authority, các quyết định baseline/mirror/fan-out/release policy.

- Destination guards/typed results → shared plan/parity SYN-17/18/20 → stale revisions SYN-21/30 → invalidation SYN-26 → DTO SYN-29 và approval revalidation SYN-27.
- Legacy conversion SYN-22/23/24 cùng reviewed transaction, dry-run trước; SYN-25 atomic/explicit partial. SYN-16/32 chỉ sau quyết định contract.
- Approval multi-step SYN-95/96 chỉ sau khi chốt có hỗ trợ mixed `ScopeStrategy`; nếu không hỗ trợ, validate/reject rõ thay vì tạo semantics ngầm.

**Gate:** baseline/preview/pin/manifest khớp effective bytes; PostgreSQL edit/approve/migrate/pin barriers; không partial output/wrong target/Pending orphan; Active+Pending và legacy file mọi scope.

**Recovery trạm:** lưu pin/config/declaration revisions và legacy file, revert hợp lệ/redeploy trong window rồi kiểm **actual** file/PreserveLocal/app. DB rollback không tự đổi bytes đã deploy.

### Giai đoạn 7 — Support recovery và ingestion consistency

**Prerequisites:** core isolation, compatible IPC, real PostgreSQL và identity/time decisions nếu đổi schema.

- Support SYN-52–56 rồi SYN-57/59/60/61; SYN-58 có thể ship giai đoạn2.
- Scanner SYN-63/metrics SYN-66; SYN-65/67 không đổi lịch sử trước khi chốt semantics. Monitor SYN-62/64/68 đã hiển thị đúng dữ liệu mới.

**Gate:** SQLite/PostgreSQL/HTTP loss trước-sau commit, force-resolve/offline queue, retention/capacity clock, InFlight recover không restart, MVC strict enum, browser auth-scope. Scanner full batches vượt overlap/restart; registry/clock identity fixture khi đổi reporter.

**Recovery trạm:** giữ exact IDs/payload và cursor compatibility; không clear queue/reset cursor toàn fleet. Pending qua OTA/restart giao một logical event; backfill bound/dedup để tránh overload/double count.

### Giai đoạn 8 — Đóng verification/decisions còn lại

- SYN-06/07 chỉ thành implementation khi có concrete failure/requirement; test pass phù hợp contract thì đóng verification, không thêm schema vì phỏng đoán.
- SYN-15 theo caller inventory; SYN-42 trust decision; SYN-65/67 event semantics; SYN-83 consumer requirement; SYN-95/96 theo quyết định multi-step approval.
- Optional improvements cần số liệu/value, không trì hoãn confirmed fixes.
- **Gate chung:** regression đúng boundary, old/new compatibility, rollback thực cho station changes, data repair có evidence và kết quả ghi đúng revision/environment. Không đổi checklist COMPLETED trong lượt synthesis này.

## 6. Quyết định tốt, nghi vấn bác bỏ và câu hỏi chưa đủ bằng chứng

### 6.1 Những thiết kế/behavior cần giữ

- **Provisioning owner-approved:** một installer token dùng cho nhiều PC; script tạo identity/state từng PC mới và máy xuất hiện trên /computer. Shared token không tự chiếm identity/station đã provision; unrelated authorization/upload checks vẫn giữ.
- **Auth/capability:** Agent identity từ claims; client token snapshot/generation và durable save-before-switch; HMAC bind Agent/job-or-release/SHA/path/expiry và re-check live graph. SYN-01 là server-write race khác delayed-401 client race đã sửa.
- **Content/deployment:** TUS purpose/actor/target/reservation/session binding, hash verification, release concurrency, active-job unique constraints, execution journal/cancel token, canonical package roots/reparse checks. AutoRemoveOnUnassign=false vẫn giữ installed/unmanaged.
- **Config:** whole-file Computer > Station > Model rồi value layers; only live/Active/MaxScope, packaged model values, Active tách Pending, không tự rebase snapshot, bulk preflight và Station-before-Computer staging, targets chỉ map vào live package files.
- **OTA/IPC:** signed manifest/exact staging/external bootstrap; state/outbox ngoài binaries; bundle version alignment; IPC netstandard2.0 cho net48/net8, IPC capabilities độc lập OTA v1; pipe active-console/LocalSystem, frames/deadlines và explicit entry commands.
- **Support:** ACK sau SQLite commit, WAL/FULL/constraints, immutable exact payload fingerprint, server replay trước current/employee validation, live ownership, recovery permission/audit/locking; không purge nonterminal graph, metrics labels có allowlist.
- **Monitoring:** shared parser/corpus/persisted enum values, giữ Partial/Unstructured/Unknown, stable cursor/save-after-ACK, khoảng thời gian [from,to), Sfis production default và streak lookback qua ca; registry snapshots riêng log totals.
- **Server:** scripts là migration owner production, AutoMigrate=false, idempotent SQL/ON_ERROR_STOP, API/SPA same-origin và protected fallback routes; dev-secret/publish guards, strict main TLS probes, watchdog30/180, operator overrides và restore drills trong đúng phạm vi.

### 6.2 Nghi vấn đã bác bỏ hoặc thu hẹp

| Nghi vấn/historical note | Kết luận cần giữ |
| --- | --- |
| Token chung tự nó là lỗi hoặc phải chuyển per-PC | Loại theo AGENTS/owner; không đổi enrollment để sửa lỗi không liên quan |
| Revoke tuần tự luôn hỏng / hai enroll thường luôn cùng thành công | R01 bác bỏ; Computer.Version chặn race có Computer write. SYN-01 giới hạn Agent-only interleavings |
| Biết SHA/plain Local URL đủ bypass download | Hiện deny thiếu auth/cap/graph; SYN-04/15 là valid-download rejection/contract mismatch |
| document.own vẫn TUS-upload software như UPL-001 cũ | R03 HTTP checks pass; direct PUT/CAS defect khác boundary |
| Checklist REL-001/STO-002/CFG-001/OTA-001/LOG-001 đóng nghĩa mọi paths an toàn | Fixes cũ còn giá trị; findings mới chỉ mở các root/path cụ thể |
| Process race chứng minh pipe authorization bị bypass | R06 không chứng minh vậy; authorized concurrent callers vẫn gây defects |
| Missing TestResult link sửa nhầm support record | R08 chỉ thấy có thể thiếu Sfis link; ingestion không sửa support request khác |
| Server query cấp quyền theo old owner snapshot | R07 dùng live ownership; SYN-57 ở DOM/observer browser |
| Production tự migrate / watchdog defaults vẫn lệch | R10 bác bỏ defaults đó; SYN-71 là ready không kiểm schema |
| Thiếu CORS port làm default console hỏng | Same-origin không cần cross-origin allowance; custom split-origin là config riêng |
| Hai host chắc chắn GC delete trùng | GC có advisory lock; SYN-80 là maintenance/full-restore boundary |
| DomainEventDispatcher là support durable outbox | Không đúng: logging-only; support có SQLite/HTTP receipts riêng |
| R02 không confirmed defect nghĩa station lifecycle đã certify | Test bị DLL lock, không revision và chưa environment gates; không phủ định R06 repros |

### 6.3 Câu hỏi cần evidence trước khi thêm hoặc đóng công việc

| Câu hỏi | Evidence/điều kiện quyết định |
| --- | --- |
| Identity/RBAC/generic approvals còn thiếu runtime evidence nào? | Report 09 đã xác định SYN-84–96; cần PostgreSQL concurrent login/approval, browser/IIS smoke, multi-tab và large-inbox performance trước khi đóng các finding tương ứng |
| Current SEC-001 containment fix đã pass đúng identity chưa? | Checklist06-09 ghi implemented nhưng identity validation pending. LocalSystem/reparse/ACL với **binary hiện hành**, không dùng canary27-08 làm proof; chưa thêm exploit finding từ thiếu fixture |
| Effective TLS/cert/bootstrap/ACL trên trạm/server là gì? | Inventory metadata an toàn, trusted TLS negatives, LocalSystem effective access; source defaults chưa chứng minh live leakage |
| MinIO/TUS/Local boundary thật có giống probes không? | Real MinIO replay/repair, HTTP final-PATCH teardown/drop, Local interruption; fake store/source không thay provider proof |
| Historical FK/tombstone/config migration data tồn tại bao nhiêu? | Read-only relationships/counts và semantic preview được phép; không log payload/secret, chốt retention/repair trước mutation |
| Catalog/cancellation hội tụ khi crash/offline thế nào? | Stage-specific SYN-06/07 fault matrix, measured stop latency và restore snapshot; chỉ nâng confirmed khi có failure |
| Mixed versions support có giữ semantics không? | HTTP malformed-success/MVC matrix, PostgreSQL cases **thực thi assertions**, LocalSystem outbox qua OTA/schema-forward rollback |
| Mất IPC ACK sau local commit có retry idempotent không? | WPF/real pipe drop response, retry intent, kiểm IDs/receipts; HTTP receipt không tự chứng minh cả IPC |
| Log event time/identity và registry multi-user có đúng không? | Writer/copy contract, time provenance, bounded workload benchmark, multi-hive/registry-view fixtures trước schema/dedup changes |
| Backup có đạt RPO/RTO và external-state recovery không? | Representative restore DB/object/git, writer fence, off-host/schedule/PITR nếu chọn. 2.543s trên DB fixture chưa có support traffic không là RTO factory |
| Metrics deployment thực có key/proxy không? | Effective endpoint boundary audit; anonymous200 ở fixture chưa là production exposure finding |
| DomainEvents có consumer business cần delivery không? | Consumer/side effect/SLA cụ thể; nếu không thì SYN-83 dừng ở semantics/docs |

## 7. Kiểm tra bản tổng hợp và điều kiện bàn giao

- **99 finding IDs** từ mười một reports được map **đúng một lần** vào 96 canonical issues; ba cặp gộp giải thích ở phần 1.1 và R09-01..13 map một-một thành SYN-84..96 ở phần 1.2. Mỗi mục có files/symbols, evidence/scenario, impact, hướng sửa, change-risk/rollback, required regression và scope.
- Bản tổng hợp gốc kiểm tra **114 source files**; META-00 đối chiếu report/probe availability và mapping, không dùng con số đó để tuyên bố source audit mới hoặc current-code acceptance.
- Đối chiếu [runtime08](08-runtime-results.json), [frontend08](08-frontend-probes.json), [Agent TRX08](08-agent-tests.trx), [Backend TRX08](08-backend-tests.trx), [runtime10](10-server-deployment-database-and-recovery-runtime-results.json) và availability của phụ lục. Không cộng focused suites khác ngày/phạm vi thành full-suite pass.
- Không chạy lại product suites/IIS/station/deploy trong synthesis. Regression từng mục là **test cần chạy khi khắc phục**; assertions của diagnostics cũ chứng minh lỗi hiện tại.
- **Còn thiếu cho permissions/approval và rollout tổng thể:** quyết định mixed-step policy, PostgreSQL/browser/IIS/provider/LocalSystem gates của đúng changes và artifacts thực. Báo cáo này hoàn tất canonical synthesis, không đóng hoặc thay những gate đó.


[r00]: 00-architecture-report.md
[r01]: 01-agent-enrollment-and-control-plane-report.md
[r02]: 02-station-deployment-job-lifecycle-report.md
[r03]: 03-software-content-upload-and-blob-lifecycle-report.md
[r04]: 04-config-overrides-baselines-and-approval-report.md
[r05]: 05-agent-release-and-self-update-report.md
[r06]: 06-launcher-ipc-and-process-supervision-report.md
[r07]: 07-production-support-durable-workflow-report.md
[r08]: 08-test-result-ingestion-and-monitoring-report.md
[r09]: 09-identity-rbac-approvals-and-console-contract-report.md
[r10]: 10-server-deployment-database-and-recovery-report.md
