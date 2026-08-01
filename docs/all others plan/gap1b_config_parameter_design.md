# GAP-1B — Config Parameter / Recipe: override theo GIÁ TRỊ thay vì theo FILE

> Kế tục [`gap1_config_override_design.md`](gap1_config_override_design.md) §7 ("Cơ chế (2) — per-key templating, Phase sau").
> Nay làm thật. Tài liệu này **không thay thế** `OverrideFile` (vẫn giữ cho file nhị phân/đục), mà **bổ sung lớp giá trị** phía trên.
> Ngày: 2026-08-01. Trạng thái: **M1 + M2 + M3 đã code xong (xem §11, §12, §13)**, M4–M6 còn ở mức thiết kế.
>
> **Quyết định đã chốt (2026-08-01, user):**
> 1. Đổi BOM/MEBOM → **Config Set / "Recipe"**: 1 package + 1 engine version + N bộ giá trị có tên & có revision. KHÔNG clone package.
> 2. Xung đột khi version mới đổi giá trị gốc của key đã override → **server thắng + cảnh báo** (override bị đánh dấu `Stale`).
> 3. Key được override → **whitelist có kiểu**: khai báo lúc publish, có `DataType` + `MaxScope`.
> 4. Nơi sửa → **chỉ trên web** + **import/export Excel** cho thao tác hàng loạt.
>
> Liên quan: [`gap_configbaseline_design.md`](gap_configbaseline_design.md) (ConfigBaseline — nửa "locked"), [`../uistore_parity_spec_verified.md`](../uistore_parity_spec_verified.md) §F15.

---

## 0. Vì sao phải làm lại — bằng chứng từ chính dữ liệu nhà máy

Đo trên `Sample_Software/EOT_Sample_CSharp_UTPG3T00T01`: **2238 file / 687 MB**; `Config/` 13 file JSON; `Debug/` 33 file (engine C# `CPEI_MFG.exe`); `FTU_.../` 2192 file. File `data/custom_config_files/ctr_04247_efbb.ini`: **~40 section / ~295 key**.

### 0.1 Ba trục thay đổi

| Trục | Đổi gì | Tần suất | Bán kính | Cơ chế hiện có |
|---|---|---|---|---|
| **A. Engine** | `Debug/*` | Hiếm, phải đồng bộ mọi chương trình | Toàn nhà máy | Bulk Engine Update (propagate + bulk release) — ĐÃ CÓ |
| **B. BOM/MEBOM/lô** | `Config/*.json` + `FTU/data/*.ini`, ~10–30 giá trị | Mỗi lô/NPI | 1 chương trình | Clone Package + upload file — **NẶNG** |
| **C. Đặc thù máy** | COM port, IP, tên thiết bị audio, ~5–15 giá trị | Khi thay máy/jig | 1 PC | `OverrideFile` toàn-file — **SAI GRANULARITY** |

### 0.2 Lỗi bản chất: một file vật lý chứa giá trị của NHIỀU scope

`Config/ProgramConfig.json` chứa đồng thời:
- `VersionConfig.BOMVer = "113-04247-11"`, `FtuConfig.FtuDataConfigs[].TargetValue = "000-08323-01"` → **trục B**
- `SfisConfig.Com = "COM8"`, `DUT_IP = "192.168.1.20"` → **trục C**

`ctr_04247_efbb.ini` cũng vậy:
- `[General] top_level_bom / me_bom / sku`, `[Firmware] bom_id / sw_version` → **trục B**
- `[BT_nRF5340_RSSI] test_board_port = COM20`, `[func_power] switch_ip / switch_port_poe`, 20+ dòng `input_device` / `output_device` (tên card âm thanh gắn theo máy) → **trục C**

⇒ **Không tồn tại một cách chia scope nào ở mức FILE mà đúng.** Đây không phải thiếu tính năng, mà là granularity sai.

### 0.3 Hệ quả cụ thể — quality escape thật, không phải giả định

`OverrideFile` gắn `SoftwarePackageId` + `TargetRelativePath`, **không gắn version**
([`OverrideFile.cs:12`](../../MProjectBackend/MProject.Domain/Entities/Software/OverrideFile.cs#L12)),
và `EffectiveBlob` thay **nguyên file**
([`InstallationJobService.cs:712-718`](../../MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs#L712)).

Kịch bản chắc chắn xảy ra:

1. PC-07 cần `SfisConfig.Com = COM3`. Kỹ sư tải `Config/ProgramConfig.json`, sửa 1 dòng, upload làm override scope **Computer**.
2. Lô mới, BOM đổi. Team phát hành version mới với `BOMVer` và `FtuDataConfigs.TargetValue` mới.
3. **PC-07 vẫn nhận file cũ của chính nó** → `BOMVer` cũ + target BOM cũ.
4. Cơ chế cross-check `FtuDataConfigs` (sinh ra chính để chặn sai BOM, enforce ở `FtuService.CheckFtuConfig()`) giờ so với **giá trị cũ** → board sai BOM vẫn PASS, hoặc board đúng BOM fail khó hiểu.
5. **Không ai được cảnh báo**: [`ConfigBaselineValidator.cs:61-65`](../../MProjectBackend/MProject.Application/Services/Software/ConfigBaselineValidator.cs#L61) đọc `SoftwareFiles` của version — tức **file gốc**, bỏ qua hoàn toàn override. Lưới an toàn thủng đúng chỗ nguy hiểm nhất.

Hệ cũ UIStore **đã giải bài này** bằng `IsCheckValue` (F15): merge INI theo key — key trong whitelist giữ giá trị trạm, key còn lại ép về bản server. Hệ mới port được 2/4 semantics (`IsCheckSum=false` → thay nguyên file; `JustExist` → `PreserveLocal`, [`InstallDirectoryService.cs:43-54`](../../MProjectAgent/Services/InstallDirectoryService.cs#L43)). **Phần tinh vi nhất — merge theo key — bị bỏ.**

### 0.4 Các vấn đề phụ

- **Không thấy được sai lệch.** Blob nguyên file → không trả lời được "máy nào lệch chuẩn, lệch chỗ nào".
- **Approval ngược rủi ro.** [`OverrideFileService.cs:166-168`](../../MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs#L166): scope Model → **Active ngay** (ảnh hưởng mọi trạm của model); scope Computer → Draft, phải duyệt (ảnh hưởng 1 máy).
- **Sửa 1 giá trị = nhận nợ 295 key**, không có đường quay về "theo chuẩn" ngoài xoá override.
- **Trục B quá nặng**: clone 2238 file để đổi ~10–30 giá trị.

---

## 1. Đối chiếu mô hình thực tế

| Hệ | Cơ chế | Ánh xạ |
|---|---|---|
| **Ansible** | `group_vars/all` → `group_vars/<group>` → `host_vars/<host>` | Trùng khít Model → Station → Computer |
| **Kustomize (K8s)** | base + strategic-merge patch, merge theo key | Base tiến hoá độc lập với overlay |
| **Puppet Hiera** | hierarchy lookup, khai báo tường minh | Sinh ra đúng để giải "cùng file, giá trị khác theo node" |
| **.NET `IConfiguration`** | `appsettings.json` + `appsettings.{Env}.json` | Idiom team đã quen |
| **NI TestStand** | `StationGlobals.ini` nằm NGOÀI sequence file, deploy không ghi đè | Câu trả lời bản địa ngành test cho trục C — nhưng free-form nên drift vô hình ⇒ phải để trong DB |
| **MES recipe management** | tách "process program" khỏi "recipe/parameter set", version riêng, gắn theo (sản phẩm, công đoạn, thiết bị) | **Chính là lựa chọn Config Set đã chốt** |

**Điểm chung tuyệt đối:** không bao giờ lưu bản render đầy đủ của file cho từng target. Lưu **file gốc** (đi theo version) + **delta thưa theo key** + **bộ render tất định**.

---

## 2. Kiến trúc mục tiêu

### 2.1 Chuỗi phân lớp

```
  file gốc trong blob của SoftwareVersion          ← team lập trình sở hữu (trục A)
      ↓ apply
  ConfigValueSet ("Recipe") gắn ở assignment       ← NPI/PE sở hữu   (trục B)
      ↓ apply
  ConfigValueOverride scope = Station              ← kỹ sư trạm/jig  (trục C)
      ↓ apply
  ConfigValueOverride scope = Computer             ← kỹ sư máy       (trục C)
      ↓ render (format-preserving) + SHA-256
  derived blob → đưa vào manifest
```

### 2.2 Insight then chốt: **agent không đổi một dòng nào**

Agent nhận `{ Path, Sha256, DownloadUrl }` rồi ghi `installRoot/Path` từ blob. Render ở server, content-addressed ⇒ agent chỉ thấy một SHA khác. Toàn bộ `InstallDirectoryService`, verify SHA sau deploy, `PcInstallationRecord`, cache blob, delta download **chạy nguyên không sửa**. Đây là lý do phương án này rẻ hơn nó trông.

Dedupe tự nhiên: mọi máy có cùng bộ giá trị → cùng rendered SHA → 1 blob duy nhất, tải 1 lần. 2000 trạm × file ini 10 KB, kể cả mỗi máy một COM port khác nhau, tổng cũng chỉ ~20 MB.

---

## 3. Mô hình dữ liệu

### 3.1 `ConfigParameter` — khai báo CÁI GÌ được sửa (whitelist có kiểu)

Thay vai trò của `SoftwareVersion.OverridablePaths: List<string>` cho file config. Gắn **package** để sống qua các version (giống `OverrideFile`).

```csharp
public class ConfigParameter : VersionedEntity, ISoftDeletable
{
    public Guid SoftwarePackageId { get; set; }
    public virtual SoftwarePackage SoftwarePackage { get; set; } = null!;

    public string TargetRelativePath { get; set; } = null!;   // "Config/ProgramConfig.json"
    public ConfigFileFormat Format { get; set; }              // Ini | Json  (enum ĐÃ CÓ)
    public string Sector { get; set; } = null!;               // "" = root với JSON
    public string Key { get; set; } = null!;

    public string? DisplayName { get; set; }                  // "Cổng COM máy SFIS"
    public string? Description { get; set; }
    public string? GroupName { get; set; }                    // gom nhóm trên UI
    public ConfigParameterType DataType { get; set; }         // String|Int|Decimal|Bool|Enum|IpAddress|ComPort
    public List<string> AllowedValues { get; set; } = new();  // JSONB, dùng cho Enum
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>Scope CỤ THỂ NHẤT được phép đặt giá trị. Model = chỉ Recipe được đổi.</summary>
    public OverrideScope MaxScope { get; set; } = OverrideScope.Model;

    public bool RequiresApproval { get; set; }
    public int SortOrder { get; set; }
    // + IsDeleted/DeletedAt/DeletedBy
}

public enum ConfigParameterType { String = 0, Int = 1, Decimal = 2, Bool = 3, Enum = 4, IpAddress = 5, ComPort = 6 }
```

**Unique index:** `(SoftwarePackageId, TargetRelativePath, Sector, Key) WHERE !IsDeleted`.

`MaxScope` là hàng rào cứng chặn kịch bản §0.3: `top_level_bom` khai `MaxScope = Model` ⇒ **không thể** tạo override cấp máy cho nó, dù có quyền gì đi nữa.

### 3.2 `ConfigValueSet` + `ConfigValueSetItem` — "Recipe" (trục B)

```csharp
public class ConfigValueSet : VersionedEntity, ISoftDeletable
{
    public Guid ResourceId { get; set; }                       // ACL, parent = Resource của Model
    public virtual Resource Resource { get; set; } = null!;

    public Guid SoftwarePackageId { get; set; }
    public string Name { get; set; } = null!;                  // "BOM10 — lô 08/2026"
    public int Revision { get; set; }                          // tự tăng theo (package, Name)
    public ConfigValueSetStatus Status { get; set; }           // Draft|PendingApproval|Active|Superseded

    public string? BomVersion { get; set; }                    // metadata truy vết
    public string? MeBomVersion { get; set; }
    public string? Note { get; set; }

    public Guid CreatedBy { get; set; }
    public virtual ICollection<ConfigValueSetItem> Items { get; set; } = new List<ConfigValueSetItem>();
    // + IsDeleted/DeletedAt/DeletedBy
}

public class ConfigValueSetItem : VersionedEntity
{
    public Guid ConfigValueSetId { get; set; }
    public Guid ConfigParameterId { get; set; }
    public string Value { get; set; } = null!;
}
```

**Gắn recipe vào đâu:** thêm `Guid? ConfigValueSetId` vào `StationSoftwareAssignment` (nơi đã quyết định "trạm này chạy package X version Y"). Ăn khớp sẵn với Multi-Active Assignment: một trạm có thể chạy cùng package với 2 recipe khác nhau nếu cần.

Đổi BOM = tạo revision mới của recipe → duyệt → đổi `ConfigValueSetId` ở assignment. **Không clone 687 MB, không upload file.**

### 3.3 `ConfigValueOverride` — delta thưa cấp Station/Computer (trục C)

```csharp
public class ConfigValueOverride : VersionedEntity, ISoftDeletable
{
    public Guid ResourceId { get; set; }                       // ACL, parent = Resource của station/computer
    public virtual Resource Resource { get; set; } = null!;

    public Guid ConfigParameterId { get; set; }
    public virtual ConfigParameter Parameter { get; set; } = null!;

    public OverrideScope Scope { get; set; }                   // Station | Computer (Model dùng Recipe)
    public Guid? StationId { get; set; }
    public Guid? ComputerId { get; set; }

    public string Value { get; set; } = null!;

    /// <summary>Giá trị hiệu lực của LỚP DƯỚI tại lúc tạo — dùng phát hiện Stale (§4).</summary>
    public string? BaseValueAtCreation { get; set; }

    public ConfigOverrideStatus Status { get; set; }           // Draft|PendingApproval|Active|Stale
    public Guid CreatedBy { get; set; }
    // + IsDeleted/DeletedAt/DeletedBy
}
```

**Unique index:** `(ConfigParameterId, Scope, StationId, ComputerId) WHERE !IsDeleted`.

### 3.4 `RenderedConfigBlob` — cache render

```csharp
public class RenderedConfigBlob            // PK: (BaseSha256, ValueSetHash)
{
    public string BaseSha256 { get; set; } = null!;   // blob file gốc (hoặc blob OverrideFile nếu có)
    public string ValueSetHash { get; set; } = null!; // SHA-256 của danh sách canonical "sector|key|value" đã sort
    public string RenderedSha256 { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}
```

⚠ **Blob GC:** blob render là một nguồn tham chiếu MỚI. Phải thêm guard ở **cả 3 vị trí** trong
[`BlobGcService.cs`](../../MProjectBackend/MProject.Application/Services/Software/BlobGcService.cs) — dòng **86-87**, **209-210**, **225-226**. Quên chỗ nào là blob đang deploy bị xoá.

---

## 4. Chính sách xung đột: server thắng + cảnh báo

Khi tạo `ConfigValueOverride`, snapshot `BaseValueAtCreation` = giá trị hiệu lực của lớp ngay dưới (file gốc hoặc recipe).

Lúc dựng manifest, với mỗi override Active:

```
currentBase = giá trị hiệu lực của lớp dưới HIỆN TẠI
if (currentBase == BaseValueAtCreation)  → override áp bình thường
else                                     → BASE THẮNG, đánh dấu Status = Stale, thông báo người tạo
```

Kỹ sư mở lại override thấy: `giá trị lúc tạo → giá trị mới của server → giá trị bạn đặt`, rồi chọn **xác nhận lại** (cập nhật snapshot, override sống lại) hoặc **xoá**.

### 4.1 Chốt an toàn bắt buộc: preview tác động

Rủi ro thật của chính sách này: phát hành version mới đổi giá trị gốc của một key cấp máy (ví dụ `test_board_port`) sẽ làm **hàng loạt override hết hiệu lực cùng lúc** → cả loạt máy âm thầm quay về giá trị mặc định → dừng chuyền.

⇒ Trước khi **release version** hoặc **activate recipe**, server tính trước và hiện: *"Thao tác này sẽ làm N override ở M máy/trạm hết hiệu lực"* kèm danh sách, bắt xác nhận. Đây là điều kiện tiên quyết để chính sách "server thắng" an toàn ngoài xưởng.

*(Đường thoát nếu về sau thấy vướng: thêm `ConflictPolicy` per-parameter — BOM = server thắng, COM port = máy thắng. Chưa làm bây giờ.)*

---

## 5. Bộ render

### 5.1 `ConfigFileWriter` — anh em của `ConfigFileReader` đã có

[`ConfigFileReader.cs`](../../MProjectBackend/MProject.Application/Services/Software/ConfigFileReader.cs) đã biết **đọc** INI + JSON theo `(sector, key)`. Cần đúng một service đối xứng để **ghi**:

```csharp
string SetValue(string content, ConfigFileFormat format, string sector, string key, string value);
```

**Yêu cầu bắt buộc — format-preserving:**
- **INI:** quét dòng, tìm section, tìm key, thay **chỉ phần sau dấu `=`**; giữ nguyên comment, thứ tự, dòng trống, và style khoảng trắng gốc (`key = value` vs `key=value`). File `ctr_04247_efbb.ini` có comment và thứ tự section mang ý nghĩa với kỹ sư — không được rewrite lại cả file.
- **JSON:** `JsonNode` parse, set property, giữ **kiểu gốc** (base là number thì ghi number, không ghi string), serialize `WriteIndented = true`.
- **Không tìm thấy section/key → ném lỗi**, không tự thêm. Key mất nghĩa là schema đã lệch so với file — phải báo, không được im lặng.

### 5.2 Điểm chèn: `InstallationJobService.EffectiveBlob`

```
hiện tại:  blob gốc  →  (blob OverrideFile nếu path ∈ OverridablePaths)
sau khi làm: blob gốc → (blob OverrideFile) → apply value overrides → render → derived blob
```

Chỉ render khi file đó có ≥1 giá trị khác base. Đa số file: 0 → không đụng gì.

### 5.3 Hiệu năng ở 2000 trạm

Manifest dựng ở `GetActiveJobsAsync` (L79), `PollAsync` (L294), `ResolveManifestAsync` (L343) — poll thường xuyên. Nhưng:
- Cache key `(BaseSha256, ValueSetHash)` **ổn định**; steady state = 1 lookup dictionary/file, không render lại.
- Resolve giá trị per-computer: `OverrideResolver` hiện 3 query, thêm ~2 query nữa cho recipe + value override. Chấp nhận được, nhưng **phải batch theo computer, tuyệt đối không N+1 theo file**.

⚠ **BẪY đã dính trước đây** (memory `feedback_npgsql_utc_datetimeoffset`, `project_test_log_collection_plan`): test InMemory KHÔNG bắt được lỗi dịch sang SQL của Npgsql. Query resolve mới phải chạy thử trên Postgres thật.

---

## 6. Vá lưới an toàn: ConfigBaseline phải kiểm file HIỆU LỰC

`ConfigBaselineValidator.ValidateVersionForModelAsync` hiện đọc `SoftwareFiles` của version (file gốc). Phải đổi sang kiểm **nội dung sau render** cho từng target. Hai lớp phòng thủ độc lập:

1. `ConfigParameter.MaxScope` — chặn từ đầu, không cho tạo override sai cấp.
2. `ConfigBaseline` trên file hiệu lực — bắt được cả trường hợp recipe đặt sai giá trị.

---

## 7. Frontend

1. **Package → tab "Tham số"** *(vai: người đóng gói)*
   Chọn file từ cây file của version → server parse trả về cây section/key → tick key nào mở, đặt `DisplayName`/`DataType`/`Min-Max`/`MaxScope`. Chính là `FileSettingForm` của Upload cũ, nhưng có kiểu dữ liệu.

2. **Package → tab "Bộ giá trị (Recipe)"** *(vai: NPI/PE)*
   Danh sách `ConfigValueSet` kèm revision/status/BOM. Editor = form sinh từ các `ConfigParameter` có `MaxScope = Model`. Duyệt → activate (kèm preview tác động §4.1) → gán vào assignment của trạm.

3. **Station / Computer → tab "Cấu hình"** *(vai: kỹ sư trạm)*
   Bảng: `Tham số | File gốc | Recipe | Trạm | Máy này | **Hiệu lực** | Nguồn`.
   Sửa inline đúng cấp mình có quyền; ô `Stale` tô đỏ. Đây là view kiểu `ansible --diff` / `hiera explain` — giá trị vận hành cao nhất, vì lần đầu tiên trả lời được "máy này đang chạy giá trị gì và vì sao".

4. **Fleet view**: "máy lệch chuẩn" + "override hết hiệu lực (Stale)".

5. **Import/Export Excel**
   - Export: hàng = parameter, cột = trạm/máy, ô = giá trị.
   - Import: **parse ở FE** (SheetJS/`xlsx`) → gửi JSON lên endpoint bulk → server validate theo `DataType`/`Min-Max`/`MaxScope` → **hiện diff preview** → apply theo lô, 1 approval cho cả lô.
   - ⇒ **không thêm package NuGet nào ở backend** (đúng `coding_rule.md`: cấm thêm package khi stack sẵn có đủ). Chỉ thêm 1 dependency FE.

---

## 8. Di trú từ `OverrideFile` hiện tại

- `OverrideFile` **giữ nguyên**, dùng cho file nhị phân/đục (`.dat`, ảnh firmware, file limit tải từ SFTP). Hai cơ chế chồng nhau đúng thứ tự §2.1 (file override làm base cho value override).
- Thêm helper **"chuyển thành tham số"**: với mỗi `OverrideFile` đang trỏ vào `.ini`/`.json`, server diff blob override vs file gốc → đề xuất danh sách `(sector, key, value)` → kỹ sư review → tạo `ConfigParameter` + `ConfigValueOverride`, xoá override file.
- Sửa luôn approval ngược rủi ro (§0.4): recipe cấp Model **phải duyệt**; override cấp máy theo `ConfigParameter.RequiresApproval`.

---

## 9. Lộ trình

| # | Milestone | Nội dung | Ship độc lập? |
|---|---|---|---|
| **M1** ✅ | Render engine | `ConfigFileWriter` + `RenderedConfigBlob` + guard BlobGc 3 chỗ + mở rộng `OverrideResolver`/`EffectiveBlob`. Entity `ConfigParameter`/`ConfigValueOverride` + migration, **chưa có UI** | **XONG 2026-08-01** — xem §11 |
| **M2** ✅ | Khai báo schema | API parse file → cây section/key; UI tab "Tham số" | **XONG 2026-08-01** — xem §12 |
| **M3** ✅ | Recipe | `ConfigValueSet` + gắn vào `StationSoftwareAssignment` + **preview tác động** (§4.1, một nửa) | **XONG 2026-08-01** — xem §13 |
| **M4** | Override cấp trạm/máy | UI tab "Cấu hình", phát hiện `Stale` + thông báo, fleet view lệch chuẩn | ✅ gỡ được trục C |
| **M5** | Excel | Export + import (parse FE) + diff preview + bulk approval | ✅ |
| **M6** | Dọn dẹp | `ConfigBaseline` kiểm file hiệu lực (§6) + helper di trú `OverrideFile` (§8) | ✅ |

**Ngân sách phức tạp:** vượt xa mức ≤5 file / ≤150 LOC của `coding_rule.md` — đã dừng và báo đúng theo rule. Đây là thiết kế lại một module, không phải minimal change. Bù lại, mỗi milestone ở trên tự nó là một thay đổi nhỏ và ship được riêng.

---

## 10. Rủi ro

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Loạt override hết hiệu lực cùng lúc → dừng chuyền | **CAO** | Preview tác động bắt buộc trước release/activate (§4.1) |
| Query resolve mới không dịch được sang Npgsql | CAO | Test trên Postgres thật, KHÔNG tin test InMemory |
| Quên guard BlobGc → xoá blob đang deploy | CAO | Sửa đủ **3** vị trí, có test |
| Render không format-preserving → kỹ sư mất comment, khó diff | TRUNG BÌNH | Ghi theo dòng cho INI; test giữ nguyên byte các dòng không đụng |
| Công khai báo schema lần đầu cho mỗi package | TRUNG BÌNH | Chỉ khai báo key thực sự cần (~15–30/295), không phải toàn bộ |
| Schema lệch khi version mới đổi cấu trúc file | TRUNG BÌNH | Render ném lỗi khi không tìm thấy key; chặn release + báo rõ key nào |

---

## 11. M1 — đã triển khai (2026-08-01)

**Trạng thái:** code xong, 674/674 test xanh, migration đã áp DB dev. **CHƯA commit, CHƯA restart backend, CHƯA E2E trạm thật.**

### 11.1 File mới

| File | Vai trò |
|---|---|
| `Domain/Enums/ConfigParameterType.cs` | String/Int/Decimal/Bool/Enum/IpAddress/ComPort |
| `Domain/Enums/ConfigValueStatus.cs` | Draft/PendingApproval/Active/**Stale** |
| `Domain/Entities/Software/ConfigParameter.cs` | Khai báo key được override + `MaxScope` |
| `Domain/Entities/Software/ConfigValueOverride.cs` | Delta thưa cấp Station/Computer + `BaseValueAtCreation` |
| `Domain/Entities/Software/RenderedConfigBlob.cs` | Cache render, PK `(BaseSha256, ValueSetHash)` |
| `Application/Interface/Software/IConfigFileWriter.cs` + `ConfigKeyNotFoundException.cs` | Hợp đồng ghi |
| `Application/Services/Software/ConfigFileWriter.cs` | Ghi INI/JSON **format-preserving** |
| `Application/Interface/Software/IConfigRenderService.cs` | Hợp đồng render |
| `Application/Services/Software/ConfigRenderService.cs` | Render + cache + ref-count |

### 11.2 File sửa

- `OverrideResolver` — thêm `ResolveValuesAsync`; memoize scope chain vì manifest gọi 2 lượt liên tiếp.
- `InstallationJobService` — `ResolveOverridesAsync` đổi thành bộ **tiền tính** trả `(VersionId, Path) → blob hiệu lực`, gom hết I/O về một chỗ; `EffectiveBlob` thành lookup thuần. Nhận thêm `IConfigRenderService`.
- `BlobGcService` — guard `RenderedConfigBlobs` ở **cả 3** vị trí + `PurgeUnusedRenderedConfigsAsync`.
- `DBContext` / `IAppDbContext` / `Program.cs` — DbSet, cấu hình, DI.
- Migration `20260801063202_AddConfigParameterAndValueOverride`.

### 11.3 Quyết định phát sinh khi code

1. **Agent không sửa gì** — đúng như thiết kế. Render ở server, agent chỉ thấy SHA khác.
2. **Value override KHÔNG bị chặn bởi `OverridablePaths`.** Tạo `ConfigParameter` chính là tác giả cho phép sửa key đó; `OverridablePaths` là cổng thô hơn, chỉ quản việc thay NGUYÊN file. Bắt khai báo cả hai là thừa.
3. **Render lười + cache, không materialize sẵn.** Trạng thái ổn định = 1 lookup, không ghi. Chỉ ghi lần đầu gặp tổ hợp `(baseSha, valueSet)`. `LastUsedAt` chỉ cập nhật tối đa 1 lần/ngày/entry để không khuếch đại ghi ở 2000 trạm.
4. **`ConfigRenderService` dùng scope DI riêng.** Render chạy giữa lúc agent poll — context lúc đó đang giữ đầy job được track; `SaveChanges` chung sẽ flush nhầm việc không liên quan.
5. **Lỗi render KHÔNG làm hỏng poll.** Key không tìm thấy / blob đọc lỗi → log Error, bỏ qua giá trị đó, deploy file gốc. Agent không bao giờ kẹt vì một key sai. (Đánh dấu `Stale` + thông báo là việc của M4.)
6. **Staleness kiểm theo từng lớp.** Áp theo thứ tự `(sector, key, scope)` nên với cùng một key, giá trị Station vào trước; `BaseValueAtCreation` của override Computer được so với giá trị Station — đúng ngữ nghĩa phân lớp.
7. **`manifestHashes` bỏ qua job Uninstall** cho khớp `BuildManifestJobsAsync` (job uninstall không mang file nào).
8. **Model scope bị chặn ở DB** (`CK_ConfigValueOverrides_ScopeMatchesId` chỉ nhận Scope 1/2) — giá trị cấp model thuộc Recipe, mỗi lớp đúng một chủ sở hữu.

### 11.4 Đã verify

- `dotnet test`: **674/674 xanh** (thêm 30 test mới).
- `ConfigFileWriterTests` (16): INI giữ nguyên byte trừ đúng 1 dòng, giữ comment/CRLF/thụt lề/style `=`, không nhầm key trùng tên khác section, không khớp dòng comment, dòng cuối không newline; JSON giữ kiểu number/bool, giữ `\` trong path, thiếu key/section thì ném lỗi.
- `ConfigRenderServiceTests` (12): cache hit không upload lại, 2 máy cùng giá trị dùng chung 1 blob, Station→Computer đúng thứ tự, base đổi thì override bị bỏ (server thắng), override Computer stale không chặn Station, giá trị trùng base không sinh blob, key lạ bị bỏ qua mà phần còn lại vẫn render, blob render có ref-count.
- `InstallationJobServiceTests` (+5): manifest lấy blob render; render đặt trên blob của **file override** chứ không phải file gốc; không cần `OverridablePaths`; `MaxScope=Model` chặn override cấp máy; status != Active thì bỏ qua.
- `BlobGcServiceTests` (+2): blob đang được cache render tham chiếu KHÔNG bị xoá; row cache quá hạn bị dọn rồi blob mới được thu hồi.
- **Postgres thật:** `ResolveValuesAsync` dịch và chạy được trên `TESSDB` (test tạm, đã xoá). Migration đã `database update` thành công.
- `MProject.Api` compile sạch (`-t:Compile`; bước copy DLL bị process backend đang chạy khoá — không phải lỗi code).

### 11.5 Còn nợ

- Chưa có API/UI nên **chưa tạo được `ConfigParameter`/`ConfigValueOverride` ngoài test** → M2/M4.
- Chưa đánh dấu `Stale` vào DB và chưa thông báo (mới chỉ log warning) → M4.
- Chưa có preview tác động §4.1 → M3.
- `ConfigBaselineValidator` vẫn kiểm file gốc → M6.

---

## 12. M2 — đã triển khai (2026-08-01)

**Trạng thái:** code xong, 689/689 test xanh, FE build + typecheck + lint sạch. **CHƯA commit, CHƯA restart backend, CHƯA verify trên browser** (backend đang chạy bản cũ nên endpoint mới chưa sống).

### 12.1 Backend

| File | Vai trò |
|---|---|
| `IConfigFileReader.ReadAll` + `ConfigFileEntry` | **Mở rộng interface có sẵn** thay vì thêm service mới — liệt kê mọi `(sector, key, value)` của file |
| `ConfigFileReader` | Cài `ReadAllIni` (bỏ dòng comment) + `ReadAllJson` (root + 1 cấp lồng) |
| `Models/ConfigParameterModels.cs` | `ConfigParameterDto`, `ConfigFileKeyDto`, `ConfigFileKeysResponse`, `ConfigParameterInput`, `SetConfigParametersRequest` |
| `IConfigParameterService` + `ConfigParameterService` | Đọc file từ blob → liệt kê key; lưu theo lô cho từng file |
| `ConfigParametersController` | `GET /config-parameters?packageId=`, `GET /config-parameters/file-keys?versionId=&path=`, `PUT /config-parameters/file` |

**Quyền: KHÔNG thêm permission mới.** Dùng lại `software.package.manage` (ghi) và `software.read` (đọc) — khai báo tham số là hành vi đóng gói y như `SetOverridablePaths`. Tránh phải rebuild + restart để re-seed permission.

### 12.2 Quyết định phát sinh khi code

1. **Format suy từ đuôi file ở server**, không nhận từ client. Để client tự khai format thì một file JSON có thể bị parse kiểu INI và sinh ra key vô nghĩa rồi render thành rác.
2. **Không xoá được tham số đang có giá trị trạm/máy.** `SetForFileAsync` chặn với thông báo liệt kê rõ key nào và bao nhiêu giá trị. Nếu cho xoá, các giá trị đó bị mồ côi và máy âm thầm quay về mặc định đóng gói — đúng loại lỗi mà cả thiết kế này sinh ra để chặn.
3. **Bỏ tick rồi tick lại thì hồi sinh đúng row cũ** (`IgnoreQueryFilters` khi đọc `existing`) — giữ nguyên `Id` nên `ConfigValueOverride.ConfigParameterId` không đứt, và index unique một phần không bao giờ thấy trùng.
4. **`OrphanedParameters`**: tham số đã khai nhưng không còn trong file của version đang xem → hiện cảnh báo vàng. Đây là cảnh báo sớm cho lỗi render "key not found" ở §11.3 mục 5.
5. **JSON chỉ lấy scalar ở root + 1 cấp lồng.** Mảng (`StartCommands`) và object lồng sâu không địa chỉ hoá được bằng `(sector, key)` nên không hiện ra — thà không cho chọn còn hơn cho chọn rồi render lỗi.

### 12.3 Frontend

- `types/configParameters.ts`, `api/configParameters.ts`.
- `components/files/ConfigParametersModal.tsx` — chọn file cấu hình → bảng mọi key kèm **giá trị thật ở version này** → tick key nào mở + đặt Tên hiển thị / Kiểu / **Được đổi tới cấp**.
- Mở từ menu version trong `VersionPanel` (`FiKey`), cạnh "Đường dẫn cho phép override" — cùng họ chức năng.
- i18n đủ 3 ngôn ngữ (`en`/`vi`/`cn`), khối `software.parameters.*`.
- Checkbox bị khoá khi tham số còn giá trị trạm/máy (server cũng chặn — 2 lớp).
- Read-only khi không có quyền, đúng pattern `OverridablePathsModal`.

### 12.4 Đã verify

- `dotnet test`: **689/689 xanh** (thêm 15 test `ConfigParameterServiceTests`): liệt kê key INI/JSON đúng, bỏ dòng comment, bỏ mảng, đánh dấu key đã khai, báo orphan, chặn file không parse được, suy format từ đuôi, xoá key khỏi danh sách, hồi sinh đúng row, chặn xoá khi còn override, đếm `overrideCount`, chặn Min/Max trên kiểu không phải số, chặn Enum rỗng, chặn key trùng, chặn thiếu quyền, ghi audit.
- **Postgres thật:** `LoadByPackageAsync` (có subquery đếm tương quan + cột `text[]`) và query `IgnoreQueryFilters` đều dịch chạy được (test tạm, đã xoá).
- FE: `tsc --noEmit` sạch, `eslint` sạch, `npm run build` thành công.

### 12.5 Còn nợ

- **Chưa verify trên browser** — cần restart backend để endpoint mới sống.
- `RequiresApproval`, `Description`, `GroupName`, `AllowedValues`, `Min`/`Max` đã có ở API + DB nhưng UI chưa phơi ra (mới có Tên hiển thị / Kiểu / Cấp). Bổ sung khi M4 cần tới.

---

## 13. M3 — đã triển khai (2026-08-01)

**Trạng thái:** code xong, 705/705 test backend xanh, FE build/typecheck/lint sạch, migration đã áp DB dev. **CHƯA commit, CHƯA restart backend, CHƯA verify browser.**

### 13.1 Backend

| File | Vai trò |
|---|---|
| `Domain/Enums/ConfigValueSetStatus.cs` | Draft / Active / Superseded — cùng ngữ pháp với `SoftwareVersion` |
| `Domain/Entities/Software/ConfigValueSet.cs` + `ConfigValueSetItem.cs` | Recipe có `Name` + `Revision`, item trỏ `ConfigParameterId` |
| `StationSoftwareAssignment.ConfigValueSetId` | Điểm gắn recipe vào trạm |
| `Models/ConfigValueSetModels.cs` | DTO + `StaleImpactDto` |
| `IConfigValueSetService` + `ConfigValueSetService` | CRUD + activate + **preview tác động** |
| `ConfigValueSetsController` | `GET/POST/PUT/DELETE /config-value-sets`, `/activate`, `/impact` |
| `StationSoftwareAssignmentService.SetValueSetAsync` + `PUT /software-assignments/{id}/value-set` | Gắn/gỡ recipe |
| `OverrideResolver.LoadRecipeValuesAsync` | **Chèn lớp Model vào resolver** |
| Migration `20260801072544_AddConfigValueSet` | |

### 13.2 Quyết định phát sinh khi code — có LỆCH so với §7.2

1. **Activate KHÔNG cần approval type mới** (§7.2 ghi "duyệt → activate"). Lý do: một revision vừa activate **chưa trạm nào trỏ tới**, nên tự nó không đổi gì ngoài xưởng. Hành vi nguy hiểm là **gắn recipe vào assignment** — mà việc đó đã đi qua approval của assignment sẵn có (`SoftwareAssignmentApprovalHandler`). Thêm `ApprovalTargetType.ConfigValueSet` sẽ là tầng duyệt thứ hai cho cùng một rủi ro.
2. **Recipe bất biến khi đã Active** — sửa = tạo revision mới. Đúng ngữ pháp `SoftwareVersion` Draft→Released mà team đã quen, và giữ được "trạm đó đã chạy giá trị gì" truy ngược được.
3. **Recipe KHÔNG có `BaseValueAtCreation`** — nó là chủ sở hữu lớp của mình nên luôn áp; chính các lớp Station/Computer bên trên mới hết hiệu lực khi recipe đổi.
4. **`ConfigValueSet` không có `Resource`/ACL riêng** (khác `ConfigBaseline`/`OverrideFile`). `SoftwarePackage` vốn không có `ResourceId`, và quyền đã là `software.package.manage` như `ConfigParameter` — thêm ACL sẽ là một mặt phẳng phân quyền thừa.
5. **Multi-active assignment**: một package có thể xuất hiện 2 assignment active. Resolver chọn recipe theo `ActivatedAt` giảm dần rồi `Id` — **quyết định tất định**, không để thứ tự dòng DB định đoạt.
6. **Activate chặn recipe rỗng**, **xoá chặn khi còn trạm trỏ tới**.

### 13.3 Preview tác động (§4.1) — làm được một nửa, có chủ đích

**Đã làm:** `PreviewImpactAsync(assignmentId, valueSetId)` — "nếu gắn recipe này cho trạm này thì giá trị trạm/máy nào hết hiệu lực". Tính **chính xác, không cần đọc file**: với key mà recipe đặt, giá trị file gốc không còn liên quan. Có **cascade 2 lớp**: nếu override cấp Trạm hết hiệu lực thì đáy của override cấp Máy đổi từ giá trị trạm sang giá trị recipe, và cái đó cũng được kiểm.

**CHƯA làm:** preview khi **release version mới** (base file đổi). Việc đó cần đọc nội dung file hiệu lực cho từng target — chính là bộ máy M6 mang lại (`ConfigBaselineValidator` kiểm file hiệu lực). Hoãn sang M6, không phải bỏ.

### 13.4 Frontend

- `types/configValueSets.ts`, `api/configValueSets.ts`.
- `ConfigValueSetsModal` — 2 cột: danh sách revision (tên/rev/trạng thái/số trạm) | bảng tham số + ô nhập giá trị, kèm BOM/MEBOM/ghi chú. Tạo revision mới có **chép giá trị từ revision cũ**. Draft: Sửa / Xoá / Kích hoạt. Active: chỉ đọc. Mở từ nút "Bộ giá trị (Recipe)" ở header panel version.
- `AssignRecipeModal` — chọn recipe cho 1 assignment; **đổi lựa chọn là tự gọi preview**, hiện bảng đỏ "giá trị bị mất → rơi về". Mở từ icon bình thí nghiệm trên mỗi dòng trạm trong DeployedDrawer (icon sáng màu brand khi trạm đang có recipe).
- i18n đủ 3 ngôn ngữ: `software.recipes.*`, `software.assign_recipe.*`.

### 13.5 Đã verify

- `dotnet test`: **705/705 xanh** (+16): `ConfigValueSetServiceTests` (13) và 3 test resolver mới.
- Recipe trong resolver: áp đúng lớp Model, **không mang base snapshot**, thứ tự Model → Station → Computer cho cùng một key, bỏ qua recipe của assignment đã tắt.
- Vòng đời: revision tự tăng theo tên, chép từ revision cũ, chặn sửa bản Active, chặn tham số của package khác, activate làm bản cũ thành Superseded, chặn recipe rỗng, chặn xoá khi trạm còn dùng.
- Preview: báo đúng khi base đổi, im lặng khi còn khớp, **cascade** khi override trạm rớt kéo theo override máy, bỏ qua tham số recipe không đặt.
- **Postgres thật:** `ResolveValuesAsync` có lớp recipe, query list/impact của `ConfigValueSetService`, và projection assignment có nhãn recipe — đều dịch chạy được (test tạm, đã xoá).
- FE: `tsc` sạch, `eslint` sạch, `npm run build` thành công.

### 13.6 Còn nợ

- **Chưa verify browser** (cần restart backend).
- Preview lúc release version → M6.
- Chưa có màn "máy nào lệch chuẩn" và chưa ghi cờ `Stale` xuống DB (mới log warning) → M4.
