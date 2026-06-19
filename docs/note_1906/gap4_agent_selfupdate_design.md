# GAP-4 — Thiết kế Agent self-update (thay `AppUpdater`)

> Mục tiêu: agent **tự cập nhật binary của chính nó** từ server, không cần re-image / cử người ra trạm — thay `AppUpdater.exe` cũ (so MD5 → kill → copy → restart `UiStore.exe`). P1, không chặn việc bỏ UIStore (GAP-1/GAP-2 đã xong).
> Phạm vi đợt này: **CHỈ agent** (Windows service). Launcher tái dùng đúng kênh này ở slice sau (xem §7). KHÔNG ký số (F-08) lần này — dùng SHA-256 + endpoint agent đã xác thực + TLS (xem §3).
> Ngày: 2026-06-20. Trạng thái: **đang code — G4-1 (backend foundation) landed.**

## Quyết định đã chốt (2026-06-20)

1. **Component**: agent trước, launcher sau (cùng kênh). (Chốt với chủ dự án.)
2. **Bảo mật/ký số**: **hoãn F-08**. Toàn vẹn = **SHA-256** của blob release, tải qua endpoint agent đã xác thực (`X-Agent-Token`) trên TLS — **đúng mô hình tin cậy hiện tại của deploy app** (server là gốc tin cậy; agent vốn đã tin blob server đẩy xuống). Ký số Authenticode/manifest là hardening để dành.
3. **Cơ chế phân phối**: tái dùng hạ tầng **Blob content-addressed** + **download stream** sẵn có. Release agent = **1 gói zip** lưu thành 1 `Blob` (dedup, SHA-256). KHÔNG dựng pipeline/giao thức tải mới.
4. **Điểm chèn báo cập nhật**: **`AgentHeartbeatResponse.AgentUpdate`** (như compare1.md chỉ định). Agent đã heartbeat đều → 0 endpoint poll mới cho việc phát hiện.
5. **Cơ chế thay exe** (§4): agent là service LocalSystem **không tự đè được exe đang chạy** → **rename-then-replace + restart service** (đúng lý do `AppUpdater` cũ là process riêng). Stage → swap ở cửa sổ an toàn (idle) → để SCM bung lại exe mới.
6. **Một release active tại một thời điểm** (unique index). Agent chỉ được "đề nghị" cập nhật khi release active **mới hơn nghiêm ngặt** version agent đang báo.

---

## 1. Hệ cũ làm gì — và ta thay thế thế nào

| `AppUpdater` (cũ) | MProject GAP-4 |
|---|---|
| Console riêng, chạy định kỳ | Tích hợp trong vòng heartbeat của agent (không thêm process thường trú) |
| So MD5 file đích vs server | So **version** (`AgentRelease.AgentVersion` vs `Agent.AgentVersion` đã báo) + verify **SHA-256** blob khi tải |
| Kill `UiStore.exe` → copy → restart | Stage → swap (rename-then-replace) → **restart service** để SCM bung exe mới |
| Nguồn: SFTP kho chung | Nguồn: **Blob** server (content-addressed, dedup), tải qua endpoint agent đã xác thực |

---

## 2. Kiến trúc tổng thể

```
┌─────────────────────────── Server (MProject.Api) ───────────────────────────┐
│  AgentRelease { AgentVersion, BlobSha256→Blob, MinServerVersion?, IsActive } │
│  (admin publish: upload zip → Blob + tạo AgentRelease, set IsActive)         │
│                                                                              │
│  POST agent/v1/heartbeat  ──►  AgentService.RecordHeartbeatAsync             │
│     so AgentRelease.IsActive (mới hơn?) → AgentHeartbeatResponse.AgentUpdate │
│        { Version, Sha256, Size }                                             │
└───────────────▲─────────────────────────────────────────────┬──────────────┘
        heartbeat (AgentVersion)                       AgentUpdate (nếu có)
                │                                                ▼
┌──────────────────────────────── Agent (Windows service) ─────────────────────┐
│  AgentWorker.HeartbeatTickAsync                                              │
│    nếu resp.AgentUpdate != null & cửa-sổ-an-toàn:                            │
│      1) tải blob qua ServerClient.OpenDownloadStreamAsync → verify SHA-256   │
│      2) giải nén vào staging  (G4-3)                                         │
│      3) swap rename-then-replace + restart service  (G4-4)                   │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Tái dùng (không viết mới):** `Blob` + `BlobCacheService`/storage, `ServerClient.OpenDownloadStreamAsync` (đã xử lý URL tương đối + token / URL tuyệt đối presigned), endpoint `agent/v1/blobs/local`, `AgentInfo.Version` (assembly version), `ServiceInstaller` `sc` failure-restart.

---

## 3. Mô hình tin cậy (vì sao hoãn F-08 vẫn ổn cho MVP)

- Blob release tải qua **HTTPS** + header `X-Agent-Token` (endpoint `[Authorize agent]`). Kẻ tấn công không có token + không phá được TLS thì không chèn được binary.
- Agent **verify SHA-256** đúng giá trị server khai trong `AgentUpdate` trước khi swap → chống hỏng/sai gói.
- Đây **bằng đúng** mức bảo đảm mà deploy app khách đang dùng (agent vốn tin blob server). Không hạ thấp chuẩn an ninh hiện hành.
- **F-08 (ký số) để dành**: khi cần chống cả "server bị chiếm" thì verify chữ ký Authenticode của exe sau giải nén trước khi swap. Tách hẳn, không phá cấu trúc dưới đây.

---

## 4. Cơ chế thay exe của service đang chạy (G4-4, chốt cách làm)

Windows cho **đổi tên** một exe đang chạy (move), rồi đặt file mới vào đường dẫn cũ. Nên:

1. **Stage**: tải + giải nén release vào `…\staging\<version>\` (cạnh thư mục cài agent).
2. **Cửa sổ an toàn**: chỉ apply khi agent **idle** (không đang chạy install job, không đang giám sát app test ở trạng thái nhạy — tối thiểu: không có job đang chạy). Tránh ngắt giữa deploy.
3. **Swap**: với mỗi file đích (bắt đầu từ `MProjectAgent.exe`): `move cũ → *.old`, `move/copy mới → đường dẫn cũ`.
4. **Restart**: yêu cầu SCM restart service (service có sẵn cấu hình `failure restart`; hoặc spawn 1 helper `sc stop/start`). SCM bung **exe mới**.
5. **Dọn**: lần khởi động kế, agent xóa `*.old` còn sót.

> Chi tiết "spawn helper vs self-restart" chốt khi code G4-4. Nguyên tắc: bước swap+restart phải **không phụ thuộc** tiến trình agent cũ còn sống (nó sẽ chết khi restart).

---

## 5. Lược đồ dữ liệu — `AgentRelease` (G4-1, ĐÃ LAND)

`MProject.Domain/Entities/Assets/AgentRelease.cs` — `VersionedEntity, ISoftDeletable`:

| Field | Kiểu | Ghi chú |
|---|---|---|
| `AgentVersion` | string(64) | version assembly agent (vd `1.2.0.0`); **unique** (lọc IsDeleted=false) |
| `BlobSha256` → `Blob` | FK | gói zip release, content-addressed (Size/StoragePath lấy từ Blob) |
| `MinServerVersion` | string(64)? | (tùy chọn) chặn cập nhật nếu server cũ hơn — gate dùng ở slice sau |
| `IsActive` | bool | **unique** khi true (`UX_AgentReleases_Active`) — đúng 1 release active |
| `Notes` | string(2048)? | changelog ngắn |
| `PublishedBy` | Guid | ai publish |

Quyết định heartbeat: lấy release `IsActive`, nếu `AgentVersion` **> nghiêm ngặt** version agent báo (parse `System.Version`; không parse được → **không** offer, fail-safe) → trả `AgentUpdateInfo { Version, Sha256, Size }`.

> **DownloadUrl chưa đưa vào `AgentUpdateInfo` ở G4-1** — sẽ thêm khi code endpoint tải (G4-3), dùng đúng định dạng URL mà manifest resolver đang phát (tránh đoán API). G4-1 chỉ lo "phát hiện + đề nghị".

---

## 6. Kế hoạch slice (giữ test xanh, mỗi slice nhỏ)

- **G4-1 — Backend foundation (✅ LAND 2026-06-20):** entity `AgentRelease` + mapping + DbSet (×2) + migration; DTO `AgentUpdateInfo` + field `AgentHeartbeatResponse.AgentUpdate`; quyết định offer trong `RecordHeartbeatAsync`. Test: heartbeat offer khi release active mới hơn / không offer khi cũ-bằng / không có release. **Additive thuần — agent chưa tiêu thụ field (STJ bỏ qua field lạ) → 0 regression.**
- **G4-2 — Publish release (admin):** endpoint + service upload zip → Blob + tạo/active `AgentRelease`; (tùy) FE admin nhỏ. Reuse blob upload sẵn có.
- **G4-3 — Agent tải + stage + verify SHA-256:** thêm `DownloadUrl` vào `AgentUpdateInfo`; agent tải blob, verify hash, giải nén staging. Chưa swap.
- **G4-4 — Agent apply (swap + restart):** rename-then-replace + restart service ở cửa sổ idle; dọn `*.old`.
- **G4-5 — Đóng gói + nghiệm thu:** `package-agent.ps1` xuất release zip + sha; verify end-to-end trạm thật.
- **(sau) Launcher self-update:** dùng lại kênh; launcher dễ hơn (agent đã quản vòng đời launcher).

---

## 7. Liên hệ GAP-2

gap2_launcher_design §11 đã dự trù: "GAP-4 mở rộng (component self-update) — kênh cập nhật tập trung agent + launcher, KHÔNG re-image, KHÔNG bỏ vào catalog test-app." GAP-4 hiện thực đúng kênh đó, agent trước.

---

## 8. Ghi chú tham chiếu

- Heartbeat 2 phía: `MProject.Application/Models/AgentModels.cs` (`AgentHeartbeatResponse`) ↔ `MProjectAgent/Models/AgentApiModels.cs`.
- Quyết định offer: `MProject.Application/Services/Assets/AgentService.cs` (`RecordHeartbeatAsync`).
- Tải: `MProjectAgent/Services/ServerClient.cs` (`OpenDownloadStreamAsync`) + `BlobCacheService.PutAsync` (verify hash sẵn).
- Version agent: `MProjectAgent/Services/SystemInfo.cs` (`AgentInfo.Version`).
- Cài/restart service: `MProjectAgent/Commands/ServiceInstaller.cs` (`sc`).
