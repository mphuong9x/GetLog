# Bộ prompt giao Fable 5 — làm lại module Software (thay UIStore)

> Dùng kèm `docs/uistore_parity_spec.md` (nguồn chân lý: checklist F1–F20, glossary cũ→mới, blast-radius 8 seam, 7 quyết định đã chốt ở Part D.1).
>
> **Cách dùng:**
> - Chạy **mỗi pha trong 1 session/hội thoại riêng** (spec dài + code sẽ tràn context). File `uistore_parity_spec.md` là "bộ nhớ" nối các pha.
> - **Pha B bật plan mode** và duyệt plan trước khi cho code.
> - **Pha C lặp lại** cho từng lát cắt.
> - Nếu Fable đọc lướt ở Pha A, yêu cầu nó *trích dẫn file:line* cho từng feature để buộc đọc thật.

---

## PROMPT — PHA A (trích & kiểm chứng spec; CHƯA code)

```
Bối cảnh: tôi sắp làm lại chức năng tạo/quản lý/phân phối phần mềm test trong MProject
(thay UIStore cũ). ĐỌC TRƯỚC: docs/uistore_parity_spec.md — nguồn chân lý (checklist
F1–F20, glossary cũ→mới, blast-radius, 7 QUYẾT ĐỊNH ĐÃ CHỐT ở Part D.1). Tôn trọng
docs/skills/coding_rule.md.

NHIỆM VỤ PHA A — CHƯA CODE, CHƯA thiết kế kiến trúc:
1) Đọc source UIStore cũ ở Old_program/UIStore/UiStore (CHỈ .cs/.xaml; BỎ QUA
   bin/obj/packages/.vs và mọi DLL) và Old_program/Upload (phía publish/đóng gói).
2) KHẢO SÁT MỨC QUAN HỆ (không đi sâu logic) các project CÒN LẠI trong Old_program để
   biết UIStore đứng đâu trong hệ cũ và ranh giới scope:
   - Old_program/AppUpdater — self-update UiStore.exe (↔ F17, hệ mới thay bằng GAP-4).
   - Old_program/{FTU Program(CPEI_MFG), FcdDownload, UiTest} = TEST ENGINE / PAYLOAD.
     CHỈ cần hiểu HỢP ĐỒNG distributor↔payload mà UIStore phải bảo toàn: entry-point,
     relative-path/WorkingDirectory (vd CPEI_MFG đọc ..\Config từ thư mục exe), file
     config overridable, và HKCU Software\CPEI_MFG\Unit{n} (KHÔNG đụng). TUYỆT ĐỐI
     KHÔNG reverse-engineer / KHÔNG viết lại logic test.
3) Với TỪNG mục F1–F20 trong spec, TỰ KIỂM CHỨNG bằng cách trích dẫn file:line thật ở
   code cũ; sửa/bổ sung nếu spec sai hoặc thiếu. Phát hiện feature UIStore chưa có trong
   checklist → thêm F21, F22…
4) Đọc 2 fixture gói: Sample_Software (có cấu trúc: Config/bin/FTU-python) và Cpp_Software
   (chương trình C++ phi cấu trúc) — xác nhận Part B mô tả đúng, ĐẶC BIỆT yêu cầu ingest
   STRUCTURE-AGNOSTIC: nhận cây thư mục bất kỳ, người publish chọn entry-point + icon +
   overridable-path THỦ CÔNG cho cả hai loại.

OUTPUT: ghi kết quả vào docs/uistore_parity_spec_verified.md — mỗi feature kèm file:line,
KÈM sơ đồ quan hệ Old_program + ranh giới distributor↔payload (bước 2); cuối file liệt kê
"điểm nghi ngờ / cần tôi xác nhận". KHÔNG viết code, KHÔNG đề xuất kiến trúc ở pha này.
```

---

## PROMPT — PHA B (đánh giá code hiện có → plan; BẬT PLAN MODE)

```
ĐỌC: docs/uistore_parity_spec.md (đặc biệt Part C blast-radius ①–⑧ + Part D 7 quyết định
đã chốt) + docs/uistore_parity_spec_verified.md (kết quả Pha A) + docs/skills/coding_rule.md.

BƯỚC B0 — ĐÁNH GIÁ (chưa code): review code software mới hiện có trong MProject —
9 entity ở Domain/Entities/Software, 18 service ở Application/Services/Software, 7
controller ở Api/Controllers/Software, FE (Software/OverrideFiles/ConfigBaselines/
Installation), và phía Agent (MProjectAgent). Chấm sức khoẻ từng thành phần và ra
BẢNG GIỮ / SỬA / THAY, mỗi dòng kèm LÝ DO, đối chiếu seam ①–⑧. Nguyên tắc (Part D.0):
code dùng được thì GIỮ/SỬA; chỉ THAY khi tối ưu hơn HẲN — không mặc định xoá.

BƯỚC B1 — THIẾT KẾ (bật plan mode): từ bảng B0, plan làm lại chức năng đạt tối thiểu
parity F1–F20, ÁP DỤNG ĐÚNG 7 QUYẾT ĐỊNH:
  #1 Agent contract ĐƯỢC ĐỔI THOẢI MÁI — hệ mới CHƯA lên sản xuất thật, chỉ cần
     rebuild+redeploy vài agent test; KHÔNG cần v1/v2 song song hay back-compat lúc này.
     Gói phần agent thành lát cắt riêng cho dễ phát hành lại.
  #2 GIỮ blob content-addressed SHA-256 + delta download (Blob/BlobCache/BlobGc).
  #3 Ingest STRUCTURE-AGNOSTIC: nhận cây thư mục bất kỳ (Sample_Software & Cpp_Software),
     chọn entry-point/icon/overridable-path THỦ CÔNG; MỞ RỘNG EntryPointMode cho exe
     opaque (native C++, không HealthCheckUrl).
  #4 GIỮ 3 khái niệm OverrideFile + OverridablePaths + ConfigBaseline (giữ validation +
     drift detection); ĐƯỢC đề xuất tinh gọn nếu phức tạp thừa, nhưng KHÔNG bỏ năng lực.
  #5 CHỈ RBAC web — BỎ login operator + allow/deny theo PcName tại trạm (F13/F14 KHÔNG
     port); thay bằng gán theo Station/Computer resource.
  #6 Cờ per-app AutoOpen/AutoUpdate/AutoRemove/CloseAndClear → map vào assignment/policy
     và ĐƯA LÊN UI web (mở rộng LaunchPolicy + AutoRemoveOnUnassign hiện có).
  #7 GIỮ approvals cho gán software / tạo override.
NÂNG CẤP KHÔNG PORT 1:1 (Part D.0): parity = tương đương CHỨC NĂNG; ưu tiên pattern
web-native khi tốt hơn, đừng dựng lại WinForms/WPF trên web.

Trả plan theo LÁT CẮT DỌC (BE→Agent→FE), mỗi lát ghi rõ: đụng seam nào, GIỮ contract hay
REDESIGN, nâng cấp gì, tiêu chí "done", verify bằng fixture nào. KHÔNG code ở pha này —
trình plan cho tôi duyệt trước.
```

---

## PROMPT — PHA C (thực thi; lặp cho từng lát cắt)

```
Làm lát cắt <TÊN LÁT CẮT> theo plan đã duyệt (docs/uistore_parity_spec.md + plan Pha B).
Chỉ làm ĐÚNG lát này, end-to-end. Theo docs/skills/coding_rule.md.

Sau khi code:
- build + chạy test.
- VERIFY bằng gói thật: Sample_Software (có cấu trúc) và/hoặc Cpp_Software (C++ phi cấu
  trúc) tuỳ lát — MÔ TẢ kết quả quan sát được, KHÔNG chỉ nói "đã xong".
- Đối chiếu mục parity F… tương ứng trong spec.
- Nếu lát này chạm Agent: verify CẢ phía Agent (rebuild + chạy thử), không chỉ backend.

Nếu lệch spec hoặc lệch 7 quyết định ở Part D.1 → DỪNG và hỏi tôi trước khi đi tiếp.
```

---

## Ghi chú vận hành

- **Thứ tự lát cắt gợi ý** (Fable sẽ chốt ở Pha B): (1) ingest structure-agnostic + publish version (Sample_Software & Cpp_Software) → (2) assignment + deploy job xuống agent → (3) override/baseline → (4) cờ per-app lên UI → (5) approvals → (6) dọn dẹp/agent contract.
- **Fixture verify:** `Sample_Software/` cho luồng có cấu trúc; `Cpp_Software/` cho luồng phi cấu trúc/native.
- **Ranh giới an toàn:** dù agent được đổi thoải mái, vẫn không tự ý phá installation/approvals/rollback đang chạy trừ khi lát cắt nêu rõ và được duyệt.
