# 01 — Agent enrollment, authentication, and remote control plane

Review bắt đầu ngày 2026-09-07, hoàn tất ngày 2026-09-08 (Asia/Saigon). Thực hiện prompt `docs/code-review/prompts/01-agent-enrollment-and-control-plane.md`. **Không sửa production code.**

- Repository revision: `0df8a0b0f3b6e55eaf91ff05e5978961861e6adc`.
- Prompt version: bản working tree, SHA-256 `f0a9b374d6d60f9b5f6dc270cc8a6e7fab3bdab46e09f4c90fe3f5e89a8b5430`. Prompt đang được stage, chưa thuộc revision trên.
- Giữ nguyên các thay đổi có sẵn: `.gitignore`, review plan/prompts, reports README, báo cáo 00 và file ghi chú.
- Đã đọc `AGENTS.md`, checklist SEC-002, SEC-004, AGT-002, appsettings Agent/API và composition của hai `Program.cs`. Remediation lịch sử chỉ được dùng để đặt giả thuyết kiểm tra.
- Giữ workflow owner đã duyệt: nhiều PC dùng chung installer token; chạy installer/script để từng PC tự đăng ký và xuất hiện trên `/computer`. Không đề xuất token bootstrap riêng cho từng PC hoặc thêm bước enrollment thủ công bắt buộc cho provisioning thông thường.

## Kết quả

Có **4 confirmed defects và 1 design risk**. Hai lỗi mức High liên quan đến việc credential cũ có thể trở lại hiệu lực sau một thao tác quản trị và việc một lệnh restart có thể thực thi nhiều lần. Không có kết luận rằng production đã bị khai thác hoặc đang gặp các lỗi này.

| ID | Severity | Confidence | Category | Nội dung |
| --- | --- | --- | --- | --- |
| AGT01-01 | High | High | confirmed defect | Các lần ghi Agent không kiểm tra version, có thể ghi đè revoke hoặc credential mới sau re-enrollment |
| AGT01-02 | High | High | confirmed defect | Agent thực thi lại lệnh gây tác động khi ACK thất bại hoặc Agent restart trước ACK |
| AGT01-03 | Medium | High | confirmed defect | Lệnh hết TTL vẫn tham gia dedup/quota, khiến lần enqueue mới không bao giờ được giao |
| AGT01-04 | Medium | High | confirmed defect | Capability hợp lệ cho cấu hình render từ file override bị download authorizer từ chối |
| AGT01-05 | Medium | High về hành vi; chưa xác minh điều kiện production | design risk | HTTP redirect có thể chuyển `X-Agent-Token` sang origin khác mặc dù URL ban đầu hợp lệ |

Severity phản ánh tác động trong điều kiện nêu ở từng finding. Các kết quả kiểm thử dưới đây không thay thế canary trên Windows Service/LocalSystem hoặc kiểm thử cạnh tranh trên PostgreSQL.

## 1. Phạm vi và cấu hình đã đối chiếu

Đã đọc các implementation được prompt chỉ định: Agent authentication handler/defaults, `AgentController`, provisioning limiter, `AgentService`, `AgentCommandService`, capability service/authorizer và liveness watchdog; phía Agent gồm options, composition, worker/client, token transitions/store/scrubber, enrollment/bootstrap/service installer, command handler, inventory/status/reachability/runtime state.

Đã đối chiếu models/interfaces/entities và mapping trong `MProjectBackend/MProject.Infrastructure/DBContext.cs`. Chỉ mở rộng sang `InstallationJobService`, `ConfigRenderService`, `UserService`, `ComputersController`, `AgentProductionSupportController` và một phần `ProcessSupervisor` để chứng minh các boundary bắt buộc. Không review sâu job execution, filesystem deployment, TUS, OTA package/apply, generic JWT/RBAC hoặc nghiệp vụ production support.

| Cấu hình/composition | Quan sát từ source hiện tại |
| --- | --- |
| Agent server origin | Appsettings gốc dùng loopback HTTP cho development. `AgentServerOrigin` chỉ cho HTTPS hoặc HTTP loopback, không cho userinfo/path/query/fragment. Installer yêu cầu HTTPS và hostname alias đúng. |
| Installer token | Appsettings Agent gốc để trống; development override có placeholder. Giá trị secret không được chép vào báo cáo. Workflow folder provisioning có token vẫn được giữ. |
| Agent HTTP | Named client `server` dùng OS TLS trust, timeout một giờ; enrollment CLI dùng timeout hai phút. Không cấu hình tắt automatic redirects. |
| Token persistence | `TokenStore` dùng thư mục state, ACL SYSTEM/Administrators, temporary file được bảo vệ trước khi ghi rồi replace bằng move. Không có version phía database trên entity `Agent`. |
| API Agent security | Appsettings không cung cấp pepper usable để kết luận cấu hình deployment. `AgentSecurityOptions` mặc định rotate sau 30 ngày, grace 7 ngày; `AgentService` kiểm tra cấu hình pepper khi được tạo. Không đọc secret store đang vận hành. |
| Provisioning limits | Middleware theo IP: announce 15/phút, enroll 120/phút; controller bổ sung theo endpoint + IP + fingerprint token: 5 và 20/phút. Token được trim trước fingerprint. |
| Forwarded headers | `UseForwardedHeaders` chạy trước authentication/rate limiting; cấu hình trust proxy/network được parse, forward limit 1. Appsettings gốc không thêm proxy/network ngoài defaults. |
| Liveness | Appsettings API bật watchdog, sweep 2 phút, offline sau 5 phút, batch 1.000. Heartbeat ghi thời gian server, không nhận timestamp liveness từ Agent. |

## 2. Sáu trace bắt buộc

### 2.1 Installer / enroll / announce → persistence

`scripts/install-agent.ps1:228–245,390–460,523–605` kiểm tra origin/certificate và probe TLS trước provisioning. Khi chưa có state, nhánh shared token bật self-announce; nhánh `-ComputerId` đọc enrollment token qua stdin. Bootstrap khởi chạy Agent child với working directory là install directory (`BootstrapCommand.cs:95–108`).

`AgentWorker.EnsureEnrolledAsync` nạp state hiện có, hoặc `AnnounceFirstContactAsync` lấy MAC/hostname rồi gọi `ServerClient.AnnounceAsync`. API kiểm tra rate limit và installer token, tìm MAC kể cả Computer đã soft-delete. MAC mới tạo Resource không có parent, Computer Online/Idle và Agent Active trong một transaction; phản hồi là `PendingAssignment`. Máy đã biết không được cấp lại token qua shared-token announce (`AgentService.cs:151–266`).

`EnrollCommand.RunAsync` kiểm tra ComputerId/state, dùng cùng parser origin, gọi enroll rồi lưu token. Server bind ID trong enrollment token với ComputerId, kiểm tra hash/expiry, thay permanent credential và xóa enrollment hash (`AgentService.cs:325–388`). Sequential replay bị chặn. Hai enrollment thông thường cùng đọc trước khi lưu cũng bị `Computer.Version` chặn một lần lưu; đã tái hiện bằng SQLite. Tuy nhiên, mutation chỉ thay `Agent` không đi qua concurrency guard này: xem AGT01-01.

Nếu phản hồi announce/enroll đầu tiên mất sau commit nhưng trước local persistence, server không phát lại credential chỉ dựa trên MAC/shared token hoặc enrollment token đã consume. Luồng recovery hiện tại dành cho máy đã provision được giữ nguyên; không đề xuất mở lại credential re-issue bằng shared token. Chưa thực hiện bài fault-injection trên installer thật để đánh giá vận hành tại điểm này.

### 2.2 Heartbeat → authentication → live state / commands / update handoff

`AgentWorker.HeartbeatTickAsync:233–289` gọi `HeartbeatWithCredentialAsync`, nhận credential generation gắn với chính request đó. `AgentController.Heartbeat:97–111` chỉ dùng ID từ Agent claims. `AgentService.RecordHeartbeatAsync:392–486` kiểm tra Agent Active và Computer còn tồn tại, ghi heartbeat/telemetry trong transaction, sau đó lấy commands và update offer.

Phản hồi mang assignment/location, maintenance, token rotation, commands và update metadata. Update handoff chỉ cấp capability cho Agent đang gọi và active release (`ResolveAgentUpdateAsync:489–520`); không review package/signature/apply ở đây. Token và commands được xử lý sau phản hồi authenticated. Watchdog re-check `LastSeenAt` trong điều kiện UPDATE trước khi chuyển Offline, tránh dùng danh sách stale đã chọn để ghi đè heartbeat vừa đến.

### 2.3 Credential snapshot → token transitions → durable state → restart

`ServerClient.cs:29–57,178–211` chụp token và generation dưới cùng lock, gắn header riêng từng request. 401 giữ generation đã dùng, không suy ra từ current token lúc nhận lỗi. `AgentTokenTransitionManager` serialize apply/clear/fallback; response thuộc generation cũ không được đổi state. Khi 401 thuộc generation hiện tại, manager ưu tiên durable latest rồi mới xét previous.

Rotation được lưu vào state **trước** khi đổi in-memory credential. Crash tiến trình sau save nhưng trước switch có thể nạp current token mới khi restart; fallback previous được lưu cùng state. Phản hồi heartbeat thành công bằng generation hiện tại mới xóa previous. Đây là thiết kế đúng cần giữ, nhưng chưa xác minh hard power-loss durability của file rename/write cache.

Server chỉ promote pending token khi một request sử dụng pending token xác thực thành công; current token tiếp tục dùng được trước promotion. Response rotation bị mất không lập tức làm current token invalid. Phần còn lỗi là cạnh tranh mutation ở server, không phải delayed-401 race phía Agent đã sửa: AGT01-01.

### 2.4 Revoke → Agent authentication → toàn bộ routes

`ComputersController.cs:107–120` gọi generate/revoke dưới permission quản lý Computer. `AgentService.RevokeAsync:729–744` đặt Revoked và xóa current/pending/enrollment hashes. Request tiếp theo qua `AgentAuthenticationHandler` gọi lại `AuthenticateAsync`; không có auth cache giữ token cũ. Authenticate yêu cầu Active và Computer không soft-delete.

Đã kiểm tra các action authenticated trong `AgentController`: heartbeat, manifest, poll, manifest resolve, blob download, job ACK/progress/complete, inventory, test metrics/results, reboot-required và command ACK. Tất cả dùng Agent scheme. `AgentProductionSupportController` áp cùng scheme ở cấp class và chuyển `CurrentAgentId` vào service. Các endpoint job có ID từ URL vẫn truyền authenticated AgentId vào service; không lấy AgentId/ComputerId do client tự khai để quyết định identity.

User disable/reset/delete trong `UserService` thu hồi user refresh tokens và đổi user auth state; không trực tiếp thu hồi permanent machine credentials. `EnrollmentTokenCreatedBy` là metadata phát enrollment token và bị clear khi consume; không phải quan hệ chủ sở hữu authorization của Agent. Không tự suy ra rằng disable người từng enroll phải làm toàn bộ máy của họ offline. Chính sách vô hiệu enrollment ticket chưa dùng khi người phát bị disable chưa được xác định; cần thống nhất trong unit 09 nếu đó là yêu cầu sản phẩm.

Revoke tuần tự hoạt động; revoke hoặc re-enrollment cạnh tranh với một Agent mutation đang giữ dữ liệu cũ có thể bị phá vỡ, đã tái hiện: AGT01-01.

### 2.5 Enqueue → delivery → command execution → ACK

Server dedup lệnh chưa ACK theo Computer/type/payload, giới hạn 100 pending. Heartbeat lấy tối đa 16 lệnh trong TTL 24 giờ; `DeliveredAt` được ghi lần đầu nhưng không loại lệnh khỏi lần giao sau. Đây là delivery có retry, không phải claim độc quyền.

Agent xử lý tuần tự rồi ACK. `CancelJob` dùng registry theo JobId và cancel token đang chạy; việc reconcile/cancellation sau restart thuộc unit 02. `Restart`/`KillAndRestart` gọi supervisor, còn handler không lưu kết quả theo CommandId. ACK thất bại dẫn đến thực thi lại ở lần giao sau: AGT01-02. TTL chỉ dùng khi chọn delivery, không dùng khi dedup/count: AGT01-03.

`AckAsync` kiểm tra Agent Active và ComputerId của lệnh. Agent của Computer khác không ACK được. ACK tuần tự lặp lại không thay kết quả đã có; result được trim đến 512 ký tự. Không đánh đồng idempotent ACK với idempotent execution.

### 2.6 Manifest capabilities → download authorization → storage stream

`InstallationJobService.BuildManifestJobsAsync:800–885` phát relative capability URL từ **effective** blob, bind AgentId + JobId + SHA + storage path, hết hạn sau một giờ. Nhánh này dùng capability endpoint bất kể Local/MinIO; không thấy đường production tại đây trả raw storage path cho Agent tải tùy ý.

`AgentBlobCapabilityService` kiểm tra HMAC, canonical base64url, expiry và điều kiện đúng một JobId hoặc ReleaseId. `AgentController.DownloadLocalBlob:138–150` yêu cầu Agent auth, capability của chính Agent, rồi gọi `AgentBlobDownloadAuthorizer` trước `IStorageService.DownloadAsync`. Authorizer kiểm tra blob/path, chưa bị GC claim; job còn Pending/Downloading/Installing và cùng Computer, hoặc release vẫn Active.

Các check này chặn đổi SHA/path/job/Agent bằng sửa capability và chặn job đã cancel. Tuy nhiên graph cho rendered configs không bao phủ cấu hình render từ file override: AGT01-04. Chưa có bằng chứng bypass cho Agent của Computer khác khi xét **toàn bộ** pipeline HMAC + issuer + authorizer.

## 3. Findings chi tiết

### AGT01-01 — Mutation Agent dùng dữ liệu cũ ghi đè quyết định revoke/re-enrollment

- **Severity:** High.
- **Confidence:** High; hai interleaving đã tái hiện bằng source/service hiện tại và SQLite relational persistence.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:334–375,696–719,729–744`; `MProjectBackend/MProject.Domain/Entities/Assets/Agent.cs:6–29`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:249–281,1093–1103`.
- **Symbol:** `EnrollAsync`, `AuthenticateAsync`, `PromotePendingToken`, `RevokeAsync`, `Agent`.
- **Evidence:** Agent kế thừa `BaseEntity`, không có concurrency token. Enroll xác minh ticket rồi SaveChanges; pending-token authentication đọc Agent, promote rồi SaveChanges. Revoke chỉ thay Agent, không bump `Computer.Version`. Harness đặt barrier ngay trước save của enroll trên Agent PendingEnrollment, cho revoke commit rồi nhả barrier: Agent trở lại Active, credential mới xác thực được, trong khi `RevokedAt` vẫn có giá trị. Ca thứ hai pause pending promotion, hoàn tất re-enrollment rồi nhả promotion: credential của re-enrollment bị từ chối, pending credential cũ được chấp nhận.
- **Finding:** Server không bảo đảm mutation credential dựa trên đúng phiên bản Agent vẫn còn hiệu lực tại lúc ghi. Một request đã bắt đầu trước quyết định quản trị có thể cấp lại quyền truy cập cho các request tương lai hoặc thay credential vừa cấp cho máy mới.
- **Realistic failure scenario:** Người quản trị revoke một enrollment bị cấp nhầm trong lúc station đang hoàn tất enroll. Hoặc vận hành re-enroll một máy đang có request dùng pending token chậm do DB/network scheduling. Request cũ lưu sau thao tác quản trị, khiến revoke/replacement không còn hiệu lực như API đã báo.
- **Operational impact:** Agent đáng ra bị chặn có thể tiếp tục heartbeat, poll, ACK và dùng capability hợp lệ theo identity đó; station vừa re-enroll có thể rơi vào 401 kéo dài. Đây là trạng thái persisted sai, không chỉ một request đã authenticate trước revoke được phép hoàn tất.
- **Root cause:** Read/check/write trên Agent không dùng conditional update theo credential generation/version hoặc row lock xuyên suốt transaction. Concurrency của Computer chỉ bảo vệ những mutation thực sự cập nhật Computer và không bảo vệ pending-token promotion/revoke.
- **Recommended direction:** Đặt một concurrency boundary thống nhất cho Agent enrollment, phát/revoke ticket, rotation/promotion và revoke. Dùng version/CAS hoặc lock row trong transaction; điều kiện ghi phải kiểm tra status và credential/ticket version hiện hành. Khi conflict, reload và xác minh lại; không retry dữ liệu cũ bằng blind save. Giữ nguyên shared-token provisioning cho MAC mới.
- **Required regression test:** Trên PostgreSQL với hai DbContext và barrier: revoke commit trước enroll save phải giữ Revoked; re-enrollment commit trước pending promotion save phải giữ replacement token, reject obsolete pending token. Thêm hai promotion đồng thời, phát ticket mới khi ticket cũ đang consume, revoke/promotion và delayed old-token heartbeat. Assert cả auth request sau race, không chỉ response của request bị delay.
- **Estimated change scope:** Medium — entity/migration, các credential transitions và relational concurrency tests.

### AGT01-02 — ACK thất bại làm lệnh restart thực thi nhiều lần

- **Severity:** High.
- **Confidence:** High; đường thực thi rõ từ source. Chưa chạy fault-injection kill/restart ứng dụng thật.
- **Category:** confirmed defect.
- **File:** `MProjectAgent/Services/AgentCommandHandler.cs:39–66,84–99`; `MProjectBackend/MProject.Application/Services/Assets/AgentCommandService.cs:72–92`; `MProjectAgent/Services/ProcessSupervisor.cs:452–491`.
- **Symbol:** `DispatchAsync`, `HandleAsync`, `ClaimPendingForDeliveryAsync`, `RequestRestartAsync`.
- **Evidence:** Mỗi delivery gọi `HandleAsync` trước ACK; không có lookup/journal CommandId đã thực thi. ACK lỗi bị catch và log sẽ redeliver. Server vẫn chọn cùng row khi `AckedAt == null`, kể cả `DeliveredAt` đã có. `RequestRestartAsync` kill/close process hiện tại rồi start lại mỗi lần gọi; không nhận CommandId để dedup.
- **Finding:** Delivery retries được chuyển thành execution retries không kiểm soát đối với lệnh có tác động không idempotent.
- **Realistic failure scenario:** Một lệnh KillAndRestart chạy thành công, sau đó POST ACK bị mất trước khi server commit. Heartbeat tiếp theo nhận cùng ID và kill/restart ứng dụng vừa chạy lại. Nếu Agent process restart sau action nhưng trước ACK, lần giao sau cũng không có durable result để nhận diện lệnh cũ.
- **Operational impact:** Có thể cắt ngang chu kỳ test đang chạy, làm mất công việc hiện tại và lặp restart trong thời gian mạng chập chờn. Retry StopApp sau khi app đã được chạy lại cũng có thể áp một yêu cầu cũ vào trạng thái mới.
- **Root cause:** Có cơ chế retry delivery và idempotent server ACK nhưng thiếu execution receipt/result ở Agent. CommandId không tham gia quyết định thực thi tác động.
- **Recommended direction:** Lưu receipt/result có giới hạn theo CommandId, xử lý lại delivery bằng gửi lại ACK của kết quả cũ. Quy định recovery cho crash giữa action và persisted completion; lệnh restart/stop nên bind thêm target process/deployment generation hoặc reconcile trạng thái mong muốn, tránh cam kết exactly-once chỉ bằng một cờ local.
- **Required regression test:** Giao cùng Restart/KillAndRestart ID hai lần, làm ACK lần đầu fail trước commit; chỉ có một lần tác động và hai lần gửi ACK. Lặp với Agent restart giữa execution và ACK. Kiểm tra lệnh cũ không tác động deployment/process generation mới. CancelJob phải tiếp tục hủy active execution khi cần.
- **Estimated change scope:** Medium — command receipt/recovery, handler và target contract nếu cần, không thay job filesystem workflow trong review này.

### AGT01-03 — Lệnh hết hạn chặn enqueue mới vô thời hạn

- **Severity:** Medium.
- **Confidence:** High; đã tái hiện qua `AgentCommandService` và EF InMemory.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentCommandService.cs:38–52,75–85`.
- **Symbol:** `EnqueueAsync`, `ClaimPendingForDeliveryAsync`.
- **Evidence:** Dedup và pending count chỉ xét `AckedAt == null`. Delivery bổ sung `EnqueuedAt > now - 24h`. Không có bước expire/terminalize/cleanup lệnh cũ trong service hoặc consumer khác đã tìm. Harness tạo Restart, đặt thời gian enqueue cách hiện tại 25 giờ rồi enqueue Restart lần nữa: API service trả cùng ID cũ; delivery trả 0 lệnh.
- **Finding:** Một lệnh không còn có thể được giao vẫn được xem là pending để dedup và chiếm quota. Lần enqueue sau có thể báo thành công mà không tạo yêu cầu có thể thực hiện.
- **Realistic failure scenario:** Máy offline qua cuối tuần, có Restart/RunInventory được gửi trong lúc offline. Khi máy online lại, operator gửi cùng lệnh nhưng nhận lại row đã hết hạn. Với nhiều payload khác nhau, 100 row hết hạn chưa ACK có thể chặn toàn bộ lệnh mới, kể cả CancelJob mới.
- **Operational impact:** Mất khả năng điều khiển từ xa đối với loại/payload bị kẹt; tích lũy quota có thể làm queue của Computer không phục hồi bằng heartbeat bình thường.
- **Root cause:** “Pending” có hai định nghĩa không thống nhất ở enqueue và delivery; expiry không phải trạng thái kết thúc.
- **Recommended direction:** Dùng cùng eligibility/expiry predicate cho dedup, quota và delivery, hoặc chuyển lệnh quá hạn sang trạng thái terminal rõ ràng. Lệnh mới sau TTL cần ID/thời gian mới; giữ lịch sử để operator biết lệnh cũ đã hết hạn.
- **Required regression test:** Enqueue giống lệnh quá 24h phải tạo lệnh mới và được claim. 100 lệnh quá hạn không chiếm active quota. Kiểm tra đúng mốc TTL, lệnh chưa hết hạn vẫn dedup, ACK cũ không kết thúc lệnh mới.
- **Estimated change scope:** Small — thống nhất eligibility predicates và bổ sung regression tests; nếu chọn mở rộng expiry status/UI thì cần đánh giá riêng.

### AGT01-04 — Blob render từ file override không đi qua authorization graph

- **Severity:** Medium.
- **Confidence:** High; đã tái hiện authorizer với capability HMAC hợp lệ, đối chiếu issuer/render pipeline.
- **Category:** confirmed defect.
- **File:** `MProjectBackend/MProject.Application/Services/Assets/AgentBlobDownloadAuthorizer.cs:68–85`; `MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:752–776,837–845`; `MProjectBackend/MProject.Application/Services/Software/ConfigRenderService.cs:252–258`.
- **Symbol:** `IsAuthorizedAsync`, `ResolveOverridesAsync`, `BuildManifestJobsAsync`, `ConfigRenderService.PersistAsync`.
- **Evidence:** Resolver thay base blob của SoftwareFile bằng file override rồi render value overrides lên blob đó. Render cache lưu `BaseSha256` của file override. Authorizer chỉ cho rendered blob nếu `BaseSha256` bằng SHA của SoftwareFile trong version; nhánh OverrideFiles chỉ so **output SHA** với SHA của override gốc. Harness có base A, file override B, rendered output C với BaseSha B: capability của A/B được chấp nhận, capability hợp lệ của C bị từ chối.
- **Finding:** Issuer có thể phát manifest download URL mà authorizer chắc chắn từ chối cho một tổ hợp cấu hình hợp lệ. Controller trả 410 trước khi mở storage stream.
- **Realistic failure scenario:** Một package cho override `config.ini`; station có active file override và active value override áp được vào file đó. Manifest yêu cầu blob đã render; máy chưa cache blob gọi download endpoint và nhận 410, dù job còn active và capability chưa hết hạn.
- **Operational impact:** Station không tải được cấu hình cần thiết để hoàn tất deployment; retry cấp capability mới cho cùng effective blob vẫn không giải quyết sai graph. Không kết luận mọi cấu hình render đều lỗi: render trực tiếp từ SoftwareFile gốc được phép.
- **Root cause:** Download authorizer mô hình hóa graph ít tầng hơn resolver/render pipeline; không liên kết rendered output với effective input đã được cấp cho đúng job/computer.
- **Recommended direction:** Đồng bộ authorization với tập effective artifacts của job/manifest, hoặc lưu quan hệ cấp phát có scope/version để kiểm tra chính xác. Nếu mở rộng graph sang rendered-from-override, phải giữ kiểm tra scope Computer/Station, target path, trạng thái và generation; không cho phép mọi rendered blob chỉ vì cùng package/base hash.
- **Required regression test:** Tạo file override và value override hợp lệ, resolve manifest bằng service thật, lấy chính capability trả ra và download thành công trên cả Local/MinIO path. Các ca khác Computer, job cancelled/completed, capability hết hạn/tamper và override không thuộc scope vẫn bị từ chối.
- **Estimated change scope:** Medium — manifest/artifact authorization và integration tests; giữ nguyên logic áp cấu hình thuộc unit 04.

### AGT01-05 — Automatic redirect vượt ra ngoài server origin đã kiểm tra

- **Severity:** Medium; phụ thuộc có cross-origin redirect từ server/proxy được tin cậy.
- **Confidence:** High về cơ chế truyền header; chưa xác minh có redirect như vậy trong deployment thực tế.
- **Category:** design risk.
- **File:** `MProjectAgent/Program.cs:113–118`; `MProjectAgent/Services/ServerClient.cs:143–175,193–204`; `MProjectAgent/Commands/EnrollCommand.cs:178–183`.
- **Symbol:** named HTTP client `server`, `PostCoreAsync`, `AttachCredential`, `EnrollCommand.RunAsync`.
- **Evidence:** Client dùng handler mặc định và gắn credential vào custom header `X-Agent-Token`. Harness dùng source `ServerClient` hiện tại và hai loopback HTTP origins: origin đầu trả 307 sang port khác, origin sau nhận synthetic credential. Microsoft xác nhận automatic redirect mặc định bật, chỉ xóa `Authorization`, không xóa các header khác. Xem [HttpClientHandler.AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-8.0).
- **Finding:** Validation origin chỉ bảo vệ URL ban đầu; không ràng buộc destination cuối cùng của authenticated request. TLS kiểm tra chứng chỉ của destination nhưng không bảo đảm destination là MProject origin được cấu hình.
- **Realistic failure scenario:** IIS/reverse proxy thêm redirect canonical/migration sang một HTTPS host khác, hoặc redirect route bị cấu hình sai. Host đích có chứng chỉ OS tin cậy nhưng không được phép nhận Agent credential. Header có thể được chuyển sang đó. Chưa tìm thấy source chứng minh deployment hiện tại phát redirect này; không coi đây là MITM với chứng chỉ không tin cậy.
- **Operational impact:** Nếu điều kiện trên xuất hiện, credential có thể lộ cho origin ngoài cấu hình, cho phép bên nhận giả Agent cho đến revoke/rotation. Với provisioning POST, cần kiểm tra thêm việc body chứa bootstrap/enrollment credential được replay qua 307/308.
- **Root cause:** Dựa vào TLS/origin parsing đầu vào trong khi giao quyền follow redirects cho HTTP handler mặc định; custom bearer header không được handler xử lý như `Authorization`.
- **Recommended direction:** Tắt automatic redirect trên authenticated/provisioning client, hoặc xử lý redirect có giới hạn và kiểm tra origin trước mỗi lần gửi. Kiểm tra URI đích sau resolve trước khi gắn credential; giữ OS TLS trust và HTTP loopback development như hiện tại.
- **Required regression test:** Dùng hai HTTPS origins có certificate fixture hợp lệ: redirect chéo origin phải không gửi header/body credential tới host thứ hai; same-origin redirect có behavior được quy định rõ. Giữ ca untrusted certificate, sai hostname và expired trusted certificate. Harness loopback hiện tại chỉ chứng minh header forwarding, không chứng minh toàn bộ HTTPS matrix.
- **Estimated change scope:** Small — HTTP handler/origin guard dùng chung cho worker và CLI, cộng transport tests.

## 4. Quyết định đúng cần giữ nguyên

- Shared installer token tạo identity mới cho MAC mới và Computer chưa gán station; không mặc nhiên cấp quyền chiếm identity đã provision hoặc nhận job của Computer khác.
- Announce dùng transaction cho Resource/Computer/Agent. Unique MAC của Computer đang active và unique Agent.ComputerId có ở database; không chỉ dựa trên một câu query trước insert.
- Enrollment bind ComputerId, có expiry, hash được consume; `Computer.Version` thực sự chặn hai stale enrollment saves trong relational reproduction.
- Permanent token có secret ngẫu nhiên, salt, keyed HMAC và key version; không lưu bearer secret dạng plaintext trong database. So sánh hash/token dùng constant-time primitive.
- Token transitions phía Agent dùng snapshot generation và serialize, lưu current/previous trước in-memory switch; không dùng global `DefaultRequestHeaders` để thay credential giữa request đang chạy.
- `TokenStore` bảo vệ cả thư mục và file tạm trước khi ghi; fail closed nếu không thiết lập được ACL. Scrubber xử lý appsettings variants và TLS backups ở thư mục cấu hình, idempotent; không nhầm scrub trên station với xóa token ở folder phân phối.
- OS TLS trust không bị bypass trong production HTTP path. Installer probe strict diễn ra trước provisioning; certificate đọc từ mạng chỉ để so sánh với certificate phân phối, không tự được đưa vào trust store.
- Authenticated Agent identity được lấy từ claims; command ACK kiểm tra Computer sở hữu. Capability được ký/bind Agent/job-or-release/SHA/path/expiry và re-check job/release/blob trước stream.
- Watchdog phân biệt live status với operational status, re-check cutoff tại UPDATE; heartbeat không reset tùy tiện deployment Updating về Idle.

## 5. Nghi vấn đã loại bỏ hoặc không nâng thành finding

| Nghi vấn | Kết luận và lý do |
| --- | --- |
| Shared token hoặc thiếu token riêng mỗi PC là defect | Loại bỏ theo quyết định owner và `AGENTS.md`. Không thay workflow provisioning. |
| Shared token có thể cấp lại token cho một MAC đã provision | Source từ chối Computer có sẵn, kể cả soft-delete; tests sequential replay/assigned/revoked đều nằm trong nhóm backend đã pass. |
| Hai enrollment thông thường đồng thời chắc chắn cùng thành công vì Agent không có version | Loại bỏ kết luận này: enroll cũng sửa Computer có Version. Barrier test SQLite xác nhận stale save ném `DbUpdateConcurrencyException`. AGT01-01 giới hạn vào interleaving không được Computer version bảo vệ. |
| Delayed 401 của token cũ luôn khôi phục previous token | Source hiện tại kiểm tra request generation và durable latest. Không lặp lại finding AGT-002 lịch sử; test execution phần ACL vẫn cần môi trường phù hợp. |
| Agent của Computer khác ACK được lệnh khi biết ID | `AckAsync` so ComputerId; regression test đã pass. |
| Biết SHA/path hoặc sửa capability là đủ tải blob bất kỳ | Pipeline yêu cầu Agent auth, HMAC và graph authorization; tamper/expiry/wrong-Agent/cancelled-job regressions đã pass. |
| Download authorizer kiểm tra override graph rộng đồng nghĩa đã có cross-station exploit | Chưa đủ: caller vẫn phải có capability do issuer cấp cho đúng Agent/job. Không coi fixture tự tạo capability ngoài issuer là bằng chứng exploit production. |
| Absolute external download URL luôn nhận Agent token | `OpenDownloadStreamAsync` hiện không gắn Agent header cho URL absolute. Rủi ro redirect của authenticated request được tách rõ ở AGT01-05. |
| TLS invalid certificate được cho qua | Không thấy callback bypass trong HTTP client production. Hai ca untrusted chạy được ngoài sandbox đều pass; trusted/wrong-host fixture còn thiếu. |
| X-Forwarded-For bất kỳ hoặc đổi token đủ bỏ mọi rate limit | Middleware theo remote IP vẫn giới hạn độc lập với fingerprint; forwarded headers chỉ được áp từ trusted proxy/network. Chưa có bypass tái hiện trên pipeline hiện tại. |
| `ReloadConfig` được ACK nghĩa là runtime config đã reload | Handler trả rõ no-op/unimplemented. Đây là phần capability chưa triển khai trong checklist CFG-002; không diễn giải thành đã áp cấu hình hoặc mở finding mới nếu chưa chứng minh workflow station bị phụ thuộc vào nó. |
| Disable người đã enroll đương nhiên phải revoke mọi Agent liên quan | Permanent machine identity không gắn quyền truy cập với tài khoản issuer. Chưa có requirement cho cascade như vậy; không tự thêm chính sách làm fleet offline. |

## 6. Kiểm chứng đã chạy

Môi trường: Windows, .NET SDK `10.0.400`, target runtime `net8.0`. Test chỉ dùng fixtures/local state hoặc database tạm; không dùng token thật, không gọi server production, không thực thi installer trên station.

### Test hiện có

| Nhóm | Kết quả | Phạm vi chứng minh |
| --- | --- | --- |
| Backend: AgentServiceTests, AgentCommandServiceTests, AgentBlobAuthorizationTests, ComputerLivenessWatchdogServiceTests | **65 passed, 0 failed, 0 skipped** | Behavior trong các test hiện có; chủ yếu EF InMemory, có relational rollback fixture. Không có đủ barrier tests cho mọi credential mutation. |
| Agent: token rotation, enrollment options, scrubber, TLS, status provider, runtime state, inventory | **33 passed, 15 failed, 0 skipped / 48** ở sandbox | 12 failures do quyền ACL, 1 do thiếu trusted TLS fixture, 2 do restricted certificate key storage. Không coi các lỗi môi trường này là production defect. |
| Chạy lại ngoài sandbox: token rotation, TLS, status provider | **2 passed, 13 failed, 0 skipped / 15** | Hai untrusted-TLS cases pass. 12 ACL failures còn lỗi quyền set owner (icacls 1307); trusted/wrong-host test vẫn thiếu certificate fixture. Chạy ngoài sandbox không cấp quyền Administrator/LocalSystem. |

Lệnh backend:

```powershell
dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore --filter 'FullyQualifiedName~AgentServiceTests|FullyQualifiedName~AgentCommandServiceTests|FullyQualifiedName~AgentBlobAuthorizationTests|FullyQualifiedName~ComputerLivenessWatchdogServiceTests' --verbosity minimal
```

Lệnh Agent ban đầu (bắt buộc fixture thiếu phải fail theo checklist):

```powershell
$env:MPROJECT_REQUIRE_TLS_FIXTURE='1'
dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter 'FullyQualifiedName~AgentTokenRotationRaceTests|FullyQualifiedName~EnrollCommandOptionsTests|FullyQualifiedName~InstallerTokenScrubberTests|FullyQualifiedName~AgentTlsTrustTests|FullyQualifiedName~AgentStatusProviderTests|FullyQualifiedName~RuntimeStateStoreTests|FullyQualifiedName~InventoryReporterTests' --verbosity minimal
```

Lần chạy lại dùng `--no-build --no-restore` và chỉ chọn `AgentTokenRotationRaceTests|AgentTlsTrustTests|AgentStatusProviderTests`. Không tắt guard ACL/TLS để biến test thành pass.

### Các ca tái hiện bổ sung

Harness review tạm dùng backend assemblies vừa build; phần transport compile trực tiếp source `ServerClient` và models. Không patch implementation hoặc thêm test vào production/test projects. Mọi credential trong harness được sinh cho fixture, không được đưa vào báo cáo.

| Ca | Cách điều khiển | Kết quả quan sát |
| --- | --- | --- |
| Pending enrollment cạnh tranh revoke | SQLite; pause trước SaveChanges, DbContext thứ hai revoke và commit, rồi resume | Agent Active, `RevokedAt` vẫn được đặt, credential của enroll được authenticate sau revoke. |
| Hai enrollment cạnh tranh | SQLite; cùng ticket, pause một save để save còn lại commit trước | Stale save bị Computer version từ chối; không kết luận cả hai thành công. |
| Pending promotion cạnh tranh re-enrollment | SQLite; pause save trong AuthenticateAsync, context khác phát ticket và hoàn tất enroll, rồi resume | Replacement token không authenticate được; obsolete pending token authenticate được. Command delivery được stub rỗng để cô lập auth, không thay credential logic. |
| Lệnh Restart hết TTL rồi enqueue lại | EF InMemory; tạo row quá 25 giờ | Enqueue trả ID hết hạn cũ, claim trả zero commands. |
| Rendered-from-file-override | EF InMemory; graph SoftwareFile A → override B → rendered C; capability ký bằng service thật | A/B được phép, C bị từ chối dù đúng Agent/job và chưa hết hạn. Chưa phải full render/download E2E. |
| Redirect origin | Hai TCP HTTP servers chỉ bind loopback; source ServerClient theo 307 sang port khác | Destination nhận synthetic Agent header. Không dùng certificate bypass và không coi ca HTTP này là HTTPS canary. |

NuGet restore cho harness ban đầu không chạy được do mạng sandbox. Đã chuyển harness sang references từ build output vừa kiểm chứng và compile source transport, không cần package fetch. Harness cuối build không warning/error và hoàn tất các ca trên. Kết quả được tổng hợp ở bảng, không chép operational logs/payloads.

## 7. Phần chưa đủ bằng chứng và việc kiểm chứng còn lại

- **Windows identity/ACL:** Chưa chạy được token persistence/race và status-provider fixtures dưới elevated Administrator hoặc LocalSystem. Static review không chứng minh ACL của bản cài đang chạy trên station.
- **TLS:** Thiếu trusted certificate fixture có private key/trust chain phù hợp. Chưa xác minh lại trusted happy path, wrong-host và expiry độc lập trên trusted chain; không dùng lịch sử SEC-002 thay cho kết quả hiện tại.
- **PostgreSQL concurrency:** Credential races đã tái hiện bằng relational SQLite với save barriers; cần port regression sang PostgreSQL để kiểm chứng isolation/migration/fix. Không có canary production trong review này.
- **Command side effects:** AGT01-02 được xác nhận từ server/handler/supervisor trace, chưa chạy lệnh restart/kill thật. Cần fault-injection vào ACK và crash window bằng app fixture biệt lập trước remediation rollout.
- **Blob E2E:** Fixture xác nhận authorization mismatch; chưa tạo full assignment + file/value overrides + render + storage download trên cả hai providers. Không mở rộng sang semantics approval/config thuộc unit 04.
- **Provisioning/network deployment:** Chưa đo burst provisioning qua NAT/IIS, multi-instance rate limiting hoặc effective ForwardedHeaders ở server thật. Không có bằng chứng đủ để gọi đây là bypass hay fleet outage hiện tại.
- **Origin redirect:** Chưa tìm thấy rule IIS/proxy đang triển khai khiến redirect chéo origin. AGT01-05 được giữ ở mức design risk, không báo như một vụ rò credential đã xảy ra.
- **Hard power loss:** `TokenStore` save-before-switch có cơ sở cho process-crash recovery; chưa có disk flush/power-cut experiment. Không khẳng định trạng thái token chịu được mọi mất điện.

Ưu tiên remediation AGT01-01 và AGT01-02, sau đó AGT01-03/04. Mọi thay đổi cần bảo toàn việc PC mới tự đăng ký bằng shared installer token và không mở quyền download vượt ngoài Agent/job context. Báo cáo này không triển khai các hướng sửa đó.
