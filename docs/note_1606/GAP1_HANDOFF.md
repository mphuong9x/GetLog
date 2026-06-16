# HANDOFF — GAP-1 OverrideFile + (B) ConfigBaseline (2026-06-16)

> File này tự chứa đủ để 1 phiên Claude Code MỚI (máy khác) tiếp tục công việc mà **không cần** memory cục bộ của máy cũ. Đọc hết file này trước, rồi mở các file ở mục [§9 Đọc gì để tiếp].

---

## 0. ĐỌC TRƯỚC — cảnh báo môi trường khi chuyển máy

1. **Branch:** mọi thay đổi nằm trên `feat/gap1-overridefile-slice1`, **CHƯA COMMIT**. Trước khi rời máy: `git add -A && git commit` rồi `git push -u origin feat/gap1-overridefile-slice1`. Máy kia: `git fetch && git checkout feat/gap1-overridefile-slice1`.
2. **`docs/` bị gitignore** (`.gitignore` dòng `/docs`). Các thiết kế `docs/gap1_config_override_design.md` và `docs/gap_configbaseline_design.md` **sẽ KHÔNG đi theo git**. Muốn mang sang: `git add -f docs/gap1_config_override_design.md docs/gap_configbaseline_design.md compare1.md` hoặc copy tay. Nếu không mang được, phần tóm tắt trong file này (§7, §8) là đủ để tiếp tục.
3. **Memory Claude cục bộ** (`~/.claude/projects/.../memory/*.md`) là của máy cũ, **không đi theo repo**. Máy mới nên tạo lại memory từ file handoff này.
4. **DB:** migration đã `database update` trên DB máy cũ. Máy mới chạy lại: xem §6.

---

## 1. Bối cảnh dự án (1 đoạn)

MProject (web: .NET 8 backend Postgres/EF + React/TS frontend + Windows agent) đang **thay dần hệ phân phối phần mềm cũ** `Old_program/` (UniFi Access/Ubiquiti–Foxconn): `UIStore`/`Upload`/`AppUpdater`. Phase 1 = thay `UIStore`. Blocker kỹ thuật chính là **GAP-1 = tùy biến config theo trạm/máy** (thay `CheckSumCustom`). Đã có quyết định scope: **chỉ thay lớp phân phối/quản lý, KHÔNG viết lại engine test** (CPEI_MFG + chương trình khách giữ làm payload).

`CheckSumCustom` cũ có **2 nửa đối nghịch**:
- **Editable** (giá trị KHÁC theo trạm: IP/COM/ServerIp) → đã làm = **`OverrideFile`** (GAP-1).
- **Locked** (thông số chốt PHẢI giữ: BOM/SKU/criteria) → mới thiết kế = **`ConfigBaseline`** (B).

---

## 2. Trạng thái tổng quan

| Phần | Trạng thái |
|---|---|
| (A) OverrideFile — Slice 1: cơ chế resolver + tráo blob ở manifest | ✅ DONE, test |
| (A) Slice 2: BE-4 authoring API (overridable-paths, service, controller, permission, seed) | ✅ DONE, test |
| (A) Slice 3: Approval pipeline + Frontend đầy đủ | ✅ DONE, test |
| (A) BE-5 drift | ✅ KHÔNG cần code (xem §4) |
| (A) Còn lại: modal Computer-scope, UI overridable-paths cho author, BE-0 đạp bỏ LimitFile cũ, inline content-edit | ⬜ TODO |
| (B) ConfigBaseline | 🟦 Mới THIẾT KẾ (chưa code) — xem §8 |

**Kiểm chứng cuối:** backend `399/399 test PASS`; frontend `tsc -b` + `eslint` sạch. Migration đã apply DB máy cũ OK.

---

## 3. ĐÃ LÀM — chi tiết (A) OverrideFile

**Ý tưởng cốt lõi:** override = thay `Sha256` của một số `Path` **server-side tại `InstallationJobService.BuildManifestJobsAsync`** theo Model/Station/Computer của máy đó → **agent KHÔNG đổi**. `OverrideFile` gắn `SoftwarePackage + TargetRelativePath + scope` (KHÔNG gắn version) → sống qua update. Chỉ tráo cho path nằm trong `SoftwareVersion.OverridablePaths`.

**Backend — domain/data:**
- `MProjectBackend/MProject.Domain/Entities/Software/OverrideFile.cs` — entity (KHÔNG có `Kind`; limit file là entity riêng (B)).
- `MProjectBackend/MProject.Domain/Enums/OverrideScope.cs` (Model0/Station1/Computer2), `OverrideFileStatus.cs` (Draft1/PendingApproval2/Active3).
- `SoftwareVersion.OverridablePaths` (List<string> → Postgres `text[]`, default `'{}'`).
- Migration `MProjectBackend/MProject.Infrastructure/Migrations/20260616073716_AddOverrideFile.cs` (UNIQUE NULLS NOT DISTINCT theo (package,path,scope,modelId,stationId,computerId) WHERE !IsDeleted + CHECK scope↔id).
- EF config + DbSet trong `MProjectBackend/MProject.Infrastructure/DBContext.cs`; interface `.../MProject.Application/Interface/Common/IAppDbContext.cs`.

**Backend — resolver + manifest:**
- `MProject.Application/Interface/Software/IOverrideResolver.cs` + `Services/Software/OverrideResolver.cs` — resolve theo Computer>Station>Model (chain resource: computer→station→model). **Dùng chung** cho manifest (và drift nếu sau này cần).
- `Services/Software/InstallationJobService.cs` — đã chèn resolve + tráo blob hiệu lực trong `BuildManifestJobsAsync`, sửa `manifestHashes` (chống cảnh báo "unexpected hashes" oan), presign theo blob hiệu lực.

**Backend — authoring (BE-4):**
- `SoftwareVersionService.SetOverridablePathsAsync` + endpoint `PUT /api/v1/software-versions/{id}/overridable-paths` (validate path tồn tại trong file tree). (`ISoftwareVersionService`, `SoftwareVersionsController`, DTO `SetOverridablePathsRequest` trong `Models/SoftwareModels.cs`.)
- `Services/Software/OverrideFileService.cs` (+ `IOverrideFileService`) — Upload (multipart, blob dedup), List (ACL theo `ReadOverrideFiles`), Get, GetContent, Delete (soft + giảm ref + hủy pending approval). Model→Active khi tạo; Station/Computer→Draft.
- `Services/Software/OverrideFilePermissionService.cs` (+ interface) — quyền `ManageOverrideFiles` neo trên scope resource (ACL kế thừa Model→Station→Computer).
- `Api/Controllers/Software/OverrideFilesController.cs` — `/api/v1/override-files` (GET list/{id}/{id}/content, POST upload, POST {id}/submit-for-approval, DELETE).
- Constants: `AppPermissions.{Read,Manage}OverrideFiles`, `ResourceTypes.OverrideFile`. Seed: `AppDbSeeder.cs` cấp Viewer(read)/TeamLeader(read+manage) + auto-seed Permission qua reflection.

**Backend — approval (Slice 3):**
- `ApprovalTargetType.OverrideFile = 4`.
- `Services/Approvals/OverrideFileApprovalHandler.cs` — Draft→PendingApproval→Active / reject→Draft.
- `ApprovalApproverResolver.cs` — thêm case OverrideFile (TargetOwningResource).
- `AppDbSeeder.cs` — seed policy `OVERRIDE_FILE_PUBLISH` (RequesterTeam, TeamLeader).
- Endpoint `POST /override-files/{id}/submit-for-approval`. **Đã gỡ `activate` tạm.**
- DI tất cả service/handler trong `MProjectBackend/MProject.Api/Program.cs`.

**Frontend:**
- `src/types/overrideFiles.ts`, `src/api/overrideFiles.ts`, `src/api/resource-normalizers.ts` (mapOverrideFile).
- `src/pages/OverrideFiles/OverrideFiles.tsx` — list/filter (package, scope, status), upload, submit-for-approval, delete.
- `src/components/files/OverrideFileUploadModal.tsx` — **MVP chỉ Model + Station** (Computer scope tạo qua API; bảng vẫn hiển thị).
- Wiring: `App.tsx` route, `components/layout/AppSidebar.tsx` (icon `LuFileCog`), `constants/{routes,permissions,access-rules}.ts`, i18n `locales/{en,vi,cn}.ts` (key `override_files` + `sidebar.override_files`).

**Tests:** `MProjectBackend/MProject.Tests/InstallationJobServiceTests.cs` (4 test override substitute/locked/priority), `OverrideFileServiceTests.cs` (6), `OverrideFileApprovalHandlerTests.cs` (3).

---

## 4. BE-5 (drift) — vì sao KHÔNG cần code

Drift là **agent-driven**: agent ghi `file.Sha256` lấy **từ manifest** (đã override-aware sau Slice 1) vào deployed-index, rồi `MProjectAgent/Services/InventoryReporter.cs` so file trên đĩa với SHA đó. Server `PcInventoryService` chỉ ghi nhận issues, KHÔNG tự tính "expected". ⇒ override không bị báo drift oan; cảnh báo trong design (§2.4 doc gap1) là moot.

---

## 5. Luồng dùng end-to-end (để test thủ công)

1. Author: `PUT /api/v1/software-versions/{id}/overridable-paths` body `{ "paths": ["Config/DhcpConfig.json"] }` (CHƯA có UI — xem TODO §9).
2. Engineer (UI **Override files**): chọn package + scope (Model/Station) + nhập targetRelativePath + upload file.
3. Station/Computer → **Submit for approval** → TeamLeader duyệt (Active). Model → Active luôn.
4. Agent poll → manifest trả blob override cho path đó → deploy đúng giá trị trạm, không drift.

---

## 6. Lệnh build / test / migrate

```bash
# Backend (từ gốc repo)
dotnet build MProjectBackend/MProject.slnx
dotnet test  MProjectBackend/MProject.Tests/MProject.Tests.csproj
# Áp migration lên DB máy mới:
dotnet ef database update \
  --project MProjectBackend/MProject.Infrastructure/MProject.Infrastructure.csproj \
  --startup-project MProjectBackend/MProject.Api/MProject.Api.csproj

# Frontend (trong MProjectFrontend)
npx tsc -b           # typecheck
npx eslint .         # lint
npm run dev          # chạy thử
```

---

## 7. QUYẾT ĐỊNH ĐÃ CHỐT (đừng làm lại từ đầu)

- OverrideFile **3 tầng scope** Model/Station/Computer; ưu tiên **Computer > Station > Model**.
- **Approval bật cho Station/Computer**; Model tạo thẳng Active.
- **KHÔNG có `Kind`** trên OverrideFile (limit file tách thành entity riêng = ConfigBaseline).
- **KHÔNG động agent, KHÔNG sửa CPEI_MFG** (payload khách).
- LimitFile thử nghiệm cũ **sẽ đạp bỏ** (BE-0) nhưng CHƯA làm — vẫn chạy song song.
- (B) ConfigBaseline: **publish-time enforce exact+range** (chặn tại pin), **runtime giữ exact** qua CPEI_MFG (không sửa payload), **range-runtime hoãn**.
- Hỏi/clarify với user: **viết bằng tiếng Việt**.

---

## 8. (B) ConfigBaseline — tóm tắt thiết kế (vì doc gốc bị gitignore)

Mục đích: per-MODEL, giữ thông số chốt (Sector/Key→giá trị kỳ vọng) để **đối chiếu** config thực tế (INI khách + JSON CPEI_MFG), đảm bảo không bị đổi sai. Tổng quát hóa `FtuConfig.FtuDataConfigs` (`{Sector,Key,TargetValue,ErrorMessage}`) trong `Sample_Software/Config/ProgramConfig.json`; runtime cũ `Old_program/FTU Program/CPEI_MFG/Services/FTU/FtuService.cs:124 CheckFtuConfig()` chỉ exact, fail cứng.

**Data:** `ConfigBaseline`(per-Model, 1 active/model) + `ConfigBaselineRule` = `{TargetRelativePath, Format(Ini|Json), Sector, Key, MatchType(Exact|Range), ExpectedValue | (Min,Max,MinInclusive,MaxInclusive), ErrorMessage}`.

**Publish-time:** validate exact+range, **chặn tại `StationSoftwareAssignmentService.PinVersionAsync`** (nơi biết cặp version×model; dùng lại `IOverrideResolver` để validate trên file hiệu lực sau override) + endpoint `/validate` dry-run.

**Runtime:** giữ CPEI_MFG exact qua `FtuDataConfigs`; baseline validate khối FtuDataConfigs ở publish-time là đủ; range-runtime hoãn (Option 2 tương lai = checker riêng, không sửa payload).

**Slice code:** CB-1 entity+enum+migration → CB-2 parser INI/JSON + evaluator Exact/Range → CB-3 validator dùng resolver → CB-4 chèn gate vào PinVersionAsync + endpoint validate → CB-5 service/controller/permission + import-from-FtuDataConfigs → FE.

Chi tiết đầy đủ ở `docs/gap_configbaseline_design.md`
---

## 9. ĐỌC GÌ ĐỂ TIẾP (theo thứ tự)

**Trong repo (đi theo git):**
1. File này (`GAP1_HANDOFF.md`).
2. `compare1.md` (gốc repo) — bản đồ 7 GAP, trạng thái tổng.
3. Code OverrideFile: `MProjectBackend/MProject.Application/Services/Software/OverrideFileService.cs`, `OverrideResolver.cs`, `InstallationJobService.cs`.
4. FE: `MProjectFrontend/src/pages/OverrideFiles/OverrideFiles.tsx`.

**Gitignore — `git add -f` nếu muốn (hoặc dựa vào §3/§8 file này):**
5. `docs/gap1_config_override_design.md` — thiết kế đầy đủ (A).
6. `docs/gap_configbaseline_design.md` — thiết kế đầy đủ (B).

**Tham chiếu hệ cũ (trong repo, có thể bị ignore tùy .gitignore):**
7. `Sample_Software/Config/ProgramConfig.json` (FtuConfig/FtuDataConfigs/VersionConfig).
8. `Old_program/FTU Program/CPEI_MFG/Services/FTU/FtuService.cs` (CheckFtuConfig).

---

## 10. VIỆC TIẾP THEO — gợi ý ưu tiên

1. **FE picker `OverridablePaths`** trên VersionPanel (`MProjectFrontend/src/pages/Software/components/VersionPanel.tsx`) — khép kín author-side (hiện chỉ gọi API tay). API đã sẵn.
2. **Modal Computer-scope** cho Override files (thêm dropdown computer; backend đã hỗ trợ).
3. **(B) ConfigBaseline** từ CB-1 (xem §8).
4. **BE-0** đạp bỏ LimitFile cũ (entity/service/controller/FE/migration/permission/approval) — destructive, xác nhận với user trước.
5. (optional) inline content-edit override (`UpdateContent`, mirror LimitFile).

> 
