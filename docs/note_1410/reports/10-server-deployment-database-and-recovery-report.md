# 10 — Server deployment, database và recovery lifecycle

Ngày review: **2026-09-09**. Repository revision: **`21c8cb3b5cb12217d8fba4581e33f12e43f635b0`**; working tree ban đầu sạch theo `git status --short`.

Prompt: [10-server-deployment-database-and-recovery.md](../prompts/10-server-deployment-database-and-recovery.md), SHA-256 **`F8ECB48B80AA11774DAD99C212C7B3CE51062686F6BF5FA7D11EBD1FA4576E66`**.

Đã đọc `AGENTS.md`, `docs/PROJECT_REVIEW_CHECKLIST.md` (DEP-001, OPS-001, SEC-002, SEC-005), `MProjectBackend/scripts/README.md`, runbook rollback, startup/configuration và các script trong scope. Checklist nằm trong thư mục `docs`, bị bỏ qua bởi lượt tìm file thông thường; SHA-256 bản đã đọc: `26A4D308F0C25BD2B327D1BC7AF3E9EA8CB51F36CC2A9E6262663FEA79BE0EDF`. Trạng thái “completed” trong checklist là bằng chứng lịch sử, không thay cho kiểm chứng revision này.

**Không sửa production code, không triển khai lên IIS, không đọc credential của hệ thống đang chạy và không thao tác database hiện hữu.** Mọi database dùng để kiểm chứng là PostgreSQL cluster riêng, bind loopback, dữ liệu giả và được dọn sau khi chạy. Giữ nguyên quyết định owner: các PC được dùng chung install token; chạy installer phải cho phép PC tự đăng ký và xuất hiện trên trang Computer. Không coi token reuse hay thiếu manual enrollment là lỗi.

## Kết quả

**13 findings: 11 confirmed defects và 2 design risks; 7 P1, 6 P2.** P1 là nguy cơ mất khả năng recovery, triển khai sai database, mất kết nối fleet hoặc lộ credential/key khi xảy ra điều kiện đã nêu. P2 là lỗi giới hạn hơn ở cấu hình, retention và công cụ vận hành. Severity không khẳng định production hiện đang gặp lỗi.

| ID | Severity | Category | Finding |
| --- | --- | --- | --- |
| R10-01 | P1 | confirmed defect | Rollback lỗi vẫn gỡ offline marker và khởi động app pool |
| R10-02 | P1 | confirmed defect | Backup/migrate/restore không ràng buộc với database API thực sự sử dụng |
| R10-03 | P1 | confirmed defect | Thiếu schema có thể bị bỏ qua và readiness vẫn trả 200 |
| R10-04 | P1 | confirmed defect | Update có thể tự đổi certificate sang certificate chưa được fleet tin cậy |
| R10-05 | P1 | confirmed defect | Đường truy cập console mặc định truyền login/token qua HTTP |
| R10-06 | P1 | confirmed defect | Đầu vào public key có thể đưa nguyên private key vào artifact/config |
| R10-07 | P2 | confirmed defect | Mật khẩu PostgreSQL chứa dấu `;` không được bảo toàn trong config |
| R10-08 | P2 | confirmed defect | Rollback không khôi phục public key/config trong archive |
| R10-09 | P2 | confirmed defect | `KeepBackups=0` xóa cả backup đang cần cho automatic rollback |
| R10-10 | P2 | confirmed defect | Các release gates vẫn gắn với migration head cũ |
| R10-11 | P2 | design risk | Migration-safety classifier bỏ qua SQL và dạng Up không hiểu |
| R10-12 | P1 | design risk | Maintenance boundary chưa bao trùm các writer và external storage |
| R10-13 | P2 | confirmed defect | `deploy-local.ps1` vẫn bỏ qua TLS certificate validation |

## Kiểm chứng đã thực hiện

| Kiểm chứng | Kết quả hiện tại | Giới hạn |
| --- | --- | --- |
| Focused backend tests | **43 passed, 0 failed, 0 skipped** | StartupCredentialGuard, DesignTimeAppDbContextFactory, ProductionWatchdogConfiguration, AppDbSeeder, ProductionSupportMigration, ProductionSupportOperational; không phải toàn bộ backend suite |
| PowerShell 5.1 parser | **13/13 script trong scope parse thành công** | Parse không chứng minh đúng IIS/Windows identity/network |
| API Release publish | **Pass**; output không có `appsettings.Development.json` | Có nullable warnings sẵn trong source; không chạy `prepare-deploy` toàn bộ/clean frontend reinstall |
| EF idempotent schema | **48 migrations**, head `20260906090000_BindTusSession`; áp dụng **hai lần thành công** trên PostgreSQL 18 riêng | SQL sinh từ source cùng revision bằng EF CLI 8.0.8; không chứng minh tương thích mọi phiên bản PostgreSQL |
| `test-database-backup-restore.ps1` | **Pass**, restore **2.543 giây** | Database fixture mới, support tables chưa có traffic; không phải đo RTO production hay bằng chứng RPO 24 giờ |
| API Release trong `Production`, `AutoMigrate=false` | `/health/ready`: **200 / Available** với schema đầy đủ | Chạy loopback, không IIS/TLS; worker gây mutation được tắt để cô lập phép đo |
| API Release với schema thiếu migration mới nhất | **Vẫn 200 / Available**, thiếu `UploadReservations.TusFileId`, history giữ head `20260902070533_AddProductionSupportReporter` | Xác nhận R10-03; không gọi API upload thật |
| `/metrics` không có key/proxy fixture | Anonymous **200** | Đúng behavior khi không cấu hình key; không suy ra trạng thái bảo vệ của production |
| Probe các hàm deploy với IIS/DB stubs | Tái hiện failed-restore restart, config rollback, password round-trip, giữ config lệch CLI, retention zero, key-label acceptance, lựa chọn certificate và giới hạn classifier | Dùng đúng function body từ source; stub không chứng minh filesystem locks/certificate chain thật |

Lệnh focused tests:

```powershell
dotnet test MProjectBackend\MProject.Tests\MProject.Tests.csproj --no-restore --filter 'FullyQualifiedName~StartupCredentialGuardTests|FullyQualifiedName~DesignTimeAppDbContextFactoryTests|FullyQualifiedName~ProductionWatchdogConfigurationTests|FullyQualifiedName~AppDbSeederTests|FullyQualifiedName~ProductionSupportMigrationTests|FullyQualifiedName~ProductionSupportOperationalTests' --logger 'console;verbosity=minimal'
```

Evidence có thể chạy lại:

- [PowerShell function probes](10-server-deployment-database-and-recovery-probes.ps1): chỉ dùng file giả trong `.tmp`, không cần IIS/PostgreSQL.
- [Runtime probe](10-server-deployment-database-and-recovery-runtime-probe.ps1): dùng API đã publish, `schema.sql` và PostgreSQL tools; tạo cluster mới trong `.tmp`, không nhận connection string production.
- [Kết quả runtime](10-server-deployment-database-and-recovery-runtime-results.json): chỉ chứa trạng thái, migration IDs và thời gian; không có secrets/log/payload.

Runtime probe mặc định dùng `.tmp/review10-artifact`, được tạo bằng `dotnet publish MProjectBackend/MProject.Api/MProject.Api.csproj -c Release --no-restore -o .tmp/review10-artifact/server/Api` và `dotnet ef migrations script --idempotent --no-build -p MProjectBackend/MProject.Infrastructure -s MProjectBackend/MProject.Api -o .tmp/review10-artifact/schema.sql --configuration Debug` sau focused build. Có thể truyền `-ArtifactRoot` và `-PostgresBin`. Bản probe lưu kèm báo cáo tạo bản sao SQL không BOM trong fixture và tự xóa fixture database/process. Sandbox ban đầu chặn restricted-token startup của PostgreSQL; phép chạy cô lập ngoài sandbox đã được automatic review cho phép. Một lượt harness gặp NOTICE của psql khi apply lần hai; đã sửa riêng harness bằng `client_min_messages=warning` và chạy thành công. Không chỉnh script sản phẩm để đạt kết quả.

## Tracing bắt buộc

| Trace | Chuỗi đã đối chiếu | Kết luận |
| --- | --- | --- |
| 1. Build/package | `prepare-deploy.ps1:165` → EF idempotent SQL/BOM removal → classifier `:186` → Agent package `:226` → API publish `:261` → clean Yarn frozen install `:278` → `server/Api`, `server/wwwroot`, SQL/manifest/install/update scripts/public key `:302` | API và SPA cùng artifact, source appsettings bị loại. Manifest chưa chứa expected migration head hoặc hash ràng buộc SQL. `-SkipSchema` có thể dùng lại SQL/manifest cũ trong artifact root; không có freshness gate. R10-06/10/11 liên quan. |
| 2. Initial install | `install-server.ps1:416` preflight → IIS/modules → directories/ACL → certificate/trust/export/hosts → site/bindings/firewall → schema `:489` → API copy/config/key/SPA `:495` → start `:500` → health `:503` | Secrets/host/JWT có kiểm tra nhưng không xác minh toàn bộ effective config trước mutation. Thiếu SQL/psql chỉ warning; schema chạy trước `Copy-BackendArtifact` là nơi stop pool. R10-03/04/05/07. |
| 3. Update/recovery | `update-server.ps1:388` artifacts/existing DLL → migration safety → `New-Backup`: stop pool, fingerprint, dump, fingerprint, restore drill, archive → prune `:437` → installer giữ config → restart → fault/health → `Restore-Backup` → health cũ | Backup mặc định dừng writer của pool hiện tại trước snapshot. Prune chạy trước deployment; exception của rollback lại mở pool. Không kiểm tra target DB khớp API, config không được restore exact. R10-01/02/08/09/12. |
| 4. Config/startup | Generated JSON → `Program.cs:37` credential fingerprints → DB/options/storage/hosted services → JWT/hash/MinIO/refresh-pepper guards → seeder `:444` → forwarded headers/CORS/auth/metrics/API/SPA | Production mặc định không auto-migrate; explicit `Database:AutoMigrate=true` vẫn bật được. Config generator ghi `false`, nhưng update giữ nguyên override hiện hữu. Watchdog 30/180 và nhiều fallback khớp. |
| 5. Operational drills | Migration/rollback, database restore, release DB, artifact smoke, query-performance, Agent TLS và test classes | Có ích cho success path, nhưng chưa bao phủ lỗi restore/partial copy/two-host fencing; smoke và metadata gắn head cũ. Bảng drill bên dưới phân biệt rõ điều đã/ chưa được chứng minh. |

### Default/config đối chiếu

| Nhóm | Mặc định/đường cấu hình hiện tại | Đánh giá |
| --- | --- | --- |
| Schema owner | Installer ghi `Database.AutoMigrate=false`; Program fallback theo environment; seeder luôn chạy nhưng chỉ migrate khi được bật | Giữ deployment script làm owner production. Seeder dùng PostgreSQL advisory lock cho quá trình seed; không nhầm lock này với deployment-wide lock. |
| Installation watchdog | JSON, domain defaults, installer và local setup: enabled, sweep 5 phút, inactivity **30**, max-attempt **180**, batch 200 | OPS-001 được xác nhận lại bằng test/probe. Update giữ override của operator. |
| Liveness/GC | Liveness true/2/5/1000; BlobGc true/UTC 2/7 ngày/500 | Config generator và defaults phù hợp; storage nằm ngoài API archive. |
| Other workers | StationRollback=false, AutoHeal=false; domain dispatcher và TUS cleanup mặc định enabled | Thiếu section trong JSON không đồng nghĩa thiếu registration. Hosted workers chạy theo lifecycle của web process. Chưa diễn tập boot/idle/recycle của IIS. |
| Storage | Installer chọn Local, hash verification=true; storage/git/tus dưới `DataRoot`, grant Modify cho pool | Phù hợp local deployment. Health chỉ kiểm DB connect, chưa kiểm quyền đọc/ghi storage. API/backup ACL đầy đủ phụ thuộc ACL kế thừa. |
| CORS | Generated origins: localhost, LAN IP HTTP, hostname HTTPS, không có custom port | Same-origin artifact mặc định không cần CORS. Chỉ khi dùng `WebApiUrl` khác origin/custom ports mới cần cấu hình bổ sung; không coi thiếu `:8443` mặc định là lỗi. |
| Forwarded headers | Trust mặc định loopback, thêm KnownProxies/KnownNetworks từ config, xử lý trước rate limiter | Không có blanket trust trong source. Không có dữ liệu để kết luận cấu hình reverse proxy hiện tại sai. |
| Metrics | Installer không tạo key; Program chỉ bảo vệ `/metrics` khi có `Metrics:ApiKey` | Fixture anonymous=200. Runbook yêu cầu key hoặc reverse proxy; chưa xác minh deployment hiện hữu có lớp đó. |
| Secrets | Source dev config trống, publish guard loại development settings, fingerprint guard tránh credential lịch sử; JWT/refresh pepper phải được cấp ngoài source | Rotation live của SEC-005 chưa có bằng chứng. Nonempty pepper trong script chưa chứng minh đúng Base64/độ dài; service constructors có kiểm tra thêm. |
| Agent bootstrap/signing | Shared installer token được truyền có chủ đích; OTA không được kèm token; public key được copy riêng | Giữ owner exception. Không có căn cứ đòi token riêng mỗi PC. Loại input key chưa được xác minh: R10-06. |

## Findings chi tiết

### R10-01 — Rollback thất bại vẫn mở lại ứng dụng

- **Severity:** P1.
- **Confidence:** High; tái hiện bằng function body hiện tại với `Restore-Database` ném exception.
- **Category:** confirmed defect.
- **File:** `scripts/update-server.ps1:353`, `:367`, `:372`, `:374`, `:455`.
- **Symbol:** `Restore-Backup`, update `catch`.
- **Evidence:** `Expand-Archive` và `Restore-Database` nằm trong `try`; `finally` luôn xóa `app_offline.htm` và gọi `Start-WebAppPool`. Probe trả `RestoreFailureRestartsPool=true`, `RestoreFailureRemovesOffline=true`.
- **Finding:** Lỗi giải nén hoặc restore DB không giữ maintenance boundary. Thông điệp runbook “old pool started only after both restores finish” không đúng ở nhánh exception.
- **Realistic failure scenario:** `pg_restore` lỗi sau khi database cũ đã bị drop/recreate, hết dung lượng hoặc một file trong archive không ghi được. Pool cũ được mở với DB restore dở hoặc code trộn phiên bản.
- **Operational impact:** Mở request/worker vào trạng thái recovery chưa hoàn tất; kéo dài outage và có thể phát sinh mutation trước khi operator sửa.
- **Root cause:** Cleanup coi việc gỡ offline/restart là thao tác luôn phải làm, không phân biệt recovery commit với recovery failure.
- **Recommended direction:** Chỉ mở pool sau khi code và DB restore thành công; lỗi giữ pool stopped/offline, giữ backup, báo phase hỏng. Lỗi xóa file phải được xử lý rõ thay vì bỏ qua rồi tuyên bố exact restore.
- **Required regression test:** Inject lỗi archive, locked destination, disk full, `pg_restore` trước/sau recreate. Assert không có start, offline còn nguyên; recovery retry dùng cùng backup thành công rồi mới mở pool.
- **Estimated change scope:** Medium.

### R10-02 — Database vận hành và database của API có thể khác nhau

- **Severity:** P1.
- **Confidence:** High; source trace đầy đủ và probe xác nhận config được giữ khi CLI target khác.
- **Category:** confirmed defect.
- **File:** `scripts/update-server.ps1:34`, `:111`, `:191`, `:253`, `:296`, `:407`; `scripts/install-server.ps1:248`; `MProjectBackend/MProject.Api/Program.cs:39`.
- **Symbol:** Update parameters, `New-Backup`, `Assert-MigrationSafety`, `Restore-Database`, `Write-BackendAppSettings`.
- **Evidence:** SQL/dump/restore dùng `DatabaseHost/User/Name` từ CLI, mặc định localhost/postgres/TESSDB. API giữ connection string trong appsettings hoặc environment override. Preflight chỉ kiểm DLL/site inputs, không so hai target; probe `PreservedDatabaseDiffersFromCli=true`.
- **Finding:** Không bảo đảm snapshot và schema deployment áp dụng đúng database đang phục vụ. Port cũng không có parameter tương ứng ở install/update và tool calls không truyền `-p`.
- **Realistic failure scenario:** Server cài với database tên riêng; operator dùng lệnh update mẫu chỉ truyền password/JWT. Database mặc định khác vẫn tồn tại nên preflight, dump và schema đều thành công trên DB đó. API tiếp tục dùng DB cũ không được migrate; rollback có thể drop/recreate nhầm DB.
- **Operational impact:** Backup sai đối tượng, sửa/restore nhầm dữ liệu và không thể dùng backup vừa tạo để khôi phục API thực.
- **Root cause:** Hai nguồn cấu hình độc lập; health không xác nhận database identity/migration head.
- **Recommended direction:** Resolve target từ effective deployment config, hoặc bắt buộc đối chiếu host/port/database/user trước mọi mutation. Khi có environment/web.config override không resolve được, yêu cầu target contract rõ ràng; không in password.
- **Required regression test:** Install với custom DB/port, update thiếu hoặc sai target; phải fail trước stop/dump/schema. Thêm environment override, hai DB cùng server và assertion không gọi DROP trên DB ngoài target.
- **Estimated change scope:** Medium.

### R10-03 — Thiếu schema vẫn có thể nhận health thành công

- **Severity:** P1.
- **Confidence:** High; tái hiện bằng API Release và PostgreSQL thật cô lập.
- **Category:** confirmed defect.
- **File:** `scripts/install-server.ps1:314`, `:318`, `:489`; `scripts/update-server.ps1:388`, `:449`; `MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs:27`; `MProjectBackend/MProject.Api/Program.cs:444`.
- **Symbol:** `Apply-Schema`, install schema branch, `HealthController.Ready`.
- **Evidence:** Thiếu psql hoặc schema chỉ warning rồi tiếp tục. Update bắt buộc manifest nhưng không bắt buộc schema file. Readiness chỉ gọi `Database.CanConnectAsync`. Runtime probe bỏ migration `BindTusSession` và cột `TusFileId`, cold-start API Release với AutoMigrate=false: readiness vẫn **200/Available**, head cũ giữ nguyên.
- **Finding:** Success gate không chứng minh database có schema tương thích với binary. Seeder chạm identity tables nên không nhất thiết phát hiện cột thiếu ở feature khác.
- **Realistic failure scenario:** Copy artifact thiếu SQL, dùng lại SQL cũ sau `-SkipSchema` ở build, hoặc CLI nhắm nhầm DB như R10-02. API mở được identity tables nhưng upload reservation truy vấn model mới sẽ gặp cột không tồn tại.
- **Operational impact:** Script báo DONE/healthy, không rollback; người dùng gặp lỗi 500 sau khi đã đưa hệ thống vào sử dụng.
- **Root cause:** Missing dependency bị hạ thành warning và readiness chỉ kiểm kết nối, không có expected migration contract.
- **Recommended direction:** Nếu không truyền `-SkipSchema` có chủ đích, thiếu SQL/psql phải fail preflight. Artifact mang head/hash/compatibility; đối chiếu history sau apply và trước mở traffic. Giữ `-SkipSchema` cho rollback code trên expand schema tương thích, không bắt buộc hạ schema.
- **Required regression test:** Binary hiện tại trên DB thiếu `BindTusSession`, thiếu SQL, SQL cũ, thiếu psql; deployment phải fail trước success. Thêm control: rollback binary cũ trên schema expand mới được phép theo contract.
- **Estimated change scope:** Medium.

### R10-04 — Update tự chọn certificate mới theo ngày hết hạn

- **Severity:** P1.
- **Confidence:** High ở selection/binding path; chưa diễn tập certificate store thật.
- **Category:** confirmed defect.
- **File:** `scripts/install-server.ps1:115`, `:135`, `:152`, `:228`, `:481`; `scripts/update-server.ps1:423`.
- **Symbol:** `Get-UsableCertificate`, `Ensure-Certificate`, `Ensure-HttpsBinding`.
- **Evidence:** Update truyền `RequireExistingCertificate=true`, nhưng selection lọc Subject/SAN/private key/EKU rồi sort `NotAfter` giảm dần. Không đọc thumbprint binding hiện tại. Hàm binding thay certificate hash khi khác; cert được import trust trên chính server trước health. Probe hai certificate cùng tên chọn cái hết hạn muộn hơn.
- **Finding:** “Require existing certificate” chỉ ngăn tạo certificate mới; không bảo toàn certificate đang dùng.
- **Realistic failure scenario:** Operator đã import cert B để chuẩn bị rollout trust, trong khi fleet còn tin cert A tự ký. Update server thường lệ tự bind B; server local health pass vì script vừa trust B, nhưng PC chưa trust B ngừng kết nối.
- **Operational impact:** Fleet heartbeat/poll/enrollment mất kết nối dù server báo update thành công. Code/DB rollback cũng không restore IIS binding/trust state.
- **Root cause:** Certificate discovery theo newest expiry thay cho binding identity; local health không chứng minh trust của client.
- **Recommended direction:** Update resolve/validate đúng thumbprint đang bind. Certificate rotation thành operation riêng có target thumbprint và kế hoạch trust distribution rõ ràng. Expired/mismatch phải fail trước mutation.
- **Required regression test:** Hai cert cùng CN/SAN, cert đang bound hết hạn sớm hơn; update phải giữ thumbprint. Missing/expired/wrong SAN phải dừng. Staging client chỉ trust A vẫn truy cập sau code update.
- **Estimated change scope:** Medium.

### R10-05 — Console mặc định dùng HTTP cho dữ liệu xác thực

- **Severity:** P1.
- **Confidence:** High; trace IIS bindings → built frontend → auth routes.
- **Category:** confirmed defect.
- **File:** `scripts/install-server.ps1:17`, `:479`, `:483`, `:514`; `scripts/prepare-deploy.ps1:278`; `MProjectFrontend/src/api/services/axios-client.ts:17`, `:43`; `MProjectFrontend/src/api/auth.ts:18`; `MProjectBackend/MProject.Api/Program.cs:455`.
- **Symbol:** HTTP bindings/firewall, empty `VITE_API_URL`, Axios auth requests, application middleware pipeline.
- **Evidence:** Installer công bố `http://<LAN-IP>` làm đường console; cùng IIS site mở HTTP và phục vụ API/SPA. Artifact mặc định bake API URL rỗng nên login, refresh và Bearer requests dùng origin HTTP. Không có HTTPS redirect/rejection tương ứng trong pipeline.
- **Finding:** Khôi phục TLS cho Agent chưa bảo vệ web console mặc định. `JwtBearer.RequireHttpsMetadata` không chặn application requests đi qua HTTP.
- **Realistic failure scenario:** Admin mở URL được installer in ra trên LAN có thiết bị/đường truyền bị quan sát hoặc can thiệp; login password và bearer/refresh credential đi qua kênh không mã hóa.
- **Operational impact:** Credential bị lấy có thể cho phép thao tác quản trị hợp lệ dưới danh tính người dùng.
- **Root cause:** Thiết kế truy cập web “không cần hosts/certificate” được ưu tiên nhưng API xác thực vẫn chia sẻ HTTP origin đó.
- **Recommended direction:** Phân phối canonical HTTPS origin và trust cho console; chuyển HTTP sang HTTPS hoặc từ chối auth/API nhạy cảm trên HTTP. Redirect không đủ nếu login đã được gửi HTTP, nên frontend entry/instructions cũng phải đổi.
- **Required regression test:** Sau install, truy cập HTTP không được trả login SPA hoạt động qua HTTP; login/token requests không được xử lý ở cleartext. Console HTTPS và Agent canonical HTTPS vẫn hoạt động với trust fixture đúng.
- **Estimated change scope:** Medium.

### R10-06 — Public-key input không loại private-key material

- **Severity:** P1.
- **Confidence:** High về đường copy nguyên nội dung; probe dùng nhãn giả, không dùng private key thật.
- **Category:** confirmed defect.
- **File:** `scripts/prepare-deploy.ps1:243`, `:308`; `scripts/package-agent.ps1:293`; `scripts/install-server.ps1:281`, `:291`, `:299`.
- **Symbol:** `PublicKeyPath` handling, `Set-AgentReleasePublicKey`.
- **Evidence:** Prepare chỉ kiểm file tồn tại rồi copy nguyên sang `agent-release-public.pem`. Package đọc nguyên PEM vào `ReleasePublicKeyPem`; installer đọc nguyên vào server config. Probe setter chấp nhận nội dung mang `BEGIN PRIVATE KEY` và ghi vào JSON.
- **Finding:** File được gọi là public key nhưng không được validate/chuẩn hóa thành public-only material trước phân phối. Comment “private key must NEVER ship” chưa có enforcement.
- **Realistic failure scenario:** Nhầm đường dẫn giữa public/private PEM khi build fresh package hoặc server artifact. File riêng tư được copy vào gói mang đi các máy trước khi phát hiện lỗi.
- **Operational impact:** Mất tính bí mật của signing key nếu input là private key thật; có thể ảnh hưởng toàn bộ fleet dùng public key tương ứng. Không có bằng chứng việc này đã xảy ra.
- **Root cause:** Tin tên parameter/file thay cho kiểm tra nội dung và xuất public-only representation.
- **Recommended direction:** Reject PEM có private-key block tại input boundary; parse key hợp lệ và export public portion chuẩn. Scan final distributable contents cho private material, kể cả public-key filename và JSON; không ghi PEM vào log.
- **Required regression test:** Public PEM hợp lệ pass; PKCS#1/PKCS#8 private PEM, mixed PEM và malformed input bị từ chối trước copy/package. Assert không private marker/material trong server/Agent output. Giữ private signing input của `SignWith` ở máy build.
- **Estimated change scope:** Small.

### R10-07 — Password không round-trip qua connection string

- **Severity:** P2.
- **Confidence:** High; chạy đúng config function rồi parse bằng connection-string builder.
- **Category:** confirmed defect.
- **File:** `scripts/install-server.ps1:258`; `scripts/setup-iis-local.ps1:353`.
- **Symbol:** `Write-BackendAppSettings`, `Ensure-AppSettings`.
- **Evidence:** Connection string được ghép `Password=$DatabasePassword` trực tiếp. Probe dùng mật khẩu giả chứa `;Application Name=...`; giá trị Password parse lại khác input. `PGPASSWORD` của psql vẫn nhận nguyên input.
- **Finding:** JSON escaping không phải connection-string escaping. Password hợp lệ cho PostgreSQL có thể tạo config API sai dù apply schema thành công.
- **Realistic failure scenario:** Secret store sinh password chứa semicolon/dấu nháy; installer tạo DB/apply SQL với đúng password nhưng API đọc password bị cắt hoặc connection string không parse được.
- **Operational impact:** IIS 500.30/DB unavailable ngay sau install hoặc overwrite config; operator dễ chẩn đoán nhầm thành lỗi schema.
- **Root cause:** Dùng string interpolation cho format có quoting riêng.
- **Recommended direction:** Dùng connection-string builder tương thích Npgsql để serialize từng field; cùng parser cho các drill hiện đang `Split(';')`. Không hạn chế password chỉ để né escaping.
- **Required regression test:** Password chứa `;`, `=`, dấu nháy và khoảng trắng đầu/cuối: value round-trip exact; cùng credential chạy psql và API thành công, không in secret.
- **Estimated change scope:** Small.

### R10-08 — Rollback giữ cấu hình đã bị update thay đổi

- **Severity:** P2.
- **Confidence:** High; function probe xác nhận key mới còn sau restore.
- **Category:** confirmed defect.
- **File:** `scripts/update-server.ps1:360`, `:370`, `:371`; `scripts/install-server.ps1:279`, `:299`, `:497`.
- **Symbol:** `Restore-Backup`, `Set-AgentReleasePublicKey`.
- **Evidence:** Restore copy live appsettings sang temp, giải nén archive có config cũ, rồi copy live config lên lại. Trong khi đó installer luôn ghi key từ artifact kể cả không `OverwriteAppSettings`. Probe old/new public-key labels trả `RollbackRetainsNewPublicKey=true`.
- **Finding:** Giả định “configuration does not change between deployments” mâu thuẫn với code update hiện tại.
- **Realistic failure scenario:** Artifact mới có public key khác hoặc sai; update lỗi health. Code/database quay lại nhưng server vẫn dùng key của artifact thất bại, không còn khớp fleet/chuỗi release cũ.
- **Operational impact:** Rollback health có thể pass trong khi publish Agent release tiếp tục thất bại hoặc kiểm chứng theo key không mong muốn.
- **Root cause:** Chính sách preserve operator config được áp dụng cả với thay đổi do chính deployment tạo ra, không có snapshot ownership.
- **Recommended direction:** Restore pre-deployment config exact theo archive, hoặc merge có danh sách field rõ giữa operator changes và deployment changes. Key rotation có lifecycle riêng; không cập nhật tự động chỉ vì có file trong artifact.
- **Required regression test:** Key A trước deploy, artifact key B, injected health failure; config sau rollback phải là A, cùng fingerprint của backup. Thêm control update thành công và preservation operator override.
- **Estimated change scope:** Small.

### R10-09 — Retention zero phá hủy rollback của lần update hiện tại

- **Severity:** P2.
- **Confidence:** High; tái hiện trên file giả.
- **Category:** confirmed defect.
- **File:** `scripts/update-server.ps1:30`, `:325`, `:328`, `:434`, `:437`.
- **Symbol:** `KeepBackups`, `Remove-OldBackups`.
- **Evidence:** Parameter là int không có ValidateRange. Sau khi lấy path archive/dump, script gọi prune trước deploy. `Select-Object -Skip 0` chọn tất cả backup, kể cả pair vừa tạo. Probe `KeepZeroDeletesCurrentRecoveryPair=true`.
- **Finding:** Script giữ path tới các backup đã bị chính nó xóa, nhưng vẫn coi automatic rollback khả dụng.
- **Realistic failure scenario:** Operator đặt retention 0 để không giữ lịch sử sau update; health fail sau migration và script không còn archive/dump để khôi phục.
- **Operational impact:** Mất recovery point ngay trong maintenance window; với R10-01 còn có thể mở pool dù recovery chưa hoàn tất.
- **Root cause:** Không validate minimum retention và không phân biệt active rollback snapshot với backup lịch sử.
- **Recommended direction:** Validate giá trị, bảo vệ pair đang dùng, chỉ prune sau deployment commit/rollback thành công. Nếu hỗ trợ zero, định nghĩa là không giữ lịch sử sau hoàn tất, không xóa active recovery pair.
- **Required regression test:** 0/1/negative retention, failed health và failed copy; current pair tồn tại đến khi recovery xong. Không xóa backup khác installation dùng chung root.
- **Estimated change scope:** Small.

### R10-10 — Release gates chưa theo kịp migration head

- **Severity:** P2.
- **Confidence:** High; head thực lấy từ SQL mới apply và source assertions xác định.
- **Category:** confirmed defect.
- **File:** `scripts/test-production-support-server-artifact-smoke.ps1:267`; `scripts/test-production-support-release-db.ps1:122`; `scripts/test-production-support-query-performance.ps1:481`; `MProjectBackend/MProject.Infrastructure/Migrations/20260906090000_BindTusSession.cs:7`.
- **Symbol:** `migrationHeadBefore` assertion, `ProductionSupportMigrationApplied`, performance report `MigrationHead`.
- **Evidence:** Smoke hard-fail nếu head khác `20260902070533_AddProductionSupportReporter`; release DB đánh dấu support applied bằng equality với head đó nhưng vẫn có `Succeeded=true`; performance report ghi literal head đó. Runtime schema source hiện tại có **48 migrations**, head **BindTusSession** ngày 06-09.
- **Finding:** Release mới hợp lệ bị smoke từ chối; reporting của hai gate còn lại có thể gây false negative hoặc ghi sai provenance.
- **Realistic failure scenario:** Chạy gate với artifact vừa build từ HEAD; smoke dừng trước startup dù schema đúng. Nếu giữ default folder release cũ để pass, phép kiểm không còn chứng minh release mới.
- **Operational impact:** Gate mất giá trị làm điều kiện phát hành, operator phải sửa tay hoặc dựa trên kết quả artifact cũ.
- **Root cause:** Đóng cứng một feature migration thành head vĩnh viễn, không phân biệt “migration đã áp dụng” với “migration mới nhất”.
- **Recommended direction:** Artifact manifest cung cấp expected head/hash; assert head thực khớp artifact. Kiểm feature migration bằng membership và required schema, không equality với MAX. Report đọc head thực.
- **Required regression test:** Thêm migration sau AddProductionSupportReporter; tất cả gate vẫn nhận artifact đúng và ghi head mới. Artifact/schema mismatch hoặc thiếu required support migration phải fail rõ.
- **Estimated change scope:** Small.

### R10-11 — Classifier coi migration không hiểu như không destructive

- **Severity:** P2.
- **Confidence:** High về khả năng bỏ sót; chưa có bằng chứng migration mất business data hiện tại đã vượt gate.
- **Category:** design risk.
- **File:** `scripts/prepare-deploy.ps1:186`, `:191`, `:195`, `:198`; `scripts/update-server.ps1:182`; `MProjectBackend/MProject.Infrastructure/Migrations/20260818090000_RevokeViewerSoftwareMutationPermissions.cs:15`; `MProjectBackend/MProject.Infrastructure/Migrations/20260906090000_BindTusSession.cs:10`.
- **Symbol:** Up-method regex, destructive operation regex, `Assert-MigrationSafety`.
- **Evidence:** Regex yêu cầu `{ ... }` và chỉ nhận DropTable/DropColumn/AlterColumn/RenameTable/RenameColumn. Parse Up không match thì `return`; SQL không được phân loại. Probe ba migrations giả chỉ nhận block-body DropTable, bỏ raw SQL DROP và expression-bodied DropTable. Repository thực có cả raw SQL và expression-bodied Up.
- **Finding:** Contract “no pending destructive migration” mạnh hơn khả năng classifier; migration mới dùng syntax khác có thể bỏ qua approval/restore gate.
- **Realistic failure scenario:** Developer thêm migration raw SQL backfill+truncate hoặc expression-bodied drop, build vẫn sinh manifest không liệt kê migration đó; update cho phép chạy như additive.
- **Operational impact:** Release có rủi ro data loss được đưa vào window không chuẩn bị destructive restore.
- **Root cause:** Fail-open khi parser không hiểu, không có explicit safety classification per migration hoặc binding với SQL artifact.
- **Recommended direction:** Dùng EF operation metadata hoặc explicit reviewed metadata; unknown/Sql yêu cầu phân loại. Manifest nên chứa tất cả migration IDs, expected head, SQL hash. Migration thu hồi Viewer grants có chủ đích phải được annotate đúng chính sách security; không tự phục hồi grants hay sửa migration lịch sử.
- **Required regression test:** Block/expression bodies, generic AlterColumn, raw SQL, helper methods, malformed/stale manifest và SQL hash mismatch. Unknown phải bị chặn hoặc có explicit classification.
- **Estimated change scope:** Medium.

### R10-12 — Snapshot/rollback chưa có boundary cho mọi writer và storage

- **Severity:** P1, khi có host thứ hai hoặc writes được tiếp nhận trước deployment commit.
- **Confidence:** High về phạm vi code; chưa chạy drill hai IIS hosts/live traffic.
- **Category:** design risk.
- **File:** `scripts/update-server.ps1:124`, `:266`, `:285`, `:314`, `:432`, `:449`; `scripts/install-server.ps1:489`, `:500`; `MProjectBackend/MProject.Api/Program.cs:172`; `MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs:216`.
- **Symbol:** `Stop-BackendPool`, `New-Backup`, installer restart, fingerprint SQL, hosted-service lifecycle.
- **Evidence:** Chỉ stop một pool trên host đang chạy script. Installer mở site/pool trước fault/health decision. `-SkipBackup` bỏ luôn lần stop trước schema; stop còn lại nằm ở backend-copy sau schema. Snapshot gồm SQL dump và API tree, không gồm storage/git/tus dưới DataRoot. Fingerprint không bao quát job/heartbeat tables và chủ yếu là count/min/max, không phải content hash. Seeder/GC advisory locks không phải deployment fence chung.
- **Finding:** Không có đảm bảo tất cả writer dừng từ snapshot đến quyết định commit/rollback. Restore DB về snapshot có thể đảo ngược writes đã ACK, trong khi filesystem/Agent side effects không quay lại cùng thời điểm.
- **Realistic failure scenario:** Host B còn phục vụ cùng DB sau khi host A tạo backup, hoặc API mới nhận support update/Agent job ACK trong lúc health retry rồi rollback. Traffic đã thành công biến mất sau database restore. Nếu có GC/git write trong window, SQL restore cũng không phục hồi bytes/refs ngoài database.
- **Operational impact:** Mất writes sau snapshot, trạng thái server và Agent hoặc DB và object storage lệch nhau, dù restore command thành công.
- **Root cause:** Local pool stop được dùng như global maintenance boundary; app startup và traffic admission chưa tách khỏi deployment validation; backup inventory chưa có external-state contract.
- **Recommended direction:** Với topology một host, giữ external traffic và mutation workers bị chặn cho đến validation commit; stop trước schema độc lập với backup option. Với nhiều host, maintenance orchestration phải drain/fence tất cả writer và serialize deployment. Chốt restore policy cho local objects/git/versioned storage; tránh tự động full-DB rewind sau khi đã nhận writes mới mà không có quy trình reconciliation.
- **Required regression test:** Hai API hosts cùng DB, write trong các mốc snapshot/schema/health/rollback; assert writer được chặn hoặc dữ liệu được reconcile theo policy. Thêm blob/object sentinel và job receipt ngoài DB, failed health sau API startup và `SkipBackup` ordering.
- **Estimated change scope:** Large.

### R10-13 — Local deploy báo health pass với certificate không hợp lệ

- **Severity:** P2.
- **Confidence:** High; explicit TLS bypass trong helper đang được gọi.
- **Category:** confirmed defect.
- **File:** `scripts/deploy-local.ps1:63`, `:72`, `:227`; `scripts/setup-iis-local.ps1:520`.
- **Symbol:** `Test-HealthWithNode`, local setup first-deploy call.
- **Evidence:** Helper Node đặt `options.rejectUnauthorized=false`; default health URL là `https://te:8443`, không chỉ loopback. Setup gọi deploy helper này. Main install/update dùng curl strict, nhưng đường local deploy vẫn giữ bypass.
- **Finding:** Health acceptance ở local IIS/staging không chứng minh trust chain/hostname, trái điều kiện strict TLS dùng cho Agent rollout.
- **Realistic failure scenario:** Staging/self-signed cert chưa được trust hoặc binding đang dùng cert sai; helper vẫn báo backend health passed, sau đó Agent strict client từ chối kết nối.
- **Operational impact:** Kết quả kiểm chứng trước rollout sai, làm operator bỏ sót certificate/trust regression.
- **Root cause:** Helper local cũ không được đồng bộ với TLS policy của install/update.
- **Recommended direction:** Dùng chung strict probe implementation, không tắt validation. Development trust cần được cấp riêng theo fixture và hostname đúng.
- **Required regression test:** Trusted đúng host pass; untrusted/wrong-host/expired trusted chain fail. Chạy helper được setup gọi, không chỉ test ServerClient hay curl helper khác.
- **Estimated change scope:** Small.

## Drill coverage và các điều chưa được chứng minh

| Script/test | Điều nó kiểm tra | Khoảng trống cần giữ rõ |
| --- | --- | --- |
| `test-server-migration-rollback.ps1` | Tạo fixture DB/site/pool/cert/ports; thêm table và marker; fault sau installer; old health, old marker/new marker và DB sentinel | Hai artifact copy từ cùng binary; không chứng minh rollback giữa hai model versions. Manifest bị thay bằng destructive-list rỗng và schema mutation là additive table, nên không exercise destructive gate. Không hash toàn tree, không restore-failure/locked-file/partial-copy/two-host case. Script gọi installer vẫn có side effect lên Default Web Site; finally start lại nhưng không phục hồi `serverAutoStart` đã đổi. Không chạy fixture này trong review. |
| `test-database-backup-restore.ps1` | Dump thật, restore vào DB mới, so fingerprint source trước/sau và restored; đo thời gian, cleanup | Fingerprint không là full data hash; fixture review không có support traffic. Không chứng minh off-host backup, RPO, object storage, roles/global PostgreSQL settings hay exact database owner/ACL-level restoration. |
| `test-production-support-release-db.ps1` | Cluster riêng, apply SQL, head nonempty, gọi restore drill | Head boolean gắn cứng như R10-10; không chạy API/IIS. Tool preflight chỉ có initdb/pg_ctl/psql nhưng drill phụ thuộc pg_dump/pg_restore. Chưa chạy nguyên script trong review; chạy trực tiếp phần DB/restore với cluster riêng. |
| `test-production-support-server-artifact-smoke.ps1` | Published executable Production, appsettings artifact exclusion, startup migration disabled, live/ready body, same-origin SPA/deep link, anonymous support API=401 | Head cũ chặn artifact mới. Dùng process credentials và workers disabled, không test JSON của installer, pool identity/ACL, binding, TLS hoặc auth happy path. Chưa chạy nguyên gate; review chạy custom runtime probe để kiểm schema/health hiện tại. |
| `test-production-support-query-performance.ps1` | Cluster riêng, mặc định 1.5M requests/4.5M logs; EXPLAIN ANALYZE, time threshold/index assertions cho các query mẫu | SQL viết tay, drop/recreate một số indexes cho bulk load, fixture durability settings tắt; không phải API load/recovery test hay planner của production hiện hữu. MigrationHead report là literal. Không chạy workload này vì mục tiêu review là deployment/recovery. |
| `test-agent-tls.ps1` / AgentTlsTrustTests | Wrapper buộc `MPROJECT_REQUIRE_TLS_FIXTURE=1`, thiếu trusted fixture phải fail | Chưa chạy, không có trusted fixture được chuẩn bị cho session. Lịch sử canary không chứng minh certificate distribution ngày review. |
| Startup/watchdog/operational unit tests | Guards, fallback values, retention/metrics và presence của strings trong scripts | Test chứa table names/`FromHours(4)` không execute automatic rollback. 43 tests pass không bao phủ các defect functions được tái hiện ở đây. |

## Good decisions cần giữ

- Deployment scripts sở hữu schema trong production; design-time factory sinh SQL không khởi động host, không seed hay mở kết nối live. Runtime đã chứng minh `AutoMigrate=false` giữ nguyên head cũ.
- Idempotent SQL và BOM removal cho psql; `ON_ERROR_STOP=1` ở schema apply; dump custom format và restore drill trước deployment mặc định.
- Mặc định dừng local pool trước database snapshot; dùng archive gồm SPA/config; rollback có xóa file mới trước giải nén. Cần sửa exception/config paths nhưng giữ mục tiêu khôi phục code và DB cùng nhau.
- API artifact và SPA cùng site; production fallback loại `/api`, `/git`, `/agent`, `/health`, `/metrics`, `/swagger`, tránh biến API 404 thành SPA 200.
- `prepare-deploy` xóa API/wwwroot output con, xóa node_modules và dùng một `yarn.lock` với frozen install; frontend workflow cũng dùng frozen install, lint, test, build. `packageManager` pin Yarn 1.22.22.
- Dev secrets trống, credential fingerprints, JWT/hash guards, publish exclusion và Gitleaks CI giúp giảm tái phân phối credential. Public key vốn không phải secret; không xóa key công khai hợp lệ vì nhầm với credential.
- Main install/update dùng curl strict và update không tự tạo cert khi không còn cert hợp lệ. Điều cần sửa là chọn/bảo toàn đúng identity và local deploy còn bypass.
- Watchdog production 30/180 đồng bộ; update không ghi đè operator overrides. Retention support tối thiểu 24 tháng có startup validation; runbook phân biệt RTO restore với RPO backup.
- Shared install-token workflow đã được owner chấp nhận; package fresh-station và OTA có boundary riêng. Không chuyển nhiệm vụ review này thành yêu cầu manual enrollment từng PC.

## Rejected suspicions và nhận định được thu hẹp

- **“Production luôn auto-migrate như Development” — bác bỏ.** Default production=false; config generator explicit false. Explicit config true vẫn phải được kiểm khi vận hành, nhưng không gán lỗi mặc định.
- **“Watchdog fresh install vẫn 10/60 hoặc lệch domain fallback” — bác bỏ.** Test/probe xác nhận 30/180 ở source/generator/domain.
- **“Manifest chỉ có schemaVersion sẽ được chấp nhận” — bác bỏ qua probe.** Trên PowerShell 5.1, trường destructiveMigrations bị thiếu làm gate ném lỗi pending migration rỗng; không báo thành confirmed bypass. R10-11 nói về classifier bỏ sót và freshness, không dựa trên giả thuyết này.
- **“Không có AgentRelease section trong fresh JSON đồng nghĩa chắc chắn không có key” — bác bỏ.** `Set-AgentReleasePublicKey` chạy sau config write và nhận file public từ artifact. Trường hợp không có file chỉ warning, cần kiểm release-specific inputs trước phân phối.
- **“CORS không chứa port backend nên same-origin console mặc định hỏng” — bác bỏ.** Same-origin API URL rỗng không cần cross-origin allowlist; custom split-origin là trường hợp cấu hình riêng.
- **“Hai host chắc chắn chạy GC trùng và xóa object đồng thời” — không kết luận.** GC có PostgreSQL advisory lock và các writer có concurrency controls riêng. Các lock đó không dừng mọi writer cho full-database restore: phạm vi R10-12.
- **“Update API overlay chắc chắn load stale DLL” — chưa đủ bằng chứng.** Probe xác nhận file chỉ có ở bản cũ vẫn tồn tại sau forward copy, nhưng chưa tìm thấy runtime dependency hiện tại khiến file đó được load sai. Đây là kiểm tra cần bổ sung cho artifact ownership, không nâng thành defect chức năng riêng.
- **“Dùng chung installer token là security defect” — bác bỏ theo AGENTS.md.** R10-05/06 liên quan transport/signing material, không phải thay đổi quyết định provisioning.

## Insufficient-evidence areas

- Chưa biết effective config/env overrides, PostgreSQL version/roles, disk layout, ACL kế thừa, certificate stores, IIS idle/recycle/preload/web-garden hoặc topology thật. Không tuyên bố live credential bị lộ, backup đọc được bởi Users hay fleet đã mất TLS.
- Chưa diễn tập server reboot, locked DLL, process crash giữa copy, disk full, interrupted pg_restore, rollback đúng hai binary/schema khác nhau và hai hosts dùng chung DB. Function probes chứng minh control-flow defects, không thay những integration drills này.
- Chưa xác minh schedule/off-host backups, PITR/WAL retention, database-level owner/ACL/settings, object/Git snapshots và restore representative production data. Dump nhỏ restore 2.543 giây không đại diện cho RTO nhà máy.
- `setup-minio-service.ps1` không tự provision root credentials và hướng dẫn còn nhắc credentials mặc định; API production chặn `minioadmin`, còn installer chính chọn Local. Chưa có bằng chứng môi trường MinIO thực dùng default nên không gán đó là sự cố live. TaskScheduler mode là at-logon, không tương đương boot service.
- `setup-iis-local`/`deploy-local` là đường hai-site cũ, khác layout same-origin của server artifact; local setup không tự hoàn tất schema/admin seeding như installer chính. Cần tài liệu hóa prerequisite/đường sử dụng được hỗ trợ trước khi coi đây là alternative fresh production install.
- Chưa chạy clean frontend install/lint/test/build, Gitleaks binary hoặc toàn bộ Agent packaging. CI/package logic đã được đọc, nhưng không chứng nhận mọi generated artifact đang có trong `artifacts/` khớp revision này. SDK/EF toolchain còn phụ thuộc máy build; EF bootstrap dùng `8.*`, không phải pinned exact version.

## Hướng ưu tiên regression

1. Sửa R10-01/02/03 và kiểm chứng fail-before-mutation, rollback-failure giữ maintenance, đúng DB identity/schema contract.
2. Giữ certificate thumbprint qua update, chuyển console sang HTTPS, chặn private material tại public-key input; giữ nguyên shared installer-token provisioning.
3. Bổ sung config snapshot/retention tests và cập nhật release gates theo manifest/head thực.
4. Chạy IIS staging drill có hai binary thật, destructive migration được khai báo, locked/partial files và failed restore; sau đó kiểm chứng maintenance boundary với mọi writer và external storage theo topology triển khai.
