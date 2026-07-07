# Định hướng phát triển MProject

> Phân tích hiện trạng và đề xuất lộ trình phát triển hệ thống MProject —
> intranet nội bộ bộ phận TE (Test Engineering), Foxconn Việt Nam.
> Ngày: 2026-07-07

---

## Hiện trạng: MProject đang ở đâu

Nhìn vào domain model và các trang FE, hệ thống hiện tại giải quyết rất tốt bài toán
**"đưa đúng phần mềm test đến đúng trạm, một cách an toàn và có kiểm soát"**:

- **Deployment**: SoftwarePackage/Version, OverrideFile, ConfigBaseline, InstallationJob, StationAssignment, update window
- **Agent platform**: agent + launcher tray, self-update có rollback, AgentCommand
- **Fleet**: Computer/Station/Model, runtime status (app đang chạy, exit code, CPU/RAM/Disk telemetry)
- **Governance**: RBAC 4 role, Approval workflow nhiều bước, audit log
- **Con người/tổ chức**: Users, Teams/Departments, ProductGroup

Đây tương đương lớp **"Software Distribution + Station Management"** trong một hệ MES.
Nhưng có một điểm mù lớn: **hệ thống biết trạm chạy phần mềm gì, nhưng không biết
kết quả test ra sao**. Dữ liệu Pass/Fail/RJ45 counter, ContinueFail, GoldenVerify
hiện chỉ nằm trong registry `CPEI_MFG` cục bộ trên từng máy — không ai nhìn thấy tập trung.

---

## Định hướng chiến lược: từ "Deployment Console" → "Test Operations Platform"

Trong ngành, các nhà máy EMS lớn đều hội tụ về mô hình này (tham chiếu: Virinco **WATS**,
Keysight **PathWave Test Data Analytics**, các hệ SFC/MES nội bộ Foxconn).
Con đường tự nhiên của MProject là leo dần lên các lớp giá trị:

```
Lớp 4: AI / Predictive          (tương lai)
Lớp 3: Quality Analytics — yield, SPC, traceability
Lớp 2: Test Result Collection   ← ĐIỂM MÙ HIỆN TẠI
Lớp 1: Deployment + Fleet       ← ĐÃ CÓ, khá hoàn chỉnh
```

### Ưu tiên 1 — Thu thập kết quả test (nền móng cho mọi thứ khác)

Agent đã có mặt trên mọi trạm — đây là lợi thế lớn nhất, chi phí biên để thu thêm
dữ liệu rất thấp:

- **Test Result ingestion**: agent đọc registry `CPEI_MFG` (Counter Pass/Fail/RJ45,
  ContinueFail) hoặc parse log của app test, đẩy về backend theo heartbeat.
  Không cần sửa app test legacy.
- **Entity mới**: `TestRecord` (serial, station, model, phần mềm + version đang chạy,
  kết quả, error code, thời điểm, ca làm việc).
- **Dashboard yield**: FPY (First Pass Yield), retest rate, Pareto lỗi theo
  station/model/ca — đây chính là báo cáo mà TE phải làm tay hằng ngày ở hầu hết
  các nhà máy.

Giá trị tức thì: PE/TE nhìn thấy trạm nào yield thấp bất thường **theo thời gian thực**
thay vì đợi báo cáo cuối ca.

### Ưu tiên 2 — Alerting & vận hành chủ động (đã có trên roadmap, nên đẩy sớm)

- Cảnh báo khi: trạm offline, ContinueFail chạm ngưỡng, app test crash lặp
  (`RestartCountInWindow` đã có sẵn), disk sắp đầy, GoldenVerify quá hạn 12h.
- Kênh: email nội bộ / Teams / webhook. Có thể tận dụng `chart-grafana.json` đã có
  — Grafana alerting là đường tắt hợp lý.
- **Golden sample verification** nâng từ registry cục bộ thành quy trình trung tâm:
  lịch verify, trạng thái trên floor map, và (giai đoạn sau) **khóa trạm nếu quá hạn**
  — đây là yêu cầu chuẩn của khách hàng như Ubiquiti khi audit.

### Ưu tiên 3 — Traceability (truy vết theo serial)

Khi đã có TestRecord, ghép với dữ liệu deployment sẵn có sẽ trả lời được câu hỏi "vàng"
khi có sự cố chất lượng:

> *"Những unit nào đã được test bằng version X bị lỗi, ở trạm nào, ca nào, ai vận hành?"*

Hiện tại câu này không trả lời được; sau khi có, mỗi lần khách hàng yêu cầu
containment/recall, việc khoanh vùng từ vài ngày còn vài phút. Đây cũng là điểm khác biệt
lớn so với UIStore cũ — thứ để "bán" hệ thống lên cấp trên.

### Ưu tiên 4 — Hoàn thiện vòng đời trạm (fleet → asset management)

- **Floor map** (đã trong roadmap Computer Fleet Health): sơ đồ xưởng, mỗi trạm 1 ô màu
  theo trạng thái — chuẩn giao diện "Andon board" treo màn hình lớn ở line.
- **Calibration/maintenance tracking**: fixture, máy đo trên trạm có hạn hiệu chuẩn
  — nhắc hạn, chặn assignment nếu quá hạn. Các audit ISO/khách hàng luôn soi mục này.
- **OEE/utilization**: từ heartbeat + runtime status đã có, tính được uptime, thời gian
  test thực tế vs idle của từng trạm — số liệu để xin/điều chuyển máy.

### Ưu tiên 5 — Con người & quy trình

- **Skill/certification matrix**: operator nào được chứng nhận chạy model/station nào
  (đã có ModelUserManager làm mầm); cảnh báo khi người chưa cert đăng nhập trạm
  (F14 login 2 cấp đã có trong spec parity).
- **E-signature + audit trail** trên approval để đạt chuẩn audit khách hàng.
- Shift handover note gắn theo station.

### Đường dài — xu hướng 3–5 năm

1. **SPC/parametric data**: không chỉ pass/fail mà thu số đo (điện áp, công suất, RSSI…)
   → control chart, Cpk, phát hiện drift trước khi ra fail. Đây là bước lên "Lớp 3" thực thụ.
2. **AI trên dữ liệu đã thu**: anomaly detection yield/telemetry ("trạm 5 fail rate lệch 3σ
   so với các trạm cùng model"), predictive maintenance (disk/RAM trend), và một
   **assistant hỏi-đáp tự nhiên** trên dữ liệu test ("tại sao line B yield giảm hôm qua?")
   — rất khả thi khi dữ liệu đã tập trung.
3. **Tích hợp SFC/MES Foxconn**: đẩy kết quả test lên SFC thay vì app test tự gọi
   — MProject thành gateway chuẩn hóa, app test mới (UIStore parity rebuild đang làm)
   chỉ cần nói chuyện với agent.
4. **Canary/staged rollout**: deploy version mới cho 1–2 trạm, tự so yield trước/sau,
   đạt ngưỡng mới rollout cả line + auto-rollback (F-05 đã trong kế hoạch — nên gắn với
   dữ liệu yield thay vì chỉ exit code).

---

## Đề xuất lộ trình cụ thể

| Giai đoạn | Việc | Vì sao trước |
|---|---|---|
| Quý này | Test result ingestion qua agent + dashboard yield cơ bản; Alerting (offline/crash/disk) | Tận dụng agent sẵn có, giá trị nhìn thấy ngay, là nền của mọi thứ sau |
| Quý sau | Golden verify trung tâm + floor map/Andon; traceability theo serial | Nhu cầu audit khách hàng, dữ liệu đã có sau giai đoạn 1 |
| 6–12 tháng | Calibration tracking, skill matrix, OEE; canary rollout gắn yield | Hoàn thiện vòng quản lý trạm-người-quy trình |
| Dài hạn | SPC/parametric, AI anomaly + assistant, tích hợp SFC | Cần khối lượng dữ liệu tích lũy đủ lớn |

**Lưu ý kiến trúc**: dữ liệu test record là **time-series, append-only, khối lượng lớn**
— khác hẳn dữ liệu quản trị hiện tại. Nên tách bảng/schema riêng (thậm chí cân nhắc
partition theo tháng) ngay từ đầu để không kéo chậm phần OLTP, và thiết kế API ingestion
theo batch để agent gom gửi.

---

## Điểm mấu chốt

**Đừng xây thêm chiều rộng (thêm trang quản lý) mà hãy xây chiều sâu (dữ liệu kết quả test).**

Hạ tầng agent + RBAC + approval hiện tại đã đủ tốt để làm nền; thứ biến MProject từ
"công cụ thay UIStore" thành "hệ thống mà cả bộ phận TE phụ thuộc vào" chính là việc nó
trở thành nơi duy nhất trả lời được câu hỏi *"chất lượng test hôm nay thế nào và tại sao"*.
