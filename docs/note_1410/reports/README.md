# Astra review reports

Save one report for each completed prompt using these names:

| Prompt | Report |
| --- | --- |
| `00-architecture.md` | `00-architecture-report.md` |
| `01-agent-enrollment-and-control-plane.md` | `01-agent-enrollment-and-control-plane-report.md` |
| `02-station-deployment-job-lifecycle.md` | `02-station-deployment-job-lifecycle-report.md` |
| `03-software-content-upload-and-blob-lifecycle.md` | `03-software-content-upload-and-blob-lifecycle-report.md` |
| `04-config-overrides-baselines-and-approval.md` | `04-config-overrides-baselines-and-approval-report.md` |
| `05-agent-release-and-self-update.md` | `05-agent-release-and-self-update-report.md` |
| `06-launcher-ipc-and-process-supervision.md` | `06-launcher-ipc-and-process-supervision-report.md` |
| `07-production-support-durable-workflow.md` | `07-production-support-durable-workflow-report.md` |
| `08-test-result-ingestion-and-monitoring.md` | `08-test-result-ingestion-and-monitoring-report.md` |
| `09-identity-rbac-approvals-and-console-contract.md` | `09-identity-rbac-approvals-and-console-contract-report.md` |
| `10-server-deployment-database-and-recovery.md` | `10-server-deployment-database-and-recovery-report.md` |

Each report must identify the prompt version/repository revision reviewed, list all required finding fields, and preserve the prompt's good decisions, rejected suspicions, and insufficient-evidence sections. Do not place credentials, tokens, private keys, payload data, or copied operational logs in a report.

`11-final-synthesis.md` consumes the completed reports and creates `11-final-synthesis-report.md` in this directory.
