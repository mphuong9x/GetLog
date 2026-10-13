# Astra review: user identity, RBAC, approvals, and browser contract

Review the human authorization boundary from browser state through server authorization and approval execution. Do not modify production code.

## Scope

- API: `Controllers/Identity/{AuthController,AuthorizationController,RolesController,UsersController}.cs`, `Controllers/Approvals/ApprovalsController.cs`, authorization filters/attributes, authentication setup in `Program.cs`, and relevant base controller behavior.
- Application: `Services/Identity/{AuthService,RefreshTokenService,AccessTokenSecurityStateValidator,PermissionService,AuthorizationService,AuthorizationMutationService,AuthorizationCacheInvalidator,RoleService,UserService,ResourceLookupService}.cs`, authorization query/resolver classes, audit logger, approval services/approver resolver/interfaces, approval entities/models, and target handlers.
- Frontend: `api/services/axios-client.ts`, auth API/types/token utilities, `AuthContext`, `RequireAccess`, `SupportQueryIsolation`, permission/access rules, authorization/approval APIs/types/components/pages, and mutation views for software assignments, Agent releases, configuration, and support recovery only as contract consumers.
- Tests: auth/revocation/version/refresh tests; permission/authorization cache/mutation/resource-query/scope tests; role/user tests; approval/handler tests; frontend access/auth/approval/deployment tests.

## Exclusions

Do not re-review Agent token authentication (`01`), job state transitions (`02`), configuration target semantics (`04`), or support outbox delivery (`07`). For approval target handlers, assess engine/authorization/snapshot orchestration here; report target-specific state effects in the owning unit.

## Context to read first

Read `AGENTS.md`, checklist SEC-003, AUTH-001 through AUTH-004, FE-001 through FE-005, app permission constants/roles, and frontend API client/auth context. The browser gate is a usability aid, never sufficient authorization evidence.

## Review objectives

- Validate registration, login, refresh, logout, password change, token issuance/rotation/revocation, single-device login semantics, disabled users, auth-version validation, and concurrent requests.
- Validate role, ACL, ownership, team/membership expiry, resource lookup/scope resolution, cache invalidation, self-elevation/system-role constraints, and audit truthfulness.
- Validate every approval transition: submit, inbox visibility, step/approver resolution, cancel/reject/approve race, policy mutation, immutable target/change snapshot, and target handler transaction behavior.
- Validate browser JWT persistence/refresh queue/401 behavior, cache isolation, permission-derived navigation/action availability, page polling/stale responses, and UI/API model synchronization for high-impact actions.
- Identify a real server authorization bypass or misleading UI contract only when a realistic route and actor can demonstrate it.

## Required tracing

1. `SignInForm` / auth API → axios request/response interceptors → `AuthController.Login`/`Refresh` → `AuthService`/`RefreshTokenService` → JWT claims → `AccessTokenSecurityStateValidator` on later requests.
2. A sensitive server mutation with `[RequirePermission]` → `PermissionAuthorizationFilter` → `PermissionService.CheckPermissionAsync` → scopes/roles/ACL/owner/team resolvers and cache invalidation after mutation.
3. Role/ACL admin requests → `AuthorizationController`/`RolesController` → `AuthorizationMutationService`/`RoleService` → authorization audit row → client UI refresh.
4. Any approval target → `ApprovalsController` → `ApprovalService.SubmitAsync`/`ApproveAsync`/`RejectAsync`/`CancelAsync` → approver resolver/policy snapshots → handler → target persistence; test policy/target changes during an in-flight request.
5. Browser access rules and pages for assignments, Agent releases, approvals, override/config actions, and support recovery → API endpoint permissions and response models. Verify a denied direct API call independently of UI hiding.

## Findings format

For every finding provide Severity, Confidence, Category, File, Symbol, Evidence, Finding, Realistic failure scenario, Operational impact, Root cause, Recommended direction, Required regression test, and Estimated change scope (Small / Medium / Large). Also report good decisions to preserve, rejected suspicions, and insufficient-evidence areas. Do not modify production code.

Use exactly one Category: confirmed defect / likely defect / design risk / maintainability issue / optional improvement.
