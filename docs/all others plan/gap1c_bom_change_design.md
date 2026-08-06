# GAP-1C — Đổi BOM/MEBOM: nhân bản chương trình test theo model

Kế tục [`gap1b_config_parameter_design.md`](gap1b_config_parameter_design.md). Tài liệu này **không thay** GAP-1B; nó bổ sung ba thứ GAP-1B chưa có: tham số ghi xuống **nhiều vị trí**, clone package **mang theo cấu hình**, và một wizard đổi BOM ở **cấp model**.

Ngày chốt: 2026-08-06. Mọi trích dẫn `file:dòng` trong tài liệu này đã kiểm chứng trực tiếp trong repo tại thời điểm đó.

---

## 1. Nghiệp vụ

Ở xưởng, sửa BOM hoặc ME BOM **đồng nghĩa với một chương trình test mới** — không phải một bản cập nhật của chương trình cũ. Lý do là audit: danh sách chương trình test phải tra ngược được ra từng BOM đã chạy.

Chương trình mới giống hệt chương trình cũ, chỉ khác giá trị BOM trong file cấu hình. Nó được release ra như một chương trình độc lập, và **cùng tồn tại** với các chương trình BOM khác trên cùng một trạm — công nhân mở đúng app ứng với lô hàng đang test. Có lúc chạy lẫn nhiều lô cùng model, cùng trạm nhưng khác BOM.

Quy ước đặt tên đã có sẵn ở bộ phận:

```
FCD10216424_FTU10416424_FW72111_BOM10_MEBOM005
```

Tên đã chứa đủ FCD / FTU / FW / BOM / MEBOM. Sau này có thể thêm code ép đúng format này.

---

## 2. Bề mặt thay đổi thật của một lần đổi BOM

Đo trên `Sample_Software/` (chương trình chuẩn C# `CPEI_MFG` + FTU của khách):

| # | File | Vị trí | Giá trị mẫu | Khai báo được hôm nay? |
|---|---|---|---|---|
| 1 | `Config/ProgramConfig.json` | `VersionConfig.BOMVer` | `113-04247-11` | có |
| 2 | `Config/ProgramConfig.json` | `FtuConfig.FtuDataConfigs[].TargetValue` (TLB) | `000-08323-01` | **không — mảng JSON** |
| 3 | `Config/ProgramConfig.json` | `FtuConfig.FtuDataConfigs[].ErrorMessage` (TLB) | `SAI TLB !000-08323-01` | **không — mảng JSON** |
| 4 | `Config/ProgramConfig.json` | `FtuConfig.FtuDataConfigs[].TargetValue` (ME BOM) | `300-01201-10` | **không — mảng JSON** |
| 5 | `Config/ProgramConfig.json` | `FtuConfig.FtuDataConfigs[].ErrorMessage` (ME BOM) | `SAI ME BOM !300-01201-10` | **không — mảng JSON** |
| 6 | `FTU_*/data/custom_config_files/ctr_*.ini` | `[General] top_level_bom` | `000-08323-01` | có |
| 7 | cùng file ini | `[General] me_bom` | `300-01201-10` | có |
| 8 | cùng file ini | `[Firmware] bom_id` | `113-04247-11` | có |

**Ba giá trị logic trải ra tám vị trí vật lý ở hai file.** Bốn vị trí nằm trong mảng JSON mà bộ đọc/ghi hiện tại không địa chỉ hoá được:

- `ConfigFileReader.ReadAllJson` (`ConfigFileReader.cs:71-104`) chỉ duyệt scalar ở root và một cấp lồng; `ScalarText` (`:106-113`) trả `null` cho `JsonValueKind.Array` nên mảng bị bỏ qua hoàn toàn → màn khai báo không liệt kê ra được.
- `ConfigFileWriter.SetValue` (`ConfigFileWriter.cs:19-35`) chỉ nhận `(sector, key)`, không đi vào phần tử mảng.

### 2.1 `FtuDataConfigs` là gì

`FtuService.CheckFtuConfig()` (`Old_program/FTU Program/CPEI_MFG/Services/FTU/FtuService.cs:123-150`) mở file ini của FTU rồi so từng `Sector`/`Key` với `TargetValue` khai trong ProgramConfig. Lệch thì bật `MessageBox` và trả `false`, **không cho test**.

Đây là **đối chiếu config với config**, không đụng board. Nó chặn đúng một loại lỗi: sửa file này quên file kia. Ghi chép cũ ở `gap1b` §0.3 mô tả nó như cái làm board sai BOM bị fail — **sai**, việc kiểm BOM của board nằm ở phía FTU (`test_check_toplevelbom`, `test_check_mebom` trong `tc_04247_efbb.json`).

Quyết định: **giữ nguyên**. Nó là một gate chặn trước khi test, mạnh hơn drift detection theo chu kỳ poll, và nó chặn cả trường hợp sửa tay ini trên máy. Xem quyết định Đ3.

### 2.2 Cùng họ: danh sách hạng mục test

```json
"CheckLogConfigs": [
  { "Keywork": "Total Test Items: [101, 20, 23, 5, ...]", "ErrorCode": "ITEMFF" },
  { "Keywork": "Pass Items: 101 20 23 5 ...",             "ErrorCode": "ITEMFF" }
]
```

đối chiếu với `FTU_*/data/selected_items.ini`:

```ini
[ITEMS]
id = 101,20,23,5,156,320,321,37,38,39,40,41,43,3069,207
```

Một giá trị logic, **ba vị trí, ba định dạng**: `101,20,23` / `[101, 20, 23]` / `101 20 23`. Nên mỗi đích cần thêm **ký tự ngăn cách**, không chỉ mẫu chuỗi.

Và hai phần tử `CheckLogConfigs` có `ErrorCode` **giống hệt nhau** — không có khoá nào phân biệt ngoài chính `Keywork`, mà đó lại là trường sẽ bị ghi đè. Khớp bằng giá trị đầy đủ thì ghi xong lần render sau **mất dấu phần tử và im lặng bỏ qua**. Xem Đ4.

---

## 3. Bảy quyết định đã chốt

**Đ1 — Một ô nhập ghi xuống nhiều vị trí.** Khai một tham số "Top Level BOM" gắn nhiều đích; người dùng nhập ba ô (TLB / ME BOM / BOM ID), hệ ghi đủ tám chỗ. Lý do: nhập tay tám ô thì sớm muộn cũng lệch, mà lệch BOM là quality escape.

**Đ2 — Mỗi loại trạm một package riêng.** FT1, FT2, RF, ICT… mỗi loại một package, đóng gói sẵn `Station` và `selected_items` của nó. Hệ quả: một lần đổi BOM đụng cả loạt package của model ⇒ wizard phải làm việc ở **cấp model**, không phải cấp package.

**Đ3 — Giữ `FtuDataConfigs`, mở rộng MProject để ghi vào mảng.** Không sửa chương trình test. Lý do: ship được ngay với đội hình package hiện có, không phải phát hành lại engine rồi chờ 2000 trạm cập nhật xong mới dùng được; giữ được gate chặn tại trạm; rủi ro nằm ở server chứ không nằm trên chuyền; và cùng bộ máy đó dùng lại được cho `CheckLogConfigs`.

**Đ4 — Địa chỉ phần tử mảng khớp theo khoá, không theo index.** `FtuDataConfigs[Sector=General, Key=top_level_bom]`. Với `CheckLogConfigs` thì khớp theo **tiền tố** của chính trường bị ghi (`Keywork` bắt đầu bằng `Total Test Items:`). Kèm một luật chặn ở màn khai báo: **áp mẫu xong mà điều kiện khớp không còn đúng thì từ chối lưu**.

**Đ5 — Giá trị BOM ghi thẳng vào file của version clone, không dùng recipe.** Lý do: (a) recipe chỉ có tác dụng khi `assignment.ConfigValueSetId` trỏ vào nó, tức đẻ thêm một bước thủ công cho từng trạm; (b) nếu để ở recipe thì file trong package mới vẫn ghi BOM cũ, mở ra xem không audit được — hỏng đúng mục tiêu của cả tính năng.

**Đ6 — Wizard dừng ở tạo package + đặt giá trị.** Không pin, không enable, không assign, không đẩy xuống agent. Các bước đó giữ nguyên thủ công như hiện nay. Hệ quả tốt: không cần chuyển trạm hàng loạt, không cần gỡ package cũ tự động, không cần thêm loại phiếu duyệt, không cần hẹn giờ cắt chuyền.

**Đ7 — BOM đọc từ tên package, không lưu thành cột.** Quy ước tên đã chứa đủ. Launcher tách tên ra để hiển thị. Không thêm cột vào `SoftwarePackage`, không đổi contract catalog, không đụng agent.

---

## 4. Kiến trúc

### 4.1 `ConfigParameterTarget` — đích phụ

`ConfigParameter` **giữ nguyên** đích chính (`TargetRelativePath` / `Format` / `Sector` / `Key`) như hiện tại. Thêm bảng con cho các đích phụ:

```csharp
public class ConfigParameterTarget : VersionedEntity, ISoftDeletable
{
    public Guid ConfigParameterId { get; set; }
    public virtual ConfigParameter Parameter { get; set; } = null!;

    /// <summary>Cho phép '*' ở tên thư mục/tệp. Phải resolve ra ĐÚNG 1 file. Xem R1.</summary>
    public string TargetRelativePath { get; set; } = null!;
    public ConfigFileFormat Format { get; set; }

    /// <summary>Với JSON có thể là đường dẫn container nhiều cấp: "FtuConfig.FtuDataConfigs".</summary>
    public string Sector { get; set; } = string.Empty;
    public string Key { get; set; } = null!;

    /// <summary>JSON: [{ "field": "Key", "op": "Equals|StartsWith", "value": "top_level_bom" }]. Rỗng = không phải mảng.</summary>
    public string ElementMatch { get; set; } = "[]";

    /// <summary>Mặc định "{value}". Ví dụ "SAI TLB !{value}", "Total Test Items: [{value}]".</summary>
    public string ValueTemplate { get; set; } = "{value}";

    /// <summary>Chỉ dùng cho tham số kiểu danh sách. Ví dụ ",", ", ", " ".</summary>
    public string? ListSeparator { get; set; }

    public int SortOrder { get; set; }
    // + IsDeleted/DeletedAt/DeletedBy
}
```

Tham số đã khai trước đây chạy y nguyên (không có đích phụ nào). **Không migrate dữ liệu.**

Ví dụ khai đầy đủ cho "Top Level BOM":

| | Đường dẫn | Sector | Key | Khớp phần tử | Mẫu ghi |
|---|---|---|---|---|---|
| chính | `Config/ProgramConfig.json` | `VersionConfig` | `BOMVer` | — | `{value}` |
| phụ 1 | `FTU_*/data/custom_config_files/ctr_*.ini` | `General` | `top_level_bom` | — | `{value}` |
| phụ 2 | `Config/ProgramConfig.json` | `FtuConfig.FtuDataConfigs` | `TargetValue` | `Sector=General`, `Key=top_level_bom` | `{value}` |
| phụ 3 | `Config/ProgramConfig.json` | `FtuConfig.FtuDataConfigs` | `ErrorMessage` | `Sector=General`, `Key=top_level_bom` | `SAI TLB !{value}` |

### 4.2 Reader / Writer

Mở rộng đúng một dạng địa chỉ: **phần tử của mảng-đối-tượng, khớp theo cặp trường/giá trị**. Không JSONPath, không biểu thức, không lồng nhiều tầng mảng. Gặp ca phức tạp hơn thì từ chối ở màn khai báo chứ không nới cú pháp.

- `ReadAll` phát thêm entry cho phần tử mảng-đối-tượng để màn khai báo liệt kê được.
- `ReadValue` / `SetValue` nhận thêm `ElementMatch`.
- `SetValue` áp `ValueTemplate` và `ListSeparator` trước khi ghi.

### 4.3 Điểm chèn

Không đổi kiến trúc render của GAP-1B. Chỗ đang ghi một đích thì ghi thêm các đích phụ, dùng chung `ConfigFileWriter`.

### 4.4 Wizard đổi BOM

Ba bước:

1. **Chọn model** → liệt kê mọi package của model (theo `SoftwarePackage.ModelId`) → tick package cần nhân bản → mỗi dòng chọn version nguồn (mặc định bản Released mới nhất).
2. **Nhập giá trị một lần** — TLB / ME BOM / BOM ID, cộng các tham số khác nếu cần. Tên package mới sinh bằng cách **thay đoạn** trong tên nguồn (`BOM10`→`BOM11`, `MEBOM005`→`MEBOM006`), giữ nguyên phần FCD/FTU/FW; sửa tay được từng dòng; cảnh báo nếu tên nguồn không khớp quy ước.
3. **Xem trước và xác nhận** — bảng "package nào, file nào, vị trí nào, cũ → mới", cộng danh sách tên package sẽ xuất hiện trên launcher.

Kết thúc: loạt package mới, mỗi cái một version **Draft**, file đã ghi đúng BOM, đã mang sẵn khai báo tham số và giá trị riêng của từng trạm/máy. Dừng.

### 4.5 Launcher

Thuần client, không đụng backend/agent/contract.

- **Badge trên tile**: tách tên theo quy ước, dòng dưới hiện `BOM10 · MEBOM005` in đậm có nền. Hiện tại tile bind thẳng `{Binding Name}` trong `StackPanel Width="100"` với `TextWrapping="Wrap"` (`MainWindow.xaml:204-211`) — chuỗi 46 ký tự không dấu cách sẽ ngắt thành khối 3–4 dòng chữ đặc, phần `BOM10` nằm gần cuối, mắt quét qua không bắt được.
- **Chuột phải → Thông tin**: bảng tách FCD / FTU / FW / BOM / MEBOM + version + trạng thái + PID + thời gian chạy + lần thoát gần nhất. Toàn bộ đã có sẵn trong `CatalogAppDto` (`IpcContracts.cs:80-103`). Khớp thói quen cũ: UIStore có sẵn menu `Open / Close / Setting / Information / Cancel update / Cancel Extract` (`Old_program/UIStore/UiStore/MainWindow.xaml:136-168`).
- Tên không khớp quy ước thì hiện nguyên như hiện nay, không cố parse.

---

## 5. Rủi ro và cách xử lý

### R1 — Đường dẫn đích chứa version của FTU *(nghiêm trọng)*

Thư mục FTU tên là `FTU_efbb_1.0.24_3.18.16_UTP-G3-Touch-Pro` — chứa version. `FtuConfig.Name => Path.GetFileName(DirPath)` và `FTUWindowsTitle => $"Factory Test Utility Version {Name}"` xác nhận chương trình lấy version FTU từ chính tên thư mục, tức tên **đổi mỗi lần lên đời FTU**. Tham số đã khai sẽ trỏ vào file không tồn tại, và theo quyết định M1 của GAP-1B thì lỗi render bị **log rồi bỏ qua, không ném** ⇒ im lặng, file giữ BOM cũ, chỉ vỡ ra khi `CheckFtuConfig` chặn ở trạm.

→ Cho phép `*` trong `TargetRelativePath`, resolve lúc clone và lúc render, **bắt buộc khớp đúng một file**. Khớp 0 hoặc nhiều thì báo lỗi rõ ràng, không im lặng. Nằm trong M1.

### R2 — Ghi nhiều file phải toàn vẹn hoặc không *(nghiêm trọng)*

BOM ghi vào hai file. Ghi được file này mà file kia không resolve → package mới có ProgramConfig BOM mới, ini BOM cũ → trạm bị chặn không test được.

→ Validate **toàn bộ** đích của **toàn bộ** tham số trước, ghi sau. Đích nào không resolve thì huỷ clone package đó và báo lý do; không tạo package nửa vời. Nằm trong M4.

### R3 — Chưa package nào có khai báo *(nghiêm trọng)*

Wizard ghi dựa trên khai báo tham số, mà hôm nay chưa package nào khai BOM đa đích. Trước lần đổi BOM đầu tiên phải khai tay cho mọi package của mọi model — 8 loại trạm × N model.

→ M2 **bắt buộc** có "chép khai báo từ package khác": khai một lần cho mỗi loại trạm rồi chép sang các model. Biến việc N×M thành việc M.

### R4 — Wizard clone N package không có transaction chung *(vừa)*

`ClonePackageAsync` cố ý không bọc transaction ngoài (clone version chạy transaction riêng). Fail ở package thứ 5 → 4 cái đã tạo và **đã chiếm tên**; bấm lại dính `A software package named '...' already exists` (`SoftwarePackageService.cs:86-89`).

→ Kiểm tra trùng tên **toàn bộ trước khi bắt đầu**. Màn kết quả liệt kê từng package thành/bại. Chạy lại chỉ những cái lỗi.

### R5 — Clone không mang `ConfigBaseline` sang *(vừa)*

`ConfigBaselineRule` gắn `ConfigBaselineId`, không gắn thẳng package; clone không mang sang ⇒ package mới mất lưới kiểm cấu hình. Cùng loại thiếu sót với `ConfigParameter`.

→ Chép trong M3.

### R6 — Giá trị cấp máy phải sửa nhiều lần *(vừa)*

COM port, thiết bị âm thanh, `selected_items` thuộc về **máy**, nhưng tham số khai theo **package**. Với 4 BOM cùng tồn tại trên một máy, đổi COM8→COM9 phải sửa 4 lần ở 4 màn khác nhau; quên một cái là chương trình BOM đó chạy sai cổng.

→ Thêm "áp cho mọi chương trình cùng model trên máy này" khi sửa giá trị cấp máy. Nằm trong M3.

### R7 — Sửa BOM gõ nhầm sau khi clone *(vừa)*

`MaxScope = Model` chặn override cấp trạm/máy cho BOM, mà Đ5 không dùng recipe ⇒ sau khi clone không có đường sửa BOM từ web.

→ Cho phép chạy lại bước "đặt giá trị" trên version còn **Draft chưa Release**. Sau khi Release thì không sửa nữa — đúng tinh thần một chương trình một BOM. Nằm trong M4.

### R8 — Dung lượng trạm *(nhẹ)*

Đo thực tế thư mục mẫu: **687 MB một chương trình**. Mỗi package cài xuống một thư mục riêng ⇒ 4 BOM cùng tồn tại là ~2,7 GB mỗi máy. Trên server blob dùng chung nên không tốn.

→ Cần thói quen gỡ package BOM cũ khi lô hàng chạy xong. Cơ chế đã có: `DeactivateAsync` gọi `CreateUninstallJobsAsync` **vô điều kiện**, gỡ dứt điểm.

### R9 — Audit theo lô *(nhẹ)*

Mỗi package đã có một dòng `SoftwarePackage.clone`. Thiếu dòng cấp đợt.

→ Thêm một audit entry cho cả đợt: ai, model nào, BOM cũ → mới, danh sách package tạo ra.

---

## 6. Lộ trình

Tổng ~1.850 LOC. **Vượt xa ngân sách trong `docs/skills/coding_rule.md`** (≤5 file, ≤150 LOC) nên không làm một lần — mỗi mốc chạy test xanh rồi mới sang mốc kế.

| Mốc | Nội dung | Ước lượng |
|---|---|---|
| **M1** ✅ | `ConfigParameterTarget` + migration; reader/writer địa chỉ hoá phần tử mảng; `ValueTemplate` + `ListSeparator`; glob đường dẫn (R1); nối vào đường render | **XONG 2026-08-06** — xem §10 |
| **M2** ✅ | Màn khai báo đích: chọn vị trí (kể cả phần tử mảng), nhập mẫu + ngăn cách, xem thử kết quả ghi, luật chặn Đ4; **chép khai báo từ package khác (R3)** | **XONG 2026-08-07** — xem §12 |
| **M3** ✅ | Clone chép `ConfigParameter` + đích + `ConfigValueOverride` Active (~~+ `ConfigBaseline` (R5)~~ — xem §13.3); "áp cho mọi chương trình cùng model trên máy này" (R6) | **XONG 2026-08-07** — xem §13 |
| **M4** ✅ | Ghi giá trị vào file version clone: validate-tất-cả-rồi-ghi (R2), sinh blob mới, chạy lại được trên Draft (R7) | **XONG 2026-08-07** — xem §14 |
| **M5** | Wizard đổi BOM cấp model: 3 bước, tách/ghép tên theo quy ước, kiểm trùng tên trước (R4), audit lô (R9) | ~550 LOC |
| **M6** ✅ | Launcher: badge BOM trên tile + chuột phải Thông tin | **XONG 2026-08-06** — xem §11 |

Thứ tự bắt buộc: M1 → M2 → M3 → M4 → M5. **M6 độc lập hoàn toàn**, làm lúc nào cũng được — rẻ nhất và công nhân cảm nhận được ngay.

### Quy tắc khi chép ở M3

- Chỉ chép `ConfigValueOverride` trạng thái **Active**. Bỏ Draft/PendingApproval/Stale — không nuốt thứ đang chờ duyệt vào package mới.
- Remap `ConfigParameterId` theo bảng ánh xạ cũ→mới. Giữ nguyên `ResourceId` / `StationId` / `ComputerId` / `Value`.
- **Tính lại `BaseValueAtCreation`** theo nội dung file **mới**, không chép nguyên — chép nguyên thì override hợp lệ bị đánh Stale hàng loạt.
- Tham số nào bị wizard đổi giá trị thì **không chép** override cấp trạm/máy của nó, và liệt kê ra cho người tạo thấy. Đây là chỗ duy nhất giá trị cấp máy có thể âm thầm đè lên BOM mới.
- Không chép assignment — giữ nguyên hành vi clone hiện tại.

### Bẫy đã biết

- `ConfigValueOverride` có check constraint `CK_ConfigValueOverrides_ScopeMatchesId` (Station→`StationId`, Computer→`ComputerId`, cái kia phải null). Chép sai là `23514` lúc `SaveChanges`.
- Unique index `UX_ConfigParameters_Package_Path_Key` theo package và `UX_ConfigValueOverrides_Parameter_Scope` theo parameter — chép sang package mới sinh id mới nên không đụng, nhưng đừng đổi thứ tự chép.
- Blob mới sinh ở M4 phải được `SoftwareFile` tham chiếu trước khi `BlobGc` chạy; xem 3 vị trí ref-guard đã ghi trong GAP-1B.
- Không thêm permission mới — dùng lại `software.package.manage` / `software.read`, tránh phải restart re-seed.

---

## 7. Cố ý không làm

- **Không chuyển trạm, không gỡ package cũ, không pin/enable tự động** (Đ6). Nhiều BOM cùng tồn tại trên một trạm là **yêu cầu**, không phải sự cố — nên cũng **không** cảnh báo khi một trạm nhận hai package cùng model. Đặt cảnh báo ở đường đi hàng ngày chỉ tạo thói quen bấm bỏ qua.
- **Không lưu `BomVersion`/`MeBomVersion` thành cột** (Đ7). Tên package đã chứa.
- **Không dùng recipe cho BOM** (Đ5). `ConfigValueSet` vẫn còn nguyên cho các ca khác của GAP-1B.
- **Không sửa chương trình test C#** (Đ3). Nếu sau này muốn dọn `ProgramConfig.json` cho gọn thì làm độc lập được, phần đa đích dùng lại nguyên vẹn.
- **Không làm JSONPath tổng quát** (Đ4).

---

## 8. Còn treo — xác minh tại trạm trước khi tin

1. **FTU có tự sinh lại `custom_config_files/*` từ `config_files/*` không.** `FtuConfig.ConfigPath` chỉ tới `data/custom_config_files/{CustomConfigFileName}` nên phía C# đọc file custom; nhưng FTU là exe của khách, chưa đọc được. Nếu FTU tái tạo file custom thì phải ghi cả bản template.
2. **`selected_items.ini` có bị FTU ghi lại lúc chạy không.** Nếu có thì việc MProject ghi đè mỗi lần cài là đúng ý — ghim lại bộ đã duyệt — nhưng cần biết trước để không tưởng là drift.
3. **FTU kiểm BOM của board bằng cách nào.** Suy từ tên test `test_check_toplevelbom` / `test_check_mebom` trong `tc_04247_efbb.json`; nếu đúng thì board BOM mới đi qua trạm chưa chuyển sẽ **fail thật**, dừng chuyền — an toàn, nhưng nên chuyển hết các trạm của một model trong cùng một khoảng nghỉ.

## 9. Việc ngoài lề phát hiện khi khảo sát

*(mục 9 giữ nguyên bên dưới; §10–§11 ghi lại hai mốc đã code)*

---

## 10. M1 — đã triển khai (2026-08-06)

**Trạng thái:** 839/839 test backend xanh, migration `20260806154034_AddConfigParameterTarget` đã áp DB dev, `MProject.Api` build sạch. **CHƯA commit, CHƯA restart backend, CHƯA E2E trạm thật.** Chưa có API/UI nên chưa khai được đích phụ ngoài test → M2.

Làm thành hai lát, mỗi lát test xanh rồi mới sang lát sau (M1 ~700 dòng, vượt xa ngân sách `coding_rule.md`).

### 10.1 M1a — reader/writer

| File | Thay đổi |
|---|---|
| `IConfigFileReader` | `ReadValue` nhận `elementMatch`; `ConfigFileEntry` mang thêm `ElementMatch`; thêm record `ConfigElementMatch` + enum `ConfigElementMatchOp` (Equals/StartsWith) |
| `IConfigFileWriter` | `SetValue` nhận `elementMatch` |
| `ConfigFileReader` | sector dạng đường dẫn nhiều cấp; `ReadValue` khớp phần tử mảng |
| `ConfigFileWriter` | như trên, phía ghi |

⚠ **`ReadAll` KHÔNG liệt kê phần tử mảng** — xem §10.4 mục 1. Việc liệt kê chuyển sang M2.

**Quyết định phát sinh:**

1. **Sector thử tên nguyên văn TRƯỚC, rồi mới tách theo dấu chấm.** Khai báo cũ có dấu chấm trong tên section vẫn chạy y nguyên — không có đường hồi quy.
2. **Nhập nhằng = không khớp.** Hai phần tử cùng thoả điều kiện thì reader trả `null` và writer **ném lỗi**, không lấy phần tử đầu. Ghi BOM vào nhầm dòng là quality escape, không phải chuyện đoán được.
3. **INI + elementMatch = lỗi** (INI không có mảng).

### 10.2 M1b — entity + wiring

| File | Thay đổi |
|---|---|
| `Domain/Entities/Software/ConfigParameterTarget.cs` | entity mới + `static Render(value, template, separator)` |
| `ConfigParameter.Targets` | navigation |
| `DBContext` / `IAppDbContext` | DbSet + cấu hình |
| Migration `20260806154034_AddConfigParameterTarget` | bảng mới, snapshot diff **chỉ thêm** 96 dòng |
| `IOverrideResolver` | `ResolvedConfigValue` mang thêm `ElementMatch` |
| `OverrideResolver` | `ExpandTargetsAsync` trải 1 giá trị ra N đích + `ParseElementMatch` |
| `ConfigRenderService` | truyền `ElementMatch` xuống reader/writer, **đưa vào `ComputeValueSetHash`** |
| `ConfigTargetPaths` (mới) | ánh xạ đích → file thật + glob (R1); **cả deploy lẫn xem trước trên web đều đi qua đây** |
| `InstallationJobService` / `SoftwareFileService` | dùng `ConfigTargetPaths.MapToFiles` |

**Quyết định phát sinh:**

1. **Đích phụ dùng lại nguyên bộ máy stale của GAP-1B.** `BaseValueAtCreation` của đích phụ = `Render(base gốc, template, separator)` — tức snapshot được dịch sang đúng dạng chữ mà vị trí đó phải giữ. Nhờ vậy luật "server thắng" chạy **theo từng vị trí**, không cần thêm cơ chế nào. Hệ quả cần biết: một file bị sửa tay thì chỉ đích ở file đó bị bỏ, các đích khác vẫn ghi ⇒ **ghi nửa vời vẫn có thể xảy ra ở đường render**. Ràng buộc toàn-vẹn-hoặc-không là R2, **nằm ở M4** (đường wizard), đúng như lộ trình.
2. **`ElementMatch` PHẢI nằm trong hash cache.** `CheckLogConfigs` chứa danh sách hạng mục ở `Keywork` của **cả hai dòng** — cùng sector, cùng key, chỉ khác điều kiện khớp. Bỏ ra ngoài hash là hai đích dùng chung một entry cache và phục vụ bản render sai.
3. **KHÔNG đặt unique index `(parameter, path, sector, key)` cho đích.** Ban đầu đã thêm rồi phải gỡ: nó chặn đúng ca thật ở mục 2. Chỉ index thường trên `ConfigParameterId`. Trùng lặp bắt ở màn khai báo (M2).
4. **Glob khớp trong PHẠM VI MỘT ĐOẠN** (`[^/]*`), không cho vượt thư mục. Khớp 0 hoặc ≥2 file thì **bỏ cả đích đó** và log Error — không đoán. Giải R1.
5. **Đích INI mang elementMatch bị bỏ ngay ở resolver.** Vị trí không đọc được thì bộ kiểm stale hiểu là "giá trị nền đã đổi" và **đánh Stale cả override** — tức giết một giá trị đang chạy đúng vì một khai báo sai. M3 chép khai báo giữa các package nên ca này có thật.

### 10.4 Sửa sau review (cùng ngày)

Ba lỗi do chính đợt M1 này gây ra, tìm thấy khi rà lại và đã sửa + có test:

1. **CRITICAL — `ReadAll` phát entry mảng làm sập `GET /override-files/{id}/migration-preview`.** `OverrideFileMigrationService.Diff` dựng `ToDictionary(e => (e.Sector, e.Key))`; một mảng sinh cặp đó **một lần cho mỗi phần tử** ⇒ `ArgumentException: An item with the same key has already been added. Key: (FtuConfig.FtuDataConfigs, Key)` với đúng `ProgramConfig.json`. `ConfigValueOverrideService` (dòng 368) thì không sập nhưng ghi đè im lặng theo kiểu last-wins.
   → **Gỡ hẳn phần liệt kê mảng khỏi `ReadAll`**, trả về M2. Nó vốn có **0 consumer thật** ở M1 (đã phải tự lọc đi ở màn khai báo), mà lại làm vỡ 2 trong 3 consumer còn lại. Gỡ luôn cả trường `ConfigFileEntry.ElementMatch` và cái lọc tạm. `ReadValue` khớp mảng thì **giữ nguyên** — đó mới là đường render dùng.
2. **HIGH — xem trước trên web không giải glob.** `SoftwareFileService.ResolveEffectiveBlobsAsync` tra từ điển bằng đường dẫn nguyên văn, nên đích khai `FTU_*/...` không bao giờ khớp: trạm ghi BOM mới còn màn hình/ZIP tải về hiện file cũ. Chính comment sẵn có trong `InstallationJobService` đã cảnh báo hai chỗ này phải đi cùng luật.
   → Tách `ConfigTargetPaths.MapToFiles`, **cả hai** cùng gọi.
3. **MEDIUM — đích INI mang elementMatch đánh Stale oan** (mục 4 ở trên).

Cả 3 test đều đã **kiểm chứng bằng mutation** (phá lại fix thì test đỏ), không phải tautology.

### 10.3 Đã verify

- `dotnet test`: **841/841 xanh** (+34 test mới).
- Reader/writer (15): khớp phần tử theo `Sector`+`Key`, `StartsWith` cho trường tự bị ghi đè, nhập nhằng bị từ chối cả hai phía, INI từ chối elementMatch, sector có dấu chấm vẫn ưu tiên tên nguyên văn, và `ReadAll` **không bao giờ trả trùng cặp (sector, key)**.
- `ConfigParameterTarget.Render` (6): ba dạng ngăn cách của §2.2, template, kết hợp cả hai.
- `InstallationJobService` (+7): trải đích phụ trong cùng file, ba dạng ngăn cách end-to-end, glob khớp đúng 1 file, glob khớp 2 file hoặc 0 file thì bỏ, glob không vượt thư mục, đích INI mang elementMatch bị bỏ.
- `SoftwareFileService` (+1) và `OverrideFileMigrationService` (+1): hai hồi quy ở §10.4.
- `ConfigRenderService` (+2): hash tách được hai đích chỉ khác `ElementMatch`; render ghi đúng phần tử mảng, phần tử anh em không đổi.
- **Postgres thật:** cả 3 query mới/đổi (`ConfigParameterTargets`, projection override có `ConfigParameterId`, projection recipe có `ConfigParameterId`) dịch và chạy được trên `TESSDB` (test tạm, đã xoá) — doc cảnh báo InMemory không bắt được lỗi Npgsql.

---

## 11. M6 — Launcher (đã triển khai 2026-08-06)

Thuần client, 2 file (`MProjectLauncher/ViewModels.cs`, `MainWindow.xaml`), ~83 LOC. Không đụng backend/agent/contract, đúng §4.5.

- **Badge**: chip `BrandDeep` `BOM10 · MEBOM005` dưới tên trên tile; tile nới `Width` 100→120 cho vừa chip. Tên không đúng quy ước thì chip ẩn.
- **Chuột phải → Thông tin**: `MessageBox` (launcher đã dùng MessageBox cho xác nhận Dừng, không dựng window riêng) liệt kê FCD/FTU/FW/BOM/MEBOM + version + trạng thái + PID + chạy từ + thoát gần nhất.
- Tách tên bằng `Split('_')` + `StartsWith`: `"MEBOM005".StartsWith("BOM")` là **false** nên BOM/MEBOM không đụng nhau.
- **Lỗi tìm thấy khi làm, đã sửa:** agent chỉ set `Pid`/`StartedAt`/`LastExit` khi `isActive` (`AgentStatusProvider.cs:150-161`), nhưng `StartedAt` **không bị xoá** lúc process chết (`ProcessSupervisor.cs:355-357` chỉ set `Pid = null`). Dòng "Chạy từ" và `BuildSubText` phải chặn thêm `dto.Status == "Running"`, không thì app đã tắt vẫn đếm uptime.
- Launcher net48 WPF **không có project test nào** (2 project test đều net8, không host được) → verify bằng build MSBuild + script kiểm logic tách tên.

---

## 12. M2 — đã triển khai (2026-08-07)

**Trạng thái:** 855/855 test backend + 126/126 test FE xanh, FE build/typecheck/lint sạch, `MProject.Api` compile sạch. **CHƯA commit, CHƯA restart backend, CHƯA verify browser** (backend đang chạy bản cũ nên endpoint mới chưa sống).

### 12.1 Backend

| File | Vai trò |
|---|---|
| `IConfigFileReader.ReadTargets` + `ConfigTargetEntry` | Liệt kê **mọi vị trí có thể trỏ tới**, kể cả phần tử mảng, kèm điều kiện khớp tối thiểu tự suy ra |
| `ConfigElementMatchJson` (mới) | **Một codec cho cả hai chiều** — màn khai báo ghi, resolver đọc |
| `ConfigTargetPaths.Resolve` | Khai báo và deploy dùng chung luật đường dẫn/glob |
| `ConfigParameterService` | `GetTargetPositionsAsync`, `PreviewTargetAsync`, `SetTargetsAsync`, `CopyAsync` |
| `ConfigParametersController` | `GET target-positions`, `POST target-preview`, `PUT {id}/targets`, `POST copy` |

**Quyết định phát sinh:**

1. **`ReadTargets` TÁCH HẲN khỏi `ReadAll`**, không thêm cờ. `ReadAll` có 3 consumer khoá map theo `(sector, key)` — chính chỗ này đã gây lỗi CRITICAL ở §10.4. Cùng một hàm phục vụ hai hợp đồng ngược nhau ("không bao giờ trùng" vs "trùng là chuyện thường") thì sớm muộn cũng có người gọi nhầm.
2. **Xem thử = mô phỏng thật, và cũng chính là luật chặn Đ4.** Ghi giá trị vào bản sao file thật rồi **đọc lại ngay**; đọc lại không ra đúng đoạn vừa ghi nghĩa là đích không còn trỏ đúng phần tử → từ chối lưu. Một phép thử trả lời cả hai câu hỏi, không có luật nào phải viết riêng nên cũng không có luật nào lệch nhau.
3. **Mẫu bắt buộc chứa `{value}`.** Mẫu không có nó sẽ ghi cùng một đoạn chữ bất kể giá trị — đó là một hằng số nằm nhầm chỗ.
4. **Lưu đích là thay nguyên bộ, xoá cứng bản cũ.** Không có gì tham chiếu tới một dòng đích, nên hồi sinh tombstone chỉ để lại rác.
5. **Chép (R3) BỎ QUA cái đích đã có, không đè.** Tính cả bản đã xoá mềm: index unique một phần vẫn phủ chúng khi hồi sinh, và âm thầm dựng lại khai báo mà ai đó đã gỡ thì không còn là "chép". **Không chép giá trị** — giá trị thuộc về trạm/máy, mang sang là áp cấu hình của chuyền này lên chuyền khác.
6. **Không thêm permission mới** — `software.package.manage` / `software.read` như M2 của GAP-1B.

### 12.2 Frontend

- `ConfigTargetsModal` (mới) — bảng `File | Vị trí | Ghi thành | Ngăn cách | Kết quả`. Chọn vị trí từ danh sách server trả (phần tử mảng hiện kèm điều kiện khớp), gõ mẫu/ngăn cách, bấm **Kiểm tra** để thấy đúng đoạn chữ sẽ ghi hoặc lý do bị từ chối.
- `ConfigParametersModal` — thêm cột **Đích phụ** (chỉ mở được sau khi tham số đã lưu, vì đích treo vào tham số) và khối **Chép khai báo từ package khác**.
- i18n đủ 3 ngôn ngữ: `software.parameters.col_targets/copy_*`, `software.targets.*`.

### 12.3 Đã verify

- `dotnet test` **855/855**; `ConfigParameterServiceTests` +14: liệt kê phần tử mảng, xem thử ra đúng chữ, chấp nhận khớp tiền tố trên chính trường bị ghi, **từ chối** mẫu phá vỡ điều kiện khớp của chính nó (cả `StartsWith` lẫn `Equals`), glob khớp đúng 1 file / không khớp thì báo, lưu rồi đọc lại qua JSON đã lưu, một đích hỏng thì **từ chối cả bộ**, mẫu thiếu `{value}` bị chặn, chép mang theo đích, chép bỏ qua cái đã có, chép không mang giá trị.
- **Postgres thật:** projection collection lồng (`ConfigParameters` → `Targets`), `Include(p => p.Targets)` và query chép có `IgnoreQueryFilters` đều dịch chạy được (test tạm, đã xoá).
- FE: `tsc` sạch, `eslint` sạch, `npm run build` thành công, **126/126** test.

---

## 13. M3 — đã triển khai (2026-08-07)

**Trạng thái:** 863/863 test backend + 126/126 test FE xanh, FE build/typecheck/lint sạch, `MProject.Api` compile sạch. **CHƯA commit, CHƯA restart backend, CHƯA verify browser.**

### 13.1 Clone mang cấu hình sang (M3a)

`ClonePackageAsync` gọi `ConfigParameterService.CopyForClonedPackageAsync`: chép `ConfigParameter` + `ConfigParameterTarget` + `ConfigValueOverride` **Active**. `CopyAsync` (M2b, người dùng bấm) và bản clone dùng chung một lõi, khác nhau đúng cờ `copyValues`.

**Quyết định phát sinh:**

1. **`BaseValueAtCreation` chép NGUYÊN, KHÔNG tính lại** — *ngược với §6 "Quy tắc khi chép ở M3"*. Lý do: version clone dùng **đúng các blob file cũ**, và **không chép assignment** nên không có lớp recipe; vậy mọi lớp bên dưới mỗi giá trị y hệt lúc trước ⇒ ảnh chụp cũ đúng **theo cấu tạo**. Tính lại thì tệ hơn: nó sẽ **âm thầm hồi sinh** một giá trị vốn đã Stale, tức áp lại thứ chưa ai xác nhận lại. Cái §6 lo (file mới khác nội dung) đã được chính luật "tham số nào wizard đổi thì không chép override của nó" xử lý.
2. **`Resource` mới cho mỗi giá trị chép**, không dùng lại của bản gốc (§6 ghi "giữ nguyên ResourceId"). Hai giá trị dùng chung một hàng ACL thì cấp quyền cho cái này lại phủ luôn cái kia.
3. **Chỉ chép Active.** Draft/PendingApproval chưa được duyệt cho chương trình cũ thì càng chưa được duyệt cho chương trình mới; Stale là giá trị mà lớp dưới đã dịch chuyển khỏi.
4. `Scope` và id của nó đi **thành cặp, không tách** — check constraint `CK_ConfigValueOverrides_ScopeMatchesId`.

### 13.2 R6 — áp cho mọi chương trình cùng model trên máy (M3b)

Cờ `ApplyToSameModelPrograms` ở scope **Máy**. Khớp tham số theo `(TargetRelativePath, Sector, Key)` — bộ ba unique trong một package và clone mang sang nguyên vẹn, nên đó chính là nghĩa của "cùng một thiết lập" sau khi một chương trình bị tách làm bốn. Chỉ đụng package **đang gán cho trạm của máy đó** và **cùng ModelId**.

**Kiểm hết rồi mới ghi**: một chương trình anh em không nhận được giá trị thì **dừng cả lượt**, vì áp trúng 3/4 chương trình đúng là hỏng hóc mà R6 sinh ra để chặn.

### 13.3 R5 — cố ý KHÔNG làm

R5 ghi: *"clone không mang `ConfigBaseline` sang ⇒ package mới mất lưới kiểm cấu hình"*. **Kiểm chứng lại thì tiền đề sai.** `ConfigBaseline` gắn `ModelId`, không gắn package, và `ConfigBaselineValidator` chọn nó bằng `Where(b => b.ModelId == modelId && Status == Active).FirstOrDefaultAsync()`. Package clone cho **cùng model** vẫn được đúng baseline đó phủ; clone sang **model khác** thì baseline của model cũ vốn không nên áp.

Tệ hơn: chép sang sẽ tạo **baseline Active thứ hai cho cùng một model**, mà truy vấn chọn bằng `FirstOrDefaultAsync` **không có `OrderBy`** ⇒ kiểm cấu hình trở nên **không tất định**. Làm R5 là tự tay tạo lỗi.

---

## 14. M4 — đã triển khai (2026-08-07)

**Trạng thái:** 869/869 test backend xanh, `MProject.Api` compile sạch. **Backend-only** — bước "đặt giá trị" trên màn hình là của wizard M5. **CHƯA commit, CHƯA restart backend.**

`POST /config-parameters/apply-values` → `ConfigParameterService.ApplyValuesAsync`.

### 14.1 Cách chạy

1. Chặn nếu version **không phải Draft** (R7).
2. Kiểm tham số thuộc đúng package + `ConfigValueValidator` theo `DataType`/`Min`/`Max`/`AllowedValues`.
3. Dựng **kế hoạch ghi**: mỗi tham số → đích chính + từng đích phụ (đã áp `ValueTemplate`/`ListSeparator`), đường dẫn resolve qua `ConfigTargetPaths` (glob phải khớp đúng 1 file).
4. Áp **toàn bộ trong bộ nhớ** trước, gom theo file, mỗi file tải nội dung đúng một lần.
5. **Có bất kỳ vấn đề nào ⇒ ném lỗi, không ghi gì cả** (R2).
6. Chỉ khi sạch mới: upload blob mới, trỏ `SoftwareFile`, tăng/giảm ref-count, **một `SaveChanges` cho tất cả các file**.

### 14.2 Quyết định phát sinh

1. **Không đụng `BlobGcService`.** Guard `SoftwareFiles.Any(sf => sf.BlobSha256 == ... && !sf.IsDeleted)` đã có sẵn ở **cả 3 vị trí**, nên blob mới được bảo vệ ngay khi `SoftwareFile` trỏ vào nó. Điều kiện là tạo Blob và trỏ file **trong cùng một `SaveChanges`** — đã làm vậy. Bẫy §6 nêu vẫn đúng, chỉ là nó tự thoả.
2. **Thứ tự giảm ref-count an toàn.** `DecrementBlobReferenceAsync` chạy ngay (ExecuteUpdate) trong khi `SoftwareFile` còn đang trỏ blob cũ. Nếu BlobGc chạy đúng lúc đó, guard của nó dựa trên **`SoftwareFiles`** chứ không dựa trên ref-count, nên blob cũ vẫn được che. Verify bằng chạy thật trên Postgres.
3. **Từ chối hai tham số nhắm cùng một vị trí.** Khoá nhận dạng gồm cả `ElementMatch` (nên hai dòng `CheckLogConfigs` vẫn hợp lệ). Không chặn thì thứ tự ghi quyết định giá trị và bên thua sai âm thầm.
4. **`DryRun`** dựng và kiểm toàn bộ kế hoạch rồi trả bảng `cũ → mới` mà không ghi gì — chính là bảng xem trước bước 3 của wizard M5.
5. **Không thêm permission** — `software.package.manage`.

### 14.3 Đã verify

- `dotnet test` **869/869** (+6): ghi đích chính + đích phụ trong một lượt và **không đụng phần tử anh em**; một đích phụ không resolve thì **file giữ nguyên byte** (R2); từ chối khi version đã Released; **chạy lại được** trên cùng một Draft (R7); `DryRun` báo `cũ → mới` mà file không đổi; từ chối hai tham số tranh một vị trí.
- **Postgres thật:** chạy trọn `ApplyValuesAsync` trong transaction rollback — file trỏ blob mới, blob mới ref-count 1, blob cũ về 0, nội dung ghi đúng cả đích phụ. Đây là chỗ `ExecuteUpdate` trộn với thay đổi đang chờ trong change-tracker, thứ InMemory che mất. DB không còn dấu vết.

### 14.4 Còn nợ

- Nếu `SaveChanges` hỏng sau khi đã upload, object mới nằm lại trong storage mà không có hàng `Blob` nào trỏ tới. Rò dung lượng, không sai dữ liệu; cùng kiểu với `ConfigRenderService` sẵn có. Chưa xử lý.

---

## 9b. Việc ngoài lề phát hiện khi khảo sát

Cờ `SoftwarePackage.AutoRemoveOnUnassign` **không được đọc ở đâu để quyết định gỡ**. Grep toàn bộ `MProject.Application/Services`, `MProjectAgent`, `MProjectLauncher` chỉ thấy nó được lưu, trả ra DTO và ghi audit trong `SoftwarePackageService`. `DeactivateAsync` gọi `CreateUninstallJobsAsync` vô điều kiện. Modal tạo/sửa package vẫn cho tick, nên người dùng có thể tưởng bỏ tick thì giữ lại chương trình cũ, thực tế vẫn gỡ.

Không thuộc phạm vi GAP-1C. Xử lý riêng: hoặc nối cờ vào logic, hoặc bỏ khỏi form.
