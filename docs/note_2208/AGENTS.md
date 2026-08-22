# MProject Working Context

Before changing this repository, read `PROJECT_MEMORY.md` and
`PROJECT_REVIEW_CHECKLIST.md`. They contain the verified monorepo map, product
boundary, backend/Agent/Launcher flows, current open risks, remediation order,
test requirements, and close criteria.

Use current code, tests, and scripts as the source of truth. Treat local docs and
Claude memory as historical evidence until reverified. Never copy credentials,
artifact status, test counts, or commit status into durable memory.

For user questions and conclusions, communicate in Vietnamese unless the user
requests another language. Preserve existing contracts and architecture, keep
changes narrowly scoped, and add focused tests for behavior changes. Review the
`Current open risk` entries in `PROJECT_MEMORY.md` before touching software
deployment, approvals/RBAC, uploads/blob GC, Agent authentication, migrations,
or factory installation.
