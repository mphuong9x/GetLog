<#
Review-only probes: extract function bodies from the current scripts, replace IIS/DB
operations with deterministic stubs, and use synthetic files below repository .tmp.
No live IIS, certificates, credentials or database are read or changed.
Run: powershell -NoProfile -ExecutionPolicy Bypass -File <this-file>
Observed defect flags are expected to be true for the reviewed revision.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$tmpParent = [IO.Path]::GetFullPath((Join-Path $repo '.tmp'))
$fixture = Join-Path $tmpParent ('review10-' + [guid]::NewGuid().ToString('N'))
if (-not ([IO.Path]::GetFullPath($fixture)).StartsWith($tmpParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped .tmp.' }
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$results = [ordered]@{}
function Get-FunctionSource([string]$File, [string]$Name) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo $File), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "Parse errors: $File" }
    $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $true)
    if (-not $node) { throw "Missing function: $Name" }
    return $node.Extent.Text
}
function Write-Step { param($m) }
function Write-Ok { param($m) }
function Write-Info { param($m) }
function Write-Warn { param($m) }
try {
    foreach ($name in @('Restore-Backup', 'Assert-MigrationSafety', 'Remove-OldBackups')) {
        . ([scriptblock]::Create((Get-FunctionSource 'scripts\update-server.ps1' $name)))
    }
    foreach ($name in @('Write-BackendAppSettings', 'Copy-BackendArtifact', 'Apply-Schema', 'Get-UsableCertificate', 'Set-AgentReleasePublicKey')) {
        . ([scriptblock]::Create((Get-FunctionSource 'scripts\install-server.ps1' $name)))
    }
    $BackendPublishPath = Join-Path $fixture 'live'
    $BackendAppPool = 'review-only-stub'
    New-Item -ItemType Directory -Path $BackendPublishPath -Force | Out-Null
    $old = Join-Path $fixture 'old'
    New-Item -ItemType Directory -Path $old -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $old 'MProject.Api.dll') -Value 'old-fixture-binary'
    Set-Content -LiteralPath (Join-Path $old 'appsettings.json') -Value '{"AgentRelease":{"PublicKeyPem":"old-public-key-fixture"}}'
    $archive = Join-Path $fixture 'old.zip'
    Compress-Archive -Path (Join-Path $old '*') -DestinationPath $archive
    function Stop-BackendPool { Set-Content -LiteralPath (Join-Path $BackendPublishPath 'app_offline.htm') -Value 'offline'; $script:poolStarted = $false }
    function Start-WebAppPool { param($Name, $ErrorAction) $script:poolStarted = $true }
    function Restore-Database { param($DumpPath) throw 'Synthetic restore failure.' }
    $script:poolStarted = $false
    $caught = $false
    try { Restore-Backup -Zip $archive -DatabaseDump 'fixture-only.dump' } catch { $caught = $true }
    $results.RestoreFailureRestartsPool = $caught -and $script:poolStarted
    $results.RestoreFailureRemovesOffline = -not (Test-Path (Join-Path $BackendPublishPath 'app_offline.htm'))

    Set-Content -LiteralPath (Join-Path $BackendPublishPath 'appsettings.json') -Value '{"AgentRelease":{"PublicKeyPem":"new-public-key-fixture"}}'
    function Restore-Database { param($DumpPath) }
    Restore-Backup -Zip $archive -DatabaseDump 'fixture-only.dump'
    $cfg = Get-Content -Raw -LiteralPath (Join-Path $BackendPublishPath 'appsettings.json') | ConvertFrom-Json
    $results.RollbackRetainsNewPublicKey = $cfg.AgentRelease.PublicKeyPem -eq 'new-public-key-fixture'

    $incoming = Join-Path $fixture 'incoming'
    New-Item -ItemType Directory -Path $incoming -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $incoming 'MProject.Api.dll') -Value 'new-fixture-binary'
    Set-Content -LiteralPath (Join-Path $incoming 'web.config') -Value '<configuration />'
    Set-Content -LiteralPath (Join-Path $BackendPublishPath 'obsolete.dll') -Value 'old-only-file'
    function Get-WebAppPoolState { param($Name) return @{ Value = 'Stopped' } }
    Copy-BackendArtifact -Src $incoming
    $results.ForwardCopyRetainsObsoleteFile = Test-Path (Join-Path $BackendPublishPath 'obsolete.dll')

    $OverwriteAppSettings = $true
    $AgentTokenPepper = 'fixture-only'; $RefreshTokenPepper = 'fixture-only'
    $DatabaseHost = '127.0.0.1'; $DatabaseName = 'fixture_live'; $DatabaseUser = 'fixture'
    $DatabasePassword = 'fixture;Application Name=split'
    $JwtKey = 'fixture-only'; $InstallerToken = ''; $AdminPassword = 'fixture-only'
    $LanIp = '127.0.0.1'; $HostName = 'fixture'; $DataRoot = Join-Path $fixture 'data'
    Write-BackendAppSettings
    $cfg = Get-Content -Raw -LiteralPath (Join-Path $BackendPublishPath 'appsettings.json') | ConvertFrom-Json
    $parsed = New-Object System.Data.Common.DbConnectionStringBuilder
    $parsed.ConnectionString = $cfg.ConnectionStrings.Default
    $results.PasswordDoesNotRoundTrip = [string]$parsed['Password'] -cne $DatabasePassword
    $results.FreshConfigWatchdog30And180 = $cfg.InstallationJobWatchdog.InactivityTimeoutMinutes -eq 30 -and $cfg.InstallationJobWatchdog.MaxAttemptDurationMinutes -eq 180
    $results.FreshConfigAutoMigrateFalse = $cfg.Database.AutoMigrate -eq $false
    $OverwriteAppSettings = $false
    $DatabaseName = 'different_cli_database'
    Write-BackendAppSettings
    $cfg = Get-Content -Raw -LiteralPath (Join-Path $BackendPublishPath 'appsettings.json') | ConvertFrom-Json
    $parsed.ConnectionString = $cfg.ConnectionStrings.Default
    $results.PreservedDatabaseDiffersFromCli = [string]$parsed['Database'] -ne $DatabaseName

    $ArtifactsDir = Join-Path $fixture 'manifest'
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $ArtifactsDir 'migration-safety.json') -Value '{"schemaVersion":1}'
    function Invoke-PsqlFile { param($Database, $Sql, [switch]$TuplesOnly) return '20260101000000_Old' }
    $SkipSchema = $false; $AllowDestructiveMigration = $false
    $rejected = $false
    try { Assert-MigrationSafety } catch { $rejected = $true }
    $results.IncompleteManifestRejected = $rejected
    function Find-Psql { return $null }
    Apply-Schema -SchemaPath (Join-Path $fixture 'nonexistent-schema.sql')
    $results.MissingPsqlReturnsNormally = $true

    $prepare = Get-Content -Raw -LiteralPath (Join-Path $repo 'scripts\prepare-deploy.ps1')
    $start = $prepare.IndexOf('    $destructiveMigrations = @()')
    $end = $prepare.IndexOf('    $migrationSafety = [ordered]@{', $start)
    if ($start -lt 0 -or $end -lt 0) { throw 'Classifier boundaries changed; inspect before running.' }
    $backendRoot = Join-Path $fixture 'classifier'
    $migrations = Join-Path $backendRoot 'MProject.Infrastructure\Migrations'
    New-Item -ItemType Directory -Path $migrations -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $migrations '20260102000000_RawSql.cs') -Value 'protected override void Up(MigrationBuilder migrationBuilder) { migrationBuilder.Sql("DROP TABLE critical;"); } protected override void Down(MigrationBuilder migrationBuilder) {}'
    Set-Content -LiteralPath (Join-Path $migrations '20260103000000_Expression.cs') -Value 'protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "critical"); protected override void Down(MigrationBuilder migrationBuilder) {}'
    Set-Content -LiteralPath (Join-Path $migrations '20260104000000_Normal.cs') -Value 'protected override void Up(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable(name: "critical"); } protected override void Down(MigrationBuilder migrationBuilder) {}'
    . ([scriptblock]::Create($prepare.Substring($start, $end - $start)))
    $results.ClassifierMissesRawSqlAndExpressionBody = $destructiveMigrations.Count -eq 1 -and $destructiveMigrations[0].migrationId -eq '20260104000000_Normal'

    $AgentReleasePublicKeyPath = Join-Path $fixture 'not-a-real-key.pem'
    Set-Content -LiteralPath $AgentReleasePublicKeyPath -Value "-----BEGIN PRIVATE KEY-----`nfixture-not-a-key`n-----END PRIVATE KEY-----"
    Set-AgentReleasePublicKey
    $cfg = Get-Content -Raw -LiteralPath (Join-Path $BackendPublishPath 'appsettings.json') | ConvertFrom-Json
    $results.PublicKeySetterAcceptsPrivateKeyLabel = $cfg.AgentRelease.PublicKeyPem.Contains('BEGIN PRIVATE KEY')

    $BackupRoot = Join-Path $fixture 'backups'
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $BackupRoot 'Api_20260909-010000.zip') -Value 'fixture'
    Set-Content -LiteralPath (Join-Path $BackupRoot 'fixture_20260909-010000.dump') -Value 'fixture'
    $KeepBackups = 0
    Remove-OldBackups
    $results.KeepZeroDeletesCurrentRecoveryPair = @(Microsoft.PowerShell.Management\Get-ChildItem -LiteralPath $BackupRoot).Count -eq 0

    $script:fakeCertificates = @(
        [pscustomobject]@{Subject='CN=fixture'; HasPrivateKey=$true; NotAfter=(Get-Date).AddYears(1); DnsNameList=@([pscustomobject]@{Unicode='fixture'}); EnhancedKeyUsageList=@([pscustomobject]@{ObjectId='1.3.6.1.5.5.7.3.1'}); Thumbprint='current-bound'},
        [pscustomobject]@{Subject='CN=fixture'; HasPrivateKey=$true; NotAfter=(Get-Date).AddYears(2); DnsNameList=@([pscustomobject]@{Unicode='fixture'}); EnhancedKeyUsageList=@([pscustomobject]@{ObjectId='1.3.6.1.5.5.7.3.1'}); Thumbprint='future-rollout'}
    )
    function Get-ChildItem { param($Path) return $script:fakeCertificates }
    $selected = Get-UsableCertificate -Name 'fixture'
    Remove-Item Function:\Get-ChildItem
    $results.CertificateChoosesLaterExpiry = $selected.Thumbprint -eq 'future-rollout'

    [pscustomobject]$results | ConvertTo-Json
    if (@($results.Values | Where-Object { $_ -ne $true }).Count) { throw 'A probe observation differed; inspect the reviewed code.' }
}
finally {
    Remove-Item Function:\Get-ChildItem -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($tmpParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup escaped .tmp.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
