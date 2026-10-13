# MProject — Implementation Taskbook From Code Review

Updated: 2026-09-13

This is an execution backlog, not a replacement for the source reports. It
groups findings by ownership, dependency, and rollback boundary; do not treat a
review report as one implementation task. Current code, tests, and scripts are
the source of truth. Reproduce and revalidate a finding against the current
HEAD before changing its status.

## How to use this file

1. Do one task at a time. A task normally produces one focused branch/PR.
2. Paste the task prompt into a new Codex conversation, select its recommended
   model and effort, then attach only the referenced report(s) if the workspace
   is not already available.
3. Complete the common checklist plus the task-specific checklist before asking
   for review. Do not update `PROJECT_MEMORY.md` or the historical checklist
   until the required evidence is real and recorded.
4. `Astra max` is reserved for architecture/concurrency/recovery boundaries.
   After it has produced an implementation plan, `Terra high` may implement a
   small, clearly bounded sub-PR and Astra `xhigh` reviews the boundary.

The model choices favour a Plus allowance while preferring `gpt-5.6-sol` for
any task that crosses backend/runtime contracts, requires non-trivial diagnosis,
or produces a feature-level change. `gpt-5.6-terra/high` is retained only where
it is the better fit: tightly bounded frontend routine work, small PowerShell
guards, or documentation-only decisions. `gpt-6-astra/max` is limited to work
that could corrupt artifacts, bypass identity controls, strand stations, or
destroy recovery.

## Common checklist — apply to every code task

- [ ] Read `AGENTS.md`, `MProjectFrontend/PROJECT_MEMORY.md`, and
      `MProjectFrontend/PROJECT_REVIEW_CHECKLIST.md` before modifying code.
- [ ] Read the cited review report and inspect the current implementation; note
      if the report's source locations or assumptions drifted.
- [ ] Keep unrelated worktree changes untouched; make no live DB, station,
      IIS, certificate, token, or credential changes.
- [ ] Add a focused regression that fails before the fix and passes after it.
- [ ] Preserve existing product boundaries: legacy payloads remain payloads;
      frontend permission checks are not a security boundary; server-side
      authorization remains authoritative.
- [ ] Run the focused tests and the affected subsystem suite. Run the specified
      provider/runtime gate where one is listed.
- [ ] Run `git diff --check`, summarize changed contracts and rollback, and
      record exact commands/results in the PR or task evidence.

## Dependency order

`META-00` → `DEP-01`/`DEP-02` → quick fixes and independent implementation →
`CNT-*`, `AUTH-*`, `AGT-*` → `OTA-*` → `CFG-*` → `SUP-*`/`ING-*` → `CLS-01`.

Development can overlap after `META-00`, but rollout must respect the order.
Do not ship station or migration changes without their runtime/rollback gate.

---

## META-00 — Reconcile review evidence and canonical backlog

**Findings:** report-09 integration; report-11 synthesis formerly reported
report 09 and appendix probes 06/07 as unavailable.

**Status:** Completed on 2026-09-13. Report 09 and probe appendices 06/07 are
present and incorporated into the canonical synthesis. The reconciled inventory
is 99 source findings mapped exactly once to 96 canonical issues: the original
three merges remain, and R09-01..R09-13 map one-to-one to SYN-84..SYN-96. This
documentation status does not mark any production finding remediated.

**Model / effort:** `gpt-5.6-sol` / `high`

**Sources:** reports `00`, `06`, `07`, `09`, `11`.

**Prompt:**

```text
Work only on review documentation, not production code. Read AGENTS.md,
docs/PROJECT_REVIEW_CHECKLIST.md, reports 00, 06, 07, 09, and 11 under
docs/code-review/reports, plus the actual probe files. If PROJECT_MEMORY.md is
absent at the current HEAD, record that limitation rather than fabricating it.
Reconcile the canonical implementation backlog: incorporate R09-01..R09-13,
correct stale report-11 claims about report 09/probe availability when current
files prove otherwise, and preserve every original finding with a traceable
mapping. Do not claim a finding is fixed. Record revision/provenance limits and
the exact total counts. Keep scope to docs/code-review and return a concise
mapping and diff summary.
```

**Task checklist:**

- [x] Every R09 finding maps to a canonical item or explicitly justified merge.
- [x] No finding is silently deleted or marked remediated.
- [x] Evidence limitations remain visible.

**Recorded evidence (2026-09-13):**

- Source totals reconcile as 86 prior findings plus 13 report-09 findings = 99;
  category totals are 81 confirmed defects, 14 design risks and 1
  maintainability issue; priority totals are P1=43, P2=49 and P3=4.
- The explicit source-to-canonical table maps each `R09-01..R09-13` exactly
  once to `SYN-84..SYN-96`. Canonical table rows and detail headings both form
  the unique contiguous set `SYN-01..SYN-96`.
- Report 09 is present from commit `5d4e5ee`; both durable probe appendices are
  present from commit `2ecaf1b`. META-00 did not rerun diagnostic probes or
  treat their reproduction assertions as post-fix acceptance evidence.
- `PROJECT_MEMORY.md` is absent at the reconciled HEAD. `AGENTS.md` and
  `docs/PROJECT_REVIEW_CHECKLIST.md` were read; the missing file remains an
  explicit provenance limitation rather than being fabricated.
- PowerShell heading inventory over reports 00–10 returned
  `4+5+2+10+16+11+9+9+7+13+13=99`. The canonical-ID audit returned 96 table
  rows, 96 detail headings and 13 one-to-one R09 mappings, with no missing or
  duplicate IDs. Reference-label and file-existence checks returned no missing
  link label or required report/probe file.
- `git diff --check` returned exit 0; Git emitted only the existing LF-to-CRLF
  working-copy warnings. No product test, deploy or diagnostic probe was run
  because META-00 changes documentation only.

---

## DEP-01 — Deployment target, schema, and release-gate correctness

**Findings:** SYN-69, SYN-70, SYN-71, SYN-75, SYN-78, SYN-79.

**Status:** Completed on 2026-09-14. Implemented in `a4d2d839` and
revalidated against the current working tree with the deployment contract,
isolated PostgreSQL, and focused regression gates. This is implementation and
isolated-fixture acceptance evidence only; it does not authorize a deployment
against an operational server or database.

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** report `10-server-deployment-database-and-recovery-report.md`.

**Prompt:**

```text
Implement only SYN-69/70/71/75/78/79 from report 10. First revalidate current
scripts, API startup behavior, generated schema artifact, and tests. Design a
single deployment contract that proves the effective API database target,
migration head/hash, and schema readiness before traffic opens. Build
connection strings with a safe builder; eliminate hard-coded migration-head
gates; make unknown/destructive SQL classification fail closed or require an
explicit reviewed override. Add focused failure tests and a disposable real
PostgreSQL integration gate. Preserve the production rule that deployment owns
migrations and production startup does not migrate automatically. Do not run
against an operational server or database. Report rollback behavior and every
changed contract.
```

**Task checklist:**

- [x] Wrong target DB, missing schema, and stale artifact head fail before
      traffic/workers open.
- [x] Semicolon/quote password round-trips safely.
- [x] Unknown migration syntax cannot silently bypass the safety gate.
- [x] Real PostgreSQL test verifies target identity, schema head, and failure.

**Recorded evidence:**

- `DeploymentSchema`, `DeploymentCommand`, the generated v2 artifact contract,
  and `deployment-contract.ps1` bind the effective target, migration list/head,
  artifact hashes, schema readiness, and fail-closed migration classification.
- `docs/code-review/DEPLOYMENT_CONTRACT.md` documents the supported deployment
  and compatibility-rollback contracts. Its isolated PostgreSQL gate creates a
  disposable loopback cluster rather than accepting an operational connection.
- Revalidated on 2026-09-14:
  `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore
  -p:OutputPath=bin/dep-review-20260914/ --filter
  FullyQualifiedName~Deployment --logger "console;verbosity=minimal"` — 33
  passed, 0 failed; and
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  scripts/test-deployment-contract.ps1 -ApiDirectory
  MProjectBackend/MProject.Api/bin/dep-review-20260914` — 12 focused cases
  passed. The explicit `-ApiDirectory` avoids a stale, running default build
  output; it does not alter production binaries.

---

## DEP-02 — Deployment rollback, maintenance fence, TLS, and IIS lifecycle

**Findings:** SYN-72, SYN-73, SYN-76, SYN-80, SYN-82.

**Status:** Completed on 2026-09-14. Implemented in `f3fb4f3e` and
revalidated against the current working tree. The recovery evidence uses an
isolated IIS/PostgreSQL lab with generated artifacts; it is not evidence of a
live server, certificate, binding, or database change.

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** reports `10` and `00-architecture-report.md`.

**Prompt:**

```text
Implement only SYN-72/73/76/80/82. Revalidate current IIS/deploy scripts,
certificate handling, backup/rollback paths, hosted-worker configuration, and
all known writers/external storage. Define and implement a maintenance fence
that prevents unsafe full-DB rollback after acknowledged writes; preserve the
predeploy TLS/config/key state when deployment fails; never replace a valid TLS
certificate merely because of expiry logic; make the browser console default to
the approved HTTPS route after the auth-boundary review. Verify effective IIS
preload/idle/startup behavior without breaking maintenance. Use an isolated
IIS/PostgreSQL lab only, with two real binary/schema versions and fault
injection. Do not alter live certificates, bindings, or databases.
```

**Task checklist:**

- [x] Failed deployment restores old binary, DB, config/key and binding as one
      recovery set.
- [x] Maintenance blocks writers before destructive recovery and keeps them
      blocked until strict health succeeds.
- [x] TLS validation is strict in staging verification and client defaults.
- [x] Cold/idle IIS test proves hosted-worker lifecycle is intentional.

**Recorded evidence:**

- `DeploymentMaintenance`, `deployment-maintenance.ps1`, and the recovery
  sidecar/fence contract implement writer fencing, exact recovery material,
  strict HTTPS admission, bound-certificate preservation, and validation-only
  preload behavior. The supported topology and rollback limits are explicit in
  `docs/code-review/DEPLOYMENT_CONTRACT.md`.
- `docs/code-review/DEP02_EVIDENCE.md` records the isolated two-version,
  fault-injected IIS/PostgreSQL lab: recovery faults/retry, writer-fence
  observations, strict TLS negatives, and cold/idle/preload checks.
- Revalidated on 2026-09-14:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  scripts/test-deployment-maintenance.ps1` — 15 focused cases passed; the
  current deployment-focused .NET suite passed 33/33 as recorded for DEP-01.

---

## QK-01 — Backend data and audit guards

**Findings:** SYN-03, SYN-09, SYN-12, SYN-13, SYN-19, SYN-31, SYN-93
(source R09-10).

**Status:** Implemented and acceptance-validated locally on 2026-09-12. The
remaining full-backend failure is confined to out-of-scope deployment
regressions SYN-76/SYN-80; retain it for DEP-02 rather than treating it as a
QK-01 failure.

**Model / effort:** `gpt-5.6-sol` / `xhigh`

**Sources:** reports `01`, `03`, `04`, `09`.

**Prompt:**

```text
Implement the narrowly scoped server-side guards SYN-03/09/12/13/19/31 and
SYN-93/R09-10 only. Reproduce each selected defect first. Ensure expired AgentCommand
rows do not block dedup/quota, release generation protects all artifact delete
paths, upload completion cannot self-conflict due to tracker state, file/tree
collisions are rejected, invalid INI/non-finite values cannot partially succeed,
and authorization audit snapshots are immutable before/after projections. Keep
transactions and existing concurrency rules intact. Add focused xUnit tests and
real PostgreSQL barriers where the finding requires relational behavior.
```

**Task checklist:**

- [x] Each selected regression fails before and passes after.
- [x] No partial configuration or invalid release artifact is persisted.
- [x] Role/ACL revoke audit shows `IsDeleted=false` before and true after.
- [x] PostgreSQL release/delete cases retain manifest identity.

**Recorded evidence (2026-09-12):**

- Reproduced the focused non-PostgreSQL defects before the guards; 18 failed
  and 7 passed. A temporary reversion of the two relational guards reproduced
  two failures in the disposable PostgreSQL release/delete and tracker cases.
- `dotnet test MProject.Tests/MProject.Tests.csproj --no-restore --filter
  "FullyQualifiedName~AgentCommandServiceTests|FullyQualifiedName~SoftwareFileServiceTests|FullyQualifiedName~SoftwareVersionServiceTests|FullyQualifiedName~ConfigFileWriterTests|FullyQualifiedName~ConfigParameterServiceTests|FullyQualifiedName~ConfigValueOverrideServiceTests|FullyQualifiedName~ConfigValueOverrideApprovalHandlerTests|FullyQualifiedName~ConfigRenderServiceTests|FullyQualifiedName~AuthorizationMutationServiceTests"`
  — passed 287, failed 0.
- `scripts/test-deployment-postgres.ps1 -Filter
  'FullyQualifiedName~PostgresConcurrencyIntegrationTests'` — disposable
  PostgreSQL gate passed 16, failed 0; the fixture cleaned up its database and
  cluster artifacts.
- `dotnet test MProject.Tests/MProject.Tests.csproj --no-restore` — 1 failing
  deployment-script test out of 1,180 (SYN-76/SYN-80 cases, outside QK-01);
  all QK-01-focused tests passed.
- `git diff --check` — exit 0 (line-ending warnings only).

---

## QK-02 — Browser stale-state, upload intent, and monitor quick fixes

**Findings:** SYN-11, SYN-28, SYN-58, SYN-62, SYN-64, SYN-68, SYN-90
(source R09-07).

**Status:** Implemented and acceptance-validated locally on 2026-09-12.

**Model / effort:** `gpt-5.6-terra` / `high`

**Sources:** reports `03`, `04`, `07`, `08`, `09`.

**Prompt:**

```text
Implement only frontend-scoped SYN-11/28/58/62/64/68 and SYN-90/R09-07. Use existing
API modules/hooks rather than direct Axios calls. Fix hash-cache identity so a
same path/size/mtime file cannot reuse stale bytes; guard modal/drawer and
monitor responses by current selection/generation; distinguish load failure
from empty data; refresh Test Monitor correctly; and make serial search
paginate beyond 20. Do not change server authorization semantics. Add Vitest/
React tests using deferred responses, fake timers, and changed file metadata.
Run lint, frontend tests, and build.
```

**Task checklist:**

- [x] Old response cannot mutate current modal/drawer/action target.
- [x] Monitor receives new data without manual reload.
- [x] Error UI cannot appear as empty success.
- [x] File selection hashes the bytes actually chosen.

**Recorded evidence (2026-09-12):**

- Focused deferred-response, changed-file-metadata/bytes, and fake-timer
  regressions reproduced the selected stale hash, config target, approval
  drawer, Monitor refresh/filter/page, and support empty-success behaviors
  before their respective guards were applied.
- `node node_modules/vitest/vitest.mjs run src/utils/fileHash.test.ts
  src/components/files/ConfigValuesModal.test.tsx
  src/pages/TestMonitor/TestMonitor.test.tsx
  src/components/approvals/ApprovalActionDrawer.test.tsx
  src/pages/SupportRequests/SupportRequests.test.tsx --reporter=dot`
  — passed 19, failed 0.
- `yarn test --run` — passed 237, failed 0 (36 test files).
- `yarn lint` — exit 0; 17 pre-existing warnings remain.
- `yarn build` — passed (`tsc -b && vite build`); Vite emitted its existing
  large-chunk advisory only.
- `git diff --check` — exit 0 (line-ending warnings only).

---

## QK-03 — Deployment script and release-input guards

**Findings:** SYN-74, SYN-77, SYN-81.

**Status:** Implemented and acceptance-validated locally on 2026-09-13.

**Model / effort:** `gpt-5.6-sol` / `high`

**Sources:** report `10`.

**Prompt:**

```text
Implement only SYN-74/77/81. Trace all signing-key inputs so a public-key field
can never distribute a private key; ensure KeepBackups=0 retains the active
archive/dump recovery pair; make local deployment health use normal TLS
validation. Add PowerShell 5.1-compatible fixture tests and source/output
assertions. Do not print, copy, or create real keys or credentials.
```

**Task checklist:**

- [x] Private-key-like input is rejected from public distribution paths.
- [x] Retention never removes the active rollback pair.
- [x] Invalid/wrong-host certificate fails local health verification.

**Recorded evidence (2026-09-13):**

- `agent-release-key-contract.ps1` accepts only structurally valid RSA public
  PEM, normalizes it, and rejects private markers, mixed blocks, malformed
  base64/DER and weak or invalid RSA parameters before output mutation.
- `package-agent.ps1`, `prepare-deploy.ps1` and `install-server.ps1` share that
  contract. Prepared server and isolated IIS-lab artifacts now include the
  helper; only normalized public PEM is written to distributable output.
- The DEP-02 retention implementation already protected the active recovery
  directory. A focused `KeepBackups=0` regression now proves its `Api.zip` and
  `db.dump` remain while older completed recovery sets are pruned.
- `deploy-local.ps1` requires absolute HTTPS and uses normal Node TLS
  verification. Cleartext, wrong-host and untrusted-certificate fixtures fail.
- `test-deployment-input-guards.ps1` contains synthetic public DER and fake
  private markers only; it creates no signing key or credential.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  scripts\test-deployment-input-guards.ps1` — exit 0; all five focused QK-03
  cases passed under Windows PowerShell 5.1.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  scripts\test-deployment-maintenance.ps1` — exit 0; all 15 DEP-02 regression
  cases passed, including the expected injected health-failure warning.
- `dotnet test MProjectBackend\MProject.Tests\MProject.Tests.csproj
  --no-restore -v minimal --filter FullyQualifiedName~DeploymentScriptTests` —
  exit 0; 3 passed, 0 failed, including the current migration-head contract.
- PowerShell parser validation passed all eight changed/integrated scripts;
  `git diff --check` returned exit 0 with line-ending warnings only. The new
  untracked source files also contain no trailing whitespace.
- Full environment IIS/certificate drills were not run. The acceptance fixture
  proves URL policy and TLS failure propagation, while source assertions prove
  that Node certificate validation remains enabled.

---

## CNT-01 — Verified CAS publication and historical Blob GC

**Findings:** SYN-08, SYN-10.

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** report `03-software-content-upload-and-blob-lifecycle-report.md` and
canonical SYN-08/SYN-10 entries in report `11-final-synthesis-report.md`.

**Prompt:**

```text
Implement SYN-08 and SYN-10 together, and nothing else. First decide/document
the historical blob retention rule with the owner if it is not already decided.
Then make publication to the content-addressed store verify bytes before an
atomic promote and never overwrite a finalized object. Cover both LocalStorage
and MinIO adapters. Repair Blob GC so historical/soft-deleted references cannot
delete storage before durable DB state permits it, one failing candidate cannot
abort the whole sweep, and a SHA can recover/reupload. Add real filesystem,
MinIO, and PostgreSQL concurrency/fault tests. Do not purge real history or
repair production storage automatically.
```

**Task checklist:**

- [x] Existing finalized bytes remain immutable through interruption/retry.
- [x] Local and MinIO both pass provider-specific tests.
- [x] GC-vs-upload/render and historical-FK retry are tested on PostgreSQL.
- [x] Cold-cache reader gets the verified SHA bytes after recovery.

**Completed 2026-09-14:** implementation and disposable-fixture acceptance only;
no production access, repair, purge, migration or deployment. Detailed reference
inventory, pre-fix reproductions, exact commands/outcomes, contract changes,
environment limits and rollback: [CNT01_EVIDENCE.md](CNT01_EVIDENCE.md).

**Owner decision:** “Approve retention while any reference exists”. Current,
historical and soft-deleted typed references retain bytes, including rendered
base/output cache rows and all retained reservation states. GC performs no
history/cache/reservation purge; a future purge needs separate authorization.

**Contracts:** verified private staging followed by no-overwrite local rename or
MinIO conditional/checksummed single PUT; idempotent retries verify the existing
object. Software init uses `uploadUrl="tus"` on both providers; canonical client
PUT URLs are no longer issued. SHA verification is unconditional, including
known-SHA repair. GC commits a claim, locks/rechecks references, proves FK deletion
inside an uncommitted PostgreSQL transaction, deletes storage, then commits.
Per-candidate failures retain retry state and do not abort other candidates.

**Acceptance commands and outcomes:** from the repository root, with
`MPROJECT_TEST_MINIO_EXE=C:\dev\MProject\.tmp\cnt01-tools\minio.exe`:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/test-deployment-postgres.ps1 -Filter 'FullyQualifiedName~BlobLifecyclePostgresTests|FullyQualifiedName~PostgresConcurrencyIntegrationTests|FullyQualifiedName~CasPublicationTests|FullyQualifiedName~MinioPublicationWireTests'`:
  **61 passed, 0 failed, 0 skipped**, exit 0. Real disposable PostgreSQL 18.1,
  LocalStorage and MinIO: 30 provider publication/wire cases plus 31 relational
  cases, covering GC/render, historical-FK/purge races, failures and recovery.
  Writers pause after an actual 8 KiB partial copy; LocalStorage stress covers
  20 rounds of eight concurrent writers.
- Clear `MPROJECT_TEST_POSTGRES_CS` and `MPROJECT_AUDIT_POSTGRES_CS`; run
  `dotnet test MProjectBackend/MProject.Tests/MProject.Tests.csproj --no-restore -v minimal`:
  **1211 passed, 0 failed, 15 skipped**, exit 0. All 15 PostgreSQL cases skipped
  here passed in the separate fixture run. Includes 30 provider publication/wire
  cases and 20 rounds of eight concurrent LocalStorage writers.
- From `MProjectFrontend`,
  `node node_modules/vitest/vitest.mjs run src/api/software-upload.test.ts --pool=threads --maxWorkers=1 --reporter=dot`:
  **7 passed, 0 failed**, exit 0. `git diff --check`: exit 0.

**Environment limits:** Windows, source-built single-node MinIO release
`RELEASE.2025-09-07T16-13-09Z` (official binary URL returned HTTP 410); largest
tested object 6 MiB plus suffix. No 10 GiB throughput, distributed MinIO, power-loss,
IIS or Agent-install canary. PostgreSQL initdb/native Git fixtures required
elevation; the final runs passed. See evidence for all intermediate failures.

**Rollout/rollback:** drain old publishers and wait out all existing canonical
MinIO PUT URLs (source TTL 30 minutes; honor the actual maximum issued TTL) before
claiming immutability. Deploy API/frontend together. For rollback, stop publication
and disable `BlobGc:Enabled` before reverting both; do not re-enable the old unsafe
historical GC. No schema rollback is needed. Code/DB rollback alone cannot restore
object bytes; any production repair needs separate authorization and verified
backup/source bytes. No automatic production cleanup was added.

---

## CNT-02 — Upload recovery and released path-policy contract

**Findings:** SYN-14, SYN-15, SYN-16.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Sources:** report `03`.

**Prompt:**

```text
Implement SYN-14/15/16. Reconcile a lost final TUS PATCH response without
creating a second upload or falsely failing a successfully stored reservation.
Inventory callers of human download-url and either return a valid human route
or deprecate it safely. Obtain/record a product decision for released
OverridablePaths and PreserveLocalPaths: draft-only mutation or explicit
audited revision; do not leave in-place undefined mutation. Add HTTP retry,
caller, and release-policy regression tests. Preserve purpose-bound TUS and
scoped Agent capabilities.
```

**Task checklist:**

- [ ] Ambiguous final PATCH converges idempotently.
- [ ] Every retained download-url caller works under its own credential type.
- [ ] Same released VersionId cannot silently resolve different path policy.

---

## AUTH-01 — Agent authority and station ACL boundary

**Findings:** SYN-01, SYN-04, SYN-05, SYN-84 (source R09-01).

**Model / effort:** `gpt-6-astra` / `xhigh`

**Sources:** reports `01` and `09`.

**Prompt:**

```text
Implement SYN-01/04/05 and SYN-84/R09-01. Add relational concurrency protection to
Agent credential transitions so stale writes cannot resurrect a revoked or
re-enrolled identity. Make whole-file override rendered blobs authorize through
the existing narrow capability graph without widening it. Prevent Agent auth
headers/bodies from following cross-origin redirects. At assignment creation,
enforce software.assignment.manage on the validated station resource so an
explicit station Deny defeats a global grant. Add PostgreSQL/HTTP negative
matrices. Keep current Deny precedence, token protections, and server-side ACL
authority.
```

**Task checklist:**

- [ ] Revoke/re-enroll race leaves exactly the intended credential valid.
- [ ] Legitimate rendered override downloads succeed; unrelated blobs fail.
- [ ] Cross-origin redirect receives no Agent credential.
- [ ] Global grant plus station Deny cannot create an assignment.

---

## AUTH-02 — Privileged reset and refresh-family concurrency

**Findings:** SYN-85, SYN-86, SYN-87 (sources R09-02, R09-03, R09-04).

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** report `09-identity-rbac-approvals-and-console-contract-report.md`.

**Prompt:**

```text
Implement SYN-85/86/87 (R09-02/03/04) as one server identity boundary. Revalidate current
grantability, reset-password, AuthVersion, refresh rotation, and
SingleDeviceLogin behavior. Prevent a delegated user manager from resetting a
more privileged or protected/system account unless an explicit capability and
target policy permits it. Bind login/refresh issuance and session replacement
to a database-enforced user security generation so a password/security mutation
cannot be bypassed by a late refresh and concurrent SingleDeviceLogin produces
one valid family. Add real PostgreSQL two-context tests. Do not weaken JWT
active/AuthVersion validation or use an in-process-only lock as the solution.
```

**Task checklist:**

- [ ] Delegated manager cannot take over a higher-privilege account.
- [ ] Password change defeats login/refresh issued from the old generation.
- [ ] Concurrent logins leave one valid refresh family when option is enabled.

---

## AUTH-03 — Browser authentication epoch and private-query isolation

**Findings:** SYN-88, SYN-89 (sources R09-05, R09-06).

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** report `09`.

**Prompt:**

```text
Implement SYN-88/89 (R09-05/06). Create one explicit browser authentication boundary for
restore, refresh, login, logout, `/me`, token persistence, replay queues, and
protected React Query data. Increment an epoch at identity changes; stale work
must not persist tokens, set the new user's profile, replay mutation, or populate
the new user's private cache. Preserve public cache only when deliberately
classified. Add deferred-promise/multi-tab-like tests for logout→login, late
refresh, late `/me`, and protected query results. Keep the Axios refresh
interceptor and AuthContext contracts coherent rather than adding a second
single-flight path.
```

**Task checklist:**

- [ ] Late old-session refresh or `/me` cannot restore that session.
- [ ] User B cannot see User A private query data in the same tab.
- [ ] Old mutation/replay is cancelled or rejected at the identity boundary.

---

## AUTH-04 — Approval visibility, action hints, and timed ACL cache

**Findings:** SYN-91, SYN-92, SYN-94 (sources R09-08, R09-09, R09-11).

**Model / effort:** `gpt-6-astra` / `xhigh`

**Sources:** report `09`.

**Prompt:**

```text
Implement SYN-91/92/94 (R09-08/09/11). Make approval eligibility filtering occur before
pagination/count or provide a correct continuation contract, so eligible work
cannot be lost behind unrelated newest records. Keep `/me` role/navigation
projection explicit, but give mutation UI target-aware effective capabilities
where ACL Allow/Deny must influence an action. Correct authorization cache TTL
to include the next start/end boundary of relevant grants, Denies, and
memberships, using one TimeProvider semantic for cached and batch evaluation.
Add actor/scope/time HTTP tests and fake-time cases. The server remains the final
authority.
```

**Task checklist:**

- [ ] Eligible request beyond 500 unrelated candidates is pageable and counted.
- [ ] Scoped ACL Allow opens only the applicable UI action; Deny does not.
- [ ] Cached and uncached authorization agree at scheduled start/end boundaries.

---

## AUTH-05 — Multi-step approval scope and stale-action contract

**Findings:** SYN-95, SYN-96 (sources R09-12, R09-13).

**Model / effort:** `gpt-6-astra` / `xhigh`

**Decision gate:** If multi-step policies with differing scopes are not a
product requirement, explicitly reject them instead of implementing full support.

**Prompt:**

```text
Resolve SYN-95/96 (R09-12/13). First determine whether mixed ScopeStrategy multi-step
approval is supported. If not, validate and reject such policies clearly. If it
is supported, snapshot per-step scope strategy/context and resolve approvers
against that snapshot. Bind approve/reject to the request Version and displayed
step, with idempotent retry semantics that return the original action result
rather than advancing a later step. Add PostgreSQL concurrency and two-tab/lost-
response tests. Preserve immutable target/change snapshots and existing requester
exclusion/current-policy checks.
```

**Task checklist:**

- [ ] Global→Team and Team→Global behavior is explicit and tested.
- [ ] A retry from a prior step cannot approve a following step.
- [ ] Policy edits after submit do not alter submitted step semantics.

---

## AGT-01 — Cancellation/catalog evidence gate

**Findings:** SYN-06, SYN-07.

**Model / effort:** `gpt-5.6-sol` / `high`

**Prompt:**

```text
Do not preemptively redesign the job schema. Reproduce or disprove SYN-06 and
SYN-07 using a fault matrix: network loss around cancellation delivery and
partial/stale/lost catalog after crash/restart. Trace current durable journal,
execution generation, registry, catalog, and reconciliation guards. If current
behavior meets an explicitly recorded offline cancellation/catalog contract,
write evidence and close verification only. If it fails, propose the smallest
separate implementation task with a failing regression. Do not change product
code unless a concrete defect is reproduced and approved in scope.
```

**Task checklist:**

- [ ] Offline cancellation latency/side-effect contract is explicit.
- [ ] Crash matrix covers catalog missing, stale, and partial states.
- [ ] No speculative schema change is made.

---

## AGT-02 — Process ownership, command receipt, and Agent isolation

**Findings:** SYN-02, SYN-43, SYN-44, SYN-46, SYN-51.

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** reports `01`, `06`, `07`.

**Prompt:**

```text
Implement SYN-02/43/44/46/51 as one owned-lifecycle boundary. Ensure remote
commands have durable receipt/idempotency semantics across lost ACK and restart.
Serialize the complete ProcessSupervisor lifecycle; cancellation during attach
must not leave an unmanaged process; supervised identity must bind to the actual
launched/attached process, not only an expected path/PID wrapper. Isolate support
storage failure so it cannot terminate the core Agent. Use LocalSystem-capable
fixtures, process barriers, restart/lost-response faults, and compatible
Launcher IPC tests. Do not use an OTA path under repair as the only recovery path.
```

**Task checklist:**

- [ ] One logical remote command has one durable outcome despite ACK loss.
- [ ] Exactly one owned payload process remains after concurrent lifecycle calls.
- [ ] Attach cancellation cleans up or durably adopts process ownership.
- [ ] Support persistence failure leaves heartbeat/poll core alive.

---

## AGT-03 — Launcher and runtime small lifecycle fixes

**Findings:** SYN-45, SYN-47, SYN-48, SYN-49, SYN-50.

**Model / effort:** `gpt-5.6-sol` / `high`

**Sources:** report `06`.

**Prompt:**

```text
Implement SYN-45/47/48/49/50 only, keeping the IPC SID/session policy and
request bounds intact. A health timeout must not terminate the whole probe loop;
an old Launcher session must not block a current console; repeated Run must be
coalesced; large/missing icon data must not hide status or mutation result; and
Stop/Restart must surface a false/failed outcome rather than success. Add focused
Agent/Launcher tests and build the net48 Launcher. Coordinate with AGT-02 rather
than redefining process ownership here.
```

**Task checklist:**

- [ ] One probe failure leaves later probes running.
- [ ] Run action is single-flight/coalesced.
- [ ] IPC failure outcome is visible to the caller.
- [ ] Launcher net48 build passes.

---

## OTA-01 — Crash-atomic OTA switch, rollback, and bundle parity

**Findings:** SYN-33, SYN-34, SYN-35, SYN-36, SYN-37.

**Model / effort:** `gpt-6-astra` / `max`

**Sources:** report `05-agent-release-and-self-update-report.md`.

**Prompt:**

```text
Implement SYN-33/34/35/36/37 together. Harden ACLs on all OTA state and bundle
paths; make apply journal/switch/cleanup recover as exactly OLD or NEW; clear an
apply latch on all launch/helper failure paths; and define one server/Agent
minimum bundle contract that includes required Launcher dependencies and the
deployed bootstrap/helper compatibility. Add signed A→B→C, SCM/LocalSystem,
reboot, kill-at-each-phase, lock, disk-full and no-readiness tests. Preserve
token/catalog/outbox/PreserveLocal state during rollback. Do not deploy to a
station until a rollback canary passes.
```

**Task checklist:**

- [ ] Fault at every phase produces all-old or all-new, never a mixed tree.
- [ ] Retry can proceed after helper/launch failure.
- [ ] Required Agent and Launcher files are present and validated as one bundle.
- [ ] Standard user cannot modify OTA recovery state.

---

## OTA-02 — OTA packaging, transport bound, retention, and key rotation

**Findings:** SYN-38, SYN-39, SYN-40, SYN-41, SYN-42.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Prompt:**

```text
Implement SYN-38/39/40/41 and prepare the decision/runbook for SYN-42. Ensure
packaging cannot include stale Launcher DLL output; bound OTA body reads so a
stalled download does not indefinitely hold the control plane; consume update
result only after server acknowledgement; add reference-aware staging retention;
and design KeyId/trust-set transitional signing and compromised-key recovery
without embedding private material. Add packaging, stalled-body, lost-response,
retention, K1→K1/K2→K2 and unauthorized-K3 tests. Treat key rollout as a
separate canary/recovery release.
```

**Task checklist:**

- [ ] Manifest matches fresh build output byte-for-byte.
- [ ] Stalled OTA body obeys a deadline/cancellation path.
- [ ] Result retry converges without duplicate consumption.
- [ ] Key transition and revocation runbook is tested with fixtures only.

---

## CFG-01 — Effective configuration plan and render correctness

**Findings:** SYN-17, SYN-18, SYN-20, SYN-21.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Sources:** report `04-config-overrides-baselines-and-approval-report.md`.

**Prompt:**

```text
Implement SYN-17/18/20/21 by introducing the smallest shared effective-config
plan used by baseline validation, preview, pin, render, and manifest delivery.
Do not rewrite the whole configuration system. A render fallback/partial result
must be typed failure, never publishable success. Make mirror/migration semantics
explicit and preserve revision/generation behavior so stale computation cannot
publish current-looking bytes. Begin with pure-plan parity tests, then add
PostgreSQL barriers and real renderer fixtures. Preserve Active value while a
replacement is pending approval.
```

**Task checklist:**

- [ ] Baseline, preview, pin, and manifest agree on effective bytes/sources.
- [ ] Partial/fallback output cannot publish or deploy.
- [ ] Stale render/revision cannot overwrite a newer state.

---

## CFG-02 — Config invalidation, approval revalidation, and DTO contract

**Findings:** SYN-26, SYN-27, SYN-29, SYN-30, SYN-32.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Prompt:**

```text
Implement SYN-26/27/29/30/32 after CFG-01. Define invalidation and revision
semantics when sources, rules, targets, or approvals change; expose DTOs that
make pending/current/effective state unambiguous; and revalidate approval at the
right boundary without reconstructing immutable submitted intent from live data.
Obtain/record the decision whether baseline is a pin-time gate or a continuously
enforced invariant. Add PostgreSQL concurrent edit/approve/pin tests and frontend
contract tests. Preserve historical target/change snapshots.
```

**Task checklist:**

- [ ] Stale revision/action gets conflict, never silently applies.
- [ ] UI distinguishes active, pending, rejected, and current effective state.
- [ ] Approval cannot promote bytes invalid under the agreed policy.

---

## CFG-03 — Legacy conversion and multi-target configuration migration

**Findings:** SYN-22, SYN-23, SYN-24, SYN-25.

**Model / effort:** `gpt-6-astra` / `max`

**Prompt:**

```text
Implement SYN-22/23/24/25 only after CFG-01/02 and an approved decision on
mirror/fan-out semantics. Build a dry-run-first, transactionally safe legacy
conversion path that preserves source evidence and distinguishes atomic apply-all
from explicit partial outcome. Do not delete retained OverrideFile compatibility
or purge legacy data casually. Add PostgreSQL migration/fault tests for every
scope, failed destination, retry, approval lifecycle, and rollback. Verify actual
station bytes/PreserveLocal behavior in an isolated canary before any cohort.
```

**Task checklist:**

- [ ] Dry run reports exact changes and blockers without mutation.
- [ ] No orphan Pending approval or partial silent fan-out remains.
- [ ] Legacy rollback/redeploy restores actual file behavior.

---

## SUP-01 — Durable production-support graph recovery

**Findings:** SYN-52, SYN-53, SYN-55, SYN-56, SYN-57.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Sources:** report `07-production-support-durable-workflow-report.md`.

**Prompt:**

```text
Implement SYN-52/53/55/56/57. Bind support update/correction actions to the
exact request identity observed by the UI; make correction handle failed-resolved
and failed predecessor graphs; make retry of a closed request converge; reclaim
InFlight safely after combined delivery/retry-record faults; and enforce auth
scope cache isolation after purge. Keep exact payload/receipt IDs and existing
SQLite graph rather than replacing it with a broker. Add SQLite, PostgreSQL,
HTTP lost-response, and React observer tests.
```

**Task checklist:**

- [ ] Stale dialog cannot update another/newer request.
- [ ] Graph correction/retry reaches a stable state without duplicate nodes.
- [ ] Purged old scope cannot repopulate current browser data.

---

## SUP-02 — Support retention, freshness, enum, and docs correctness

**Findings:** SYN-54, SYN-59, SYN-60, SYN-61.

**Model / effort:** `gpt-5.6-sol` / `high`

**Prompt:**

```text
Implement SYN-54/59/60/61. Prevent reconcile from resetting the original
closed/retention timestamp; represent sync status as current freshness rather
than any historical success; restore strict enum rejection at the HTTP boundary
without breaking intended API serialization; and update support documentation to
match current Agent/API composition. Add clock, MVC negative, and frontend state
tests. Do not implement a generic DomainEvent outbox in this task.
```

**Task checklist:**

- [ ] Closed graph expires at the intended original retention time.
- [ ] UI marks stale state after freshness expiry.
- [ ] Invalid enum is rejected by HTTP contract test.
- [ ] Docs identify actual current stage/API behavior.

---

## ING-01 — Scanner overlap, metric monotonicity, and event semantics

**Findings:** SYN-63, SYN-65, SYN-66, SYN-67.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Sources:** report `08-test-result-ingestion-and-monitoring-report.md`.

**Prompt:**

```text
Implement SYN-63/65/66/67. First obtain a product decision for cross-source test
identity and event time/timezone; do not backfill history before that decision.
Then ensure scanner overlap retains late files until replay capacity exists,
metrics snapshots cannot roll back newer counters, and source/time provenance is
explicit in the ingestion model. Add filesystem restart/batch-overlap tests,
service ordering tests, and migration/backfill tests only after the identity
contract is approved. Keep cursor acknowledgement atomic.
```

**Task checklist:**

- [ ] More-than-batch overlap drains each logical event once.
- [ ] Metrics never decrease due to an older snapshot race.
- [ ] Identity/time semantics are documented before schema/backfill work.

---

## ARCH-01 — DomainEventDispatcher decision and truthful semantics

**Findings:** SYN-83.

**Model / effort:** `gpt-5.6-terra` / `high` for logging-only; `gpt-6-astra` /
`xhigh` only if a real consumer/SLA is approved.

**Prompt:**

```text
Resolve SYN-83 as a product/architecture decision. Identify actual DomainEvent
consumers and required side effects/SLA. If none exist, rename/document the
current dispatcher as logging/audit-only and ensure no code or runbook calls it
a transactional outbox. If a consumer is approved, stop and write a separate
implementation design covering transaction boundary, idempotency, retries,
poison handling, replay, and PostgreSQL crash/duplicate tests before coding.
Do not build a generic broker, CQRS layer, or microservice platform speculatively.
```

**Task checklist:**

- [ ] Consumer and delivery requirement are explicitly recorded.
- [ ] Current behavior is named truthfully.
- [ ] No outbox platform is built without an approved side effect contract.

---

## CLS-01 — Cross-cutting verification and closure

**Findings:** all completed tasks.

**Model / effort:** `gpt-6-astra` / `xhigh`

**Prompt:**

```text
Perform closure review only; do not expand scope. For each completed task,
revalidate the original finding on current HEAD, inspect focused and subsystem
tests, and check that required provider/runtime evidence exists: PostgreSQL for
relational races, Local+MinIO for CAS, LocalSystem/net48 for station lifecycle,
IIS/two-version/TLS for deploy, and browser behavior for auth/UI races. Confirm
old/new compatibility and a practical rollback for every station or schema
change. Update PROJECT_REVIEW_CHECKLIST.md and PROJECT_MEMORY.md only for items
with evidence meeting their stated close criteria. Report remaining risks as open
rather than inferring success from code inspection.
```

**Task checklist:**

- [ ] Every closed item links source finding, tests, environment, and result.
- [ ] No live credential, private key, token, or transient artifact status enters
      durable documentation.
- [ ] Runtime gates were not replaced by unit-test claims.
- [ ] Remaining unverified environment work stays open.

## Global release checklist

- [x] `META-00` completed before changing canonical prioritization.
- [ ] Deployment recovery gates pass before deploying remediation to production.
- [ ] CAS changes pass Local, MinIO, and PostgreSQL tests before release.
- [ ] Auth changes pass HTTP and PostgreSQL race tests before relying on ACL or
      delegated administration.
- [ ] Agent/OTA changes pass LocalSystem, rollback, and compatible Launcher
      checks before station canary.
- [ ] Config migration passes dry-run, PostgreSQL, and actual-byte canary checks.
- [ ] No task marks a historical checklist item complete without its required
      environment evidence.
