# F-08 — Ký số bản phát hành agent (release signing)

> Đóng nợ bảo mật của GAP-4 (agent self-update). Mục tiêu: self-update **chỉ apply bản đã ký** bằng khóa
> riêng giữ NGOÀI server, để dù kẻ xấu chiếm server/DB hoặc MITM TLS cũng **không giả được** bản phát hành
> (agent apply update với quyền LocalSystem trên mọi máy → nếu giả được là RCE diện rộng / supply-chain).
> Ngày: 2026-06-20.

## Mô hình tin cậy

- **Chữ ký tạo OFFLINE** (máy build/pipeline), bằng **private key không bao giờ đặt trên server**.
- Server **chỉ lưu chữ ký dạng đục** (opaque) trên `AgentRelease.Signature` và trả về cho agent khi offer
  update trên heartbeat. Server không có private key ⇒ chiếm server cũng không ký được bản độc.
- Agent **nhúng public key** (`Agent:ReleasePublicKeyPem`) và **verify chữ ký sau khi khớp SHA-256, trước khi
  stage/apply**. **Fail-closed**: thiếu chữ ký, sai chữ ký, hoặc chưa cấu hình public key ⇒ **từ chối** update.
- Thuật toán: **RSA (3072-bit) PKCS#1 v1.5 + SHA-256**, chữ ký detached trên **SHA-256 của file zip**, lưu
  base64. Tương thích `openssl dgst -sha256 -sign` ↔ .NET `RSA.VerifyHash(..., SHA256, Pkcs1)`.

Chuỗi tin cậy: `bytes zip` → `SHA-256` (agent so khớp `update.Sha256`) → `chữ ký trên SHA-256` (agent verify
bằng public key nhúng). Đổi file ⇒ đổi hash ⇒ vỡ chữ ký; đổi `update.Sha256` ⇒ vỡ chữ ký.

## Quy trình vận hành

### 1. Tạo cặp khóa (một lần)

```powershell
pwsh -File scripts/sign-agent-release.ps1 -GenerateKeyPair
# -> agent-release-private.pem  (GIỮ KÍN, không bao giờ lên server)
# -> agent-release-public.pem
```

Tương đương openssl:

```bash
openssl genrsa -out agent-release-private.pem 3072
openssl rsa -in agent-release-private.pem -pubout -out agent-release-public.pem
```

> **Bảo vệ private key là phần khó nhất, không phải code.** Giữ ở HSM / secret store của pipeline, phân quyền
> hẹp, có thể luân chuyển. Mất private key ⇒ phải đổi public key nhúng trong agent (qua cấu hình + tái phát hành).

### 2. Nhúng public key vào agent

Dán nội dung `agent-release-public.pem` (một dòng, `\n` literal) vào `MProjectAgent/appsettings.json`:

```json
"Agent": {
  "SelfUpdateEnabled": true,
  "ReleasePublicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----\n"
}
```

`-GenerateKeyPair` in sẵn dòng `"ReleasePublicKeyPem": "..."` để dán. **Chưa cấu hình key ⇒ agent từ chối mọi
update** (fail-closed) — đúng tinh thần "chưa có F-08 thì không bật self-update production".

### 3. Ký mỗi bản phát hành

```powershell
pwsh -File scripts/sign-agent-release.ps1 -Sign -ArchivePath .\artifacts\MProjectAgent-win-x64.zip
# In ra SHA-256 và "Release signature (base64)"
```

Tương đương openssl:

```bash
openssl dgst -sha256 -sign agent-release-private.pem release.zip | base64 -w0
```

### 4. Publish kèm chữ ký

Trang **Agent releases** → *Publish release* → dán base64 vào ô **Release signature**. Bản chưa ký hiển thị
tag đỏ **UNSIGNED** (agent có self-update sẽ từ chối). Backend kiểm tra base64 hợp lệ lúc publish (bắt typo
sớm); việc verify chữ ký thật do agent đảm nhận.

## Bản đồ code

| Lớp | File | Vai trò |
|---|---|---|
| Domain | `MProject.Domain/Entities/Assets/AgentRelease.cs` | cột `Signature` (nullable) |
| Infra | `MProject.Infrastructure/.../AddAgentReleaseSignature` | migration thêm cột |
| App | `AgentReleaseService.PublishAsync` | nhận + validate base64 + lưu chữ ký |
| App | `AgentService.ResolveAgentUpdateAsync` | trả `Signature` trong `AgentUpdateInfo` |
| Agent | `Services/ReleaseSignatureVerifier.cs` | verify RSA/SHA-256 (fail-closed) |
| Agent | `Services/AgentUpdater.StageFromFileAsync` | verify sau SHA-256, trước stage/apply |
| Agent | `Configuration/AgentOptions.ReleasePublicKeyPem` | public key nhúng |
| FE | `pages/AgentReleases` | ô nhập chữ ký + tag SIGNED/UNSIGNED |
| Tool | `scripts/sign-agent-release.ps1` | tạo khóa + ký (no openssl needed) |

## Tùy chọn tăng cường (chưa làm, không chặn)

- **Authenticode-sign** `MProjectAgent.exe` (chống cảnh báo Windows + một lớp tin cậy nữa).
- **Server pre-verify**: cho server giữ public key để verify lúc publish (phản hồi nhanh hơn). Hiện tại đường
  biên bảo mật là agent, nên bỏ qua để tối giản.
- **Compiled-in public key**: nhúng cứng trong binary thay vì appsettings để chống sửa file cấu hình cục bộ
  (ngoài phạm vi mối đe dọa F-08 — vốn là server/DB/TLS, không phải file cục bộ trên máy đã bị chiếm).
