# GAP-1 — Thiết kế cơ chế Config Override theo trạm/máy (thay `CheckSumCustom`)

> Mục tiêu: cho phép cùng một package-version deploy ra nhiều PC nhưng **một số file config mang giá trị riêng theo Model/Station/PC**, và **giữ nguyên giá trị đó qua mỗi lần update version** — đúng vai trò `CheckSumCustom` của UIStore.
> Phạm vi tài liệu: **Cơ chế (1) — override TOÀN-FILE theo scope** (MVP, đủ gỡ blocker). Cơ chế (2) per-key templating để ở mục cuối (Phase sau).
> Ngày: 2026-06-16. Trạng thái: thiết kế, chờ duyệt trước khi code.
>
> **Quyết định đã chốt (2026-06-16):**
> 1. ~~**HỢP NHẤT** với `LimitFile` (Kind=Config|Limit)~~ → **ĐÃ HỦY, xem ĐÍNH CHÍNH bên dưới.** Vẫn dùng `OverrideFile` cho GAP-1, nhưng **không** gộp limit file vào.
> 2. **3 tầng scope:** Model / Station / Computer (ưu tiên Computer > Station > Model).
> 3. **Approval:** bật cho scope **Station/Computer** (Model không bắt buộc), tái dùng pipeline approval của LimitFile.
>
> **⚠️ ĐÍNH CHÍNH (2026-06-16) — "limit file" KHÔNG phải override:** User làm rõ limit file = **baseline/validation per-MODEL** (giữ thông số kỹ thuật/giới hạn QUAN TRỌNG, Sector/Key→giá trị kỳ vọng, để **đối chiếu** file config FTU qua các lô, đảm bảo không bị đổi) — NGƯỢC hướng với OverrideFile (đẩy giá trị KHÁC theo trạm). Tương ứng `FtuDataConfigs`+`FtuService.CheckFtuConfig()`. ⇒ **Tách thành entity RIÊNG** (tên gợi ý `ConfigBaseline`/`SpecLock`), không phải `Kind` của `OverrideFile`. Tài liệu này (override) **vẫn đúng cho GAP-1 (A)**; chỉ bỏ phần gộp Kind=Limit. Thiết kế baseline (B) làm riêng ở session mới.

---

## 0. Insight kiến trúc (vì sao rẻ)

Agent deploy **thuần content-addressed**: nó nhận danh sách `{ Path, Sha256, DownloadUrl }` rồi ghi `installRoot/Path` từ blob `Sha256` (xem `InstallDirectoryService.DeployAsync`, `JobExecutor`). Danh sách này được server dựng **per-computer** tại [`InstallationJobService.BuildManifestJobsAsync`](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L603).

⇒ **Override = thay `Sha256` của một số `Path` ngay tại bước dựng manifest, theo Model/Station/PC của computer đó.** Agent thấy nó như file bình thường → **không cần sửa agent**. Nếu blob override đã có trong cache (cùng SHA) → không tải lại (dedup/delta giữ nguyên).

Ví dụ pilot: file `Config/DhcpConfig.json` có `ServerIp=192.168.1.254`. Mỗi station up một bản `DhcpConfig.json` riêng (ServerIp đúng của fixture). Khi computer ở station đó poll, server thay blob của `Config/DhcpConfig.json` bằng bản của station → agent deploy đúng giá trị, **không cần đổi version**.

---

## 1. Mô hình dữ liệu

### 1.1 Entity hợp nhất: `OverrideFile` (thay cả `LimitFile` lẫn `ConfigOverride`)

Một entity duy nhất, phân loại bằng `Kind`. Mô phỏng + thừa kế pattern của `LimitFile` cũ (blob dedup + Resource/ACL + approval). Khác cốt lõi: **gắn vào `SoftwarePackage` + `TargetRelativePath` + scope 3 tầng**, **độc lập version** (để sống sót qua update).

```csharp
public class OverrideFile : VersionedEntity, ISoftDeletable   // tên working; thay LimitFile
{
    public Guid ResourceId { get; set; }            // ACL: parent = resource của scope (model/station/computer)
    public virtual Resource Resource { get; set; }

    public OverrideFileKind Kind { get; set; }      // Config | Limit (mở rộng sau)

    public Guid SoftwarePackageId { get; set; }     // gắn PACKAGE → sống qua các version
    public virtual SoftwarePackage SoftwarePackage { get; set; }

    public string TargetRelativePath { get; set; }  // vd "Config/DhcpConfig.json" hoặc "Config/LimitConfig.json"

    public OverrideScope Scope { get; set; }         // Model | Station | Computer (đã chốt 3 tầng)
    public Guid? ModelId { get; set; }               // set khi Scope=Model
    public Guid? StationId { get; set; }             // set khi Scope=Station
    public Guid? ComputerId { get; set; }            // set khi Scope=Computer

    public string BlobSha256 { get; set; }           // nội dung override
    public virtual Blob Blob { get; set; }
    public string FileName { get; set; }

    public OverrideFileStatus Status { get; set; } = OverrideFileStatus.Draft; // Draft|Pending|Active
    public Guid CreatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public enum OverrideFileKind   { Config = 0, Limit = 1 }
public enum OverrideScope      { Model = 0, Station = 1, Computer = 2 }
public enum OverrideFileStatus { Draft = 0, Pending = 1, Active = 2 }   // Pending khi chờ approval (Station/Computer)
```

**Unique index:** `(SoftwarePackageId, TargetRelativePath, Scope, <scopeId>) WHERE !IsDeleted` — mỗi path chỉ 1 override active / scope-instance.
**Approval:** Scope **Station/Computer** → đi qua approval (Draft→Pending→Active). Scope **Model** → Draft→Active thẳng. (Đã chốt.)
**"Limit file chuẩn"** = `Kind=Limit`, `TargetRelativePath` trỏ file limit của package (vd `Config/LimitConfig.json`) — nay được deploy thật xuống PC qua cùng cơ chế.

### 1.2 Khai báo "file được phép override" trên Version (an toàn)

Thêm vào `SoftwareVersion`:

```csharp
public List<string> OverridablePaths { get; set; } = new();  // JSONB; vd ["Config/ProgramConfig.json","Config/DhcpConfig.json"]
```

- Người đóng gói (package author) chọn các file được phép override trong wizard upload (mặc định **rỗng = khóa toàn bộ**, giống `IsCheckSum` của hệ cũ).
- Manifest **chỉ** thay blob cho path nằm trong `OverridablePaths` của version đang deploy → exe/dll không bao giờ bị override (giữ integrity).
- Path đổi giữa version (vd folder khách có version trong tên) thì author khai báo lại path mới ở version mới — chấp nhận được cho MVP.

### 1.3 Đạp bỏ `LimitFile` thử nghiệm + tái dùng phần hay

`LimitFile` hiện tại chỉ là thử nghiệm, **không có data thật** → gỡ và thay bằng `OverrideFile`:
- **Xóa/đổi:** entity `LimitFile`, `LimitFileService`, `LimitFilesController`, FE `pages/LimitFiles`, DTO, migration `LimitFile`, `ResourceTypes.LimitFile`, `AppPermissions.*LimitFiles`, `ApprovalTargetType.LimitFile`.
- **Giữ & generalize (không vứt):** `LimitFileApprovalHandler` → `OverrideFileApprovalHandler`; `ILimitFilePermissionService` (model-manager based) → `IOverrideFilePermissionService`; mẫu blob dedup; FE upload modal + `hashWorker`.

Hạ tầng tái dùng:
- Blob dedup + ref-count: `Blob`, `IncrementBlobReferenceAsync`/`DecrementBlobReferenceAsync`, `SoftwareFileService.BuildBlobStoragePath`, `HashUtils.ComputeSha256Async`.
- ACL: `Resource` (Type mới `ResourceTypes.OverrideFile`), `IAuthorizedResourceQueryService`.
- Approval: `ApprovalTargetType.OverrideFile` + `OverrideFileApprovalHandler` (bật cho Station/Computer).
- Quyền: `AppPermissions.ReadOverrideFiles`, `ManageOverrideFiles` (có thể tách theo Kind nếu cần phân quyền limit vs config khác nhau).

---

## 2. Bộ giải (resolver) + điểm chèn trong manifest

### 2.1 Phân giải scope từ computer

Hệ phân cấp resource đã có: `Model.Resource ◄ Station.Resource ◄ Computer.Resource` (xác nhận: `Computer.Resource.ParentResourceId = stationResourceId`; `Station.Resource.ParentResourceId = model.ResourceId`). Từ computer suy ra `(ComputerId, StationId, ModelId)`.

### 2.2 Độ ưu tiên

Với mỗi `TargetRelativePath`: **Computer > Station > Model** (cụ thể nhất thắng). Trả về `Dictionary<path, ConfigOverride>`.

### 2.3 Chèn vào `BuildManifestJobsAsync`

Truyền thêm context (computer/station/model) vào hàm và thay blob trong vòng lặp file ([dòng 624](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L624)):

```csharp
// resolve 1 lần cho computer này:
var overrides = await _overrideResolver.ResolveAsync(packageIds, modelId, stationId, computerId); // path -> {Sha256,Size,ContentType,StoragePath}

foreach (var file in job.SoftwareVersion.Files.Where(x => !x.IsDeleted).OrderBy(x => x.RelativePath))
{
    var effSha   = file.BlobSha256;
    var effBlob  = file.Blob;             // {StoragePath, Size, ContentType}
    if (job.SoftwareVersion.OverridablePaths.Contains(file.RelativePath)
        && overrides.TryGetValue(file.RelativePath, out var ov))
    {
        effSha = ov.Sha256; effBlob = ov.Blob;   // ← THAY BLOB
    }
    var needsDownload = knownBlobs == null || !knownBlobs.Contains(effSha);
    // ... presign theo effBlob.StoragePath, emit { Path = file.RelativePath, Sha256 = effSha, ... }
}
```

Các caller cần truyền context (đều đã có `agent` → Computer/Resource): `GetActiveJobsAsync` (L79), `PollAsync` (L205), `ResolveManifestAsync` (L276). Presign hiện gom theo `storagePaths` — gom **theo blob hiệu lực** (đã override).

### 2.4 Hai chỗ BẮT BUỘC dùng chung resolver (nếu quên sẽ sai)

1. **`ResolveManifestAsync` "unexpected hashes" check** ([L255](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L255)): tập `manifestHashes` phải tính theo **blob hiệu lực** (sau override), nếu không sẽ cảnh báo nhầm.
2. **Drift detection** (`PcInventoryService` so file đã deploy với "expected"): "expected" phải là manifest hiệu lực per-computer (sau override), nếu không các file override sẽ bị báo **drift oan**. → Trích resolver thành service dùng chung (`IOverrideResolver`) cho cả manifest lẫn drift.

---

## 3. API + luồng upload (kế thừa `LimitFileService`)

- `GET  /api/v1/override-files?kind=&packageId=&scope=&modelId=&stationId=&computerId=` — list (lọc theo Kind: Config/Limit).
- `POST /api/v1/override-files` — tạo (body: **kind**, packageId, targetRelativePath, scope, scopeId, fileName, fileHash, fileSize, contentType). Blob dedup như `LimitFileService.CreateLimitFileRecordAsync`.
- `POST /api/v1/override-files/{id}/content` hoặc upload stream — đẩy nội dung nếu blob chưa có (FE băm SHA-256 client như wizard hiện tại).
- `GET/PUT /api/v1/override-files/{id}/content` — xem/sửa inline (tái dùng `UpdateLimitFileContentAsync` cho file text JSON/INI).
- `POST /api/v1/override-files/{id}/submit` — Station/Computer: tạo approval (Draft→Pending); Model: Draft→Active.
- `DELETE /api/v1/override-files/{id}` — soft delete + giảm ref blob.
- `PUT /api/v1/software-versions/{id}/overridable-paths` — author đặt danh sách path override-được.

Cho phép ext: `.json .ini .cfg .xml .txt .dat` (allowlist sẵn của LimitFile).

---

## 4. Frontend

1. **Author (đóng gói):** ở Version wizard/panel, từ cây file đã upload **tick các file override-được** → lưu `OverridablePaths`. (vd tick `Config/ProgramConfig.json`, `Config/DhcpConfig.json`.)
2. **Engineer (đặt giá trị trạm):** màn **Override Files** (thay màn `LimitFiles` cũ; có **filter Kind = Config / Limit**):
   - Chọn Package → thấy danh sách `OverridablePaths` của version Released hiện tại.
   - Với mỗi path: chọn scope (Model/Station/Computer) → upload file riêng **hoặc** sửa inline (editor text, tái dùng component của LimitFile).
   - Hiển thị override đang áp theo độ ưu tiên (Computer>Station>Model) để khỏi nhầm; trạng thái Draft/Pending/Active (Station/Computer cần duyệt).
   - **Limit file** xuất hiện ở đây dưới Kind=Limit (thay cho màn LimitFiles cũ).
3. Tái dùng `hashWorker.ts` (băm client) + modal upload của LimitFile.

---

## 5. Vì sao MVP này đủ gỡ blocker

- Các giá trị chặn khởi động/đúng-trạm nằm ở **path ổn định**: `Config/ProgramConfig.json` (`Station`, `DUT_IP`, `SfisConfig.Com`, `VersionConfig`) và `Config/DhcpConfig.json` (`ServerIp` = .254). Engineer up bản riêng 2 file này theo Station → xong.
- **Sống qua update:** override gắn `Package + Path` (không gắn version) → đẩy version code-only mới, override của station vẫn tự áp (miễn path còn trong `OverridablePaths`). Đây chính là tính chất "giá trị trạm sống sót qua mỗi update" của `CheckSumCustom`.
- **Integrity giữ nguyên:** path không khai báo override-được thì không bao giờ bị thay (giống `IsCheckSum` khóa cứng).
- File INI của khách (`data/custom_config_files/*.ini`) cũng override-được theo cùng cơ chế (toàn-file). Phần đa giá trị model-scope (BOM/SKU/criteria) đặt scope **Model**; IP fixture đặt scope **Station**.

---

## 6. Kế hoạch triển khai (đề xuất thứ tự)

0. **BE-0** Đạp bỏ `LimitFile` thử nghiệm: gỡ entity/service/controller/FE/migration; **generalize** approval handler + permission service sang `OverrideFile` (xem 1.3). *(GAP-3 cũ — LimitFile→agent — được hấp thụ vào đây.)*
1. **BE-1** Entity `OverrideFile` + 3 enum (Kind/Scope/Status) + `SoftwareVersion.OverridablePaths` + migration + `ResourceTypes.OverrideFile`/`AppPermissions`/`ApprovalTargetType.OverrideFile`.
2. **BE-2** `IOverrideResolver` (resolve theo Computer/Station/Model, ưu tiên Computer>Station>Model) — service dùng chung.
3. **BE-3** Chèn resolver vào `BuildManifestJobsAsync` + sửa `manifestHashes` (L255) + presign theo blob hiệu lực.
4. **BE-4** `OverrideFileService` + `OverrideFilesController` (kế thừa `LimitFileService`/controller) + `OverrideFileApprovalHandler` (Station/Computer).
5. **BE-5** Cập nhật **drift** (`PcInventoryService`) dùng resolver → không báo drift oan. *(bắt buộc trước khi bật drift cho package có override)*
6. **FE-1** Đặt `OverridablePaths` trong version panel.
7. **FE-2** Màn **Override Files** (thay `LimitFiles`; filter Kind; upload/sửa inline theo scope; trạng thái approval) + hiển thị độ ưu tiên.
8. **Test E2E** trên pilot: 2 station, mỗi station 1 `DhcpConfig.json`/`ProgramConfig.json` riêng → cùng package-version chạy đúng từng trạm; đẩy version code-only mới → override giữ nguyên; kiểm không drift; kiểm 1 file `Kind=Limit` cũng tới được PC.

**Không động tới agent.** Tất cả thay đổi ở server + FE.

---

## 7. Cơ chế (2) — per-key templating (Phase sau, KHÔNG làm bây giờ)

Khi cần khóa phần lớn file, chỉ cho sửa vài key (giống `IsCheckValue` + whitelist key của hệ cũ):
- Lưu **template** (blob gốc, ship theo version) + bảng `(path, key) → value` theo scope.
- **Render** ra blob hiệu lực: ưu tiên server-side (giữ agent "ngu", content-addressed) — server merge template+value, băm SHA-256, tạo blob tạm rồi đưa vào manifest như mục 2. Cần **editer theo định dạng** (JSON + INI), mô phỏng `IniExtensionEditer` cũ; INI cần thư viện parser.
- Lợi: không phải up nguyên file/станor; chỉ nhập đúng key. Hại: phức tạp, phải quản template-version + render cache.
- **Quyết định khi nào làm:** sau khi MVP (cơ chế 1) chạy pilot ổn và xuất hiện nhu cầu thực (quá nhiều file gần-giống nhau giữa các station).

---

## 8. Rủi ro & lưu ý

- **Drift** (mục 2.4) là cái dễ quên nhất — phải dùng chung resolver.
- **Approval:** nên bật cho override Station/Computer (đổi IP có thể làm hỏng trạm). Tái dùng pipeline approval của LimitFile.
- **Path khớp chính xác:** `TargetRelativePath` phải khớp `SoftwareFile.RelativePath` (kể cả prefix folder gốc khi upload). FE nên cho **chọn từ cây file của version** thay vì gõ tay.
- **GC blob:** override giữ ref-count như LimitFile → `BlobGcService` an toàn.
- **Provisioning NIC/IP** (vd máy phải có .254) **không thuộc** GAP-1 — đó là cấu hình OS của PC trạm, tách riêng (có thể tự động hóa bằng agent sau, ngoài phạm vi này).
```
