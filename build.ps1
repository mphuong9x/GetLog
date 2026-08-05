# Build the portable exe: one self-contained single file in .\publish that runs on any
# Windows 10/11 x64 machine, including ones that only have .NET Framework 4.8 installed
# (the .NET 8 runtime is bundled inside the exe, nothing to install on the target machine).
#
#   powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Remove-Item (Join-Path $root 'publish\*') -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish (Join-Path $root 'LogEOT.UI\LogEOT.UI.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o (Join-Path $root 'publish')

if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE) — close LogEOT.UI.exe if it is running" }

Get-ChildItem (Join-Path $root 'publish')
