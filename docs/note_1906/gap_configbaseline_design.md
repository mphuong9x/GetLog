# ConfigBaseline — Thiết kế cơ chế đối chiếu thông số chốt per-Model (thay nửa "limit/locked" của `CheckSumCustom` + `FtuDataConfigs`)

> Mục tiêu: với **mỗi Model**, giữ một bộ **thông số kỹ thuật/giới hạn QUAN TRỌNG** (Sector/Key → giá trị kỳ vọng) và **đối chiếu** với file config thực tế (INI khách + JSON CPEI_MFG) qua các lô (BOM/FTU ver khác nhau), đảm bảo các thông số chốt **không bị đổi sai**. Đây là **validation contract**, NGƯỢC hướng với `OverrideFile` (đẩy giá trị KHÁC theo trạm). Hai nửa đối nghịch của `CheckSumCustom`.
> Ngày: 2026-06-16. Trạng thái: thiết kế, chờ duyệt trước khi code.
>
> **Quyết định đã chốt (2026-06-16, session này):**
> 1. **Tên entity = `ConfigBaseline`** (header per-model) + `ConfigBaselineRule` (mỗi rule Sector/Key). (User ủy quyền chọn theo khuyến nghị.)
> 2. **Đối chiếu ở cả hai thời điểm** nhưng phân vai:
>    - **Publish-time (server MProject):** kiểm **CẢ exact + range** → **chặn cứng** release/pin lô nếu lệch.
>    - **Runtime (CPEI_MFG, GIỮ NGUYÊN):** chỉ **exact** qua `FtuDataConfigs` (engine payload không sửa). Range ở runtime **tạm hoãn** (xem §7).
> 3. **Kiểu so khớp:** `Exact` (BOM/SKU/firmware…) **và** `Range` min–max (emmc/ddr/iperf/power…).
> 4. **Khi lệch = CHẶN CỨNG (fail)** — giữ đúng hành vi hệ cũ.
>
> Liên quan: [`gap1_config_override_design.md`](gap1_config_override_design.md) (OverrideFile — nửa "editable"), [`compare1.md`](../compare1.md).

---

## 0. Bối cảnh & vì sao tách khỏi `OverrideFile`

Hệ cũ `CheckSumCustom` có **hai nửa đối nghịch**:

| | Nửa "editable" (giá trị KHÁC theo trạm) | Nửa "locked" (giá trị PHẢI GIỮ theo model) |
|---|---|---|
| Bản chất | Override per-Station/PC | Baseline/validation per-Model |
| Ví dụ | `DUT_IP`, `COM`, `ServerIp`, PoE port | `top_level_bom`, `me_bom`, SKU, firmware, criteria đo |
| MProject | **`OverrideFile`** (GAP-1, đã code Slice 1) | **`ConfigBaseline`** (tài liệu này) |
| Hướng | Đẩy giá trị xuống | Đối chiếu giá trị lên |

**Bằng chứng hệ cũ (đã xác nhận trong repo):**

- `Sample_Software/Config/ProgramConfig.json > FtuConfig.FtuDataConfigs` là một **mảng** `{ Sector, Key, TargetValue, ErrorMessage }`:
  ```json
  "FtuDataConfigs": [
    { "Sector": "General", "Key": "top_level_bom", "TargetValue": "000-08323-01", "ErrorMessage": "SAI TLB !000-08323-01" },
    { "Sector": "General", "Key": "me_bom",        "TargetValue": "300-01201-10", "ErrorMessage": "SAI ME BOM !300-01201-10" }
  ]
  ```
- Runtime check: `Old_program/FTU Program/CPEI_MFG/Services/FTU/FtuService.cs:124` `CheckFtuConfig()` —
  đọc file INI khách (`FtuConfig.CustomConfigFileName`, vd `ctr_04247_efbb.ini`) bằng `iniFile.GetValue(Sector, Key)`, nếu **không tìm thấy** → fail; nếu `ftuCfValue != TargetValue` → **fail cứng** (MessageBox + return false). **Chỉ exact, không có range.**

⇒ `ConfigBaseline` = **tổng quát hóa `FtuDataConfigs`**: per-Model, thêm `MatchType = Range`, thêm **cổng publish-time** (server chặn trước khi lô tới trạm). Engine runtime (CPEI_MFG) **không đổi** — vẫn là nguồn enforce exact tại từng unit.

---

## 1. Mô hình dữ liệu

```csharp
public class ConfigBaseline : VersionedEntity, ISoftDeletable
{
    public Guid ResourceId { get; set; }              // ACL parent = Resource của Model
    public virtual Resource Resource { get; set; } = null!;

    public Guid ModelId { get; set; }                  // per-MODEL
    public virtual Model Model { get; set; } = null!;

    public string? Name { get; set; }                  // nhãn tùy chọn, vd "UTP-G3-Touch-Pro spec lock"
    public ConfigBaselineStatus Status { get; set; } = ConfigBaselineStatus.Draft;

    public Guid CreatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public virtual ICollection<ConfigBaselineRule> Rules { get; set; } = new List<ConfigBaselineRule>();
}

public class ConfigBaselineRule : VersionedEntity, ISoftDeletable
{
    public Guid ConfigBaselineId { get; set; }
    public virtual ConfigBaseline Baseline { get; set; } = null!;

    public string TargetRelativePath { get; set; } = null!; // khớp SoftwareFile.RelativePath, vd
                                                            // "FTU_.../data/custom_config_files/ctr_04247_efbb.ini"
                                                            // hoặc "Config/ProgramConfig.json"
    public ConfigFileFormat Format { get; set; }            // Ini | Json (suy từ đuôi, lưu cho rõ)

    public string Sector { get; set; } = null!;             // INI: section; JSON: tên object cấp (vd "VersionConfig")
    public string Key { get; set; } = null!;                // INI: key; JSON: tên field

    public BaselineMatchType MatchType { get; set; }        // Exact | Range
    public string? ExpectedValue { get; set; }              // dùng khi Exact
    public double? Min { get; set; }                        // dùng khi Range
    public double? Max { get; set; }                        // dùng khi Range
    public bool MinInclusive { get; set; } = true;
    public bool MaxInclusive { get; set; } = true;

    public string? ErrorMessage { get; set; }               // map thẳng sang FtuDataConfigs.ErrorMessage

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public enum ConfigFileFormat     { Ini = 0, Json = 1 }
public enum BaselineMatchType    { Exact = 0, Range = 1 }
public enum ConfigBaselineStatus { Draft = 1, Active = 2 }   // (PendingApproval = 3 nếu sau này bật approval)
```

**Ràng buộc:**
- Unique: **1 `ConfigBaseline` Active / Model** — `UX_ConfigBaseline_Model` trên `ModelId WHERE !IsDeleted` (giống `LimitFile`). Bộ rule = nhiều dòng con.
- Unique rule: `(ConfigBaselineId, TargetRelativePath, Sector, Key) WHERE !IsDeleted`.
- Check constraint `CK_ConfigBaselineRule_Match`:
  `(MatchType = 0 AND ExpectedValue IS NOT NULL) OR (MatchType = 1 AND (Min IS NOT NULL OR Max IS NOT NULL))`.
- `Range` chỉ áp cho giá trị parse được ra số; parser fail → coi như **lệch** (chặn).

**Vì sao tách 2 bảng (header + rule):** một model có hàng chục rule trải nhiều file; tách giúp CRUD từng rule, import/diff theo lô, và bật/tắt cả baseline qua `Status` mà không xóa rule.

---

## 2. Cách tạo baseline (authoring) — khuyến nghị

**Rule có cấu trúc + bootstrap import** (không upload file mờ như OverrideFile, vì đây là *rule* chứ không phải *nội dung file*):

1. **Import bootstrap (1 lần):** chọn 1 version "known-good" của package gắn model → server đọc `ProgramConfig.json > FtuConfig.FtuDataConfigs` hiện có → sinh sẵn các rule `Exact` (Sector/Key/Expected/ErrorMessage). Engineer chỉ việc rà soát.
2. **Thêm/sửa rule thủ công (grid):** đặc biệt cho rule `Range` (criteria đo) mà `FtuDataConfigs` cũ không có; chọn `TargetRelativePath` từ **cây file của version** (tránh gõ sai path), nhập Sector/Key, chọn Exact/Range, nhập giá trị.
3. Cho phép đuôi: `.ini .cfg .json .xml .txt .dat` (allowlist như LimitFile/OverrideFile).

> Khác `OverrideFile`: OverrideFile lưu **blob nội dung** (đẩy nguyên file); ConfigBaseline lưu **rule** (so từng key). Cùng tái dùng: ACL `Resource`, `ILimitFilePermissionService → model-manager` (quyền theo model), FE chọn-path-từ-cây-file.

---

## 3. Đối chiếu **publish-time** (cổng chặn chính — nơi exact + range được enforce)

### 3.1 Điểm chèn: lúc (version × model) đã xác định

Baseline là **per-Model**, còn `SoftwareVersion` thuộc `SoftwarePackage` (không gắn model trực tiếp). Cặp (version, model) **chỉ rõ ràng khi pin version vào station** (station → model qua cây resource). Vì vậy **cổng enforce đặt tại pin/assign**, đúng nghĩa "chặn release lô FTU mới xuống trạm":

- **`StationSoftwareAssignmentService.PinVersionAsync`** (và `AssignAsync` nếu pin kèm): trước khi kích hoạt, resolve `modelId` từ `stationResourceId` (cây resource, **dùng lại logic `IOverrideResolver`**), lấy `ConfigBaseline` Active của model → validate các file của version → **lệch thì throw, chặn pin**.
- Bổ sung **API kiểm thử trước**: `POST /api/v1/config-baselines/{modelId}/validate?versionId=` trả danh sách rule pass/fail để engineer xem trước khi pin (không chặn, chỉ báo).

> Tùy chọn mở rộng: nếu sau này có liên kết Package↔Model trực tiếp thì có thể chặn ngay tại "release version". Hiện chưa có liên kết đó nên anchor ở pin là chuẩn nhất.

### 3.2 Validate cái gì

Với mỗi rule của baseline:
1. Tìm `SoftwareFile` của version có `RelativePath == rule.TargetRelativePath` → tải blob (text) → parse theo `Format`.
2. Lấy giá trị `value = parse(Sector, Key)`. **Không tìm thấy → fail** (giống legacy "not found").
3. So khớp:
   - `Exact`: `value == ExpectedValue` (string compare, trim; giữ nguyên hành vi `!=` của legacy).
   - `Range`: `double.TryParse(value)` rồi kiểm `Min/Max` theo `MinInclusive/MaxInclusive`; parse fail → fail.
4. Lệch → gom vào danh sách lỗi (kèm `ErrorMessage`); cuối cùng nếu có lỗi → **chặn** (`InvalidOperationException`/400) + liệt kê đủ rule sai.

### 3.3 Tương tác với `OverrideFile` (quan trọng)

- Baseline = khóa **giá trị model-invariant** (BOM/SKU/criteria). Override = thay **giá trị station-variant** (IP/COM). Về bản chất **không giao nhau** → bình thường validate trên file gốc của version là đủ cho rule model-scope.
- **Guardrail (khuyến nghị bật):** khi validate tại pin (đã biết station/computer), chạy trên **file hiệu lực sau override** (gọi `IOverrideResolver` rồi parse blob override nếu path bị override). Như vậy nếu ai đó lỡ đưa key model-locked vào file override → vẫn bị baseline tóm. → **dùng chung resolver với GAP-1** (tránh lệch logic).
- Khuyến nghị quy ước: path/key đã có rule baseline **không** nên nằm trong `OverridablePaths` cho cùng key đó; FE cảnh báo khi trùng.

---

## 4. Runtime (giữ nguyên engine, chỉ exact)

- **Không sửa CPEI_MFG.** Runtime vẫn là `CheckFtuConfig()` đọc `FtuDataConfigs` trong `ProgramConfig.json` đã deploy → enforce **exact** tại từng unit (như hệ cũ).
- **Baseline là nguồn sự thật cho `FtuDataConfigs`:** ở publish-time, ngoài việc đối chiếu INI khách, baseline **cũng validate chính khối `FtuDataConfigs` trong `ProgramConfig.json`** khớp tập rule `Exact` của model (Sector/Key/Expected). Nhờ đó "cái mà runtime enforce" được bảo đảm đúng baseline ngay từ lúc lô được duyệt.
- **Range ở runtime:** tạm hoãn (quyết định của user). Range được bắt ở publish-time. Khi nào cần bắt range theo từng unit → §7 (checker runtime riêng), **không** đụng CPEI_MFG.
- *(Tùy chọn Phase sau)* tự **sinh** khối `FtuDataConfigs` từ baseline rồi ghi vào `ProgramConfig.json` qua cơ chế per-key templating (cơ chế (2) của GAP-1) — khi đó baseline vừa validate vừa generate. MVP chỉ **validate**, chưa generate.

---

## 5. API + Service + Quyền

- `GET    /api/v1/config-baselines?modelId=` — lấy baseline + rules của model.
- `POST   /api/v1/config-baselines` — tạo baseline cho model (Draft).
- `POST   /api/v1/config-baselines/{id}/import?versionId=` — bootstrap rule từ `FtuDataConfigs` của 1 version.
- `POST   /api/v1/config-baselines/{id}/rules` / `PUT` / `DELETE` — CRUD rule.
- `POST   /api/v1/config-baselines/{id}/activate` — Draft→Active.
- `POST   /api/v1/config-baselines/{modelId}/validate?versionId=[&stationResourceId=]` — kiểm thử (không chặn), trả pass/fail từng rule.
- Enforce thực tế: bên trong `PinVersionAsync` (chặn).
- **Quyền:** tái dùng pattern model-manager (`ILimitFilePermissionService`) → chỉ model-manager/role phù hợp sửa baseline. **Approval:** MVP tắt (model-scope tạo thẳng, giống Model-scope của OverrideFile); có thể bật `PendingApproval` sau vì nới lock là nhạy cảm.

**Hạ tầng cần thêm:**
- **Parser:** INI (port ý tưởng `IniExtensionEditer` cũ hoặc thư viện ini nhẹ) + JSON (`System.Text.Json`, quy ước Sector = object cấp 1, Key = field; hỗ trợ path lồng nếu cần sau).
- **Evaluator:** `Exact` (string) + `Range` (double, inclusive flags).

---

## 6. Kế hoạch triển khai (slice, song song được với FE)

0. **CB-1** Entity `ConfigBaseline` + `ConfigBaselineRule` + 3 enum + migration (+ ACL `ResourceTypes.ConfigBaseline`, `AppPermissions.*ConfigBaselines`).
1. **CB-2** `IConfigFileReader` (parse INI/JSON từ blob) + `IBaselineEvaluator` (Exact/Range) — service thuần, unit-test dày.
2. **CB-3** `IConfigBaselineValidator.ValidateVersionForModelAsync(versionId, modelId, [stationResourceId])` → dùng `IOverrideResolver` để lấy file hiệu lực; trả `BaselineValidationResult`.
3. **CB-4** Chèn cổng chặn vào `StationSoftwareAssignmentService.PinVersionAsync` + endpoint `/validate` (dry-run).
4. **CB-5** `ConfigBaselineService` + controller + permission (model-manager) + import-from-FtuDataConfigs.
5. **FE-1** Màn Baseline theo Model: grid rule (Exact/Range), import bootstrap, chọn path từ cây file, nút Validate-version (dry-run) hiển thị pass/fail.
6. **Test E2E:** đổi 1 thông số chốt trong INI khách / `FtuDataConfigs` → pin bị chặn; số đo ngoài range → chặn; đúng hết → pin OK.

**Không động agent, không động CPEI_MFG.**

---

## 7. Tương lai — Range tại runtime (Option 2, CHƯA làm)

Khi cần bắt range theo **từng unit** tại trạm (không chỉ publish-time):
- Ship một **checker baseline độc lập** (exe/dll nhỏ của ta), agent/launcher gọi trước/sau khi chạy test, enforce exact+range từ baseline đã deploy.
- Hoặc kết hợp generate `FtuDataConfigs` (chỉ exact) + checker phụ cho range.
- **Không** sửa CPEI_MFG (giữ scope payload). Chỉ làm khi xuất hiện nhu cầu thực và pilot publish-time đã ổn.

---

## 8. Bảng phủ so với hệ cũ

| Chức năng hệ cũ | ConfigBaseline | Trạng thái |
|---|---|---|
| `FtuDataConfigs` exact, runtime, fail cứng | Rule `Exact` + runtime CPEI_MFG giữ nguyên + publish-time validate khối FtuDataConfigs | ✅ Phủ + mạnh hơn |
| Báo lỗi kèm `ErrorMessage` | `ConfigBaselineRule.ErrorMessage` map thẳng | ✅ |
| (không có) range cho số đo | Rule `Range` enforce ở **publish-time** | ✅ Mới |
| (không có) cổng chặn trước khi lô tới trạm | Chặn tại `PinVersionAsync` | ✅ Mới |
| Per-model | `ConfigBaseline.ModelId`, 1 active/model | ✅ |
