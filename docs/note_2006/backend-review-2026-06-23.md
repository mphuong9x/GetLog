# Backend Code Review — GAP-4/5/6

**Ngày:** 2026-06-23
**Người review:** Claude Code (Opus 4.8)
**Repo:** `C:\dev\MProject` (branch `master`, ngang `origin/master`)

---

## Bối cảnh & phạm vi

- Repo thật là `C:\dev\MProject` (thư mục OneDrive không phải git repo). HEAD ngang `origin/master`,
  working tree chỉ có thay đổi frontend → **không có "diff backend"** theo nghĩa PR.
- **Phạm vi review:** 12 commit gần nhất = GAP-4/5/6 (AgentRelease/self-update, version metadata,
  uninstall/auto-remove), khoảng **~700 LOC source thật** (trừ migrations/tests).
- ⚠️ `docs/skills/review_rule.md` **không tồn tại trong repo** (chỉ được nhắc trong memory). Đã áp dụng
  nguyên tắc của nó từ memory: *tìm code không nên tồn tại; correctness + security trước; minimal safe change*.

**Đánh giá tổng thể:** code khá sạch, comment giải thích "tại sao" tốt, không over-engineer rõ rệt.
Luồng auto-remove (transaction + huỷ job cũ trước khi tạo Uninstall job) và các unique-index đều **đúng**.
Có **1 bug mất dữ liệu**, **1 field chết**, **1 lỗ atomicity**, còn lại là cleanup nhỏ.

---

## 🔴 P1 — Mất dữ liệu: sửa label version sẽ xoá sạch metadata BOM/FCD/FTU/FW/Region

**File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:101` + `:124`

`ApplyMetadata` ghi đè **vô điều kiện** cả 5 cột thành `null` khi `request.Metadata == null`:

```csharp
version.BomVersion = Norm(metadata?.BomVersion);  // metadata == null ⇒ null
```

Nhưng client duy nhất gọi update — `MProjectFrontend/src/pages/Software/NewSoftwareWizard.tsx:269` —
chỉ gửi `{ label, changelog }`, **không gửi metadata**. Đây là lỗi kinh điển "thêm field vào entity +
create, quên call-site update".

**Kịch bản:** tạo version kèm BOM/FCD/... (qua trang SoftwarePackages) → sau đó sửa label qua wizard →
**toàn bộ metadata bị null hoá im lặng**.

**Fix tối thiểu** (chuyển metadata sang ngữ nghĩa PATCH: `null` = không đụng, `{}` = xoá có chủ đích):

```csharp
if (request.Metadata != null)
    ApplyMetadata(version, request.Metadata);
```

---

## 🟠 P2 — Field chết: `MinServerVersion` được validate/lưu/map DTO nhưng không bao giờ được enforce

**File:** `MProjectBackend/MProject.Domain/Entities/Assets/AgentRelease.cs:21`,
`MProjectBackend/MProject.Application/Services/Assets/AgentService.cs:468`

XML doc mô tả đây là "gate" (*"do not offer this release to agents talking to a server older than this"*),
nhưng `ResolveAgentUpdateAsync` **không hề đọc nó**. Grep toàn repo: chỉ có validate khi publish + map DTO,
**không có chỗ nào so sánh để chặn**.

Đây đúng kiểu "code không nên tồn tại": một cột + doc dài 5 dòng nhưng không làm gì → hợp với yêu cầu
*"không thừa"*.

**Quyết định:** hoặc **wire gate** (so server version với `MinServerVersion` trong `ResolveAgentUpdateAsync`),
hoặc **xoá field + doc**. Theo tinh thần tránh over-engineer, nếu chưa có nhu cầu thật → nên xoá.

---

## 🟠 P3 — `PublishAsync` không atomic: publish lỗi giữa chừng có thể tắt self-update toàn fleet

**File:** `MProjectBackend/MProject.Application/Services/Assets/AgentReleaseService.cs:45`

Hàm gọi `SaveChangesAsync` **3 lần** (blob → `DeactivateAllAsync` → insert release) mà **không có transaction**,
trong khi các luồng tương tự ở `StationSoftwareAssignmentService` đều dùng `_context.ExecuteInTransactionAsync(...)`.

**Kịch bản:** `DeactivateAllAsync` (line 125) commit xong → insert release lỗi (ví dụ unique-violation
`AgentVersion` do publish đồng thời) → kết quả: **0 release active** → tất cả agent ngừng nhận self-update.
Hoặc blob đã lưu nhưng release fail → orphan blob.

**Fix:** bọc thân `PublishAsync` trong `ExecuteInTransactionAsync` cho đồng nhất với phần còn lại của codebase.
Comment "two saves so the unique index is never momentarily violated" cho thấy tác giả đã nghĩ tới timing
index nhưng bỏ sót rollback nguyên tử.

---

## 🟡 P4 — Trùng lặp: audit object 7 field lặp 2×

**File:** `MProjectBackend/MProject.Application/Services/Software/SoftwareVersionService.cs:120` & `:131`

Anonymous object `{ Label, Changelog, Bom, Fcd, Ftu, Fw, Region }` cho `before`/`after` lặp y hệt; thêm
field mới dễ lệch một vế. Rút thành 1 local helper `Snapshot(version)`.

> Lưu ý: khối `new VersionMetadata { ... }` lặp 3× trong các `.Select()` projection (`:321`, `:435`, `:476`)
> **là do giới hạn EF** (không translate được method call trong projection) → chấp nhận được, **đừng** cố
> gom (sẽ làm hỏng query hoặc over-engineer).

---

## 🟡 P5 (tuỳ chọn) — Nhánh early-return trong `PollAsync` lặp việc dựng `AgentManifestResponse`

**File:** `MProjectBackend/MProject.Application/Services/Software/InstallationJobService.cs:134-146`

Khi `candidates.Count == 0`, code build response riêng, trùng với nhánh chính ở `:230`. Để fall-through
xuống nhánh chính sẽ cho kết quả y hệt.

**Đánh đổi:** early-return tránh được 3 query DB (installedVersionIds / existingJobs / installedPackageIds)
khi không có candidate. Nếu coi 3 query đó là rẻ → gộp cho gọn; nếu coi là hot-path → giữ. Ưu tiên thấp,
không bắt buộc.

---

## 🔵 P6 — Ghi chú nhỏ (không cần sửa gấp)

- `ResolveAgentUpdateAsync` (`AgentService.cs:468`) chạy **mỗi heartbeat** và không truyền
  `CancellationToken` (các query khác trong service có). Thêm 1 query có index/heartbeat — rẻ. **Đừng**
  thêm cache cho "active release" trừ khi fleet rất lớn (sẽ là over-engineer đúng thứ cần tránh).
- `IsBase64` cấp buffer `(len*3+3)/4` — đã kiểm tra: luôn đủ chỗ, không bug. OK.

---

## Tóm tắt ưu tiên

| #  | Mức độ        | Vấn đề                              | Hành động                                |
|----|---------------|-------------------------------------|------------------------------------------|
| P1 | 🔴 Bug        | Update version xoá sạch metadata    | `if (request.Metadata != null) ApplyMetadata(...)` |
| P2 | 🟠 Dead code  | `MinServerVersion` không enforce    | Wire gate **hoặc** xoá field+doc         |
| P3 | 🟠 Atomicity  | `PublishAsync` không transaction    | Bọc `ExecuteInTransactionAsync`          |
| P4 | 🟡 Trùng lặp  | Audit object 7 field lặp 2×         | Gom 1 helper `Snapshot()`                |
| P5 | 🟡 Tuỳ chọn   | Early-return lặp dựng response      | Cân nhắc fall-through                     |
| P6 | 🔵 Nhỏ        | thiếu `ct` hot-path                 | Truyền `ct`; **không** thêm cache         |
