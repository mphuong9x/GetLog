# Hướng dẫn deploy agent + dùng khóa ký số (F-08)

> Hướng dẫn **thao tác từng bước** khi triển khai `MProjectAgent` lên trạm và phát hành bản cập nhật **đã ký số**.
> Phần nguyên lý/threat-model xem [F08_release_signing.md](F08_release_signing.md). Quy tắc nền: agent **fail-closed**
> — bật self-update mà thiếu/sai chữ ký hoặc chưa cấu hình public key thì agent **từ chối** mọi bản cập nhật.
> Ngày: 2026-06-20.

## 0. Toàn cảnh luồng

```
[1 lần] Tạo cặp khóa ──► private key (GIỮ KÍN, ngoài server)
                         public  key ──► nhúng vào agent appsettings (ReleasePublicKeyPem)

[Mỗi bản] bump <Version> ─► package-agent.ps1 ─► nén release.zip ─► KÝ release.zip ─► Publish (zip + chữ ký)
                                                                                          │
                                                              Activate ◄──────────────────┘
                                                                  │
                                              Agent heartbeat thấy bản mới ─► tải ─► verify SHA-256
                                                                              ─► verify CHỮ KÝ (public key)
                                                                              ─► stage ─► apply ─► restart
```

Hai bí mật cần phân biệt rõ:
- **Private key** (`agent-release-private.pem`): chỉ ở máy build/pipeline. **KHÔNG bao giờ** đưa lên server hay nhét vào gói agent.
- **Public key** (`agent-release-public.pem`): nhúng vào **mọi agent** qua `appsettings.json`. Public nên lộ cũng không sao.

---

## PHẦN A — Chuẩn bị một lần (tạo & bảo vệ khóa)

> Yêu cầu: **pwsh (PowerShell 7+)**. Làm trên máy build/pipeline, **không** làm trên server.

1. Tạo cặp khóa:
   ```powershell
   pwsh -File scripts/sign-agent-release.ps1 -GenerateKeyPair `
     -PrivateKeyPath .\agent-release-private.pem `
     -PublicKeyPath  .\agent-release-public.pem
   ```
   Lệnh in sẵn dòng `"ReleasePublicKeyPem": "...\n..."` (một dòng) để dán vào appsettings ở Phần B.

2. **Bảo vệ private key** (đây là phần khó nhất, không phải code):
   - Cất ở secret store/HSM của pipeline; quyền đọc tối thiểu; **không commit vào git** (thêm `*.pem` vào `.gitignore`).
   - Backup an toàn (mất key ⇒ phải đổi public key trên toàn bộ agent — xem Phần E).

3. Giữ lại `agent-release-public.pem` để cấu hình agent (có thể commit/chia sẻ nội bộ).

---

## PHẦN B — Deploy agent lần đầu lên trạm

1. **Cấu hình `MProjectAgent/appsettings.json`** (trước khi đóng gói), tối thiểu:
   ```json
   "Agent": {
     "ServerUrl": "https://<server>:8443",
     "InstallerToken": "<token lấy từ trang Admin>",
     "AllowUntrustedCertificate": true,
     "SelfUpdateEnabled": true,
     "ReleasePublicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----\n"
   }
   ```
   - Dán public key (một dòng, `\n` literal) vào `ReleasePublicKeyPem` — đúng chuỗi script in ở Phần A.
   - `SelfUpdateEnabled`: đặt `true` nếu muốn agent tự cập nhật. ⚠️ **Bật mà để trống `ReleasePublicKeyPem` ⇒ agent từ chối mọi update.**

2. **Đóng gói** agent + launcher:
   ```powershell
   pwsh -File scripts/package-agent.ps1
   # -> artifacts\MProjectAgent-win-x64\  (MProjectAgent.exe + MProjectLauncher.exe + deps + appsettings.json)
   ```

3. **Cài service** trên trạm (chạy **as Administrator**):
   ```powershell
   .\MProjectAgent.exe install
   ```
   Agent tự announce bằng `InstallerToken`, heartbeat, rồi nhận assignment. (Self-update chạy khi đã enroll thành công.)

4. **Xác nhận** agent đang chạy phiên bản nào: phiên bản agent = **assembly version** của `MProjectAgent.exe`
   (mặc định `1.0.0.0` nếu chưa set `<Version>`). Ghi nhớ con số này để Phần C phát hành bản **cao hơn**.

---

## PHẦN C — Phát hành một bản agent mới (đã ký)

> Agent chỉ nhận bản có version **lớn hơn nghiêm ngặt** version đang chạy. Vì version agent = assembly version,
> **bắt buộc bump `<Version>`** rồi mới build — nếu không, bản mới vẫn báo version cũ và agent sẽ **cập nhật lặp vô tận**.

1. **Tăng version** trong `MProjectAgent/MProjectAgent.csproj`:
   ```xml
   <PropertyGroup>
     <TargetFramework>net8.0</TargetFramework>
     <Version>1.2.0.0</Version>   <!-- bump mỗi lần phát hành -->
   </PropertyGroup>
   ```

2. **Đóng gói** lại (như Phần B bước 2):
   ```powershell
   pwsh -File scripts/package-agent.ps1
   ```

3. **Nén thành release.zip** — `MProjectAgent.exe` phải nằm **ở gốc** file zip (apply-update copy đè theo gốc):
   ```powershell
   Compress-Archive -Path "artifacts\MProjectAgent-win-x64\*" -DestinationPath "artifacts\agent-1.2.0.0.zip" -Force
   ```
   > Dùng `\*` để nén **nội dung** thư mục (đưa `MProjectAgent.exe` lên gốc zip), không nén cả thư mục cha.

4. **Ký đúng file zip sẽ upload**:
   ```powershell
   pwsh -File scripts/sign-agent-release.ps1 -Sign `
     -ArchivePath .\artifacts\agent-1.2.0.0.zip `
     -PrivateKeyPath .\agent-release-private.pem
   ```
   Lệnh in `SHA-256` và **`Release signature (base64)`**. ⚠️ Phải ký **chính file zip** sẽ upload (đổi 1 byte ⇒ chữ ký vô hiệu).

5. **Publish** trên web: trang **Agent releases** → *Publish release*:
   - **Version**: `1.2.0.0` (khớp `<Version>` ở bước 1).
   - **Release archive**: chọn đúng `agent-1.2.0.0.zip` ở bước 3.
   - **Release signature**: dán base64 ở bước 4.
   - (tuỳ chọn) **Min server version**, **Notes**.
   - Bản hiện tag **SIGNED** (xanh). Nếu thấy **UNSIGNED** (đỏ) ⇒ chưa dán chữ ký, agent sẽ từ chối.

6. **Activate**: bản vừa publish tự thành active (chỉ 1 active tại một thời điểm). Nếu cần đổi, bấm **Activate** ở bản mong muốn.

---

## PHẦN D — Agent tự cập nhật & nghiệm thu

1. Trong vòng ~1 nhịp heartbeat (mặc định 30s), agent cũ hơn sẽ: tải bản active → verify SHA-256 → **verify chữ ký** →
   stage → spawn `apply-update` (dừng service, copy đè, restart; rollback nếu bản mới không lên).

2. **Kiểm tra log apply** trên trạm:
   ```
   C:\ProgramData\MProjectAgent\update\apply-update.log
   ```
   Mong đợi: `Service running on the new version. Done.`

3. **Xác nhận version mới**: heartbeat sau đó báo version mới; trên web không còn offer update cho trạm đó.

4. **Test fail-closed** (khuyến nghị làm 1 lần khi nghiệm thu): publish một bản **không** dán chữ ký (UNSIGNED) và activate →
   agent **không** apply; log sẽ ghi `signature verification failed`. Sau đó activate lại bản đã ký.

---

## PHẦN E — Vận hành khóa

- **Backup private key** ở nơi an toàn, tách khỏi repo và server.
- **Xoay (rotate) khóa**: tạo cặp mới (Phần A) → cập nhật `ReleasePublicKeyPem` cho agent → phát hành **một bản agent
  mang public key mới** (ký bằng **private key cũ**, vì agent đang chạy vẫn tin key cũ) → sau khi toàn bộ trạm lên bản
  mới, các bản kế tiếp ký bằng private key mới.
- **Mất/lộ private key**: coi như mọi chữ ký cũ không còn đáng tin. Phải xoay khóa như trên; trạm chưa kịp nhận public
  key mới sẽ phải cập nhật thủ công (gỡ/cài lại gói có appsettings chứa public key mới).

---

## PHẦN F — Sự cố thường gặp

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| Agent không cập nhật, log `signature verification failed` (signed=false) | Bản publish chưa dán chữ ký (UNSIGNED) | Publish lại kèm chữ ký, hoặc Activate bản đã ký |
| Log `signature verification failed` (publicKeyConfigured=false) | `ReleasePublicKeyPem` để trống trên agent | Dán public key vào appsettings, cài lại/đẩy cấu hình |
| `signature verification failed` dù đã ký | Ký file **khác** với file upload, hoặc dùng **sai private key** so với public key nhúng | Ký đúng file zip sẽ upload; đảm bảo cặp khóa khớp |
| Publish báo lỗi *"Signature must be base64"* | Dán nhầm chuỗi/ xuống dòng lạ | Dán đúng base64 do script in ra |
| Agent cập nhật **lặp đi lặp lại** | Quên bump `<Version>` ⇒ bản mới vẫn báo version cũ | Bump `<Version>` trùng version publish, đóng gói lại |
| `apply-update.log`: rollback | Bản mới không khởi động được | Kiểm tra bản build; rollback đã tự khôi phục bản cũ |
| Agent từ chối / không tải | `SelfUpdateEnabled=false` | Đặt `true` (kèm public key) nếu muốn tự cập nhật |

---

## Checklist nhanh

**Một lần:**
- [ ] Tạo cặp khóa; private key cất an toàn, ngoài server, không commit.

**Deploy agent:**
- [ ] appsettings: `ServerUrl`, `InstallerToken`, `SelfUpdateEnabled`, **`ReleasePublicKeyPem`** đã điền.
- [ ] `package-agent.ps1` → `MProjectAgent.exe install` (Administrator).

**Mỗi bản phát hành:**
- [ ] Bump `<Version>` trong csproj (cao hơn bản đang chạy).
- [ ] `package-agent.ps1` → `Compress-Archive ...\*` (exe ở gốc zip).
- [ ] Ký **đúng** file zip → lấy base64.
- [ ] Publish: version khớp + đúng zip + dán chữ ký → tag **SIGNED**.
- [ ] Activate; theo dõi `apply-update.log` đến `Done`.
