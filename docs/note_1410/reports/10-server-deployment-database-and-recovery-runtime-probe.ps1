[CmdletBinding()]
param([string]$ArtifactRoot = '', [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $ArtifactRoot) { $ArtifactRoot = Join-Path $repo '.tmp\review10-artifact' }
$root = Join-Path $repo ('.tmp\review10-pg-' + [guid]::NewGuid().ToString('N'))
$pg = $PostgresBin
$data = Join-Path $root 'data'
$apiDir = Join-Path $ArtifactRoot 'server\Api'
$inputSchema = Join-Path $ArtifactRoot 'schema.sql'
$schema = Join-Path $root 'schema.sql'
$savedEnv = @{}
$apiProcess = $null; $pgStarted = $false
$result = [ordered]@{}
function Port { $l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $l.Start(); try { return ([Net.IPEndPoint]$l.LocalEndpoint).Port } finally { $l.Stop() } }
$pgPort = Port; $apiPort = Port
function PgCtl([string[]]$Arguments) { $p=Start-Process -FilePath (Join-Path $pg 'pg_ctl.exe') -ArgumentList $Arguments -WindowStyle Hidden -PassThru; $p.WaitForExit(); if ($p.ExitCode -ne 0) { throw 'pg_ctl failed' } }
function Sql([string]$Text) { $path=Join-Path $root 'query.sql'; [IO.File]::WriteAllText($path,$Text,(New-Object Text.UTF8Encoding($false))); $out=@(& (Join-Path $pg 'psql.exe') -X -q -tA -h 127.0.0.1 -p $pgPort -U postgres -d review10 -v ON_ERROR_STOP=1 -f $path); if ($LASTEXITCODE -ne 0) { throw 'fixture SQL failed' }; return ($out -join "`n").Trim() }
function RandomSecret { $b=New-Object byte[] 48; $rng=[Security.Cryptography.RandomNumberGenerator]::Create(); try {$rng.GetBytes($b)} finally {$rng.Dispose()}; return [Convert]::ToBase64String($b) }
function StopApi { if ($script:apiProcess) { if (-not $script:apiProcess.HasExited) { $script:apiProcess.Kill(); $script:apiProcess.WaitForExit(10000) | Out-Null }; $script:apiProcess.Dispose(); $script:apiProcess=$null } }
function StartApi([string]$Tag) {
    $script:apiProcess=Start-Process -FilePath (Join-Path $apiDir 'MProject.Api.exe') -WorkingDirectory $apiDir -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root ($Tag+'.stdout')) -RedirectStandardError (Join-Path $root ($Tag+'.stderr'))
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 60) { if ($script:apiProcess.HasExited) { throw 'Fixture API exited' }; try { $r=Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$apiPort/health/ready" -TimeoutSec 2; if ($r.StatusCode -eq 200) { return $r } } catch {}; Start-Sleep -Milliseconds 500 }
    throw 'API readiness timeout'
}
New-Item -ItemType Directory -Force -Path $root | Out-Null
try {
    $savedEnv['PGOPTIONS']=$env:PGOPTIONS; $env:PGOPTIONS='-c client_min_messages=warning'
    & (Join-Path $pg 'initdb.exe') -D $data -U postgres --auth=trust --encoding=UTF8 --no-locale | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'initdb failed' }
    PgCtl @('-D', $data, '-l', (Join-Path $root 'pg.log'), '-o', ('"-h 127.0.0.1 -p '+$pgPort+'"'), 'start', '-w')
    $pgStarted=$true
    & (Join-Path $pg 'psql.exe') -X -q -h 127.0.0.1 -p $pgPort -U postgres -d postgres -c 'CREATE DATABASE review10;' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Create fixture failed' }
    [IO.File]::WriteAllText($schema,[IO.File]::ReadAllText($inputSchema),(New-Object Text.UTF8Encoding($false)))
    foreach ($iteration in 1..2) { & (Join-Path $pg 'psql.exe') -X -q -h 127.0.0.1 -p $pgPort -U postgres -d review10 -v ON_ERROR_STOP=1 -f $schema 2> (Join-Path $root 'schema.stderr') | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Schema apply failed' } }
    $result.IdempotentSchemaAppliedTwice=$true
    $result.MigrationHead=Sql 'SELECT MAX("MigrationId") FROM "__EFMigrationsHistory";'
    $result.MigrationCount=[int](Sql 'SELECT COUNT(*) FROM "__EFMigrationsHistory";')
    $savedEnv['PATH']=$env:PATH; $env:PATH=$pg+';'+$env:PATH
    $drill = & (Join-Path $repo 'scripts\test-database-backup-restore.ps1') -ConnectionString "Host=127.0.0.1;Port=$pgPort;Database=review10;Username=postgres;Password=fixture-only"
    $result.BackupRestoreSucceeded=[bool]$drill.Succeeded
    $result.BackupRestoreSeconds=$drill.RestoreElapsedSeconds
    $overrides=@{
        'ASPNETCORE_ENVIRONMENT'='Production'; 'DOTNET_ENVIRONMENT'='Production'; 'ASPNETCORE_URLS'="http://127.0.0.1:$apiPort"; 'ASPNETCORE_CONTENTROOT'=$apiDir;
        'ConnectionStrings__Default'="Host=127.0.0.1;Port=$pgPort;Database=review10;Username=postgres;Pooling=false"; 'Database__AutoMigrate'='false';
        'JwtKey'=(RandomSecret); 'Agent__TokenPepper'=(RandomSecret); 'AuthTokens__RefreshTokenPepper'=(RandomSecret); 'Seed__AdminPassword'=(RandomSecret);
        'Storage__Provider'='Local'; 'Storage__Local__RootPath'=(Join-Path $root 'storage'); 'Repository__RootPath'=(Join-Path $root 'git'); 'TusUpload__TempStoragePath'=(Join-Path $root 'tus');
        'BlobGc__Enabled'='false'; 'InstallationJobWatchdog__Enabled'='false'; 'StationRollback__Enabled'='false'; 'ComputerLiveness__Enabled'='false'; 'TusTempCleanup__Enabled'='false'; 'DomainEventDispatcher__Enabled'='false'
    }
    foreach ($key in $overrides.Keys) { $savedEnv[$key]=[Environment]::GetEnvironmentVariable($key,'Process'); [Environment]::SetEnvironmentVariable($key,$overrides[$key],'Process') }
    $ready=StartApi 'healthy'
    $result.ReleaseProductionReadyStatus=[int]$ready.StatusCode
    $result.ReleaseProductionReadyDatabase=($ready.Content | ConvertFrom-Json).Database
    $result.MetricsAnonymousStatus=[int](Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$apiPort/metrics" -TimeoutSec 5).StatusCode
    StopApi
    Sql 'ALTER TABLE "UploadReservations" DROP COLUMN "TusFileId"; DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = ''20260906090000_BindTusSession'';' | Out-Null
    $ready=StartApi 'stale-schema'
    $result.MissingLatestMigrationReadyStatus=[int]$ready.StatusCode
    $result.MissingLatestMigrationReadyDatabase=($ready.Content | ConvertFrom-Json).Database
    $result.MissingTusColumn=(Sql 'SELECT COUNT(*) FROM information_schema.columns WHERE table_name=''UploadReservations'' AND column_name=''TusFileId'';') -eq '0'
    $result.PreservedStaleHead=Sql 'SELECT MAX("MigrationId") FROM "__EFMigrationsHistory";'
    StopApi
    [pscustomobject]$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '10-server-deployment-database-and-recovery-runtime-results.json') -Encoding UTF8
    [pscustomobject]$result | ConvertTo-Json
}
finally {
    StopApi
    foreach ($key in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($key,$savedEnv[$key],'Process') }
    if ($pgStarted) { PgCtl @('-D',$data,'stop','-m','fast','-w') }
    $resolved=[IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith((Join-Path $repo '.tmp\review10-pg-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup target' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
