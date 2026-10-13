# 09 — Identity, RBAC, approvals và hợp đồng browser console

Ngày review: **2026-09-09, Asia/Bangkok**. Prompt: `docs/code-review/prompts/09-identity-rbac-approvals-and-console-contract.md`; SHA-256: `33d18620479e9143c1694e1e0f592472b0cfac48c70ba99b35944bd827f52bf9`. Repository revision: **`21c8cb3b5cb12217d8fba4581e33f12e43f635b0`**. Đối chiếu source trong workspace, không lấy kết luận của báo cáo cũ làm bằng chứng hiện tại.

Đã đọc `AGENTS.md`, `MProjectFrontend/PROJECT_MEMORY.md`, các mục SEC-003, AUTH-001–004 và FE-001–005 trong `MProjectFrontend/PROJECT_REVIEW_CHECKLIST.md`. Không sửa production code, migration, project memory hay checklist. Probe dùng dữ liệu giả, database InMemory/SQLite của test và HTTP loopback; không thao tác tài khoản, station hoặc database vận hành. Nội dung thay đổi độc lập trong `docs/code-review/note effort.txt` được giữ nguyên.

## Kết luận

Có **12 confirmed defects và 1 design risk**: **5 P1, 8 P2**. Hai kiểm tra HTTP tái hiện được authorization bypass; các race identity và lỗi browser được tái hiện bằng interleaving có kiểm soát. Đây là báo cáo trước remediation, không phải bằng chứng hệ thống đã được sửa.

| ID | Severity | Confidence | Category | Kết quả chính |
| --- | --- | --- | --- | --- |
| R09-01 | P1 | High | confirmed defect | Tạo assignment bỏ qua ACL Deny tại station đích |
| R09-02 | P1 | High | confirmed defect | Người chỉ có `user.manage` chiếm tài khoản có quyền cao hơn qua reset password |
| R09-03 | P1 | High | confirmed defect | Login đang xử lý sống sót qua đổi mật khẩu bằng refresh token phát muộn |
| R09-04 | P2 | High | confirmed defect | Hai login đồng thời vẫn tạo hai refresh family với SingleDeviceLogin |
| R09-05 | P1 | High | confirmed defect | Refresh và `/me` trả muộn khôi phục hoặc ghi đè phiên đăng nhập mới |
| R09-06 | P1 | High | confirmed defect | Cache riêng tư ngoài support còn đọc được sau đổi tài khoản |
| R09-07 | P2 | High | confirmed defect | Approval drawer nhận response cũ và gửi action cho request cũ |
| R09-08 | P2 | High | confirmed defect | 500 request mới không liên quan làm biến mất request người duyệt cần xử lý |
| R09-09 | P2 | High | confirmed defect | Action gates dùng danh sách quyền navigation, bỏ qua ACL Allow hợp lệ |
| R09-10 | P2 | High | confirmed defect | Audit revoke ghi cùng trạng thái đã xóa ở cả before và after |
| R09-11 | P2 | High | confirmed defect | ACL Deny có StartTime không vô hiệu cached Allow đúng thời điểm |
| R09-12 | P2 | High | confirmed defect | Policy nhiều bước dùng scope của bước đầu cho mọi bước |
| R09-13 | P2 | High | design risk | Action không mang expected version/step, replay có thể duyệt tiếp bước sau |

P1 cần xử lý trước khi dựa vào delegated administration, ACL station hoặc chuyển tài khoản trên browser dùng chung. R09-12/13 phát sinh với policy nhiều bước; seeder hiện tại tạo các policy một bước, chưa xác nhận deployment nào đang dùng cấu hình nhiều bước.

## Phạm vi và trace bắt buộc

| Trace | Source và kết luận |
| --- | --- |
| Sign-in → API → token → request sau | `SignInForm` → `use-login-form` → `use-login` → `api/auth.ts` → Axios → `AuthController.Login/Refresh` → `AuthService`/`RefreshTokenService`. JWT ký HS256 có identity và `auth_version`, không nhúng quyền RBAC. `Program.cs:361` đăng ký JwtBearer, kiểm signature/issuer/audience/lifetime và gọi `AccessTokenSecurityStateValidator` đối chiếu Active user/version trên từng request. Register tạo Pending user và không phát token; disable/password change thông thường vô hiệu JWT cũ. Race cấp refresh nằm ở R09-03/04. |
| Sensitive mutation → permission → scope | Controller dùng `RequirePermission` → `PermissionAuthorizationFilter` → `PermissionService` → `AuthorizationService` → subject, scope hierarchy, RBAC, ACL và owner whitelist. Deny được ưu tiên trước Allow và owner. Invalidation có user/role/resource generation; membership được đọc lại và TTL xét EndTime. Đã kiểm HTTP create assignment và activate tại station bị Deny; R09-01 là khác biệt thực tế giữa hai endpoint. |
| Role/ACL admin → audit → UI | `AuthorizationController` yêu cầu global `assignment.manage`; service kiểm lại scope, subject, grantability và self-impact. `RolesController`/`RoleService` chặn sửa system role, quyền ngoài actor và role actor đang/sắp giữ. Mutation role/ACL invalidates subject/role cache. `UserRolesPanel`/`AssignRoleModal` tải lại bảng sau thao tác. R09-02 đi qua credential administration, không đi qua các guard của RoleService. R09-10 làm sai before snapshot trong audit revoke. |
| Submit/approve/reject/cancel → snapshot → handler | `ApprovalService.SubmitAsync` kiểm handler, policy active/đúng target/có bước và first-step approvers; lưu step và target snapshots. Resolver loại requester/inactive user, áp role/membership hiện hành. Approve/Reject cần eligible approver; Cancel chỉ requester. Assignment handler kiểm policy hiện tại trước approve. Handlers hiện hành chủ yếu sửa tracked entities để service SaveChanges cùng request/action. Version concurrency và unique pending index có trong EF model. Scope nhiều bước và replay ở R09-12/13. |
| Console → API permission/DTO | Assignment UI tách package CRUD/publish/assignment permissions, phân biệt Pending/Rejected/Cancelled và dùng DTO approval; pin server chặn Pending. Agent Releases action dùng `agentrelease.manage`, endpoint cũng kiểm quyền này. Support recovery dùng `productionsupport.recover` ở UI/controller/service; read tiếp tục owner-team scoped. Config/override/baseline consumers có service-level scope checks, các notice admin là quyết định UI đã ghi nhận. R09-09 đánh giá sự lệch giữa projection navigation với action availability, không xem UI là security boundary. |

Các file ngoài scope được đọc giới hạn để chứng minh consumer: `StationSoftwareAssignmentService` ở creation/pin/activation guard; `TeamService` ở membership/leader; repository content hook/controller ở cache riêng tư; support ở permissions/response. Không review lại Agent credentials (`01`), job execution (`02`), config target effects (`04`) hoặc support outbox delivery (`07`).

## Kiểm thử và bằng chứng

| Nhóm | Kết quả hiện tại | Giới hạn |
| --- | --- | --- |
| Backend tests hiện có thuộc auth/RBAC/approvals, resource queries, team, seeder | **160 pass**, 0 fail | Hai lượt không trùng: 133 + 27. Gồm auth-version, JwtBearer revocation HTTP fixture, SQLite refresh rotation, scoped authorization, mutation/cache/owner/query và approval handlers. |
| Backend probes bổ sung | **9 pass**, 0 fail | Assertions xác nhận hành vi lỗi hiện tại; có actual controllers/JwtBearer/permission services trên Kestrel loopback và barrier login. Các probe orchestration còn lại dùng EF InMemory. |
| Frontend tests hiện có | **120 pass**, 0 fail | 12 test files: token/access rules/RequireAccess/SupportQueryIsolation, approvals và software/deployment consumers. |
| Frontend probes bổ sung | **5 pass**, 0 fail | Axios thật với adapter giả, AuthContext/component/hooks thật; API và trình bày Ant Design được mock trong jsdom. |

Lượt backend đầu không chạy được test vì API output đang bị một tiến trình khác khóa. Lượt thành công dùng output riêng `.review09-build/backend`. Lượt frontend đầu bị sandbox chặn esbuild đọc thư mục cha; chạy lại nhóm test ngoài sandbox được auto-review chấp nhận. Không dừng tiến trình API/IDE của người dùng. Các lỗi fixture ban đầu đã được sửa trước lượt kết quả cuối; chúng không được tính là product failures.

**Pass của diagnostic probe nghĩa là tái hiện được vấn đề, không phải regression đã đạt trạng thái mong muốn.** Các artifact tạm và source probe đã được dọn theo phạm vi bàn giao chỉ gồm báo cáo này. Không dùng InMemory để khẳng định transaction rollback, unique index hoặc race PostgreSQL đã được xác minh. Không chạy browser smoke với IIS, full backend/frontend suites hoặc build production trong review này.

### HTTP matrix đã chạy

| Actor/thiết lập giả | Request trực tiếp | Quan sát |
| --- | --- | --- |
| Delegated user chỉ có `user.manage` | `GET /api/v1/roles` | **403** |
| Cùng actor | `PUT /api/v1/users/{privileged-user}/password` | **204**; mật khẩu tài khoản đặc quyền đổi |
| Login tài khoản đặc quyền bằng mật khẩu vừa đặt | `GET /api/v1/roles` | **200** |
| Actor có global `software.assignment.manage`, ACL Deny tại station S | `POST /api/v1/software-assignments` với station S, package không cần approval | **200**; assignment Active được lưu |
| Cùng actor và assignment vừa tạo ở S | `POST /api/v1/software-assignments/{id}/activate` | **403** |

JWT được cấp bởi AuthService và xác thực bằng JwtBearer cùng security-state validator. Đây là host fixture cục bộ, không phải kiểm thử IIS production. Không giả lập AllowAll ở permission filter của matrix này.

## Findings

### R09-01 — Assignment creation không kiểm ACL tại station đích

- **Severity:** P1.
- **Confidence:** High; tái hiện HTTP và kiểm trạng thái persisted.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Api/Controllers/Software/SoftwareAssignmentsController.cs:43`; `MProjectBackend/MProject.Api/Filters/PermissionAuthorizationFilter.cs:57`; `MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs:53`.
- **Symbol:** `Assign`, `TryResolveResourceIdAsync`, `AssignAsync`.
- **Evidence:** Create chỉ có `[RequirePermission(ManageSoftwareAssignments)]`, nên filter truyền resource null. Service đọc StationResourceId trong body nhưng không kiểm quyền cho resource đó. Probe `DirectHttp_UserManagerTakeover_AndScopedAssignmentDenyBypass` xác nhận evaluator trả false tại S, POST create trả 200/lưu Active, trong khi POST activate cùng S trả 403.
- **Finding:** Global grant vượt được explicit station Deny qua đường tạo assignment. Scope enforcement khác nhau giữa create và mutation trên assignment đã tồn tại.
- **Realistic failure scenario:** Engineer được giao quản lý assignment toàn hệ thống nhưng bị ACL Deny tại station đang khóa bảo trì. Engineer gọi trực tiếp create với station đó và package không cần approval.
- **Operational impact:** Tạo deployment intent Active tại station actor bị cấm. Review này xác minh DB intent, không chạy Agent deployment.
- **Root cause:** Authorization không bind target nằm trong request body; service không bù kiểm tra scope.
- **Recommended direction:** Kiểm `software.assignment.manage` trên StationResourceId đã validate ở application mutation boundary; thiết kế gate create cho cả scoped grant hợp lệ và global grant có scoped Deny. Rà cùng pattern ở read/create consumers trước khi khẳng định ACL bảo vệ đầy đủ.
- **Required regression test:** HTTP matrix global Allow + station Deny, team Deny inherited, scoped Allow ở S và station khác; request bị từ chối không tạo assignment, approval hoặc audit success.
- **Estimated change scope:** Small.

### R09-02 — Delegated user manager có thể nhận quyền của tài khoản đặc quyền

- **Severity:** P1.
- **Confidence:** High; actual HTTP controllers và login bằng credential mới.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Api/Controllers/Identity/UsersController.cs:100`; `MProjectBackend/MProject.Application/Services/Identity/UserService.cs:453`.
- **Symbol:** `ResetPassword`, `ResetUserPasswordAsync`.
- **Evidence:** Guard duy nhất là `user.manage`; không so privilege của actor/target và không bảo vệ Admin/system-role holder. HTTP probe bắt đầu với actor chỉ có user.manage, GET Roles 403; reset tài khoản có role.manage 204; login vào target và GET Roles 200.
- **Finding:** Cấp quyền quản lý user thực tế cho phép chiếm mọi human account mà actor biết ID/username, dù không được cấp quyền quản trị role hay quyền của target. Guard chống tự nâng quyền trong RoleService không bảo vệ đường credential này.
- **Realistic failure scenario:** Admin tạo custom role cho helpdesk hỗ trợ tài khoản thông thường. Helpdesk reset mật khẩu một Admin; ID/username có thể đã biết từ console hoặc công việc hỗ trợ trước đó, không cần dò token.
- **Operational impact:** Chiếm quyền publish, assignment, recovery hoặc quản trị RBAC mà tài khoản target đang có. User.manage trở thành quyền tương đương quản trị mọi human identity nhưng không được biểu diễn như vậy trong mô hình delegation.
- **Root cause:** Tách quyền thao tác role nhưng không có boundary tương ứng cho thao tác chiếm credential của role holder.
- **Recommended direction:** Tách reset tài khoản đặc quyền thành capability rõ ràng hoặc áp target-privilege/grantability policy; bảo vệ system-role accounts. Đồng bộ UI và audit credential operations với chính sách đó.
- **Required regression test:** Actor có riêng user.manage reset được user thông thường nhưng không reset Admin/actor có quyền ngoài phạm vi delegation; xác minh HTTP denial, hash/AuthVersion/refresh rows không đổi. Có positive case cho credential administrator được phép.
- **Estimated change scope:** Medium.

### R09-03 — Đổi mật khẩu không chặn refresh session của login đang xử lý

- **Severity:** P1.
- **Confidence:** High tại service interleaving; chưa chạy PostgreSQL barrier.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:100`, `:133`, `:164`; `MProjectBackend/MProject.Application/Services/Identity/RefreshTokenService.cs:43`; `MProjectBackend/MProject.Domain/Entities/Identity/RefreshToken.cs`.
- **Symbol:** `LoginAsync`, `ChangePasswordAsync`, `RefreshAsync`, `IssueAsync`.
- **Evidence:** Probe pause ngay trước refresh Issue, sau khi password cũ đã verify/JWT cũ đã dựng; context khác đổi password, tăng AuthVersion và revoke xong; resume login. Access JWT trả muộn vẫn mang version cũ, nhưng refresh token mới được lưu. Một context mới dùng token đó refresh thành công và nhận JWT mang AuthVersion hiện tại.
- **Finding:** Refresh flow nâng credential phát từ lần xác thực cũ sang security state mới, làm mất hiệu lực ý nghĩa “đổi mật khẩu thu hồi session cũ”.
- **Realistic failure scenario:** Login bằng mật khẩu đã lộ đang chờ I/O đúng lúc chủ tài khoản/admin đổi/reset mật khẩu. Token mới chưa tồn tại lúc RevokeAll quét nên không bị thu hồi.
- **Operational impact:** Session dùng lần xác thực mật khẩu cũ tiếp tục cấp access JWT hợp lệ sau thao tác bảo vệ tài khoản.
- **Root cause:** Password verification, user version update, revoke và refresh issuance không chia sẻ atomic boundary; refresh row không ràng buộc AuthVersion tại lần xác thực gốc.
- **Recommended direction:** Bind refresh family với security generation đã xác thực; issue/rotate chỉ thành công nếu generation còn hiện hành. Serialize/conditionally claim theo user trên database, bao gồm login và password/security mutations, không chỉ thêm lock trong một API process.
- **Required regression test:** PostgreSQL hai context/barrier tại verify→issue và rotate→revoke, với change/reset password; mọi token bắt nguồn từ generation cũ không mint được JWT generation mới. Bao phủ disabled user và explicit logout-all nếu bổ sung workflow đó.
- **Estimated change scope:** Medium.

### R09-04 — SingleDeviceLogin vẫn cho phép hai refresh families

- **Severity:** P2.
- **Confidence:** High; tái hiện deterministic bằng hai context và services thật.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:111`; `MProjectBackend/MProject.Application/Services/Identity/RefreshTokenService.cs:186`; `MProjectBackend/MProject.Domain/Options/AuthTokenOptions.cs:13`.
- **Symbol:** `LoginAsync`, `RevokeAllActiveForUserAsync`.
- **Evidence:** Probe cho cả hai login hoàn tất revoke trước khi một trong hai Issue chạy; kết quả có hai active refresh rows thuộc hai family. Option đang true. Đây là xác minh mới cho backlog AUTH-004, không chỉ sao chép checklist.
- **Finding:** Revoke trước issue không serialize các login cạnh tranh.
- **Realistic failure scenario:** User đăng nhập gần đồng thời ở hai browser, hoặc gửi hai lần form khi mạng chậm.
- **Operational impact:** Hai session tiếp tục refresh dù cấu hình quảng bá single-device. Access JWT đã cấp trước đó cũng không được thu hồi chỉ bởi login mới; không diễn giải option hiện tại là bảo đảm một access token duy nhất.
- **Root cause:** Hai thao tác độc lập không có user session generation/DB lock hoặc uniqueness invariant phù hợp.
- **Recommended direction:** Dùng cùng cơ chế generation của R09-03 cho replace-session; xác định rõ option quản lý refresh family hay cả access session.
- **Required regression test:** Concurrent login PostgreSQL chỉ để một family hợp lệ; refresh bằng family thua phải bị từ chối. Test logout/rotate/login xen kẽ và retry sau lost response.
- **Estimated change scope:** Medium.

### R09-05 — Browser nhận response cũ sau khi đổi phiên

- **Severity:** P1.
- **Confidence:** High; 3 frontend probes tái hiện bằng deferred responses.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/api/services/axios-client.ts:84`, `:101`; `MProjectFrontend/src/contexts/AuthContext.tsx:59`, `:83`; `MProjectFrontend/src/components/header/drop-down/UserDropDown.tsx:37`.
- **Symbol:** `performRefresh`, response interceptor, `refreshMe`, `syncAuthState`.
- **Evidence:** Hai Axios probes bắt đầu refresh A, clear token, tùy ca login B, rồi trả refresh A: storage bị ghi lại token A và original request được replay bằng A. Probe AuthContext clear token khi `/me` đang chờ: response A trả muộn đặt lại `me=A`, `isLoggedIn=true`. Chỉ branch AuthContext không tự tạo token mới, nhưng vẫn khôi phục identity/UI cũ.
- **Finding:** Logout/login không tạo boundary cho công việc HTTP/auth đang bay; response thuộc phiên trước được phép cập nhật phiên hiện tại. Bare refresh trong AuthContext và interceptor còn là hai đường single-flight riêng.
- **Realistic failure scenario:** Operator logout trên LAN chậm, người khác login ngay cùng tab; refresh/profile cũ hoàn tất sau thao tác chuyển người.
- **Operational impact:** UI hiển thị identity không khớp token, hoặc người mới tiếp tục thao tác dưới token của người cũ. Audit server có thể ghi actor A cho hành động người B thực hiện sau đó.
- **Root cause:** Không có auth generation/expected-token check ở success, failure và retry; logout chỉ xóa storage.
- **Recommended direction:** Một coordinator cho restore/refresh với epoch thay đổi khi logout/login; kiểm epoch và token đã dùng trước persist, setMe, clearToken hoặc replay. Hủy/reject queue thuộc phiên cũ, không replay mutation qua boundary identity.
- **Required regression test:** Deferred refresh và `/me` đi qua logout→login B; stale 401/error không xóa B; late success không phục hồi A; pending writes không replay bằng actor khác; startup restore và interceptor chỉ rotate một lần.
- **Estimated change scope:** Medium.

### R09-06 — Cache riêng tư không được cô lập giữa user ngoài support

- **Severity:** P1.
- **Confidence:** High ở real query hook/cache; server private-repository check đã trace bằng source.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/App.tsx:88`; `MProjectFrontend/src/components/auth/SupportQueryIsolation.tsx:17`; `MProjectFrontend/src/hooks/api/repositories/use-get-file-content.ts:20`; `MProjectFrontend/src/hooks/api/repositories/use-get-repository-overview.ts:15`.
- **Symbol:** App QueryClient, `purgeSupportQueries`, `useGetFileContent`.
- **Evidence:** QueryClient sống qua SPA logout/login; purge chỉ chọn production-support. Repository overview/tree/content keys không chứa identity và staleTime 10 phút. Probe load content giả của A, đổi B và phát token event: hook trả cache A, không gọi API lần hai dù mock API sẽ trả 403 cho B. `GitAPIRepositoriesController.GetBlob` có HasAccess/Forbid, nên đây là browser disclosure, không phải kết luận server bỏ auth.
- **Finding:** SessionStorage isolation không cô lập dữ liệu giữ trong React Query của cùng tab.
- **Realistic failure scenario:** A xem private repository rồi logout; B có quyền vào trang repositories nhưng không là member của repository đó, mở URL vừa dùng/back history trong cùng SPA.
- **Operational impact:** B có thể đọc nội dung private A đã cache trong thời gian cache còn tồn tại. Các key như my-profile cũng chia sẻ identity; chưa khẳng định mọi page đều có cùng mức lộ dữ liệu.
- **Root cause:** Cơ chế isolation được giới hạn cho support, trong khi QueryClient và các protected query keys khác dùng chung giữa users.
- **Recommended direction:** Identity-scope mọi protected cache hoặc hủy/xóa toàn bộ protected queries ở auth boundary; loại stale response trước khi nó có thể populate cache mới. Giữ public cache riêng nếu có nhu cầu rõ ràng.
- **Required regression test:** A fetch private repo overview/tree/blob → logout → B mở cùng URL: không render dữ liệu A và API B nhận denial. Bao phủ request của A đang chờ khi B login, profile và user/team scope changes.
- **Estimated change scope:** Medium.

### R09-07 — Response cũ trong approval drawer có thể đổi request được thao tác

- **Severity:** P2.
- **Confidence:** High; component probe tới action dispatch.
- **Category:** confirmed defect.
- **File:** `MProjectFrontend/src/components/approvals/ApprovalActionDrawer.tsx:91`, `:100`, `:116`, `:154`, `:212`.
- **Symbol:** effect tải detail, `load`, `handleApprove`, `handleCancel`.
- **Evidence:** Không có generation/abort guard cho load; khi đổi ID vẫn giữ request trước đó trong lúc loading và footer không gate loading. Probe mở A → đóng → mở B → B trả trước → A trả sau; drawer đang được yêu cầu mở B hiển thị A, Cancel gửi ID A.
- **Finding:** Selected request của parent không còn là invariant của dữ liệu/action trong drawer.
- **Realistic failure scenario:** Người duyệt/requester chuyển nhanh giữa hai request trên đường mạng có độ trễ khác nhau, hoặc bấm footer còn lại trong lúc detail mới đang tải.
- **Operational impact:** Approve/reject/cancel nhầm request mà actor vẫn có quyền xử lý. Server không thể phát hiện ý định lựa chọn B nếu client gửi ID A hợp lệ. Probe xác minh Cancel; approve/reject dùng cùng mutable `request.id`.
- **Root cause:** Async detail và action state không bind vào selected ID/generation, modal/comment state cũng không được reset đầy đủ khi đổi request.
- **Recommended direction:** Clear detail/action state khi đổi ID/đóng drawer; chỉ nhận latest response; disable footer khi loading/submitting hoặc data.id khác selected ID. Bind confirm vào snapshot đã mở.
- **Required regression test:** A trả sau B, close→reopen, load B thất bại khi A đang có dữ liệu; mọi action chỉ gửi current selected ID và không chạy khi detail chưa hợp lệ.
- **Estimated change scope:** Small.

### R09-08 — Inbox cắt tập toàn hệ thống trước khi lọc người duyệt

- **Severity:** P2.
- **Confidence:** High; probe 501 pending requests.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:19`, `:307`; `MProjectFrontend/src/pages/Approvals/Inbox.tsx:63`.
- **Symbol:** `GetInboxAsync`, `MaxInboxScan`.
- **Evidence:** Query lấy 500 newest Pending không do actor submit, rồi mới CanAct và paginate. Probe có một request cũ actor xử lý được và 500 newer thuộc role khác: detail của request cũ CanCurrentUserAct=true nhưng Inbox trả Total=0/items rỗng.
- **Finding:** Server-driven pagination ở FE-003 không khắc phục truncation trước authorization ở backend.
- **Realistic failure scenario:** Hệ thống tích lũy nhiều approval từ các station/team khác; việc của approver ít hoạt động bị đẩy ra khỏi scan window.
- **Operational impact:** Request có thể chờ vô hạn, badge/total báo thiếu hoặc bằng không; chuyển trang/polling không tìm lại được request bị loại.
- **Root cause:** Safety cap áp trên candidates trước actor eligibility và không có continuation/truncation contract.
- **Recommended direction:** Query eligibility ở DB rồi paginate/count, hoặc triển khai scan có continuation và total semantics chính xác; không chỉ tăng 500 lên một số khác.
- **Required regression test:** 501+ pending với nhiều scopes/roles, eligible nằm ngoài 500 mới nhất; đi hết mọi trang nhận đủ eligible một lần, total/badge đúng và actor khác không thấy request.
- **Estimated change scope:** Medium.

### R09-09 — Action availability bỏ qua ACL grants

- **Severity:** P2.
- **Confidence:** High; backend probe và consumer source khớp nhau.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Identity/AuthService.cs:212`; `MProjectFrontend/src/pages/Software/DeploymentMatrix.tsx:24`; `MProjectFrontend/src/pages/Software/SoftwarePackages.tsx:52`; `MProjectFrontend/src/pages/SupportRequests/SupportRequests.tsx:60`.
- **Symbol:** `GetMeAsync`, `hasNavigationPermission`, `canManageAssignments`, `canRecover`.
- **Evidence:** `/me.permissions` chỉ tổng hợp RoleAssignments. Test hiện có `GetMe_ReturnsRolePermissions_AndExcludesAclAllow` cho thấy đây là projection có chủ ý. Probe user có read roles và global ACL Allow software.assignment.manage: backend cho phép nhưng `/me` thiếu permission. Deployment Matrix dùng đúng tập thiếu đó để ẩn cả render và execution path.
- **Finding:** Consumer dùng quyền navigation như effective action capabilities, nên ACL có hiệu lực ở server không mở được workflow hợp lệ trên console. Không kết luận bản thân role-only projection là sai hay cần đưa mọi ACL thẳng vào navigation.
- **Realistic failure scenario:** Admin cấp ACL Allow cho engineer có sẵn quyền đọc deployment; engineer đăng nhập lại vẫn không có nút quản lý assignment. Pattern tương tự ở support recovery nếu cấp bằng ACL.
- **Operational impact:** Delegation phải dùng API thủ công hoặc bị mở rộng role để “sửa UI”; chiều có scoped Deny thì UI cũng có thể hứa action mà server từ chối.
- **Root cause:** Một projection không chứa target/effective ACL bị dùng cho hai hợp đồng khác nhau: navigation và action authorization hints.
- **Recommended direction:** Giữ navigation contract rõ ràng; trả hoặc query effective capabilities theo actor/target cho mutation UI. Server tiếp tục là nơi quyết định cuối cùng.
- **Required regression test:** Read-only base role + ACL Allow mở đúng action ở scope được cấp; scoped Deny không mở action tại scope bị cấm; user không được cấp quyền không có action. Bao phủ refresh sau grant/revoke.
- **Estimated change scope:** Medium.

### R09-10 — Audit before của revoke đã bị mutate

- **Severity:** P2.
- **Confidence:** High; probe dùng AuthorizationMutationService và audit logger thật.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Identity/AuthorizationMutationService.cs:106`, `:181`; `MProjectBackend/MProject.Application/Services/Identity/AuthorizationAuditLogger.cs:30`.
- **Symbol:** `DeleteRoleAssignmentAsync`, `DeleteAclEntryAsync`, `LogAsync`.
- **Evidence:** `var before = entity` giữ cùng reference; entity được set IsDeleted/save trước lúc logger serialize. Probe revoke role assignment ghi BeforeJson == AfterJson, cả hai IsDeleted=true. ACL delete dùng cùng pattern.
- **Finding:** Audit row có operation đúng nhưng before snapshot không đại diện dữ liệu trước revoke.
- **Realistic failure scenario:** Người vận hành mở History để xác định grant/deny nào đã bị thu hồi và trạng thái tại thời điểm thao tác.
- **Operational impact:** Diff không chứng minh được thay đổi, có thể gây sai diễn giải khi điều tra authorization hoặc khôi phục quyền.
- **Root cause:** Snapshot bằng entity reference thay vì immutable value trước mutation.
- **Recommended direction:** Chụp DTO before có fields được phép audit trước khi thay entity; after cũng dùng projection rõ ràng. Giữ transaction mutation+audit hiện có của AuthorizationMutationService.
- **Required regression test:** Delete role assignment và ACL: before.IsDeleted=false, after.IsDeleted=true, identity/scope/time/effect đúng; serialization không kéo navigation graph ngoài hợp đồng audit.
- **Estimated change scope:** Small.

### R09-11 — Scheduled ACL Deny không chặn cached Allow đúng StartTime

- **Severity:** P2.
- **Confidence:** High; probe đi qua thời điểm bắt đầu thật, so cached và uncached evaluator.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Identity/AuthorizationService.cs:130`, `:143`, `:187`, `:206`; `MProjectBackend/MProject.Application/Authorization/SubjectGrantQueryExtensions.cs:38`.
- **Symbol:** `EvaluateAsync`, `ComputeEffectiveCacheTtl`, `WhereActiveForSubject`.
- **Evidence:** Future ACL bị loại khỏi query active; TTL chỉ xét EndTime của matches và membership boundaries. Probe warm Allow trước Deny.StartTime, chờ qua boundary: IsAllowedAsync vẫn true, cùng service AreAllowedAsync không dùng decision cache trả false. TTL tối đa hiện là 3 phút.
- **Finding:** Lịch Deny đã lưu có thể áp muộn đến khi cached decision hết hạn. Các grant có StartTime tương lai cũng có thể xuất hiện muộn theo hướng deny stale.
- **Realistic failure scenario:** Admin đặt thời điểm ngừng quyền assignment trên ca tiếp theo; actor có cached Allow từ trước thời điểm đó tiếp tục gửi mutation trong cửa sổ cache.
- **Operational impact:** API authorization không khớp lịch hiệu lực đã cấu hình; hai đường evaluator có thể cho kết quả khác nhau cùng lúc.
- **Root cause:** Không tính next start boundary của grants/denies chưa active. EvaluateAsync còn dùng wall clock trực tiếp trong khi batch evaluator dùng injected TimeProvider.
- **Recommended direction:** Tính next transition từ mọi rule liên quan và membership, kể cả rules chưa active; dùng chung clock và TTL semantics cho cached/batch paths.
- **Required regression test:** Fake-time boundary trước/tại/sau scheduled Deny/Allow của user/team, global/scoped, có owner shortcut; cached và uncached outcomes phải đồng nhất.
- **Estimated change scope:** Medium.

### R09-12 — ScopeStrategy của bước sau không được thực thi

- **Severity:** P2.
- **Confidence:** High ở cấu hình multi-step được fixture tạo; chưa xác minh có policy như vậy trong deployment.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:91`; `MProjectBackend/MProject.Domain/Entities/Approvals/ApprovalStepSnapshot.cs:5`; `MProjectBackend/MProject.Application/Services/Approvals/ApprovalApproverResolver.cs:142`.
- **Symbol:** `SubmitAsync`, `ApprovalStepSnapshot`, `GetApproversAsync`.
- **Evidence:** Scope chỉ resolve từ orderedSteps[0], được lưu một lần trên request; step snapshot không lưu ScopeStrategy/ScopeContext. Probe policy step 1 Global, step 2 RequesterTeam: resolver đúng cho step 2 sẽ fail vì requester chưa có team, nhưng engine vẫn nhận submit và approver global duyệt cả hai bước thành Approved.
- **Finding:** Policy lưu strategy riêng từng step nhưng engine áp strategy của bước đầu cho toàn workflow.
- **Realistic failure scenario:** Policy được cấu hình qua quản trị dữ liệu/seeding thành Global review rồi review trong team requester; người có role global nhưng ngoài team vẫn được resolver của request chấp nhận ở bước 2.
- **Operational impact:** Lựa chọn người duyệt không đúng policy, hoặc workflow bị kẹt khi bước sau cần scope rộng hơn. Các policy một bước hiện seed không gặp nhánh này.
- **Root cause:** Request-level scope không biểu diễn contract step-level scope.
- **Recommended direction:** Snapshot scope/strategy cho từng bước và resolve nhất quán, hoặc từ chối policy có strategies khác nhau nếu sản phẩm chỉ hỗ trợ một scope cho cả request. Không âm thầm dùng bước đầu.
- **Required regression test:** Global→Team, Team→Global, Resource→Department, requester đổi membership sau submit; policy sửa sau submit không thay đổi snapshot, eligible actor đúng từng bước, invalid scope fail rõ ràng.
- **Estimated change scope:** Medium.

### R09-13 — Approval action chưa gắn với version/step người duyệt đã xem

- **Severity:** P2.
- **Confidence:** High về hành vi replay; impact phụ thuộc policy nhiều bước.
- **Category:** design risk.
- **File:** `MProjectBackend/MProject.Application/Models/Approvals/ApprovalActionRequest.cs:5`; `MProjectBackend/MProject.Application/Services/Approvals/ApprovalService.cs:150`, `:166`; `MProjectFrontend/src/components/approvals/ApprovalActionDrawer.tsx:116`.
- **Symbol:** `ApprovalActionRequest`, `ApproveAsync`, `handleApprove`.
- **Evidence:** Detail trả Version nhưng action chỉ gửi Comment. Mỗi call tải current step mới. Probe hai Global steps cùng role: gửi cùng action tuần tự hai lần tạo hai ApprovalActions và finalize. Đây không phải race lost update; EF optimistic concurrency không chặn call thứ hai bắt đầu sau commit thứ nhất.
- **Finding:** Protocol không phân biệt ý định mới với retry của bước trước.
- **Realistic failure scenario:** Approver có đủ role cho hai bước nhận network error sau khi server commit bước 1, hoặc hai tab giữ cùng detail; thao tác retry được hiểu là duyệt bước 2.
- **Operational impact:** Có thể mất một lần review riêng biệt mà policy nhiều bước dự định yêu cầu. Policy một bước sẽ gặp terminal-state rejection nên không bị auto-advance sang bước khác.
- **Root cause:** Không có expected request version/step hoặc action idempotency key trong mutation contract.
- **Recommended direction:** Ràng buộc action với version/step đã hiển thị; trả conflict khi stale. Nếu hỗ trợ retry idempotent, replay phải trả kết quả action ban đầu thay vì áp vào current step.
- **Required regression test:** Hai tabs xem cùng version; lost response sau first-step commit; same-role/different-role multi-step và approve-versus-reject/cancel. Một user intent chỉ được ghi cho đúng step đã xem.
- **Estimated change scope:** Medium.

## Những quyết định nên giữ

- JwtBearer kiểm user Active và AuthVersion bằng state hiện tại trên từng request. Existing revocation fixture chứng minh token đã phát bị chặn trước approval endpoint; R09-03 yêu cầu bổ sung issuance boundary, không bỏ cơ chế này.
- Refresh secret ngẫu nhiên, chỉ lưu HMAC với pepper riêng; rotation có conditional claim và replay revokes family. SQLite concurrent-rotation test hiện có thực sự chạy. Không thay bằng refresh token tái sử dụng vô hạn để che lỗi client concurrency.
- Viewer được reconcile về tập quyền đọc, không gồm software mutation/download. System role và no-self-impact/grantability guards của RoleService có negative tests và cần giữ.
- ACL Deny thắng RBAC/owner; owner whitelist chỉ áp action/resource type được cho phép. Resource lookup dùng ID đúng loại, cache ngắn và các invalidation hooks. R09-01 nằm ở caller chưa cung cấp scope, không phải PermissionEvaluator đổi precedence.
- Membership active/expiry và user status được xét ở team/approver resolvers. TeamLeader assignment clip interval theo membership; request không cho requester tự approve/reject và không mở admin override chỉ vì có ViewAllApprovals.
- EF model có Version concurrency cho ApprovalRequest và partial unique index cho một Pending request/target; middleware ánh xạ concurrency/duplicate-pending sang 409. Giữ các ràng buộc này trong mọi remediation.
- Step role/name và target/change snapshots được lưu tại submit, drawer ưu tiên snapshot bất biến; legacy null snapshot có fallback được nhận diện. Assignment pin chặn Pending và approval handler kiểm current package policy trước final activation.
- Deployment Matrix và approval list có generation guard/polling/server paging; assignment UI không biến Pending thành Inactive có nút Activate. Các notice admin hiện hữu không được coi là authorization của server hoặc tự ý đổi sản phẩm.
- Support query keys có user identity, cancellation/purge theo token/team boundary, recovery permissions và stale response handling riêng. R09-05/06 yêu cầu hoàn thiện boundary chung, không gỡ protection support đang có.

## Nghi vấn đã loại hoặc giới hạn lại

| Nghi vấn | Kết luận sau đối chiếu |
| --- | --- |
| Approvals approve/reject thiếu RequirePermission nên user bất kỳ duyệt được | Không đúng: engine gọi resolver, loại requester/inactive và yêu cầu role/scope của current step. Không thêm global `approvals.act` như một bản sửa thay thế resolver. |
| Mọi simultaneous approve/cancel đều last-writer-wins | Không đủ cơ sở: Version là concurrency token và SaveChanges của relational provider có transaction. Không báo defect thiếu optimistic concurrency; R09-13 là sequential replay khác vấn đề. |
| User bị disable vẫn dùng access JWT thông thường | Existing HTTP validator fixture và source bác bỏ; R09-03 là phát refresh muộn sau security mutation, không phải bỏ Active/version validation. |
| Viewer vẫn được publish/deploy theo finding SEC-003 lịch sử | Current seeder/reconciliation/test không còn grant đó. R09-01 dùng actor có global write và explicit scoped Deny, không phải Viewer escalation. |
| Pin tuần tự đổi version trong khi request Pending làm snapshot assignment sai | `PinVersionAsync:311` chặn Pending trước mutation. Không tái báo sequential snapshot bypass; pin-versus-submit race cần relational fixture riêng. |
| Snapshot test chứng minh release approval cho đủ bốn target chạy production | Không đúng: Program chỉ đăng ký ba handlers hiện hữu; SoftwareVersion trong generic snapshot test dùng pass-through handler. Generic submit loại target chưa có handler; release đang theo publish permission riêng. Không tuyên bố SoftwareVersion approval đã triển khai end-to-end. |
| `/me` cố ý bỏ ACL là bản thân lỗi security server | Không: test hiện có xác nhận projection navigation role-only. R09-09 thuộc consumers dùng projection đó làm effective action gate. |
| Các trang admin notice bị hiện là lộ quyền | Không: đây là product presentation được ghi nhận; direct API cần guard riêng. Helper isAdmin còn chấp nhận role.manage như semantics UI hiện tại, không lấy nó làm server proof. |
| Log out đương nhiên thu hồi access JWT tức thời | Source chỉ revoke presented refresh token; access JWT còn lifetime trừ khi AuthVersion đổi. Không báo điều này là regression nếu contract chưa yêu cầu immediate session revocation. R09-05 vẫn là lỗi vì browser phục hồi phiên đã xóa. |

## Khu vực chưa đủ bằng chứng và phần việc còn lại

- Chưa chạy PostgreSQL two-context matrix cho approve/reject/cancel/duplicate submit, role grantability đang thay đổi, password/login/rotation và policy update-versus-final-approve. Cần test provider thật khi remediation; không đánh dấu các close criteria này hoàn thành nhờ InMemory.
- Assignment handler ValidateApproval đọc current policy trước SaveChanges; package-policy change ở context khác có thể xen vào. Snapshot hiện không bind toàn bộ live target graph bằng expected target generation. Đã thấy guard cho sequential pin và test snapshot không đổi sau live edits; chưa tái hiện đường policy/target race qua relational host nên không gắn nhãn confirmed bypass riêng.
- Generic ApprovalService và RoleService có một số audit calls sau mutation SaveChanges, không có transaction bao quanh cả hai ở mọi caller. Assignment submission còn có catch cleanup nếu SubmitAsync ném lỗi sau commit. Cần fault injection vào audit/notification/response construction để định lượng committed-but-error và recovery; handlers hiện tại chưa thực hiện external notification thật. Không suy từ sự thiếu outer transaction rằng tracked target/request/action luôn persist riêng lẻ.
- Resource approver matching hiện chỉ global hoặc exact resource, không dùng ancestor hierarchy của generic RBAC. Seeder assignment mô tả role scoped đúng station; chưa có requirement rõ cho inherited approver role nên không tự coi model-scoped approver bị loại là defect.
- Read detail approval chỉ cho requester, current eligible approver hoặc ViewAllApprovals; approver của bước đã qua không mặc nhiên có quyền đọc history khi request hoàn tất. Cần chốt requirement review history trước khi đổi policy này.
- Đường `UsersController.RemoveUserTeam` chỉ xóa membership, khác cleanup Member/TeamLeader của `TeamService.RemoveTeamMemberAsync`. Có thể còn direct scoped role row; chưa chứng minh actor khai thác một sensitive route thực tế từ quyền thừa trong hierarchy hiện tại. Cần kiểm membership removal contracts, không kết luận cache invalidation tự nó bảo đảm revoke mọi direct role.
- `/me` cache 30 giây, browser không tự poll quyền độc lập, permission decision cache là per-process. Chưa chạy nhiều API instances hoặc role/ACL revoke trong nhiều tabs. R09-11 chỉ xác nhận start-boundary đã nêu, không tuyên bố mọi expiry/cache scenario lỗi.
- Chưa có browser thật/IIS smoke, multi-tab refresh test, hoặc hiệu năng Inbox lớn trên PostgreSQL. Frontend race probes kiểm component/callback/hook behavior với mock transport. Không tái sử dụng kết quả deploy/canary, artifact status hay credential trong tài liệu cũ.

## Hướng remediation

Ưu tiên R09-01/02 để làm kín scope và delegated identity administration; tiếp theo xử lý chung R09-03/04 ở server session generation, R09-05/06 ở browser auth/cache lifecycle. Sửa R09-07/08 trước khi tin vào danh sách công việc và action trong approval drawer. R09-09–11 cần focused contract/audit/time tests. Trước khi dùng policy nhiều bước, hoàn thiện R09-12/13 và chạy relational concurrency matrix.

Các finding config-specific như declaration thay đổi trước approve vẫn thuộc báo cáo `04`; các hậu quả job execution thuộc `02`. Báo cáo này chỉ đề xuất hướng sửa, không thay đổi các kiến trúc/contract đó hoặc tự đóng backlog lịch sử.
