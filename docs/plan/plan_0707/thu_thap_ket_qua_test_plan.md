# Plan: Thu thập kết quả test (Ưu tiên 1)

> Kế hoạch triển khai cụ thể cho **Ưu tiên 1 — Test Result Collection**,
> nền móng để MProject leo từ "Deployment Console" lên "Test Operations Platform".
> Ngày: 2026-07-07 · Trạng thái: DRAFT, chờ chốt các "Quyết định cần chốt" ở cuối.

---

## 0. TL;DR

- **Mục tiêu Ưu tiên 1**: đưa dữ liệu kết quả test (đang nằm rải rác trên từng máy) về
  backend tập trung, đủ để dựng **dashboard yield thời gian thực** (FPY, fail rate,
  Pareto lỗi theo station/model/ca).
- **Chiến lược 2 pha**, vì dữ liệu tồn tại ở 2 mức:
  - **Pha 1 (MVP, giá trị nhanh)**: thu **counter tổng hợp** từ registry
    `HKCU\Software\CPEI_MFG\Unit{n}` (Pass/Fail/RJ45/ContinueFail/GoldenVerify).
    Đủ dựng yield & fleet health, gần như **0 rủi ro với app test legacy** (chỉ đọc).
  - **Pha 2 (traceability)**: thu **record per-serial** (mỗi unit + kết quả + error code).
    Nguồn = log file trong `LogDir` của app, hoặc — tốt hơn — app UIStore-parity mới tự
    emit record. Đây là nền cho Ưu tiên 3 (truy vết theo serial).
- **Nguyên tắc kiến trúc**: KHÔNG nhồi vào heartbeat. Thêm một **endpoint batched riêng**
  theo đúng mẫu đã có của `agent/v1/inventory` + một agent loop riêng.
- **Ràng buộc coding**: mỗi lát cắt ≤ 5 files / ≤ 150 LOC (theo `docs/skills/coding_rule.md`).
  Plan được chia nhỏ để từng PR vừa budget.

---

## 1. Phát hiện then chốt về nguồn dữ liệu

Khảo sát code thực tế (không phải giả định):

| Nguồn | Ở đâu | Chứa gì | Granularity | Độ khó thu |
|---|---|---|---|---|
| **Registry** `HKCU\Software\CPEI_MFG\Unit{n}` | Mỗi máy, per test-slot | `Counter.PassCount/FailCount/RJ45`, `ContinueFaile.COUNT/ERROR/MAC`, `GoldenVerify` timestamps | **Tổng hợp** (counter cộng dồn) | **Thấp** — chỉ đọc key, agent đã chạy trên máy |
| **Log file** | `FtuConfig.LogDir` trên máy (app `LogService` ghi) | Log per-unit, có serial/barcode, kết quả từng bước | **Per-serial** | **Cao** — format app-specific, phải parse |
| **SFC/Shipping** | App `ShippingService` đã bắn lên (SFTP) | Kết quả shipping | Per-serial | N/A (đã có hệ khác nhận) |

**Hệ quả thiết kế:**
- Registry cho **fleet-level yield** ngay, rủi ro thấp → làm Pha 1.
- Log file cho **per-serial traceability** nhưng format phụ thuộc từng app test →
  Pha 2, và **nên** để app mới (UIStore-parity rebuild) emit record chuẩn thay vì parse
  log legacy dài hạn.
- MProject **chỉ đọc**, tuyệt đối không ghi/xoá registry `CPEI_MFG`
  (xem [[reference-cpei-mfg-registry-state]] — counter phải giữ nguyên qua update).

---

## 2. Kiến trúc & luồng dữ liệu

```
┌─────────────── Trạm test (đã có Agent) ───────────────┐
│  App test (CPEI_MFG) ──ghi──► Registry HKCU\CPEI_MFG   │
│                          └──ghi──► LogDir (per-serial) │
│                                                        │
│  Agent (BackgroundService)                             │
│   ├─ heartbeat loop   (đã có — KHÔNG đụng shape)        │
│   ├─ poll loop        (đã có)                           │
│   ├─ inventory loop   (đã có — mẫu để nhân bản)         │
│   └─ TEST-METRICS loop (MỚI) ──► đọc registry, batch    │
└───────────────────────────┬───────────────────────────┘
                            │ POST agent/v1/test-metrics  (X-Agent-Token)
                            ▼
┌──────────────────── Backend ──────────────────────────┐
│ AgentController.ReportTestMetrics                       │
│   → AgentService (validate agent→computer→station)      │
│   → lưu StationTestCounter (snapshot mới nhất)          │
│   → (Pha 2) TestRecord append-only                      │
│   DbContext + EF migration (chạy tay: dotnet ef ...)    │
└───────────────────────────┬───────────────────────────┘
                            │ REST (JWT, RBAC quality.read)
                            ▼
┌──────────────────── Frontend ─────────────────────────┐
│ Trang "Test Results / Yield" (MỚI)                     │
│   stat strip: FPY · Fail rate · Units today            │
│   bảng theo station/model · Pareto error code          │
└────────────────────────────────────────────────────────┘
```

Transport dùng lại **nguyên mẫu `inventory`**:
- `ServerClient.ReportInventoryAsync` → thêm `ReportTestMetricsAsync` (cùng pattern
  `PostAsync<TReq,TResp>("agent/v1/...")`).
- `RunInventoryLoopAsync` → nhân bản thành loop test-metrics với interval riêng
  (`Agent:TestMetricsIntervalSeconds`, mặc định ví dụ 60s; 0 = tắt để rollout an toàn).

---

## 3. Data model đề xuất

### Pha 1 — Snapshot counter (upsert, không bùng nổ dung lượng)

`StationTestCounter` (1 dòng / (Computer × slot), cập nhật đè):
```
ComputerId        Guid   (FK Computer)
StationResourceId Guid?  (resolve từ assignment hiện tại)
SlotIndex         int    (Unit{n})
PassCount         long
FailCount         long
Rj45Count         int
ContinueFailCount int
ContinueFailError string?     (ERROR_KEY)
LastGoldenGoodAt  DateTimeOffset?
LastGoldenBadAt   DateTimeOffset?
CollectedAt       DateTimeOffset
UpdatedAt         DateTimeOffset
```
> Đủ để tính yield hiện thời, phát hiện ContinueFail chạm ngưỡng, GoldenVerify quá hạn.
> Vì là upsert theo slot nên số dòng = số trạm × số slot → **nhỏ, an toàn**.

### Pha 2 — Per-serial (append-only, time-series)

`TestRecord` (mỗi unit test một dòng):
```
Id            Guid
ComputerId    Guid
StationResourceId Guid?
ModelId       Guid?         (product/model đang test)
VersionId     Guid?         (software version đang chạy — nối với deployment đã có)
SerialNumber  string        (index)
Mac           string?
Result        enum {Pass, Fail}
ErrorCode     string?
StartedAt / FinishedAt  DateTimeOffset
Shift         string?       (tính từ FinishedAt)
OperatorUserId Guid?        (nếu có login 2 cấp — F14)
RawRef        string?       (đường dẫn/hash log gốc để trace)
```
Index: `(StationResourceId, FinishedAt)`, `(ModelId, FinishedAt)`, `(SerialNumber)`.

> **Lưu ý dung lượng**: đây là bảng time-series lớn. Ngay từ đầu:
> tách bảng riêng, chỉ index cần thiết, và thêm **retention/partition** theo tháng
> ở milestone sau (đừng để chung nhịp với bảng OLTP quản trị).

---

## 4. Phân rã công việc theo milestone

Mỗi milestone (M) là một nhóm PR nhỏ vừa complexity budget. Thứ tự tôn trọng phụ thuộc.

### PHA 1 — Counter → Yield dashboard (mục tiêu: nhìn thấy yield thật)

**M1 · Backend: entity + migration + endpoint (chưa có UI)**
- Thêm `StationTestCounter` vào `Domain/Entities/Software` (hoặc thư mục `Quality` mới nếu gọn hơn).
- `DBContext`: thêm `DbSet<StationTestCounter>` + cấu hình key (Computer+Slot unique).
- Model DTO `ReportTestMetricsRequest/Response` trong `Application/Models/AgentModels.cs`.
- `IAgentService` + `AgentService.ReportTestMetricsAsync(agentId, request)`:
  validate agent→computer (mẫu như `RecordHeartbeatAsync`), resolve station từ assignment,
  upsert counter.
- `AgentController`: action `POST agent/v1/test-metrics` (auth agent-token, mẫu như inventory).
- Migration: `dotnet ef migrations add AddStationTestCounter` → **chạy tay**
  `dotnet ef database update` (backend KHÔNG auto-migrate — xem [[project_computer_fleet_health]]).
- Test: unit test cho `ReportTestMetricsAsync` (validate + upsert), theo mẫu test AgentService hiện có.
- *Ước lượng*: ~2 PR (entity+migration; service+controller+test). Mỗi PR ≤ budget.

**M2 · Agent: đọc registry + gửi batch**
- `RegistryReader` mới (agent-side, dùng `Microsoft.Win32.Registry.CurrentUser`,
  chỉ đọc `Software\CPEI_MFG\Unit{n}`; enumerate slot; best-effort, không bao giờ throw ra loop).
- Model `TestMetricsReport` trong `Models/AgentApiModels.cs`.
- `ServerClient.ReportTestMetricsAsync` (mẫu `ReportInventoryAsync`).
- `AgentWorker`: thêm `RunTestMetricsLoopAsync` (nhân bản `RunInventoryLoopAsync`) +
  option `Agent:TestMetricsIntervalSeconds` (mặc định 0 = **tắt**, bật dần khi rollout).
- Test: `RegistryReader` parse đúng key (mock registry hoặc tách pure-parse), theo mẫu
  `AgentStatusProviderTests`.
- *Ước lượng*: ~2 PR. Gate mặc định OFF → deploy an toàn, không ảnh hưởng trạm đang chạy.

**M3 · Backend: API đọc yield cho FE**
- Endpoint `GET api/quality/yield` (JWT, RBAC `quality.read`): tổng hợp từ
  `StationTestCounter` → FPY, fail rate, theo station/model, top error code.
- RBAC: thêm permission `quality.read` (mẫu như `computer.read`, gán cho Viewer/Member —
  xem [[reference-rbac-roles-permissions]]). **Cần rebuild+restart BE để re-seed.**
- *Ước lượng*: 1–2 PR.

**M4 · Frontend: trang Test Results / Yield (v0)**
- Trang mới theo convention redesign đã có (xem [[project_frontend_redesign_convention]]):
  full-bleed, stat strip (FPY · Fail rate · Units), bảng native theo station/model,
  Pareto error code; 1 accent `#465fff`; i18n en/vi; dark-mode; tabular-nums.
- Nav: thêm mục "Test Results" (hoặc gộp nhóm "Quality").
- Reuse component/pattern từ Computers/AgentReleases (stat-strip + native table đã có sẵn).
- *Ước lượng*: 2–3 PR (list/stat-strip; drill-down theo station; i18n + polish).

**➡️ Sau M4: đã có yield thời gian thực. Đây là mốc giá trị đầu tiên, nên demo/nghiệm thu ở đây.**

### PHA 2 — Per-serial → Traceability (nền cho Ưu tiên 3)

**M5 · Backend: `TestRecord` + ingestion batched**
- Entity `TestRecord` (mục 3) + DbSet + index + migration (chạy tay).
- Endpoint `POST agent/v1/test-records` (batch, idempotent theo (Serial+FinishedAt) để
  agent gửi lại an toàn khi mất mạng).
- *Lưu ý*: thiết kế nhận **batch** ngay từ đầu (agent gom rồi gửi), không 1-record-1-request.

**M6 · Agent: nguồn per-serial**
- **Quyết định nguồn** (xem Quyết định cần chốt #2):
  - (a) Parse log `LogDir` của CPEI_MFG — nhanh có dữ liệu nhưng **giòn** theo format app.
  - (b) App UIStore-parity mới emit file record chuẩn (JSON/CSV) → agent đọc.
    **Khuyến nghị dài hạn**, ăn khớp với [[project_uistore_parity_rebuild]].
- Agent theo-dõi file mới, parse, đẩy batch, đánh dấu đã gửi (tránh gửi trùng).

**M7 · Frontend: tra cứu theo serial + báo cáo**
- Ô tìm theo serial → timeline test của unit (trạm/version/ca/operator).
- Export báo cáo (CSV) cho containment/audit khách hàng.

### PHA 1.5 (song song, tận dụng dữ liệu M1–M2) — Alerting (Ưu tiên 2 khởi động sớm)
- Từ `StationTestCounter` + `ComputerRuntimeStatus` (đã có `RestartCountInWindow`, disk %):
  cảnh báo offline / crash lặp / disk đầy / ContinueFail chạm ngưỡng / GoldenVerify quá 12h.
- Kênh: tận dụng `chart-grafana.json` sẵn có → **Grafana alerting** là đường tắt,
  không cần code kênh gửi trong app giai đoạn đầu.

---

## 5. Danh sách file dự kiến chạm (để ước lượng & review)

**Backend (Pha 1):**
- `MProject.Domain/Entities/Software/StationTestCounter.cs` (mới)
- `MProject.Infrastructure/DBContext.cs` (thêm DbSet + config)
- `MProject.Infrastructure/Migrations/*_AddStationTestCounter.cs` (sinh tự động)
- `MProject.Application/Models/AgentModels.cs` (+ DTO)
- `MProject.Application/Interface/Assets/IAgentService.cs` (+ method)
- `MProject.Application/Services/Assets/AgentService.cs` (+ ReportTestMetricsAsync)
- `MProject.Api/Controllers/Assets/AgentController.cs` (+ action)
- `MProject.Application/Services/...` quality read + RBAC seed (M3)

**Agent (Pha 1):**
- `MProjectAgent/Services/RegistryReader.cs` (mới)
- `MProjectAgent/Models/AgentApiModels.cs` (+ TestMetricsReport)
- `MProjectAgent/Services/ServerClient.cs` (+ ReportTestMetricsAsync)
- `MProjectAgent/Services/AgentWorker.cs` (+ loop)
- `MProjectAgent/Configuration/AgentOptions.cs` (+ TestMetricsIntervalSeconds)
- `MProjectAgent.Tests/RegistryReaderTests.cs` (mới)

**Frontend (Pha 1):**
- `MProjectFrontend/src/pages/Quality/*` (mới) + nav + i18n en/vi

---

## 6. Quyết định cần chốt (trước khi code)

1. **Đơn vị "Unit{n}" = gì trên trạm của bạn?** Là test-slot vật lý (1 máy test nhiều unit
   song song) hay 1 slot/máy? → quyết định `SlotIndex` có cần không, và cách hiển thị yield.
2. **Nguồn per-serial cho Pha 2**: parse log CPEI_MFG hiện tại (nhanh, giòn) hay chờ app
   UIStore-parity emit record chuẩn (bền, nhưng phụ thuộc tiến độ rebuild)? Khuyến nghị:
   Pha 1 làm ngay bằng registry; Pha 2 ưu tiên hướng app-emit.
3. **Định nghĩa yield**: FPY tính theo gì khi chỉ có counter cộng dồn? (Counter registry là
   cộng dồn, không phân biệt lần test đầu vs retest → FPY "thật" cần per-serial ở Pha 2;
   Pha 1 hiển thị **Pass rate cộng dồn** và ghi rõ nhãn để không gây hiểu nhầm.)
4. **Interval & tải**: 60s/heartbeat có đủ tươi không, hay cần sát thời gian thực hơn?
   (Ảnh hưởng tải DB khi nhân số trạm.)
5. **Ca làm việc (shift)**: định nghĩa mốc ca (VD 08:00/20:00?) để nhóm yield theo ca.
6. **RBAC**: `quality.read` gán cho role nào? (đề xuất Viewer xem read-only, mẫu `computer.read`.)
7. **Retention**: giữ TestRecord bao lâu (yêu cầu audit khách hàng thường 6–12 tháng)?

---

## 7. Tiêu chí hoàn thành (Definition of Done)

**Pha 1 done khi:**
- Bật `Agent:TestMetricsIntervalSeconds` trên ≥1 trạm thật → counter về DB đúng, không
  ảnh hưởng app test đang chạy (registry chỉ-đọc, đã verify).
- Trang Yield hiển thị FPY/pass-rate/fail-rate theo station/model, Pareto error code,
  cập nhật theo interval.
- Alerting (Pha 1.5) bắn được ít nhất: offline, disk đầy, ContinueFail ngưỡng.
- Có unit test cho service + registry reader; build + test xanh; migration đã apply.

**Pha 2 done khi:**
- Tra 1 serial → ra đầy đủ trạm/version/ca/kết quả; export CSV containment chạy được.

---

## 8. Non-goals (giai đoạn này — tránh phình scope)

- KHÔNG thu số đo parametric/SPC (điện áp, RSSI…) — đó là "Lớp 3", làm sau khi có nền.
- KHÔNG AI/anomaly detection — cần khối lượng dữ liệu tích lũy trước.
- KHÔNG tích hợp trực tiếp SFC/MES Foxconn ở pha này (app đã tự shipping; MProject chỉ quan sát).
- KHÔNG ghi/sửa registry hay can thiệp hành vi app test.

---

## 9. Rủi ro & giảm thiểu

| Rủi ro | Giảm thiểu |
|---|---|
| Đọc registry sai/khác nhau giữa app test | `RegistryReader` best-effort, null-safe, không throw ra loop; verify trên trạm thật trước khi bật đại trà |
| Tải DB tăng khi nhân trạm | Pha 1 dùng upsert (số dòng cố định); interval cấu hình; batch |
| Format log per-serial thay đổi (Pha 2) | Ưu tiên app-emit chuẩn thay vì parse; idempotent ingestion |
| FPY hiểu nhầm từ counter cộng dồn | Ghi rõ nhãn "Pass rate (cumulative)" ở Pha 1; FPY thật ở Pha 2 |
| Migration không tự chạy | Ghi rõ trong runbook: `dotnet ef database update` sau deploy BE |
| Ảnh hưởng trạm đang chạy | Gate mặc định OFF; bật dần; chỉ-đọc; verify trạm thật |

---

## 10. Bước tiếp theo ngay

1. Trả lời 7 "Quyết định cần chốt" ở mục 6 (đặc biệt #1 slot, #2 nguồn per-serial, #6 RBAC).
2. Bắt đầu **M1** (backend entity + endpoint) — lát cắt nhỏ, không đụng luồng đang chạy.
3. Verify M1+M2 trên **1 trạm thật** với gate OFF→ON trước khi mở rộng.

> Liên quan: [[project_uistore_parity_rebuild]] · [[project_computer_fleet_health]] ·
> [[reference-cpei-mfg-registry-state]] · [[reference-rbac-roles-permissions]] ·
> [[project_frontend_redesign_convention]]
