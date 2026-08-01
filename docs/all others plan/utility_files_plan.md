# Utility Files — Kế hoạch triển khai

> Plan triển khai tính năng phân phối **utility software** (Unikey, VS Code, Git, driver, …) tới PC theo per-PC assignment. Agent chỉ **tải file về `D:\Softwares\`**, KHÔNG cài đặt, KHÔNG launch, KHÔNG reboot. Kỹ thuật vào lấy file để chạy thủ công khi cần.

---

## 0. Tóm tắt & Quyết định đã chốt

| Quyết định | Giá trị |
|---|---|
| Mô hình hệ thống | **1 platform thống nhất**, phân biệt qua `PackageType` (không tách 2 hệ thống) |
| Phạm vi assignment | **Per-Computer** (sibling của Station assignment hiện có) |
| Đích đến file | `D:\Softwares\<PackageName>\<file>` (subfolder per package) |
| Payload | 1 file đơn (.exe / .msi / .zip), agent **giữ nguyên**, không extract |
| Fallback khi không có `D:` | `C:\MProjectAgent\Softwares\` + report `OperationalStatus = Error` |
| Desktop shortcut cho operator | **Không** (utility cho kỹ thuật, không cho worker) |
| Khái niệm version | **Không** cho utility (1 package = 1 file cố định; update = tạo package mới) |
| Lifecycle khi unassign | **Stop tracking** thôi, không xóa file trên đĩa |
| Team ownership | Cùng team với test software → 1 UI, không phân quyền theo PackageType ở phase 1 |
| Reboot | **Tuyệt đối không** — vì không cài đặt nên không phát sinh |

---

## 1. Mục tiêu & Success Criteria

**Mục tiêu:**
- Admin gán 1 utility package vào 1 PC cụ thể → trong < 2 phút PC đó có file ở `D:\Softwares\<Package>\`.
- Reuse tối đa hạ tầng hiện có: `BlobCacheService`, `InstallDirectoryService` (atomic deploy), `InstallationJob` state machine, hash-verified download.
- Không ảnh hưởng flow TestApp hiện tại (zero regression).
- Per-Computer assignment có thể tồn tại song song với Station assignment cho TestApp.

**Success criteria (đo được):**
- Gán package → job xuất hiện trên PC mục tiêu trong vòng 1 poll cycle (≤ 60s mặc định).
- Download hoàn tất → file ở đúng `D:\Softwares\<Package>\<filename>`, hash khớp 100%.
- Re-poll khi file đã tồn tại → agent skip (idempotent, không re-download).
- Unassign → `PcInstallationRecord` chuyển `Uninstalled` nhưng file vẫn còn trên đĩa.
- D: drive không tồn tại → file về `C:\MProjectAgent\Softwares\`, computer status = `Error`, dashboard hiển thị.
- TestApp deploy flow không bị regression (test suite cũ pass 100%).

---

## 2. Hiện trạng & Gap analysis

### Đã có (reuse)
| Component | File:Line | Vai trò trong plan này |
|---|---|---|
| `SoftwarePackage` entity | [SoftwarePackage.cs](MProjectBackend/MProject.Domain/Entities/Software/SoftwarePackage.cs) | Thêm trường `PackageType` |
| `SoftwareVersion` entity | [SoftwareVersion.cs](MProjectBackend/MProject.Domain/Entities/Software/SoftwareVersion.cs) | Reuse nguyên, utility vẫn cần 1 version để chứa file |
| `StationSoftwareAssignment` | [StationSoftwareAssignment.cs](MProjectBackend/MProject.Domain/Entities/Software/StationSoftwareAssignment.cs) | KHÔNG đụng. Plan thêm sibling `ComputerSoftwareAssignment` |
| `InstallationJob` | [InstallationJob.cs](MProjectBackend/MProject.Domain/Entities/Software/InstallationJob.cs) | Reuse nguyên. Mỗi utility download = 1 job |
| `PcInstallationRecord` | `MProjectBackend/MProject.Domain/Entities/Software/PcInstallationRecord.cs` | Reuse. Tracking utility đã tải về PC nào |
| `BlobCacheService` (agent) | `MProjectAgent/Services/BlobCacheService.cs` | Reuse 100% — content-addressed, hash-verified |
| `InstallDirectoryService.DeployAsync` | [InstallDirectoryService.cs:22](MProjectAgent/Services/InstallDirectoryService.cs#L22) | Reuse — atomic tmp+rename, hardlink/copy |
| `JobExecutor.ExecuteAsync` | [JobExecutor.cs:37](MProjectAgent/Services/JobExecutor.cs#L37) | Sửa nhánh: utility KHÔNG gọi `TryLaunchWithSupervisorAsync` |
| `AgentManifestJob` DTO | [AgentApiModels.cs:82](MProjectAgent/Models/AgentApiModels.cs#L82) | Thêm trường `PackageType` |
| `InstallationJobService.BuildManifestJobsAsync` | `MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:476` | Map `PackageType` vào manifest response |

### Gap (cần làm mới)

| Khả năng cần | Hiện trạng | Cần làm |
|---|---|---|
| Phân loại package theo "loại deploy" | Không | Enum `PackageType` + cột DB |
| Gán package theo PC cụ thể (không qua Station) | Không (chỉ có Station) | Entity `ComputerSoftwareAssignment` + service + endpoints |
| Agent biết là TestApp hay UtilityFile | Không | Trường `PackageType` trong manifest |
| Agent ghi UtilityFile vào folder khác | Không | `UtilityRootOptions` + path strategy |
| Skip launch cho UtilityFile | `TryLaunchWithSupervisorAsync` gọi unconditionally (chỉ skip nếu `EntryPointPath` rỗng) | Skip dựa trên `PackageType`, độc lập với `EntryPointPath` |
| Fallback khi D: không có | Không xử lý | Detect + fallback C: + report Error |
| UI gán package theo PC | Chỉ có gán theo Station | Trang Computer detail thêm tab + modal gán |
| Form tạo package có chọn type | Form chỉ có Name/Description | Field `PackageType` (TestApp / UtilityFile) |

---

## 3. Kiến trúc giải pháp (high-level)

```
┌────────────────────────────────────────────────────────────────────┐
│ Backend                                                            │
│                                                                    │
│  SoftwarePackage { PackageType: TestApp|UtilityFile }              │
│        │                                                           │
│        ├── StationSoftwareAssignment   (chỉ TestApp dùng)          │
│        └── ComputerSoftwareAssignment  (chỉ UtilityFile dùng)      │
│                              │                                     │
│                              ▼                                     │
│  InstallationJobService.PollAsync                                  │
│    - Resolve assignments cho ComputerId = Station ∪ Computer       │
│    - Tạo InstallationJob cho mỗi (PC, Version) chưa có record      │
│    - Manifest job kèm PackageType                                  │
└────────────────────────────────────────────────────────────────────┘
                              │ (poll/manifest API — y nguyên)
                              ▼
┌────────────────────────────────────────────────────────────────────┐
│ Agent                                                              │
│                                                                    │
│  JobExecutor.ExecuteAsync                                          │
│   ├── ResolveAsync         (giữ nguyên)                            │
│   ├── DownloadMissingAsync (giữ nguyên — BlobCache hash check)     │
│   ├── DeployAsync                                                  │
│   │     ├── if PackageType == TestApp                              │
│   │     │     → D:\Apps\<Package>\        (giữ nguyên)             │
│   │     └── if PackageType == UtilityFile                          │
│   │           → D:\Softwares\<Package>\   (mới)                    │
│   │           → fallback C:\MProjectAgent\Softwares\ nếu D: missing│
│   └── if PackageType == TestApp                                    │
│           → TryLaunchWithSupervisorAsync   (giữ nguyên)            │
│         else                                                       │
│           → skip launch, mark Completed                            │
└────────────────────────────────────────────────────────────────────┘
```

**Nguyên tắc thiết kế:**
1. **Single dispatch điểm**: chỉ 1 nhánh `if PackageType ==` trong `JobExecutor` quyết định deploy root + có launch hay không. Mọi thứ khác giữ nguyên.
2. **2 loại assignment là độc quyền theo PackageType**: validate ở service. Không cho gán TestApp qua per-Computer, không cho gán UtilityFile qua Station (phase 1; có thể nới sau).
3. **Backwards compatible**: `PackageType` default = `TestApp` cho mọi package cũ. Migration không phá data.
4. **Server-side validation chặt**: agent tin manifest, không tự suy luận. Loại sai → server từ chối từ trước khi tạo job.

---

## 4. Phân tách Milestones

| MS | Tên | Phụ thuộc | Kết quả |
|---|---|---|---|
| **B1** | Domain & Migration | — | DB có cột `PackageType` + bảng `ComputerSoftwareAssignments`. Build pass. |
| **B2** | Application Service + API | B1 | Endpoint assign/unassign per-computer, list, validation. |
| **B3** | Job generation (server) | B2 | `PollAsync` resolve cả 2 loại assignment, manifest có `PackageType`. |
| **B4** | Agent flow | B3 | Agent download utility về `D:\Softwares\`, skip launch. Fallback C:. |
| **B5** | Frontend | B2–B3 | Form chọn type, trang Computer detail có tab Utility + gán. |
| **B6** | Tests + observability | B4–B5 | Unit + integration test pass; metric `utility_files_downloaded_total`. |

> B1–B4 là core. B5 song song được khi API ổn định. B6 cuối.

---

## 5. B1 — Domain Model & Migration

### 5.1 Enum mới — `PackageType`

File mới: `MProjectBackend/MProject.Domain/Enums/PackageType.cs`

```csharp
namespace MProject.Domain.Enums;

public enum PackageType
{
    TestApp = 0,      // Default cho data cũ — deploy + launch + supervise
    UtilityFile = 1   // Download file, không cài, không launch
}
```

### 5.2 Sửa `SoftwarePackage`

[SoftwarePackage.cs](MProjectBackend/MProject.Domain/Entities/Software/SoftwarePackage.cs):

```csharp
public class SoftwarePackage : VersionedEntity, ISoftDeletable
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public PackageType PackageType { get; set; } = PackageType.TestApp;  // ← thêm
    public Guid CreatedBy { get; set; }
    // ...
    public virtual ICollection<StationSoftwareAssignment> StationAssignments { get; set; } = ...;
    public virtual ICollection<ComputerSoftwareAssignment> ComputerAssignments { get; set; } = new List<ComputerSoftwareAssignment>();  // ← thêm
}
```

**Lưu ý:** không thêm field cho file location vào entity — agent biết bằng `PackageType` + config root.

### 5.3 `SoftwareVersion` — KHÔNG đổi schema

Utility vẫn cần 1 `SoftwareVersion` để gắn file (vì `SoftwareFile` join qua `SoftwareVersionId`). Trong UI:
- Khi admin tạo utility package, **auto-create 1 version** ngầm với `VersionNumber = "1.0"`, `Status = Released`.
- Form upload file đẩy thẳng vào version đó.
- UI không hiển thị version selector cho utility package.

`EntryPointPath` để `null` (đã nullable). `EntryPointMode` mặc định `LongRunning` — không matter vì agent skip launch.

### 5.4 Entity mới — `ComputerSoftwareAssignment`

File mới: `MProjectBackend/MProject.Domain/Entities/Software/ComputerSoftwareAssignment.cs`

```csharp
using MProject.Domain.Entities.Assets;
using System;

namespace MProject.Domain.Entities.Software;

public class ComputerSoftwareAssignment : BaseEntity, ISoftDeletable
{
    public Guid ComputerId { get; set; }
    public virtual Computer Computer { get; set; } = null!;

    public Guid SoftwarePackageId { get; set; }
    public virtual SoftwarePackage SoftwarePackage { get; set; } = null!;

    // Pinning version — phase 1 luôn = "version mặc định" của package.
    // Giữ field để tương lai mở rộng (vd: pin từng version utility).
    public Guid? TargetVersionId { get; set; }
    public virtual SoftwareVersion? TargetVersion { get; set; }

    public Guid AssignedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
```

**Khác biệt với `StationSoftwareAssignment`:**
- Không có `IsActive` (utility không có khái niệm "1 active per scope" như TestApp).
- Không có `PinnedAt/PinnedBy` (chưa cần — sẽ thêm khi cần).
- Unique constraint: `(ComputerId, SoftwarePackageId, IsDeleted = false)`.

### 5.5 DBContext + cấu hình

File: `MProjectBackend/MProject.Infrastructure/Persistence/AppDbContext.cs` (hoặc tương ứng):

```csharp
public DbSet<ComputerSoftwareAssignment> ComputerSoftwareAssignments { get; set; } = null!;

// trong OnModelCreating:
modelBuilder.Entity<ComputerSoftwareAssignment>(b =>
{
    b.HasOne(x => x.Computer).WithMany().HasForeignKey(x => x.ComputerId)
        .OnDelete(DeleteBehavior.Restrict);
    b.HasOne(x => x.SoftwarePackage).WithMany(p => p.ComputerAssignments)
        .HasForeignKey(x => x.SoftwarePackageId).OnDelete(DeleteBehavior.Restrict);
    b.HasOne(x => x.TargetVersion).WithMany().HasForeignKey(x => x.TargetVersionId)
        .OnDelete(DeleteBehavior.SetNull);

    // Filtered unique index — chặn duplicate active assignment
    b.HasIndex(x => new { x.ComputerId, x.SoftwarePackageId })
        .HasFilter("[IsDeleted] = 0")
        .IsUnique();
});

modelBuilder.Entity<SoftwarePackage>()
    .Property(p => p.PackageType)
    .HasConversion<int>()
    .HasDefaultValue(PackageType.TestApp);
```

Cũng cần update `IAppDbContext` interface để expose `ComputerSoftwareAssignments`.

### 5.6 Migration

```bash
dotnet ef migrations add AddPackageTypeAndComputerSoftwareAssignments \
  --project MProjectBackend/MProject.Infrastructure \
  --startup-project MProjectBackend/MProject.Api
```

**Migration script kỳ vọng:**
- `ALTER TABLE SoftwarePackages ADD PackageType INT NOT NULL DEFAULT 0` (= `TestApp`).
- `CREATE TABLE ComputerSoftwareAssignments (...)` với index unique như trên.
- Không động data cũ.

### 5.7 Constraints & validation rules (rút gọn)

| Rule | Enforce ở đâu |
|---|---|
| `PackageType` chỉ set lúc tạo, không cho đổi | Service `SoftwarePackageService.UpdateAsync` từ chối nếu request đổi field này |
| `StationSoftwareAssignment` chỉ cho `PackageType = TestApp` | Validate trong `StationSoftwareAssignmentService.AssignAsync` |
| `ComputerSoftwareAssignment` chỉ cho `PackageType = UtilityFile` (phase 1) | Validate trong `ComputerSoftwareAssignmentService.AssignAsync` |
| Mỗi UtilityFile package phải có đúng 1 `SoftwareVersion` `Released` | Validate khi tạo package + khi upload file |

---

## 6. B2 — Application Services

### 6.1 `IComputerSoftwareAssignmentService` (mới)

File mới: `MProjectBackend/MProject.Application/Interface/IComputerSoftwareAssignmentService.cs`

```csharp
public interface IComputerSoftwareAssignmentService
{
    Task<Guid> AssignAsync(AssignSoftwareToComputerRequest request, Guid userId);
    Task RemoveAssignmentAsync(Guid assignmentId, Guid userId);
    Task<IReadOnlyList<ComputerSoftwareAssignmentResponse>> GetByComputerAsync(Guid computerId);
    Task<IReadOnlyList<ComputerSoftwareAssignmentResponse>> GetByPackageAsync(Guid packageId);
}
```

DTOs trong `MProjectBackend/MProject.Application/Models/`:

```csharp
public sealed class AssignSoftwareToComputerRequest
{
    public Guid ComputerId { get; set; }
    public Guid SoftwarePackageId { get; set; }
}

public sealed class ComputerSoftwareAssignmentResponse
{
    public Guid Id { get; set; }
    public Guid ComputerId { get; set; }
    public string ComputerName { get; set; } = null!;
    public Guid SoftwarePackageId { get; set; }
    public string PackageName { get; set; } = null!;
    public DateTimeOffset AssignedAt { get; set; }
    public Guid AssignedBy { get; set; }
}
```

### 6.2 `ComputerSoftwareAssignmentService` (mới)

File mới: `MProjectBackend/MProject.Application/Services/Software/ComputerSoftwareAssignmentService.cs`

```csharp
public class ComputerSoftwareAssignmentService : IComputerSoftwareAssignmentService
{
    private readonly IAppDbContext _context;

    public async Task<Guid> AssignAsync(AssignSoftwareToComputerRequest req, Guid userId)
    {
        var computer = await _context.Computers
            .FirstOrDefaultAsync(c => c.Id == req.ComputerId && !c.IsDeleted)
            ?? throw new KeyNotFoundException("Computer not found.");

        var package = await _context.SoftwarePackages
            .FirstOrDefaultAsync(p => p.Id == req.SoftwarePackageId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Software package not found.");

        if (package.PackageType != PackageType.UtilityFile)
            throw new InvalidOperationException(
                "Only UtilityFile packages can be assigned per-computer. Use Station assignment for TestApp.");

        var existing = await _context.ComputerSoftwareAssignments
            .AnyAsync(a => a.ComputerId == req.ComputerId
                        && a.SoftwarePackageId == req.SoftwarePackageId
                        && !a.IsDeleted);
        if (existing)
            throw new InvalidOperationException("Package already assigned to this computer.");

        // Resolve "version mặc định" của utility package
        var defaultVersion = await _context.SoftwareVersions
            .Where(v => v.SoftwarePackageId == req.SoftwarePackageId
                     && v.Status == SoftwareVersionStatus.Released
                     && !v.IsDeleted)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                "Utility package has no Released version. Upload a file first.");

        var a = new ComputerSoftwareAssignment
        {
            ComputerId = req.ComputerId,
            SoftwarePackageId = req.SoftwarePackageId,
            TargetVersionId = defaultVersion.Id,
            AssignedBy = userId
        };
        _context.ComputerSoftwareAssignments.Add(a);
        await _context.SaveChangesAsync();
        return a.Id;
    }

    public async Task RemoveAssignmentAsync(Guid assignmentId, Guid userId)
    {
        var a = await _context.ComputerSoftwareAssignments
            .FirstOrDefaultAsync(x => x.Id == assignmentId && !x.IsDeleted)
            ?? throw new KeyNotFoundException("Assignment not found.");

        var now = DateTimeOffset.UtcNow;

        await _context.ExecuteInTransactionAsync(async _ =>
        {
            // Cancel pending jobs cho cùng (Computer, Package)
            var pending = await _context.InstallationJobs
                .Where(j => j.ComputerId == a.ComputerId
                         && j.SoftwareVersion.SoftwarePackageId == a.SoftwarePackageId
                         && (j.Status == InstallationJobStatus.Pending
                             || j.Status == InstallationJobStatus.Downloading
                             || j.Status == InstallationJobStatus.Installing))
                .ToListAsync();
            foreach (var j in pending)
            {
                j.Status = InstallationJobStatus.Cancelled;
                j.CompletedAt = now;
                j.LastErrorCode = "assignment_removed";
                j.LastErrorDetail = $"Computer assignment removed by user {userId}.";
            }

            // Quyết định #2: stop tracking thôi, KHÔNG xóa PcInstallationRecord.
            // File trên disk cũng để nguyên (agent không nhận lệnh xóa).
            // Để khi re-assign, set record về Uninstalled để job regenerate:
            var records = await _context.PcInstallationRecords
                .Where(r => r.ComputerId == a.ComputerId
                         && r.SoftwareVersion.SoftwarePackageId == a.SoftwarePackageId
                         && r.Status == InstallationStatus.Installed)
                .ToListAsync();
            foreach (var r in records) r.Status = InstallationStatus.Uninstalled;

            a.IsDeleted = true;
            a.DeletedAt = now;
            a.DeletedBy = userId;

            await _context.SaveChangesAsync();
        });
    }

    // GetByComputerAsync, GetByPackageAsync — projection thẳng, AsNoTracking
}
```

### 6.3 Sửa `StationSoftwareAssignmentService.AssignAsync`

Thêm validation đầu method ([StationSoftwareAssignmentService.cs:34](MProjectBackend/MProject.Application/Services/Software/StationSoftwareAssignmentService.cs#L34)):

```csharp
var package = await _context.SoftwarePackages
    .FirstOrDefaultAsync(p => p.Id == request.SoftwarePackageId && !p.IsDeleted)
    ?? throw new KeyNotFoundException("Software package not found.");

if (package.PackageType != PackageType.TestApp)
    throw new InvalidOperationException(
        "Only TestApp packages can be assigned to a Station. Use Computer assignment for UtilityFile.");
```

(Thay thế kiểm tra `packageExists` cũ — gộp 2 query.)

### 6.4 Sửa `SoftwarePackageService`

Trong `SoftwarePackageService.cs`:
- `CreateAsync`: accept `PackageType` từ request. Default `TestApp` để backwards-compat.
- `UpdateAsync`: KHÔNG cho đổi `PackageType` (throw nếu request khác với value DB).
- Khi tạo `UtilityFile` package, auto-create 1 `SoftwareVersion` ngầm với `VersionNumber = "1.0"`, `Status = Draft`. (Released sau khi upload file.)

### 6.5 Sửa `InstallationJobService.PollAsync` (server-side job generation)

Logic hiện tại (đã xem ở [InstallationJobService.cs:148](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L148)): với mỗi Computer, lấy danh sách package qua Station, tạo job nếu chưa có record.

Sửa: **union với danh sách package qua Computer assignment.**

```csharp
// Phần Station hiện có giữ nguyên.
// Thêm bên cạnh:
var computerAssignments = await _context.ComputerSoftwareAssignments
    .Where(a => a.ComputerId == computerId && !a.IsDeleted)
    .Select(a => new
    {
        a.SoftwarePackageId,
        a.TargetVersionId,
        PackageType = a.SoftwarePackage.PackageType
    })
    .ToListAsync();

foreach (var ca in computerAssignments)
{
    // Cùng pattern: tìm version effective, check có PcInstallationRecord chưa, không có thì tạo InstallationJob
    var version = await ResolveTargetVersionAsync(ca.SoftwarePackageId, ca.TargetVersionId);
    if (version == null) continue;
    if (await HasInstalledRecordAsync(computerId, version.Id)) continue;

    jobs.Add(new InstallationJob
    {
        ComputerId = computerId,
        SoftwareVersionId = version.Id,
        JobType = InstallationJobType.Install,   // utility chỉ có Install, không Update
        Status = InstallationJobStatus.Pending,
        ScheduledAt = now
    });
}
```

### 6.6 Sửa `InstallationJobService.BuildManifestJobsAsync`

Thêm trường `PackageType` vào `AgentManifestJobResponse` và map. Hiện tại đã map `EntryPointMode` ở [InstallationJobService.cs:521](MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L521) → thêm 1 dòng tương tự.

```csharp
result.Add(new AgentManifestJobResponse
{
    // ... fields hiện có
    EntryPointMode = job.SoftwareVersion.EntryPointMode,
    PackageType = job.SoftwareVersion.SoftwarePackage.PackageType.ToString(),  // mới
});
```

(Eager-load `SoftwarePackage` trong query gốc nếu chưa.)

---

## 7. B3 — API Endpoints

### 7.1 Controller mới — `ComputerSoftwareAssignmentsController`

File mới: `MProjectBackend/MProject.Api/Controllers/Software/ComputerSoftwareAssignmentsController.cs`

```
POST   /api/computer-software-assignments
       body: { computerId, softwarePackageId }
       → 201 { id }

DELETE /api/computer-software-assignments/{id}
       → 204

GET    /api/computer-software-assignments?computerId={id}
GET    /api/computer-software-assignments?packageId={id}
       → 200 [{...}]
```

Auth: cùng policy với `StationSoftwareAssignmentsController`.

### 7.2 Sửa `SoftwarePackagesController`

- Request body `CreatePackageRequest` thêm `PackageType` (default = TestApp).
- Response `SoftwarePackageResponse` thêm `PackageType` để FE biết.
- Endpoint upload file cho version: không đổi.

### 7.3 Sửa `ComputersController` (nếu có endpoint detail)

GET `/api/computers/{id}` response thêm `UtilityAssignments`, hoặc tạo endpoint riêng `/api/computers/{id}/utility-assignments` (recommend riêng để giữ response gọn).

---

## 8. B4 — Agent Changes

### 8.1 Config mới — `UtilityRootOptions`

Sửa [AgentOptions.cs](MProjectAgent/Configuration/AgentOptions.cs) — thêm class:

```csharp
public sealed class UtilityRootOptions
{
    public const string SectionName = "UtilityRoot";

    /// <summary>Đích chính cho utility files.</summary>
    public string PreferredBase { get; set; } = @"D:\Softwares";

    /// <summary>Fallback khi PreferredBase drive không tồn tại.</summary>
    public string FallbackBase { get; set; } = @"C:\MProjectAgent\Softwares";
}
```

`appsettings.json` thêm:
```json
"UtilityRoot": {
  "PreferredBase": "D:\\Softwares",
  "FallbackBase": "C:\\MProjectAgent\\Softwares"
}
```

DI registration trong `Program.cs`:
```csharp
builder.Services.Configure<UtilityRootOptions>(
    builder.Configuration.GetSection(UtilityRootOptions.SectionName));
```

### 8.2 DTO — `AgentManifestJob` thêm `PackageType`

Sửa [AgentApiModels.cs:82](MProjectAgent/Models/AgentApiModels.cs#L82):

```csharp
public sealed class AgentManifestJob
{
    // ... fields hiện có
    public EntryPointMode EntryPointMode { get; set; } = EntryPointMode.LongRunning;
    public PackageType PackageType { get; set; } = PackageType.TestApp;  // ← thêm
    public List<AgentManifestFile> Files { get; set; } = new();
    // ...
}
```

Thêm enum cùng namespace (mirror server):
```csharp
public enum PackageType { TestApp = 0, UtilityFile = 1 }
```

`ServerClient.JsonOptions` đã có `JsonStringEnumConverter` (theo agent_note A.15) → enum round-trip OK.

### 8.3 `JobExecutor` — branching theo `PackageType`

Sửa [JobExecutor.cs](MProjectAgent/Services/JobExecutor.cs):

```csharp
public sealed class JobExecutor
{
    // ... fields hiện có
    private readonly UtilityRootOptions _utilityRoot;   // ← thêm

    public JobExecutor(
        // ... params hiện có
        IOptions<UtilityRootOptions> utilityRoot,
        ILogger<JobExecutor> logger)
    {
        // ... assignments hiện có
        _utilityRoot = utilityRoot.Value;
    }

    public async Task ExecuteAsync(AgentManifestJob jobSummary, CancellationToken ct)
    {
        // ... try block đến DownloadMissingAsync giữ nguyên ...
        var installRoot = await DeployAsync(resolved, ct);

        await _server.CompleteAsync(jobSummary.JobId, new CompleteInstallationJobRequest { ... }, ct);

        // ← thay đổi: chỉ launch nếu là TestApp
        if (resolved.PackageType == PackageType.TestApp)
            await TryLaunchWithSupervisorAsync(installRoot, resolved, ct);
        else
            _logger.LogInformation(
                "Job {JobId} package type {Type}; skipping launch.",
                jobSummary.JobId, resolved.PackageType);
    }

    private async Task<string> DeployAsync(AgentManifestJob job, CancellationToken ct)
    {
        var baseRoot = job.PackageType switch
        {
            PackageType.UtilityFile => ResolveUtilityRoot(),
            _ => _installRoot.Base
        };

        var installRoot = Path.Combine(baseRoot, SanitizePackageName(job.PackageName));
        _logger.LogInformation(
            "Deploying job {JobId} (type={Type}) to {InstallRoot}.",
            job.JobId, job.PackageType, installRoot);
        await _installer.DeployAsync(installRoot, job.Files, job.PackageId, job.VersionId, ct);
        return installRoot;
    }

    private string ResolveUtilityRoot()
    {
        var preferred = _utilityRoot.PreferredBase;
        // Check drive tồn tại — DriveInfo.GetDrives() hoặc Directory.Exists root path
        try
        {
            var rootDir = Path.GetPathRoot(preferred);
            if (!string.IsNullOrEmpty(rootDir) && Directory.Exists(rootDir))
                return preferred;
        }
        catch { /* fall through */ }

        _logger.LogWarning(
            "Preferred utility root {Preferred} unavailable. Falling back to {Fallback}.",
            preferred, _utilityRoot.FallbackBase);

        // Note: không gửi lệnh "set Error status" từ JobExecutor —
        // signal qua AgentWorker để heartbeat tới cập nhật.
        UtilityRootFallback.Signal();
        return _utilityRoot.FallbackBase;
    }
}
```

### 8.4 Signal fallback → Error status

Tạo class `UtilityRootFallback` (singleton, đơn giản):

```csharp
public static class UtilityRootFallback
{
    private static int _flag;
    public static void Signal() => Interlocked.Exchange(ref _flag, 1);
    public static bool IsActive => Volatile.Read(ref _flag) == 1;
    public static void Reset() => Interlocked.Exchange(ref _flag, 0);
}
```

Trong `AgentWorker.BuildRuntimeReport` (đã có theo agent_note A.15): override `OperationalStatus = "Error"` nếu `UtilityRootFallback.IsActive`. Log line chi tiết để admin biết.

Alternative cleaner: inject `IFallbackStateStore` thay vì static — nhưng tăng complexity. Phase 1 dùng static, refactor sau nếu cần.

### 8.5 `InstallDirectoryService` — KHÔNG cần đổi

`DeployAsync` hiện tại tổng quát đủ: tạo folder, tmp+rename, hardlink/copy, ref counting cache. Utility chỉ là "files = [1 file]" — flow vẫn chạy.

`InstallRoot` = `D:\Softwares\<Package>\` → file đi vào `D:\Softwares\<Package>\<filename>.exe`. Đúng yêu cầu.

**Cảnh báo**: hiện `RecordDeployedAsync` track ref-count theo `(packageId, versionId)`. Utility cũng cần. Nhưng vì utility "stop tracking ≠ delete", ref-count sẽ tăng dần và không bao giờ giảm. Không phá gì, chỉ là cache evict logic không "biết" file utility là dead. Acceptable phase 1; refactor cleanup sau.

### 8.6 Idempotency

`InstallDirectoryService.TargetMatchesAsync` (có sẵn ở line 60) đã check hash → re-poll không re-download. OK.

Nhưng `InstallationJobService` không nên tạo job mới nếu file đã match. Hiện logic gating qua `PcInstallationRecord.Installed`. Sau khi job Completed, record được set Installed → re-poll không tạo job. OK.

---

## 9. B5 — Frontend

### 9.1 Form tạo package — chọn `PackageType`

File: [NewSoftwareWizard.tsx](MProjectFrontend/src/pages/Software/NewSoftwareWizard.tsx)

Bước đầu tiên thêm radio:
```
Loại phần mềm:
  ( ) Test app — phần mềm test sản phẩm (deploy + auto-launch)
  ( ) Utility — phần mềm hỗ trợ (download về D:\Softwares, không cài)
```

Nếu chọn Utility:
- Skip bước "Entry point path"
- Skip bước "Entry point mode"
- Sau khi tạo package, mở thẳng modal upload file → auto-create version 1.0 ngầm + Released.

### 9.2 Trang Computer detail — tab "Utility Files"

File: thêm tab/section vào trang detail Computer (`MProjectFrontend/src/pages/Computer/...`). Hiện tại có thể chưa có trang detail riêng — nếu chưa, tạo route `/computers/:id`.

UI:
```
┌─ Computer: PC-LINE-A-01 ─────────────────┐
│ [Overview] [Test App] [Utility Files]    │  ← thêm tab
├──────────────────────────────────────────┤
│ Utility Files                            │
│                              [+ Gán]     │
│ ┌────────────────────────────────────┐   │
│ │ VS Code     v? (1 file, 90 MB)     │   │
│ │ Status: Downloaded 2026-06-04      │   │
│ │ Path: D:\Softwares\VSCode\         │   │
│ │                          [Bỏ gán]  │   │
│ └────────────────────────────────────┘   │
│ ┌────────────────────────────────────┐   │
│ │ Unikey      (1 file, 1.2 MB)       │   │
│ │ Status: Pending — đang tải...      │   │
│ └────────────────────────────────────┘   │
└──────────────────────────────────────────┘
```

Modal "Gán":
- Dropdown chỉ list các package có `PackageType = UtilityFile` và chưa gán cho PC này.
- Submit → POST `/api/computer-software-assignments`.

"Bỏ gán" → confirm modal → DELETE `/api/computer-software-assignments/{id}`. Hiển thị thông báo: "File trên PC sẽ KHÔNG bị xóa. Bạn có thể gán lại sau."

### 9.3 Software packages list — filter theo type

File: [SoftwarePackages.tsx](MProjectFrontend/src/pages/Software/SoftwarePackages.tsx) hoặc [PackageList.tsx](MProjectFrontend/src/pages/Software/components/PackageList.tsx)

Thêm filter chip: `[All] [TestApp] [Utility]`. Cột mới "Type". Badge khác màu.

### 9.4 Dashboard tổng

Phase 1: không bắt buộc. Phase 2 thêm card "Utility files deployed: 42 across 15 PCs".

---

## 10. Edge cases

| Edge case | Cách xử lý |
|---|---|
| Gán utility package nhưng chưa upload file | Service từ chối: "no Released version" (xem 6.2). UI disable nút Gán khi package chưa có file. |
| Gán 2 utility cùng có file `setup.exe` | Không xung đột vì mỗi package có subfolder riêng (`D:\Softwares\VSCode\setup.exe` vs `D:\Softwares\Notepad\setup.exe`). |
| File size lớn (> 1 GB, vd VS Code installer) | BlobCache `MaxSizeBytes` mặc định 50 GB OK. Cẩn thận: nhiều PC tải đồng thời = nghẽn mạng — đã có `MaxDownloadConcurrency` per agent, nhưng nên có server-side rate limit (out of scope, ghi nhận). |
| D: drive được mount sau khi agent boot (vd USB mount lại) | Không tự migrate. Job sau đó sẽ dùng D:. File cũ ở C: ở lại. Acceptable. Có thể thêm CLI `mproject-agent utilities migrate-to-preferred` sau. |
| Path quá dài (> 260 ký tự) trên Windows cũ | Sanitize package name + giới hạn 100 ký tự. Hoặc enable LongPaths (Windows 10+). |
| Tên package có ký tự đặc biệt | `SanitizePackageName` ở [JobExecutor.cs:239](MProjectAgent/Services/JobExecutor.cs#L239) đã handle. |
| Agent download nửa chừng thì mất điện | `InstallDirectoryService` tmp+rename atomic. `BlobCacheService.PutAsync` cũng nên atomic. Resume bằng re-run job (đã có cơ chế per agent_note Vấn đề 2). |
| Re-assign sau khi đã unassign | Record Uninstalled → PollAsync tạo job mới → Agent thấy file đã ở đúng path + hash đúng → `TargetMatchesAsync` skip → mark Completed nhanh. Idempotent. |
| Worker tự copy file ra ngoài | Không vấn đề, file là copy/hardlink — đụng vào không ảnh hưởng cache. Hash check next poll vẫn pass. |
| Worker xóa nhầm file ở `D:\Softwares\` | Next poll: `TargetMatchesAsync` fail → re-deploy từ BlobCache. Self-healing. |
| 2 admin gán cùng package + cùng PC đồng thời | Filtered unique index chặn → 1 request thắng, 1 throw `DbUpdateException` → service convert thành `InvalidOperationException` (already assigned). |
| Package bị soft-delete mà còn assignment | `SoftwarePackageService.DeleteAsync` nên block nếu có active `ComputerSoftwareAssignment` (cùng pattern Station). |
| Computer bị soft-delete mà còn assignment | `ComputerService.DeleteAsync`: soft-delete cascade các assignment kèm (set IsDeleted). |
| UtilityFile có nhiều file (vd zip extract) | Phase 1 giả định 1 file. Nếu admin upload zip, agent vẫn lưu zip nguyên vẹn. Documentation rõ. |
| Agent version cũ không hiểu `PackageType` | Field default `TestApp` ở deserializer → agent cũ sẽ launch nhầm. Mitigation: bump agent min version; backend từ chối heartbeat từ agent < N nếu có UtilityFile job pending. |

---

## 11. Testing strategy

### 11.1 Unit tests (Backend — `MProject.Tests`)

| Test class | Coverage |
|---|---|
| `ComputerSoftwareAssignmentServiceTests` | Assign happy path / package không tồn tại / computer không tồn tại / package type sai / duplicate / unassign + job cancellation + record uninstalled |
| `StationSoftwareAssignmentServiceTests` (extend) | Thêm test: gán UtilityFile lên Station → reject |
| `SoftwarePackageServiceTests` | Create UtilityFile → auto-create version. Update không cho đổi PackageType |
| `InstallationJobServicePollTests` | Computer có 2 station assignment (TestApp) + 3 computer assignment (UtilityFile) → tạo 5 job với type đúng |

### 11.2 Unit tests (Agent — `MProjectAgent.Tests`)

| Test class | Coverage |
|---|---|
| `JobExecutorPackageTypeTests` | Mock manifest UtilityFile → assert `_supervisor.LaunchAndSuperviseAsync` KHÔNG được gọi |
| `UtilityRootResolverTests` | D: tồn tại → preferred; D: không tồn tại → fallback + UtilityRootFallback.IsActive |
| `InstallDirectoryServiceTests` (extend) | Deploy 1 file 90MB vào path mới `D:\Softwares\X\` → hash khớp |

### 11.3 Integration test (cần `WebApplicationFactory` + InMemory DB)

Scenario:
1. Tạo Computer C1.
2. Tạo UtilityFile package "Unikey" + upload file (1 SoftwareFile).
3. POST `/api/computer-software-assignments` { C1, Unikey }.
4. Agent C1 poll → manifest trả 1 job UtilityFile.
5. Agent download + deploy → assert file ở đúng `D:\Softwares\Unikey\unikey.exe` (mock path), hash match.
6. Agent re-poll → 0 job mới.
7. DELETE assignment → assert: PcInstallationRecord Uninstalled, file trên disk còn nguyên.
8. Re-POST assignment → assert job mới, agent thấy file đã có + hash đúng → mark Completed nhanh.

### 11.4 Manual E2E

1. PC staging có D:\Softwares chưa tồn tại (xóa folder nếu có).
2. Gán Unikey → đợi 60s → check `D:\Softwares\Unikey\unikey.exe` xuất hiện.
3. Gán VS Code (~ 90 MB) → đợi → check tải hoàn tất.
4. Xóa thủ công `D:\Softwares\Unikey\` → next poll → file xuất hiện lại.
5. Eject D: drive (test machine có ổ rời) → gán Notepad++ → assert file về `C:\MProjectAgent\Softwares\`, dashboard hiển thị Error.

### 11.5 Regression — TestApp không vỡ

Chạy lại toàn bộ test suite cũ + manual E2E TestApp flow. Test app v.x.y deploy + launch + supervisor recover sau restart agent.

---

## 12. Sequencing & timeline ước lượng

| Tuần | Việc | Owner |
|---|---|---|
| 1 | B1: Enum + entity + migration + DbContext config + build green | BE dev |
| 1 | B1: Test schema không phá data cũ (run migration trên DB clone staging) | BE dev |
| 2 | B2: `ComputerSoftwareAssignmentService` + validation cập nhật ở Station + Package services | BE dev |
| 2 | B2: Unit tests | BE dev |
| 3 | B3: `PollAsync` mở rộng + `BuildManifestJobsAsync` map `PackageType` | BE dev |
| 3 | B3: API endpoints + Swagger | BE dev |
| 3 | B3: Integration test full flow | BE dev |
| 4 | B4: Agent — `UtilityRootOptions`, `JobExecutor` branching, fallback signal | Agent dev |
| 4 | B4: Agent unit tests | Agent dev |
| 5 | B5: FE — form tạo package, trang Computer detail, modal gán | FE dev |
| 5 | B5: FE — filter theo PackageType ở package list | FE dev |
| 6 | B6: Manual E2E trên PC staging, fix bug phát sinh | All |
| 6 | B6: Observability (metric utility_files_downloaded_total, fallback_active_total) | DevOps |

**Ước lượng**: ~6 tuần với 1 BE FT + 1 Agent dev (part-time) + 1 FE FT. Nén được 4 tuần nếu gộp BE + Agent vào 1 người.

---

## 13. Risks & Mitigations

| Risk | Mức | Mitigation |
|---|---|---|
| Agent cũ deploy không hiểu `PackageType` → launch utility file như test app | Cao | Bump agent min-version, backend reject agent < N nếu có job UtilityFile. Auto-update agent là plan tách (out of scope). |
| Migration không lùi được (rollback DB) | Trung | Migration phải có `Down()` đầy đủ: drop bảng + drop cột. Test rollback trên clone staging. |
| File utility quá lớn nghẽn cache | Trung | Document hard-limit per file (vd 2 GB). Throw `InvalidDataException` khi upload vượt. |
| D: fallback active không được admin để ý | Trung | Heartbeat report Error + alert dashboard. Doc runbook "PC ở fallback mode thì xử lý sao". |
| Validation chéo TestApp vs UtilityFile thiếu nhất quán | Cao | Tập trung 1 helper `PackageTypeGuard.RequireType(package, expected)`, gọi từ mọi service entry point. |
| Worker chạy nhầm installer làm hỏng test env | Cao (ngoài kỹ thuật) | Doc nội bộ: utility chỉ kỹ thuật thao tác, có thể ẩn ổ D: với operator account (xem agent_note M5 kiosk). |
| Cache ref-count phình vì không decrement khi unassign | Thấp | Acceptable phase 1. Phase 2 thêm cleanup task per assignment delete. |
| Filtered unique index syntax khác giữa SQL provider (SQL Server vs PostgreSQL) | Trung | Hiện dùng SQL Server (`HasFilter("[IsDeleted] = 0")`). Nếu migrate Postgres sau, đổi sang `.HasFilter("\"IsDeleted\" = false")`. |
| Naming `Softwares` (chữ s) trên ổ D: gây nhầm lẫn | Thấp | Confirmed với user. Doc trong runbook. |

---

## 14. Definition of Done

- [ ] Migration `AddPackageTypeAndComputerSoftwareAssignments` apply sạch trên staging, rollback test pass.
- [ ] `SoftwarePackage.PackageType` enum + auto-default = `TestApp` cho data cũ.
- [ ] `ComputerSoftwareAssignment` entity + service + endpoints + tests pass.
- [ ] `StationSoftwareAssignmentService` reject khi gán UtilityFile.
- [ ] `InstallationJobService.PollAsync` resolve cả Station + Computer assignment, không double-job.
- [ ] `AgentManifestJob.PackageType` round-trip server ↔ agent đúng.
- [ ] Agent deploy UtilityFile về `D:\Softwares\<Package>\<file>`, không gọi `TryLaunchWithSupervisorAsync`.
- [ ] D: fallback hoạt động + heartbeat báo Error.
- [ ] FE: form tạo package có chọn type, trang Computer detail có tab utility, modal gán hoạt động.
- [ ] Test suite tổng pass. Manual E2E 5 scenario ở section 11.4 pass.
- [ ] Doc cập nhật trong `docs/` (this file + runbook ngắn cho oncall).
- [ ] Grafana panel `utility_files_downloaded_total`, `utility_root_fallback_active`.

---

## 15. Mở rộng tương lai (out of scope phase 1)

- **Bulk assign**: chọn nhiều PC cùng lúc gán 1 utility package.
- **Group via tags**: tag Computer "needs-vscode" → gán utility lên toàn bộ PC có tag.
- **Auto-clean stale files**: khi unassign N tháng + disk thiếu chỗ → CLI/UI cho phép xóa.
- **Version pinning cho utility**: nếu sau này cần quản nhiều version VS Code cùng tồn tại.
- **Desktop shortcut optional**: cờ trên package "create shortcut" cho 1 số utility kỹ thuật hay dùng.
- **Allow UtilityFile gán theo Station**: bỏ ràng buộc per-Computer-only nếu thực tế cần.
- **Auto-install** (out of scope theo quyết định ban đầu): nếu sau này muốn agent tự chạy `msiexec /i /quiet`, bổ sung trường `InstallCommand` + `DetectionRule` vào `SoftwareVersion`, không phá schema hiện tại.
