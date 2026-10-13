# Astra review: software content, upload, release, and blob lifecycle

Review the byte and metadata lifecycle for deployable software content. Do not modify production code.

## Scope

- Browser: `MProjectFrontend/src/api/software.ts`, upload tests, version-file utilities, and Software page upload/release UI paths.
- API: `Controllers/Software/{SoftwareFilesController,SoftwareVersionsController,SoftwarePackagesController}.cs`, `Infrastructure/TusUploadHandler.cs`, `Infrastructure/TusTempCleanupService.cs`, and TUS-related configuration in `Program.cs`.
- Application: `Services/Software/{SoftwareFileService,SoftwareVersionService,SoftwarePackageService,BlobGcService}.cs`, `Services/Uploads/{TusUploadCapabilityService,UploadReservationService,UploadReservationCleanupService}.cs`, blob reference extensions/interfaces, path rules, and relevant models/entities.
- Infrastructure: `Storage/{LocalStorageService,MinioStorageService,MinioBucketInitializer}.cs`, storage options, `AppDbContext`, migrations for blobs/reservations/tombstones.
- Tests: TUS HTTP authorization/capability/reservation/cleanup tests, software version/file tests, blob GC/reference tests, storage tests, PostgreSQL concurrency tests, frontend upload tests.

## Exclusions

Do not deep-review Agent job execution (`02`), configuration-specific override files (`04`), Agent release archives (`05`), or user documents except the shared TUS endpoint's purpose isolation required to establish security. Station-scoped capability validation is owned by `01`.

## Context to read first

Read `AGENTS.md`, checklist UPL-001, UPL-002, REL-001, STO-001, and STO-002, storage configuration, and TUS registration in `Program.cs`. Treat hash verification, immutable releases, storage/database ordering, and recoverability as primary.

## Review objectives

- Validate the UI's resumable-upload metadata and backend's purpose, actor, target, SHA-256, size, expiration, and reservation binding.
- Validate concurrent create/resume/complete behavior, abandoned session cleanup, quota accounting, duplicate blobs, client-disconnect recovery, and storage failure handling.
- Validate release immutability, entry-point/file path validation, effective archive/download selection, authorization boundaries, and how file changes invalidate a draft.
- Validate database object-reference accounting, tombstone/claim behavior, Local/MinIO semantics, and garbage collection against concurrent writers/readers.
- Do not promote speculative storage redesigns; identify concrete cases that lose, substitute, strand, or delete content.

## Required tracing

1. Frontend `software.ts` TUS call and metadata → `SoftwareFileService.InitializeUploadAsync` → `TusUploadCapabilityService.Create` / `UploadReservationService` → `TusUploadHandler.OnBeforeCreateAsync` / `OnCreateCompleteAsync` / `OnFileCompleteAsync`.
2. Completion → hash/size validation → storage upload → `MarkUploadedAsync` → `SoftwareFileService.CompleteUploadAsync`/`RegisterUploadedFileAsync` → `Blob`, `SoftwareFile`, `UploadReservation`, and blob-reference updates.
3. Draft edits → `SoftwareVersionService` version/update/entry point paths → `ReleaseVersionAsync` → immutability, files, and manifest/archive/download paths.
4. `SoftwareFileService.GetDownloadUrlAsync` and agent/admin download routes → storage implementation, including presigned/local URL assumptions.
5. `BlobGcService` and cleanup workers → database queries/tombstone claim → object deletion → database record/reference update; examine races with upload, registration, Agent/release references, and failed storage delete.
6. Cross-check configuration and documents only to prove TUS purpose isolation and shared blob accounting.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
