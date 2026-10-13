# DEP-02 — evidence và giới hạn

Phạm vi: chỉ SYN-72, SYN-73, SYN-76, SYN-80, SYN-82. Revalidate trên
`a4d2d8392e10463f50248b687ed355889da722ef` và working tree hiện tại; source,
tests và scripts là source of truth. Đã đọc AGENTS, PROJECT_MEMORY,
PROJECT_REVIEW_CHECKLIST, DEP-02 và reports 00/10. Không sửa trạng thái completed
trong memory/checklist, không commit, không thay schema/migration của ứng dụng.

## Revalidation và regression mapping

Năm regression focused ban đầu đều tái hiện lỗi trước khi sửa. Các bổ sung sau
đó kiểm tra fail-closed, recovery retry và tương thích PowerShell 5.1.

| Finding | HEAD được xác minh | Regression và runtime gate |
| --- | --- | --- |
| SYN-72 | Certificate discovery sort theo expiry, không giữ identity đang bind | `test-deployment-maintenance.ps1`: bound A dù B hết hạn muộn hơn; health request PS5.1. IIS: hai cert thật, vẫn dùng A; strict TLS từ chối cert chưa trust, hết hạn và URL sai host. |
| SYN-73 | Installer quảng bá HTTP; API/SPA/auth không có transport gate | PS test URL; `DeploymentMaintenanceTests.SYN73_CleartextNeverReachesSpaOrAuthentication`; IIS HTTP GET 308, HTTP login POST 426; smoke HTTPS SPA/deep-link 200, unauthenticated API 401. Không đổi auth/RBAC policy. |
| SYN-76 | Restore overlay live appsettings của bản lỗi lên archive cũ | PS test exact config/key, hidden files, recovery identity/path; IIS fault Health và Restore*: old DLL + DB head + exact appsettings/public RSA key A + certificate/binding A; retry khi binary bị xóa dở. |
| SYN-80 | DEP-01 đã giữ offline khi restore lỗi, nhưng chỉ stop một pool; startup/worker/host thứ hai chưa có fence chung | Shared-handle tests với hai writers; writer timeout, health failure, persisted marker, production missing proof, ACL, recovery-window replay, external drift và peer roots; IIS tám fault và recovery retry; forward commit từ chối DB rewind sau admission. |
| SYN-82 | Pool lifecycle/preload/idle phụ thuộc cấu hình kế thừa | PS effective pool properties; xUnit không đăng ký/construct hosted services khi validation; IIS cold pool start không HTTP, 1.260 giây idle, DB observation của dispatcher thật; preload trong maintenance không xử lý event. |

## Thay đổi

- API: `Infrastructure/DeploymentMaintenance.cs`, `Infrastructure/DeploymentCommand.cs`,
  `Program.cs` dưới `MProjectBackend/MProject.Api/`: lifetime writer lease,
  validation-only host, HTTPS boundary; thêm capability offline, không đổi v2.
- Scripts production: `deployment-maintenance.ps1`, `deployment-contract.ps1`,
  `install-server.ps1`, `update-server.ps1`, `prepare-deploy.ps1` dưới `scripts/`:
  fence, protected recovery set, exact config/key restore, certificate identity,
  strict health admission, IIS lifecycle và `-RecoverFrom`.
- Backend tests: `DeploymentMaintenanceTests.cs`, `DeploymentSchemaPostgresTests.cs`,
  `DeploymentScriptTests.cs` dưới `MProjectBackend/MProject.Tests/`.
- Script tests/build: `test-deployment-maintenance.ps1`, `test-deployment-contract.ps1`,
  `test-deployment-postgres.ps1`, `test-deployment-iis.ps1`,
  `build-deployment-lab-artifacts.ps1`, `test-server-migration-rollback.ps1`,
  `test-production-support-server-artifact-smoke.ps1` dưới `scripts/`.
  Runner đổi `BaseOutputPath` thành `OutputPath` để SDK vẫn loại toàn bộ `bin/`
  khỏi content glob; không đóng gói build output lồng nhau.
- Tài liệu: `DEPLOYMENT_CONTRACT.md` và file evidence này.

## Commands và kết quả

Chạy tại repository root. Runtime scripts dùng Windows PowerShell 5.1 elevated,
trên máy dev/test mà owner đã cho phép. Mỗi drill tự tạo PostgreSQL SCRAM cluster,
database, hai site/pool IIS, certificate, hostname và port riêng. Không nhận
connection string của database hiện hữu. Không in credential/token/response body.

| Command | Kết quả đã quan sát |
| --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-maintenance.ps1` | 15 focused cases pass. |
| `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore -p:OutputPath=bin/DEP02/ --filter FullyQualifiedName~Deployment --logger 'console;verbosity=minimal'` | 32 passed, 0 failed, 0 skipped. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-postgres.ps1 -FullSuite` | 1.142 passed, 0 failed, 0 skipped với PostgreSQL thật cô lập. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build-deployment-lab-artifacts.ps1 -OutputDirectory .tmp/dep02-artifacts-runtime5` | Hai binary/schema/manifest v2 thật được generate và verify thành công; frontend TypeScript/Vite build pass. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-iis.ps1 -SourceArtifactDir .tmp/dep02-artifacts-runtime5/old -NewArtifactDir .tmp/dep02-artifacts-runtime5/new -EnableInitializationForLab` | PASS: tám fault, forward commit, TLS negatives, cold/idle 1.260 giây và maintenance preload. `.tmp/dep02-runtime-result.json`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-iis.ps1 -SourceArtifactDir .tmp/dep02-artifacts-runtime5/old -NewArtifactDir .tmp/dep02-artifacts-runtime5/new -EnableInitializationForLab -IdleObservationSeconds 30 -ReportPath .tmp/dep02-writer-runtime-result.json` | PASS trên source/guards mới nhất: hai login writers thật, 3.690 mẫu trong cả chín exclusive recovery windows, không có ACK 2xx; tám fault/retry, TLS và cold/preload đều pass. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-production-support-server-artifact-smoke.ps1 -ArtifactRoot .tmp/dep02-artifacts-runtime5/smoke-release -ReportPath .tmp/dep02-smoke-result.json` | PASS: strict loopback HTTPS live/ready, SPA/deep-link 200, unauthenticated 401, seed một admin; migration head trước/sau startup không đổi. Layout smoke chỉ copy nguyên binary/SQL/manifest đã verify. |
| PowerShell parser cho 12 script thay đổi; `git diff --check` | PASS trên Windows PowerShell 5.1.18362.1171; chỉ có Git CRLF warnings. |

Gate writer đồng thời dùng login thật trên cả hai API, chỉ ghi HTTP
status/window ID. Cả hai baseline có login 200 ngoài maintenance. Trong 3.690
mẫu thuộc chín window, không có ACK 2xx khi exclusive recovery vẫn giữ cùng
window trước/sau request. Lần dài 1.260 giây và lần bổ sung writer đồng thời
đều kết thúc exit 0. Lần bổ sung chỉ idle 30 giây; không dùng
nó thay thế evidence idle 21 phút của lần dài.

Đã dọn 11 thư mục tạm của sáu lần fixture thất bại trước đó sau khi xác minh
site/pool/cluster của chúng đã dừng. Đây là dữ liệu lab có thể dựng lại bằng
script, không phải backup/dữ liệu ứng dụng hiện hữu. Hai site có sẵn
`Default Web Site` và `MProjectApi` vẫn Stopped như trước drill. Không đổi
certificate development có sẵn chỉ vì SDK mới in thông báo first-run.
Audit sau đó phát hiện HTTP.sys còn giữ 21 SNI entries dù site đã bị xóa.
Đã bổ sung `Remove-LabTlsBindings`, kiểm tra guard bằng focused test và chạy
chính function này trên các hostname/port đã xác minh của tám fixture. Audit
cuối PASS: không còn site/pool/certificate/SNI binding DEP-02. Đây là cleanup
follow-up riêng, không được suy diễn là `Remove-Website` tự dọn SNI.

Artifact cũ build từ revision thật
`78b02e88b5c20e80fb37ae86b887f1d57330630a`; chỉ backport sáu file deploy/maintenance
liệt kê trong `.tmp/dep02-artifacts-runtime5/provenance.json`. Không sửa migration,
model snapshot, generated SQL, manifest hoặc migration history để giả tạo delta:

- Old head: `20260902070533_AddProductionSupportReporter`.
- New head: `20260906090000_BindTusSession`.

Các lần thử đầu đã phát hiện lỗi fixture/PS5.1 (revocation property, POST framing,
recovery khi binary thiếu, chờ pool dừng, HTTP smoke chuyển HTTPS, SDK output glob).
Đã sửa và chạy lại; không tính các lần thất bại đó là runtime PASS. Nội dung log
và config nhạy cảm không được đưa vào evidence.

## Recovery và compatibility

| Điểm lỗi | Hành vi |
| --- | --- |
| Archive chưa có recovery set hoàn chỉnh | Giữ marker/pool stopped; chưa mutation schema; không tự suy đoán recovery pair. |
| Schema, copy hoặc new-version health | Khôi phục exact archive + dump + IIS state, rồi strict old-version health; chỉ sau đó admission. |
| Restore trước DB, sau recreate hoặc sau xóa binary | Giữ maintenance; `-RecoverFrom` cùng recovery window xác minh archive/target và retry được khi binary chưa đầy đủ. |
| Recovery health | Old state đã khôi phục nhưng không mở traffic; retry strict health trong cùng window. |
| External tree drift, sai archive/hash/window/site, thiếu cert/key | Fail closed; không tuyên bố phục hồi thành công hoặc mở writer. |
| Sau admission | Không cho full-DB rewind; cần forward recovery/reconciliation riêng. |

DEP-01 v2 hash/head/target, classifier, safe connection-string builder và
`-SkipSchema` compatibility rule giữ nguyên. Production startup **không migrate**;
deployment script vẫn sở hữu schema mutation. `-SkipHealthCheck` chỉ stage offline.

Automatic recovery cần baseline có maintenance capability/proof; không tự chạy
workflow full-DB rollback trên binary cũ không hợp tác. Existing DB không được tự
gán inventory single-writer mới. Certificate rotation tách khỏi code update.
Certificate/private key/trust cũ được giữ nguyên, không export PFX hay thay key;
nếu key bị xóa ngoài workflow thì recovery dừng, không tự tạo key thay thế.
Update yêu cầu canonical bindings ở hai port dùng cùng certificate trong My
store và SAN/EKU hợp lệ; layout/binding khác cần review riêng, không bị tự
đổi certificate để chạy qua gate.
Production launch trực tiếp hoặc legacy local-IIS tooling cũng phải cấp fence
proof hợp lệ; không có bypass tự động khi thiếu metadata. Task này không sửa
TLS-bypass finding của các local-deploy helpers ngoài phạm vi DEP-02.

Inventory phải đầy đủ cho mọi API writer cùng máy và Local blob/Git/TUS roots.
External bytes được giữ bất biến và fingerprint, không phải backup độc lập để
phục hồi arbitrary storage loss. MinIO, independent writers, SMB/network fence
và cross-machine topology chưa được hỗ trợ; scripts từ chối thay vì giả định an
toàn. Station payload/Agent side effects không nằm trong recovery archive của
server. Không sửa các finding backend/Agent khác.

## Gates chưa chạy / không tuyên bố

- OS reboot/power-loss thật: đã cold stop/start app pool và crash-marker tests,
  chưa reboot máy dùng chung.
- Multi-machine/network partition/SMB fencing, MinIO/versioned object restore,
  independent DB/storage writer reconciliation: ngoài topology được hỗ trợ.
- Station/fleet rollout và trust distribution thật; không dùng lab này để đóng
  các finding Agent hoặc rollout checklist.
- Full frontend suite và Agent suite không chạy trong task này; frontend build
  đã chạy, backend suite và server runtime là gates liên quan trực tiếp.

Lifecycle theo cấu hình IIS được Microsoft mô tả tại
[Application Initialization and Idle Timeout](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-8.0#application-initialization-module-and-idle-timeout).
Fixture TLS smoke tạo certificate bằng API .NET Framework hỗ trợ
[CertificateRequest](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest?view=netframework-4.8).
Các nguồn này giải thích API/config; kết quả PASS ở trên dựa trên command runtime.
