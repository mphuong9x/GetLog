# QK-03 acceptance evidence

Date: 2026-09-13

Scope: SYN-74, SYN-77 and SYN-81 only.

## Implemented contracts

- All release public-key entry points use the shared PowerShell 5.1-compatible
  `agent-release-key-contract.ps1`. It accepts one structurally valid RSA public
  PEM block, emits a normalized public PEM, and rejects private markers, mixed
  content, malformed DER and invalid RSA parameters before any artifact or
  configuration write.
- `prepare-deploy.ps1` writes only the normalized public PEM and ships the shared
  validator with the server artifact. Agent signing remains isolated behind
  `-SignWith`; private material is never copied to a distributable path.
- The existing DEP-02 retention fence is locked by a focused regression:
  `KeepBackups=0` may remove older completed sets but preserves the active
  `Api.zip` and `db.dump` pair.
- Local deployment health accepts only absolute HTTPS and leaves Node certificate
  chain, hostname and expiry verification enabled.

## Acceptance commands

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  scripts\test-deployment-input-guards.ps1`: exit 0; five focused cases passed.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
  scripts\test-deployment-maintenance.ps1`: exit 0; 15 maintenance/recovery
  cases passed, including active-pair retention.
- `dotnet test MProjectBackend\MProject.Tests\MProject.Tests.csproj
  --no-restore -v minimal --filter FullyQualifiedName~DeploymentScriptTests`:
  exit 0; 3 passed, 0 failed.
- Windows PowerShell parser validation: all eight changed/integrated scripts
  parsed successfully.
- `git diff --check`: exit 0; Git emitted line-ending warnings only. A separate
  trailing-whitespace scan of the two new files returned no matches.

The project-integrated test supplies its current API output directory to the
deployment-contract fixture. A direct invocation without `-ApiDirectory` uses
the optional `bin/DEP01` test output and is not a valid gate unless that output
has first been created.

No IIS or certificate-store drill was run for this quick fix. The disposable
fixture checks the HTTPS-only policy and propagates simulated Node TLS failures;
source assertions confirm that Node's certificate validation is enabled.

## Rollback

Revert the QK-03 script, test and documentation changes together. Server
artifacts prepared after this change depend on `agent-release-key-contract.ps1`,
so do not remove only the helper while retaining installers that dot-source it.
