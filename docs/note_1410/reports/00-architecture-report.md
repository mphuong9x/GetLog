# 00 — Architecture, composition, and contract ownership

Ngày review: 2026-09-07. Phạm vi: chạy `docs/code-review/prompts/00-architecture.md`, không sửa production code.

- Revision mã nguồn: `0df8a0b0f3b6e55eaf91ff05e5978961861e6adc`.
- Prompt: bản working tree ngày review, SHA-256 `a479f877b7c8201d136842c1b05838df1942cded9fd84d304db77d253cfadb0f`. Prompt/review plan đang được stage, chưa thuộc HEAD trên.
- Working tree lúc bắt đầu có thay đổi `.gitignore`, review plan, các prompt, reports README và một file ghi chú. Review giữ nguyên các thay đổi đó.
- Đã đọc `AGENTS.md`, overview và phần đối chiếu tài liệu của `docs/PROJECT_REVIEW_CHECKLIST.md`, `docs/code-review/00-REVIEW-PLAN.md`. Checklist được dùng làm giả thuyết hồi quy, không làm bằng chứng lỗi hiện tại.
- Chấp nhận workflow owner đã duyệt: cùng install token cho nhiều PC; installer/script phải tự đăng ký mỗi PC lên `/computer`. Không đề xuất token riêng từng máy hoặc bắt buộc enrollment thủ công.

## Kết quả chính

Không thấy vòng project-reference hoặc đăng ký thiếu các hosted service bắt buộc. Có **4 findings**: một lỗi composition/serialization đã tái hiện, hai design risks và một sai lệch tài liệu. Không có finding Critical/High trong phạm vi này; đây không phải kết luận an toàn cho các workflow thuộc prompts 01–10.

| ID | Severity | Confidence | Category | Nội dung |
| --- | --- | --- | --- | --- |
| ARC00-01 | Medium | High | confirmed defect | JSON converter chung của API vô hiệu hóa strict enum contract của production support |
| ARC00-02 | Medium | High | design risk | Installer IIS không thiết lập lifecycle bảo đảm hosted workers chạy khi không có HTTP traffic |
| ARC00-03 | Medium | High | design risk | DomainEventDispatcher đánh dấu processed sau logging, chưa có delivery/handler contract |
| ARC00-04 | Low | High | maintainability issue | Tài liệu support còn mô tả current Agent ở Stage-0, khác composition hiện tại |

## 1. Runtime và hướng dependency thực tế

| Thành phần | Entry point, runtime | Boundary/persistence |
| --- | --- | --- |
| API | `MProjectBackend/MProject.Api/Program.cs`, ASP.NET Core net8.0 | Npgsql/PostgreSQL, Local hoặc MinIO blobs, Git repositories trên filesystem, TUS temp; chứa cả HTTP pipeline và server hosted workers |
| Application | net8.0, được API compose | Business services qua `IAppDbContext` và `IStorageService`; auth/RBAC, jobs, configuration, approvals, support, test monitor |
| Infrastructure | `DBContext.cs`: `AppDbContext : DbContext, IAppDbContext` | EF mapping, migrations, seeding, version/timestamp updates, soft-delete query filters, PostgreSQL-specific queries; hai storage implementations |
| Domain | net8.0 | Entities, domain enums/options; dùng enum/parser types từ `MProject.TestResults` |
| Agent | `MProjectAgent/Program.cs`: CLI verbs hoặc `RunAgentAsync`, net8.0 | Windows Service `MProjectAgent`; ServerClient, deployment, state/cache, supervisor, support outbox và IPC |
| Launcher | `App.xaml.cs` → `MainWindow.xaml.cs` → `MainViewModel`, net48 WPF | Một UI instance trong session, tray, refresh 1,5 giây; `IpcClient` gọi Agent bằng named pipe. Mở M-System bằng trình duyệt là navigation, không phải một server API client trong Launcher |
| Frontend | `src/main.tsx` → `App.tsx`, React 19/Vite | Auth/theme providers, QueryClient, BrowserRouter, route guards; Axios gọi browser API. Artifact triển khai được đặt trong API `wwwroot` |

Project-reference graph của **shipping projects**:

```text
API ------------> Application, Infrastructure, ProductionSupport.Contracts
Infrastructure -> Application, Domain, TestResults
Application ----> Domain, ProductionSupport.Contracts, TestResults
Domain ---------> TestResults
Agent ----------> Ipc.Contracts, ProductionSupport.Contracts, TestResults
Launcher -------> Ipc.Contracts
Ipc.Contracts / ProductionSupport.Contracts / TestResults -> không có ProjectReference
Frontend -------> HTTP contracts; không tham chiếu assembly C#
```

Graph không có chu trình; shared contracts không phụ thuộc ngược API/Agent/Launcher. Domain → TestResults hợp lý vì các enum parser là persisted contract, không kéo EF/Agent vào Domain. Tuy nhiên Application không hoàn toàn độc lập persistence framework: `.csproj` trực tiếp dùng EF Core/Relational/Npgsql và LibGit2Sharp; `IAppDbContext` phơi bày các EF abstractions. Đây là kiến trúc thực tế cần giữ trong bản đồ, chưa phải lỗi cần tái cấu trúc.

`MProjectBackend/MProject.slnx` chỉ có API, Application, Domain, Infrastructure, ProductionSupport.Contracts và TestResults. Không có Agent, Launcher, IPC, hoặc test projects. Build solution không chứng minh toàn bộ hệ thống shipping đã build; chạy test solution này cũng không đại diện Backend test suite. `MProjectAgent.UpdateFixture` và `MProjectAgent.ServiceFixture` là fixture, không phải runtime triển khai cho station; packaging chỉ đi qua Agent và Launcher.

## 2. Trace API composition → implementation → storage

`Program.cs:49–178` đăng ký scoped `AppDbContext` dùng `UseNpgsql`; `IAppDbContext` resolve **cùng instance** `AppDbContext` trong scope. Đã đối chiếu các mapping scoped với constructor implementation, không thấy mapping sang implementation sai hoặc dependency cycle trong graph được đọc.

### Các DI group chính

| Nhóm | Concrete services đã trace | Dependency kết thúc |
| --- | --- | --- |
| Authorization | `PermissionService` → `AuthorizationService`; `SubjectResolver`, `ScopeResolver`, `RbacGrantQueryService`, `AclQueryService`, `OwnerAccessResolver`, `PermissionEvaluator`; `ResourceLookupService`, `AuthorizedResourceQueryService` | PostgreSQL qua `IAppDbContext`, process-local `IMemoryCache`, `TimeProvider` |
| Mutation/audit | `AuthorizationMutationService`, `AuthorizationCacheInvalidator`, `AuthorizationAuditLogger`, `AuditLabelResolver`; cả `IAuditLogger` và `IAuthorizationAuditLogger` map về `AuthorizationAuditLogger` | Cùng scoped DB; audit/cache invalidation, không có message broker |
| Identity/organization | `RefreshTokenService`, `AuthService`, `AccessTokenSecurityStateValidator`, `UserService`, `TeamService`, `DepartmentService`, `DepartmentOwnershipService`, `RoleService`, `AdminService` | DB, auth options/config, permission/cache/audit; `UserService` còn nhận tập approval handlers |
| Assets/control plane | `ComputerService`, `AgentService`, `AgentCommandService`, `AgentBlobDownloadAuthorizer`, `AgentReleaseService`, `TestResultQueryService`, `ProductGroupService`, `ModelService`, `StationService`, `TestAssetService` | DB; release service thêm `IStorageService`; singleton `AgentBlobCapabilityService` cung cấp capability, không thay download authorization |
| Content/deployment | `SoftwarePackageService`, `SoftwareVersionService`, `SoftwareFileService`, `StationSoftwareAssignmentService`, `PcInstallationService`, `PcInventoryService`, `InstallationJobService` | DB; file/job services thêm storage, render/override resolution, cache/capabilities; assignment đi qua approval, command, baseline/config services |
| Config | `OverrideResolver`, `ConfigParameterService`, `ConfigValueOverrideService`, `OverrideFileService`, `OverrideFilePermissionService`, `OverrideFileMigrationService`, `ConfigBaselineService`, `ConfigBaselineValidator`, `ConfigBaselinePermissionService` | DB và storage theo constructor; `ConfigFileReader`, `ConfigFileWriter`, `BaselineEvaluator` xử lý nội dung. `ConfigRenderService` tạo scope riêng rồi resolve DB + storage (`ConfigRenderService.cs:49–51`) |
| Approval | `ApprovalService`, `ApprovalApproverResolver`, `OverrideFileApprovalHandler`, `SoftwareAssignmentApprovalHandler`, `ConfigValueOverrideApprovalHandler` | DB + permission/audit; handlers được resolve bằng `IEnumerable<IApprovalTargetHandler>`. `NoOpApprovalNotificationService` chỉ logging, không gửi notification ngoài hệ thống |
| Support | `ProductionSupportService`, `ProductionSupportQueryService`, `ProductionSupportRecoveryService` | DB; query/recovery thêm permission/audit; singleton `PrometheusProductionSupportMetrics` được inject qua `IProductionSupportMetrics` |
| Documents/uploads | `UserDocumentService`, `UploadReservationService`, `UploadReservationCleanupService` | DB; documents/reservation path phối hợp storage; cleanup reservation cập nhật DB. `TusUploadCapabilityService` singleton, `TusUploadHandler` sử dụng disk temp |
| Git | `GitRepositoryService`, `GitBasicAuthFilter` | DB cho metadata, `RepoStorageOptions`/LibGit2Sharp cho repository filesystem; không dùng blob provider |
| Storage selection | `Storage:Provider == Local` → `LocalStorageService`; nhánh còn lại → singleton `IMinioClient` + scoped `MinioStorageService` | Local dùng `Storage:Local:RootPath`; MinIO dùng endpoint/bucket/options. Chỉ nhánh MinIO đăng ký `MinioBucketInitializer` |

Cross-cutting registration: JWT bearer xác minh security state bằng scoped `AccessTokenSecurityStateValidator`; Agent dùng scheme/handler riêng. Forwarded headers được cấu hình proxy/network rõ ràng; authentication đứng trước rate limiter, cho phép support limiter partition theo Agent claim. Memory cache, rate limiter và Prometheus collector là state trong process, không phải distributed services.

### Tất cả API `AddHostedService`

| Registration | Concrete execution → storage | Cấu hình/lifecycle |
| --- | --- | --- |
| `MinioBucketInitializer` (`Program.cs:143`) | `Infrastructure/Storage/MinioBucketInitializer.cs`, `StartAsync` → `IMinioClient` kiểm tra/tạo bucket | Chỉ nhánh MinIO; lỗi startup được log warning, không có retry loop ở initializer |
| `TusTempCleanupService` (:172) | `Api/Infrastructure/TusTempCleanupService.cs` → scoped `UploadReservationCleanupService` → DB; `TusDiskStore` → temp directory; capability validator cho legacy sessions | `TusTempCleanupOptions`, section `TusUpload`; tránh quét OS temp root |
| `BlobGcService` (:173) | `Application/Services/Software/BlobGcService.cs` → scope → `IAppDbContext` + `IStorageService` | `BlobGcOptions`, DB metadata + object deletion |
| `InstallationJobWatchdogService` (:174) | `Application/Services/Software/InstallationJobWatchdogService.cs` → scope → DB jobs/installations/events | `InstallationJobWatchdogOptions`; cài mới bật, inactivity 30 phút, max attempt 180 phút |
| `StationRollbackWatchdogService` (:175) | `Application/Services/Software/StationRollbackWatchdogService.cs` → DB + `IStationSoftwareAssignmentService` → assignment/job paths | `StationRollbackOptions`; default `Enabled=false`, là opt-in, không phải thiếu registration |
| `ComputerLivenessWatchdogService` (:176) | `Application/Services/Assets/ComputerLivenessWatchdogService.cs` → scope → DB Agent/Computer/runtime/events | `ComputerLivenessOptions`; cài mới bật, sweep 2 phút/offline 5 phút |
| `DomainEventDispatcherService` (:177) | `Application/Services/Common/DomainEventDispatcherService.cs` → DB `DomainEvents` → logging + `ProcessedAt` | `DomainEventDispatcherOptions`; xem ARC00-03 |
| `ProductionSupportMetricsCollector` (:178) | `Api/Observability/ProductionSupportMetricsCollector.cs` → scope → DB support rows + PostgreSQL catalogs → Prometheus gauges | `ProductionSupportOptions`, validate retention/metrics interval khi startup, refresh loop riêng |

Sáu sweep workers từ TUS tới DomainEventDispatcher dùng `IntervalSweepService`: đợi interval, đọc `IOptionsMonitor`, kiểm tra Enabled, tạo scope trong sweep; lỗi thông thường được log rồi retry interval sau. Không thấy singleton trực tiếp giữ scoped DbContext trong các hosted workers này. Điều đó không tự bảo đảm process được IIS giữ sống (ARC00-02), hoặc mọi sweep đang khỏe.

Startup seeding chạy trước `app.Run`: `Database:AutoMigrate` mặc định theo Development (`Program.cs:444–453`), Production mặc định false; installer ghi false. Infrastructure sở hữu schema nhưng deploy script sở hữu việc áp migration Production. `/health/live` chỉ trả process response; `/health/ready` chỉ kiểm tra DB connectivity, không chứng minh storage hoặc hosted workers hoạt động.

## 3. Trace Agent và Launcher

`RunAgentAsync` bind và validate Agent origin/M-System URL cùng SupportOutbox bounds, đăng ký named HttpClients `server`/`health-probe`, một singleton `ServerClient`, token-transition manager và các store dùng chung.

| Hosted service | Trace và state |
| --- | --- |
| `AgentWorker` (`Program.cs:141`) | `Services/AgentWorker.cs:87–138`: initialize cache + enrollment với retry, recover `ProcessSupervisor`, bootstrap scan, rồi `Task.WhenAll` heartbeat/poll/inventory/test metrics/test results. `JobExecutor` dùng journal/catalog/cache/install directory và supervisor; không cần đăng ký mỗi loop là hosted service riêng |
| `BootstrapShutdownService` (:142) | Lắng nghe bootstrap stop-event khi biến môi trường được cấp, gọi `IHostApplicationLifetime.StopApplication`; là phối hợp bootstrap/update, không phải worker nghiệp vụ bị bỏ sót |
| `ProductionSupportDeliveryWorker` (:143) | `Support/ProductionSupportDeliveryWorker.cs` → `ProductionSupportDeliveryService.InitializeAsync/RunCycleAsync` → `SupportOutboxStore` + API client/token transition; signal hoặc idle delay đánh thức delivery |
| `IpcServer` (:144) | `Services/IpcServer.cs` → `IpcRequestHandler` → catalog/status/supervisor và support preview/commands. Server lấy capabilities từ handler, kiểm tra protocol, giới hạn frame/concurrency/timeout và peer session |
| `LauncherBootstrapper` (:145) | `Services/LauncherBootstrapper.cs`: mỗi 30 giây bảo đảm `MProjectLauncher.exe` cạnh Agent được chạy ở interactive session; xử lý Launcher cũ ở Session 0. Không gọi backend, không sở hữu DB |

State station có chủ sở hữu riêng:

- `TokenStore`: `StateDirectory/agent-state.json`.
- `AppCatalogStore`, `RuntimeStateStore`: `StateDirectory/runtime/catalog.json`, `supervised.json`.
- `JobJournalStore`: `StateDirectory/jobs/<job-id>.json`.
- `CacheIndex`: SQLite `Cache.Root/index.db`; bytes cache do `BlobCacheService` quản lý.
- `SupportOutboxStore`: SQLite `StateDirectory/runtime/support-outbox.db`; default state nằm ngoài station binaries. Đây là durable support workflow thật, không liên quan logging dispatcher phía server.
- Preview scanner và process supervision có filesystem/process dependencies; buffers progress/recent events/reachability là state trong memory, không phải nguồn durable.

Launcher `App.OnStartup` → `MainWindow` → `MainViewModel.RefreshAsync` → `IpcClient.SendAsync` → pipe `MProjectAgent.Launcher` → Agent `IpcServer` → `IpcRequestHandler`. Launcher dùng Newtonsoft camelCase, Agent dùng System.Text.Json; shared POCOs ở `netstandard2.0`, không buộc net48 tham chiếu hai contracts net8. `IpcClient` stamp protocol/correlation, single-flight và timeout; Agent chấp nhận omitted/v1 và v2, các support action yêu cầu exact capability. Composition hiện tại inject support services thật, nên capabilities support được quảng bá khi tạo handler bình thường.

## 4. Ownership và compatibility của contracts

| Contract/version | Owner và consumers | Compatibility assumptions |
| --- | --- | --- |
| Browser `/api/v1` | API `BaseController` version 1.0 + URL-segment routing; Frontend `axios-client.ts` và các TypeScript service/models | Frontend/backend deploy cùng server artifact. Không có C# project-reference hay generated TS contract được xác nhận; thay DTO phải kiểm tra cả hai phía |
| Core Agent `/agent/v1` | `AgentController`, Application `AgentModels`/`IAgentService`; Agent `ServerClient` và `Models/AgentApiModels` | Client/server DTO riêng, phải giữ backward compatibility cho fleet chưa update; không đồng nhất với browser API version |
| IPC `mproject-agent-ipc-v2` | `MProjectAgent.Ipc.Contracts`, direct consumers Agent + Launcher; Agent tests dùng IPC transitive và link Launcher transport source | `netstandard2.0`, không external package; v2 chấp nhận legacy v1/omitted. Station assembly version không thay thế IPC protocol/capabilities |
| Parser `mproject-test-result-parser-v1` | `MProject.TestResults`; direct production refs Domain, Application, Infrastructure, Agent; API dùng transitive qua backend graph | net8.0, pure parser. Domain test-result/support-log entities, Agent scanner/models/support preview và server ingest/query dùng types/parser chung; enum numbers persisted phải giữ nguyên |
| Support `production-support-agent-v2` | `MProject.ProductionSupport.Contracts`; direct production refs API, Application, Agent; tests hai phía | net8.0; routes `/agent/v2/support-requests`, DTO/enums/errors/outcomes, fingerprint schemas `create-v2`/`update-v1`; domain support enums riêng có parity tests. Launcher dùng IPC DTO mapping, không tham chiếu support HTTP assembly |
| Station bundle `2.0.0.3` | `MProject.StationBundleVersion.props`, import bởi Agent/Launcher/IPC csproj; packaging kiểm tra built executable/file versions | Agent net8 win-x64 self-contained + Launcher net48 cần Windows/.NET Framework 4.8. Cùng assembly/file version chưa thay thế wire compatibility hoặc kiểm tra payload |
| OTA manifest schema/protocol `1/1` | `package-agent.ps1` tạo; updater/release path tiêu thụ | Độc lập IPC v2; không bump OTA protocol chỉ vì IPC thay đổi; station bundle ký/rollback như một unit |

Đã đọc source, contract texts, fixtures và shared tests của TestResults/ProductionSupport.Contracts. Parser corpus gồm 17 cases; test source và JSON cùng được link vào hai test assemblies. Support có manifest routes/outcomes/errors, create/current/update/problem examples và deployment compatibility matrix; shared matrix tests chạy ở hai phía. Đây là kiểm tra parity tại revision hiện tại, không phải bằng chứng mọi binary cũ trong fleet đã tương thích.

`axios-client.ts:18–30` mặc định base URL rỗng, đổi `/api/...` thành `/api/v1/...`, giữ URL đã có version. `Program.cs:393–410` dùng default API 1.0, URL-segment reader và JSON configuration; `Program.cs:498–522` chỉ bật SPA khi có index, bảo vệ prefixes `/api`, `/git`, `/agent`, `/health`, `/metrics`, `/swagger` khỏi fallback HTML. Route frontend `/computer` được BrowserRouter phục vụ qua SPA fallback. TUS dùng literal path `/api/v1/files/upload` trong `TusUploadHandler.cs:39`, được xử lý bởi middleware riêng thay vì MVC API version selection.

## 5. Build → package → install/update

| Script | Source input → artifact → runtime |
| --- | --- |
| `build-local.ps1` | Infrastructure/API → idempotent `artifacts/schema.sql`; build API bằng `dotnet build`; frontend `yarn build` → `dist`; gọi `package-agent.ps1` → `artifacts/MProjectAgent-local`. Đây là local workflow, không publish server bundle; frontend dev được chạy riêng |
| `scripts/prepare-deploy.ps1` | Tạo schema + `migration-safety.json` từ migrations; gọi station packaging; `dotnet publish` API framework-dependent vào `artifacts/server/Api`; frontend clean install bằng frozen Yarn lock rồi build vào `server/wwwroot`; copy schema/manifest và install/update scripts |
| `scripts/package-agent.ps1` | Publish Agent win-x64 self-contained single-file; build net48 Launcher bằng VS MSBuild; copy Launcher dependencies + installers/defaults/helpers vào cùng folder; loại development/nested settings; bake config trước hashing; đọc version từ Agent và so Agent/Launcher/IPC file version; tạo manifest hashes schema/protocol 1; ZIP/sign khi được yêu cầu |
| `scripts/install-agent.ps1` | Chạy tại folder package: hosts/trust + strict TLS probe, ACL binaries/state/install root, giữ existing state hoặc shared-token self-announce; `MProjectAgent.exe install` qua `ServiceInstaller`, autostart/recovery + Launcher bootstrap. Fresh shared-token path đợi state đăng ký tối đa 90 giây; không bắt buộc `ComputerId` |
| `scripts/install-server.ps1` | Server artifact → IIS pool/site, PostgreSQL schema, data-root ACL, server-specific appsettings, API binaries + frontend vào `BackendPublishPath/wwwroot`, startup/readiness. Artifact config không đè cấu hình thật; API publish không mang development settings |
| `scripts/update-server.ps1` | Preflight migration safety; mặc định dừng writers, DB dump/restore drill + code archive; invoke installer với existing config/certificate, health check; failure restore code + DB khi backup đã tạo. Post-traffic support rollback cần theo compatibility runbook, không mặc nhiên áp lại pre-traffic snapshot |

Server bundle là API + frontend + schema/deploy metadata; station bundle là Agent + Launcher + IPC, không phải toàn bộ `.slnx`. `-SkipSchema`, `-SkipPackage`, `-SkipServerBuild` có thể dùng lại/bỏ qua inputs: không xem artifact cũ là bằng chứng build revision hiện tại. Package có shared install token được hỗ trợ cho fresh stations; script tách trường hợp này khỏi signed OTA ZIP.

## 6. Findings

### ARC00-01 — Global JSON converter làm mất strict enum contract

- **Severity:** Medium
- **Confidence:** High
- **Category:** confirmed defect
- **File:** `MProjectBackend/MProject.Api/Program.cs:408–410`; `MProject.ProductionSupport.Contracts/StrictStringEnumConverter.cs:13–30`; `MProject.ProductionSupport.Contracts/ProductionSupportEnums.cs:5`; `MProjectBackend/MProject.Tests/ProductionSupportContractTests.cs:27–36`.
- **Symbol:** `AddControllers().AddJsonOptions`, `StrictStringEnumConverter<TEnum>.Read`, `WireEnums_AreExactStringsAndRejectNumericOrWrongCaseValues`.
- **Evidence:** API thêm `new JsonStringEnumConverter()` vào global options. Support enums dùng type-level `[JsonConverter(typeof(StrictStringEnumConverter<...>))]`. Probe net8.0 compile trực tiếp hai source files enum/converter và dùng đúng global option như API cho kết quả: `2` → `Resolved`, `"resolved"` → `Resolved`. Nếu bỏ global converter, hai input này bị reject, nhưng `"2"` và `" Open "` vẫn được strict converter chấp nhận. Test hiện tại dùng `new JsonSerializerOptions(JsonSerializerDefaults.Web)` **không thêm converter chung** cho negative enum assertions, chỉ thử numeric JSON token và wrong case.
- **Finding:** Runtime serialization config giành quyền xử lý enum trước converter trong shared contract. Vì vậy contract “exact, case-sensitive strings; no numeric values” không được thực thi ở composition root. Bản thân strict converter còn dựa vào `Enum.TryParse`, nên chưa kiểm tra exact enum name cho numeric strings/whitespace.
- **Realistic failure scenario:** Một client support gửi `status: 2` hoặc `status: "resolved"` thay vì `"Resolved"`; model binding chuyển thành trạng thái hợp lệ rồi service không còn phân biệt wire representation ban đầu. Client lỗi vẫn hoạt động ở API hiện tại nhưng thất bại với consumer thực thi strict contract.
- **Operational impact:** Compatibility và input-validation contract sai so với fixtures; test xanh che khuất sai lệch. Chưa chứng minh bypass quyền hoặc state-transition rules: đó là phạm vi prompt 07.
- **Root cause:** Serializer policy toàn API và policy shared DTO có hai owner; negative tests không dùng production options. `Enum.TryParse` được dùng thay exact-name membership.
- **Recommended direction:** Đăng ký strict converters cho support enums trước broad converter hoặc dùng converter policy loại trừ các type đã có strict contract; kiểm tra ordinal exact name trong strict reader. Đối chiếu thêm `ProductionSupportApiClient.cs:68–73` vì client cũng thêm broad converter (`allowIntegerValues:false` chỉ xử lý một phần vấn đề).
- **Required regression test:** Dùng MVC production JSON configuration để bind create/update support DTO; reject số, chuỗi số, sai case, whitespace và unknown values trên mọi support enum; valid names phải round-trip. Kiểm tra cả Agent JSON options và ảnh hưởng đến enums legacy ngoài support. Thêm HTTP negative test ở prompt 07.
- **Estimated change scope:** Small

### ARC00-02 — IIS lifecycle chưa bảo đảm background workers tiếp tục chạy

- **Severity:** Medium
- **Confidence:** High
- **Category:** design risk
- **File:** `scripts/install-server.ps1:200–218`; `MProjectBackend/MProject.Api/Program.cs:172–178`; `MProjectBackend/MProject.Api/Controllers/Common/HealthController.cs:26–45`.
- **Symbol:** `Ensure-AppPool`, `Ensure-Website`, API hosted-service composition.
- **Evidence:** `Ensure-AppPool` chỉ tạo pool, đặt managed runtime và identity; `Ensure-Website` chỉ tạo/cập nhật path/pool. Script không đặt idle timeout, AlwaysRunning hoặc application preload/initialization. Mọi watchdog/GC/dispatcher nằm trong API process. Readiness chỉ kiểm tra DB. Microsoft hướng dẫn cấu hình application initialization/preload và idle timeout để ứng dụng IIS có background services được duy trì khi không có request: [Host ASP.NET Core on Windows with IIS — Application Initialization and Idle Timeout](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#application-initialization-module-and-idle-timeout).
- **Finding:** Deployment tự cấu hình IIS nhưng để lifetime của workers phụ thuộc inherited IIS settings và HTTP traffic. Không có worker host riêng hoặc đảm bảo tự khởi động ứng dụng sau reboot/recycle khi chưa có request.
- **Realistic failure scenario:** Server khởi động lại khi station network đang mất kết nối và chưa có người mở web; pool ở chế độ khởi động theo request nên sweeps chưa chạy. Hoặc giờ nghỉ không có traffic, pool bị idle shutdown; cleanup/job timeout không tiến triển cho tới request kế tiếp.
- **Operational impact:** Background housekeeping/recovery bị trì hoãn dù cấu hình Enabled đúng; GC/sweeps theo lịch có thể bỏ lỡ thời điểm. Heartbeat thường xuyên giảm khả năng gặp lỗi nhưng không phải bảo đảm lifecycle.
- **Root cause:** Host HTTP được dùng làm chủ sở hữu các công việc định kỳ nhưng installer chưa khai báo lifecycle tương ứng.
- **Recommended direction:** Trong IIS deployment hiện tại, cấu hình/verify explicit idle timeout, start mode và application initialization/preload phù hợp; không cần tái thiết kế toàn hệ thống. Theo dõi last successful sweep để readiness/monitoring phân biệt HTTP sống và worker khỏe.
- **Required regression test:** IIS thật với artifact cài mới: cold boot/recycle không gửi HTTP, chứng minh workers tự chạy; idle không traffic dài hơn timeout cũ, theo dõi sweep trực tiếp qua DB/log thay vì polling health (polling sẽ che lỗi). Kiểm tra lại khi update giữ existing pool.
- **Estimated change scope:** Medium

Confidence High xác nhận khoảng trống cấu hình trong source; chưa có evidence cấu hình IIS production đang dùng defaults hoặc đã xảy ra outage. Không xếp là sự cố production đã xác nhận.

### ARC00-03 — Domain event “processed” chỉ có nghĩa đã logging

- **Severity:** Medium
- **Confidence:** High
- **Category:** design risk
- **File:** `MProjectBackend/MProject.Application/Services/Common/DomainEventDispatcherService.cs:43–68`; `MProjectBackend/MProject.Infrastructure/DBContext.cs:1168–1178`; `MProjectBackend/MProject.Api/Program.cs:177`.
- **Symbol:** `DomainEventDispatcherService.DispatchAsync`, `AppDbContext.AddDomainEvent`.
- **Evidence:** `AddDomainEvent` thêm entity vào DbContext nên producer có thể lưu event cùng business changes. Dispatcher đọc pending rows, `LogInformation`, đặt `ProcessedAt`, rồi `SaveChangesAsync`; không resolve event handlers, không có delivery result/retry/dead-letter protocol. `DomainEventDispatcherServiceTests` kiểm tra batch/processed timestamps, không kiểm tra external side effect.
- **Finding:** Có persisted event record nhưng chưa có outbox delivery semantics. Tên dispatcher và processed timestamp dễ bị hiểu là business notification/consumer đã chạy. Đây là xác nhận lại giới hạn ARCH-001 trong checklist, không phải phát hiện mất side effect đang được yêu cầu trong workflow hiện tại.
- **Realistic failure scenario:** Một integration tương lai dựa vào pending DomainEvents để gửi thông báo hoặc đồng bộ; worker hiện tại đánh dấu processed trước khi integration nhận event, khiến consumer bỏ qua chúng.
- **Operational impact:** Có thể mất delivery khi tái sử dụng abstraction này; hiện tại chỉ chứng minh logging-only, không khẳng định jobs/support workflow đang phụ thuộc event delivery và đã hỏng.
- **Root cause:** Persistence của event và acknowledgement của consumer chưa có contract chung; logging được dùng làm điểm kết thúc.
- **Recommended direction:** Nếu chỉ cần audit/logging, đặt tên và tài liệu đúng semantics. Nếu cần delivery, xác định handlers/durable retry/idempotency và chỉ acknowledge sau side effect thành công; không ép thêm broker khi chưa có nhu cầu.
- **Required regression test:** Theo quyết định semantics: logging-only phải có contract test/documentation rõ; delivery thì cần crash-before/after-side-effect, retry, duplicate handling và concurrency trên PostgreSQL thật.
- **Estimated change scope:** Medium

### ARC00-04 — Support documentation mô tả sai capability state hiện tại

- **Severity:** Low
- **Confidence:** High
- **Category:** maintainability issue
- **File:** `MProject.ProductionSupport.Contracts/COMPATIBILITY_CONTRACT.txt:39–41`; `MProject.ProductionSupport.Contracts/CONTRACT.txt:1`; `MProjectAgent/Program.cs:104–145`; `MProjectAgent/Services/IpcRequestHandler.cs:46–59`.
- **Symbol:** “The current Stage-0 Agent”; `RunAgentAsync`; `IpcRequestHandler.GetCapabilities`.
- **Evidence:** Compatibility text gọi current Agent là base-only Stage-0. Production composition inject preview/command/status implementations và chạy delivery worker; handler quảng bá `support.status.v1`, `support.preview.v1`, `support.report.v2`, `support.update.v1`. `CONTRACT.txt` còn gọi Agent API là v1 trong khi constants/route manifest là `production-support-agent-v2`/`agent/v2`.
- **Finding:** Documentation của shared compatibility boundary đang trộn baseline lịch sử và runtime hiện tại. Matrix version v1 tự nó vẫn đúng và độc lập Agent API v2; sai lệch là phát biểu “current Stage-0” và nhãn Agent API cũ.
- **Realistic failure scenario:** Người chuẩn bị station rollout đọc text và cho rằng build này chưa bật support UI/enqueue; sau cài đặt các action xuất hiện vì DI services đã có. Hoặc một integration được viết theo tên API version cũ.
- **Operational impact:** Đánh giá sai phạm vi pilot/rollback và khả năng station hiện tại; chưa có bằng chứng operator đã rollout sai.
- **Root cause:** Contract text giữ trạng thái milestone cũ sau khi implementation được compose vào runtime; tests xác minh matrix invariants nhưng không kiểm tra phát biểu prose về current build.
- **Recommended direction:** Gắn nhãn đoạn Stage-0 là historical baseline; ghi rõ current composition, API version, và phân biệt code capability với các gate rollout vận hành. Không kết luận các gate nhà máy chưa pass chỉ vì không có chứng cứ trong review này.
- **Required regression test:** Composition smoke test resolve handler từ DI production và kiểm tra capability set; đối chiếu tài liệu/version manifest khi đổi phase. Giữ matrix tests cho các tổ hợp legacy.
- **Estimated change scope:** Small

## 7. Quyết định tốt cần giữ

- Shared install-token mass provisioning theo owner: runtime vẫn tạo identity/state từng station; không thay workflow bằng manual enrollment bắt buộc.
- Ba shared assemblies có ownership rõ, không kéo API/Agent implementation qua boundary; IPC netstandard2.0 cho net48/net8 interoperability.
- Một parser source và shared corpus chạy ở cả Backend/Agent; persisted enum numbers có regression tests.
- Station bundle props, built-binary version checks, manifest hashing và tách OTA schema/protocol khỏi IPC version.
- API compose DB qua scoped alias; workers mở scope cho mỗi sweep; support outbox nằm ngoài binary bundle và có delivery worker riêng.
- API và SPA deploy cùng server artifact, default same-origin; reserved routes không nhận nhầm `index.html` khi endpoint không tồn tại.
- Production schema mặc định do deploy script quản lý; server publish loại development settings và install/update giữ cấu hình thật; station enrollment state được giữ ở luồng reinstall thông thường.

## 8. Nghi vấn đã điều tra và bác bỏ

| Nghi vấn | Kết luận từ source/tests |
| --- | --- |
| Job/liveness watchdog bị quên `AddHostedService` | Không đúng; cả hai đăng ký, fresh-install values khớp domain defaults; focused config tests pass |
| Station rollback không hoạt động vì thiếu registration | Có registration, default disabled là opt-in. Chưa có yêu cầu bật mặc định để gọi đây là defect |
| Support services vẫn là Stage-0 stub | Không đúng với composition hiện tại; thực sự inject preview/command/status/API/delivery implementations. Sai lệch được ghi ở ARC00-04 |
| Domain → TestResults là đảo dependency vào Agent | Không đúng; parser không phụ thuộc Agent/EF/filesystem |
| net48 Launcher buộc load net8 support assembly | Không có reference đó; Launcher dùng IPC netstandard2.0 |
| IPC v2 buộc OTA protocol lên 2 | Không đúng; manifest protocol vẫn 1, tests bảo vệ sự độc lập |
| Tất cả route `/api` phải là MVC versioned route | Không đúng với TUS middleware; cần phân biệt protocol endpoint với browser controller |
| Production startup luôn migrate, làm `-SkipSchema` vô hiệu | Không đúng ở defaults và installer hiện tại; explicit configuration override vẫn có thể bật migration |
| `NoOpApprovalNotificationService` là DI trỏ nhầm class | Source đăng ký có chủ đích logging-only; chưa thấy yêu cầu email/push hay concrete sender khác phải dùng. Web inbox không phụ thuộc việc gửi email |
| Toàn hệ thống/test suite được đại diện bởi backend solution | Không đúng; graph và scripts chứng minh Agent/Launcher/tests nằm ngoài solution |

## 9. Bàn giao có giới hạn cho prompts 01–10

| Prompt | Trace cần tiếp tục; chưa kết luận business defect trong report này |
| --- | --- |
| 01 | `AgentWorker.EnsureEnrolledAsync` → `ServerClient` → `AgentController`/`AgentService`, token transitions, Agent authentication và blob authorizer. Giữ shared provisioning exception |
| 02 | `InstallationJobService`/assignment → Agent journal/executor → watchdog/rollback; kiểm tra multi-process concurrency và crash recovery trên DB/filesystem thật |
| 03 | `TusUploadHandler` → reservation → storage → BlobGc; kiểm tra provider selection và object availability, không suy diễn readiness DB là storage healthy |
| 04 | `ConfigRenderService` tạo DB scope riêng và có fallback khi đọc base blob thất bại; trace transaction/manifest implications trong workflow config, không kết luận từ composition đơn lẻ |
| 05 | `package-agent.ps1` → signer/release API → updater/bootstrap; xác minh archive exactness, state preservation, mixed-version binaries và rollback trên station |
| 06 | `IpcClient` → `IpcServer`/peer authorization → supervisor/bootstrapper; net48 execution/session boundary, capability negotiation và process recovery |
| 07 | ARC00-01: HTTP negative tests với production serializer, Agent response parsing; support outbox initialization/error behavior, durable delivery/reconcile. ARC00-04: đồng bộ rollout documentation |
| 08 | Parser/scanner → Agent ingestion → PostgreSQL/query; parity corpus đã pass nhưng cursor/dedup/timezone cần review riêng |
| 09 | Browser DTO/auth/refresh/route guard parity; no-op approval notification semantics; không coi client route guards là server authorization |
| 10 | ARC00-02 IIS lifecycle; MinIO initializer warning-only/readiness chỉ DB; artifact/schema provenance khi dùng skip flags, install/update rollback và data storage consistency. `build-local.ps1` gợi ý `dotnet test MProjectBackend` trong khi solution không chứa test project: xác minh/sửa hướng dẫn test ở workflow build |

## 10. Kiểm chứng và giới hạn bằng chứng

Môi trường: Windows/PowerShell, .NET SDK `10.0.400`, tests target net8.0. Đã chạy:

```powershell
dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProductionWatchdogConfigurationTests|FullyQualifiedName~ProductionSupportContractTests|FullyQualifiedName~ProductionSupportCompatibilityMatrixContractTests|FullyQualifiedName~TestResultParserCorpusContractTests' --verbosity quiet

dotnet test MProjectAgent.Tests/MProjectAgent.Tests.csproj --no-restore --filter 'FullyQualifiedName~StationBundleVersionContractTests|FullyQualifiedName~ProductionSupportApiContractTests|FullyQualifiedName~ProductionSupportCompatibilityMatrixContractTests|FullyQualifiedName~TestResultParserCorpusContractTests|FullyQualifiedName~IpcProtocolContractTests' --verbosity quiet
```

- Backend: **21 passed, 0 failed, 0 skipped**. Build phát cảnh báo nullable ở Repository/RepositoryModels/GitRepositoryService; không biến warnings này thành architecture findings.
- Agent: **20 passed, 0 failed, 0 skipped**.
- Probe riêng net8.0, compile trực tiếp source enum + strict converter trong thư mục tạm, đã tái hiện ARC00-01 với default options và global API options. Probe không gọi server, không sửa production source; kết quả không được trình bày như HTTP end-to-end test.
- 41 tests này không khởi động đầy đủ API/Agent production DI graph. Registration audit còn dựa vào source/constructors; không tuyên bố đã có full-host smoke test.

Chưa chạy install/update scripts, IIS recycle/idle test, real Windows Service/interactive Launcher, full package/signing, frontend build hoặc full regression suites. Không đọc state/token/config bí mật của máy triển khai. Không dùng generated outputs hoặc payload lịch sử làm evidence review; build/test artifacts chỉ được tạo bởi verification nêu trên.

Chưa đủ evidence để kết luận: IIS effective settings trên server; MinIO bucket/storage permissions và retry sau startup failure; multi-instance scheduling/cache behavior; compatibility của tất cả binary đang chạy trong fleet; các factory pilot gates đã đạt hay chưa; yêu cầu future consumers đối với DomainEvents; full API/browser DTO parity. Các điểm này được giữ là giới hạn/handoff, không biến thành confirmed production defects.
