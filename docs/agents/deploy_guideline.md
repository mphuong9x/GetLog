# Deploy MProject Len LAN Bang IIS

Tai lieu nay tom tat cac buoc deploy da thuc hien tren may dev ca nhan.

Moi truong da dung:

- Frontend: React/Vite, host bang IIS port `80` va `443`.
- Backend: ASP.NET Core, host bang IIS port `8081` va `8443`.
- Database: PostgreSQL local, database `TESSDB`.
- IP LAN may dev: `10.197.246.231`.
- Frontend HTTP URL: `http://10.197.246.231`.
- Backend HTTP URL: `http://10.197.246.231:8081`.
- Frontend HTTPS URL: `https://tess`.
- Backend HTTPS URL: `https://tess:8443`.
- Local hostname: `tess` -> `10.197.246.231`.

Khi deploy tren may khac, thay `10.197.246.231` bang IP LAN cua may do. Neu dung HTTPS, tao lai certificate co SAN phu hop voi hostname muon dung.

## 1. Kiem Tra IP LAN

Chay o PowerShell bat ky:

```powershell
ipconfig
```

Muc dich: lay dia chi IPv4 cua card mang dang dung.

Ket qua da dung:

```txt
IPv4 Address: 10.197.246.231
```

## 2. Bat IIS Neu Chua Co

Chay o PowerShell Administrator:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole,IIS-WebServer,IIS-ManagementConsole,IIS-StaticContent,IIS-DefaultDocument,IIS-HttpErrors,IIS-HttpRedirect -All
iisreset
Get-Service W3SVC
```

Muc dich:

- Cai/bat IIS.
- Restart IIS.
- Kiem tra service `World Wide Web Publishing Service` dang `Running`.

## 3. Backup Cau Hinh IIS Truoc Khi Reset

Chay o PowerShell Administrator:

```powershell
cd $env:windir\system32\inetsrv
.\appcmd.exe add backup "before-mproject-iis-reset"
.\appcmd.exe list backup
```

Muc dich: tao backup de co the restore neu xoa nham config IIS.

Lenh restore neu can:

```powershell
cd $env:windir\system32\inetsrv
.\appcmd.exe restore backup "before-mproject-iis-reset"
```

## 4. Xoa Site Va App Pool Cu Neu Muon Tap Lai Tu Dau

Chay o PowerShell Administrator:

```powershell
cd C:\Windows\system32\inetsrv
.\appcmd.exe list site
.\appcmd.exe list apppool
```

Muc dich: xem cac site/app pool cu.

Trong lan deploy nay da xoa:

- Site: `TESS-FE`, `TESS-BE`.
- App pool: `TESS-FE`, `TESS-BE`.

Lenh xoa:

```powershell
.\appcmd.exe stop site "TESS-FE"
.\appcmd.exe stop site "TESS-BE"
.\appcmd.exe delete site "TESS-FE"
.\appcmd.exe delete site "TESS-BE"
.\appcmd.exe delete apppool "TESS-FE"
.\appcmd.exe delete apppool "TESS-BE"
```

Kiem tra lai:

```powershell
.\appcmd.exe list site
.\appcmd.exe list apppool
```

## 5. Kiem Tra Port

Chay o PowerShell Administrator:

```powershell
Get-NetTCPConnection -LocalPort 80 -ErrorAction SilentlyContinue
Get-NetTCPConnection -LocalPort 8081 -ErrorAction SilentlyContinue
```

Muc dich:

- Port `80` dung cho frontend.
- Port `8081` dung cho backend.
- Neu lenh khong in gi, port dang trong.

Ghi chu: port `8080` tren may nay dang duoc dung boi `NI Application Web Server`, nen backend chuyen sang port `8081`.

## 6. Tao Site Test Cho IIS Port 80

Chay o PowerShell Administrator:

```powershell
New-Item -ItemType Directory -Force C:\inetpub\wwwroot
Set-Content -Path C:\inetpub\wwwroot\index.html -Value "<h1>IIS OK</h1><p>MProject deploy test</p>"

cd C:\Windows\system32\inetsrv
.\appcmd.exe add site /name:"Default Web Site" /bindings:"http/*:80:" /physicalPath:"C:\inetpub\wwwroot"
.\appcmd.exe start site "Default Web Site"
.\appcmd.exe list site
```

Muc dich: xac nhan IIS port `80` hoat dong truoc khi dua frontend that vao.

Test tren trinh duyet:

```txt
http://localhost
http://10.197.246.231
```

## 7. Kiem Tra ASP.NET Core Hosting Bundle

Chay o PowerShell Administrator:

```powershell
Get-WebGlobalModule | Where-Object { $_.Name -match "AspNetCore" }
```

Muc dich: backend ASP.NET Core chay qua IIS can `AspNetCoreModuleV2`.

Ket qua mong muon:

```txt
AspNetCoreModuleV2
```

Neu thieu module nay, backend IIS se loi:

```txt
HTTP Error 500.19
Error Code: 0x8007000d
Config File: C:\inetpub\MProject\Api\web.config
```

Cach xu ly: cai ASP.NET Core Hosting Bundle dung voi runtime backend, sau do chay:

```powershell
iisreset
```

## 7.1 Kiem Tra IIS URL Rewrite Module

Chay o PowerShell Administrator:

```powershell
Get-WebGlobalModule | Where-Object { $_.Name -match "Rewrite" }
```

Muc dich: frontend React SPA can URL Rewrite Module de `web.config` rewrite route ve `index.html`.

Ket qua mong muon:

```txt
RewriteModule
```

Neu thieu module nay, frontend co the bi `HTTP Error 500.19` khi IIS doc `C:\inetpub\wwwroot\web.config`, hoac refresh route con bi loi.

## 8. Kiem Tra PostgreSQL

Chay o PowerShell:

```powershell
Get-Service | Where-Object { $_.Name -match "postgres|pgsql" -or $_.DisplayName -match "postgres|pgsql" }
```

Muc dich: kiem tra PostgreSQL service dang chay.

Neu `psql` khong co trong PATH, tim bang:

```powershell
Get-ChildItem "C:\Program Files\PostgreSQL" -Recurse -Filter psql.exe -ErrorAction SilentlyContinue
```

Liet ke database:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -l
```

Database da dung:

```txt
TESSDB
```

Connection string:

```txt
Host=localhost;Database=TESSDB;Username=postgres;Password=ubntubnt
```

## 9. Build Backend

Chay o PowerShell:

```powershell
dotnet --info

cd "C:\Users\Administrator\Desktop\New folder\MProject\MProjectBackend"
dotnet build MProject.Api\MProject.Api.csproj -c Release
```

Muc dich:

- Kiem tra .NET SDK.
- Dam bao backend build thanh cong truoc khi publish.

## 10. Tao Thu Muc Publish Va Data

Chay o PowerShell Administrator:

```powershell
New-Item -ItemType Directory -Force "C:\inetpub\MProject\Api"
New-Item -ItemType Directory -Force "C:\inetpub\MProject\Frontend"
New-Item -ItemType Directory -Force "C:\MProjectData\storage"
New-Item -ItemType Directory -Force "C:\MProjectData\git-storage"
New-Item -ItemType Directory -Force "C:\MProjectData\tus-temp"
```

Muc dich:

- `C:\inetpub\MProject\Api`: noi chua backend publish.
- `C:\MProjectData\storage`: noi backend luu file.
- `C:\MProjectData\git-storage`: noi backend luu repository.
- `C:\MProjectData\tus-temp`: noi luu upload tam.

## 11. Publish Backend

Chay o PowerShell:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject\MProjectBackend"
dotnet publish MProject.Api\MProject.Api.csproj -c Release -o "C:\inetpub\MProject\Api"
```

Kiem tra file publish:

```powershell
Test-Path "C:\inetpub\MProject\Api\MProject.Api.dll"
Test-Path "C:\inetpub\MProject\Api\web.config"
Test-Path "C:\inetpub\MProject\Api\appsettings.json"
```

Muc dich: dua backend ban Release vao thu muc IIS.

## 12. Cau Hinh Backend `appsettings.json`

Mo file:

```powershell
notepad "C:\inetpub\MProject\Api\appsettings.json"
```

Noi dung da dung:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=TESSDB;Username=postgres;Password=ubntubnt"
  },
  "JwtKey": "mproject_local_deploy_secret_key_32_chars_minimum_2026",
  "JwtIssuer": "MProject",
  "JwtAudience": "MProjectClient",
  "Storage": {
    "Provider": "Local",
    "VerifyUploadHash": true,
    "Local": {
      "RootPath": "C:\\MProjectData\\storage"
    }
  },
  "Repository": {
    "RootPath": "C:\\MProjectData\\git-storage"
  },
  "TusUpload": {
    "TempStoragePath": "C:\\MProjectData\\tus-temp",
    "MaxFileSizeBytes": 10737418240
  },
  "AuthTokens": {
    "RefreshTokenPepper": "YWJjZGVmMDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
    "AccessTokenMinutes": 30,
    "RefreshTokenMinutes": 120,
    "SingleDeviceLogin": true
  },
  "Agent": {
    "TokenPepper": "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
    "InstallerToken": "dev_installer_token_change_me_min_24_chars"
  },
  "AllowedOrigins": [
    "http://localhost",
    "http://10.197.246.231",
    "https://tess"
  ],
  "BlobGc": {
    "Enabled": true,
    "RunAtHourUtc": 2,
    "GracePeriodDays": 7,
    "BatchSize": 500
  },
  "InstallationJobWatchdog": {
    "Enabled": true,
    "SweepIntervalMinutes": 5,
    "InactivityTimeoutMinutes": 10,
    "MaxAttemptDurationMinutes": 30,
    "BatchSize": 200
  },
  "ComputerLiveness": {
    "Enabled": true,
    "SweepIntervalMinutes": 2,
    "OfflineAfterMinutes": 5,
    "BatchSize": 1000
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Kiem tra JSON:

```powershell
Get-Content "C:\inetpub\MProject\Api\appsettings.json" | ConvertFrom-Json | Out-Null
```

Muc dich:

- Cau hinh database.
- Cau hinh JWT.
- Cau hinh storage local.
- Cho phep frontend origin bang `AllowedOrigins`, gom ca HTTP va HTTPS.

## 13. Tao App Pool Va Site Cho Backend

Chay o PowerShell Administrator:

```powershell
cd C:\Windows\system32\inetsrv
.\appcmd.exe add apppool /name:"MProjectApiPool"
.\appcmd.exe set apppool "MProjectApiPool" /managedRuntimeVersion:""
.\appcmd.exe add site /name:"MProjectApi" /bindings:"http/*:8081:" /physicalPath:"C:\inetpub\MProject\Api"
.\appcmd.exe set app "MProjectApi/" /applicationPool:"MProjectApiPool"
.\appcmd.exe start site "MProjectApi"
```

Kiem tra:

```powershell
.\appcmd.exe list site
.\appcmd.exe list apppool
```

Muc dich:

- Tao app pool rieng cho backend.
- Set `No Managed Code` bang `/managedRuntimeVersion:""` vi ASP.NET Core chay ngoai CLR cua IIS.
- Bind backend vao port `8081`.

## 14. Cap Quyen Ghi Cho Backend

Chay o PowerShell Administrator, sau khi app pool da ton tai:

```powershell
icacls "C:\MProjectData" /grant "IIS AppPool\MProjectApiPool:(OI)(CI)M" /T
Restart-WebAppPool MProjectApiPool
```

Muc dich: cho backend quyen ghi file vao `C:\MProjectData`.

## 15. Test Backend

Test bang trinh duyet hoac PowerShell.

Chay o PowerShell:

```powershell
Invoke-RestMethod "http://localhost:8081/health/live"
Invoke-RestMethod "http://localhost:8081/health/ready"
Invoke-RestMethod "http://10.197.246.231:8081/health/live"
Invoke-RestMethod "http://10.197.246.231:8081/health/ready"
```

Muc dich:

- `/health/live`: kiem tra app backend dang song.
- `/health/ready`: kiem tra backend ket noi database duoc.

Ket qua mong muon:

```json
{"status":"Healthy"}
```

Va voi `ready`:

```json
{"status":"Healthy","database":"Available"}
```

## 16. Kiem Tra Node/Yarn Frontend

Chay o PowerShell:

```powershell
node -v
yarn.cmd -v
```

Muc dich: kiem tra runtime build frontend.

Ghi chu: tren may nay PowerShell chan `yarn.ps1`, nen dung `yarn.cmd`.

## 17. Cai Dependency Frontend

Chay o PowerShell:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject\MProjectFrontend"
yarn.cmd install
```

Muc dich: dam bao dependency frontend da san sang.

Canh bao `package-lock.json found` co the bo qua neu du an dang dung Yarn.

## 18. Tao Cau Hinh Frontend Production

Chay o PowerShell trong thu muc `MProjectFrontend`:

```powershell
Set-Content -Path ".env.production" -Value "VITE_API_URL=https://tess:8443"
Get-Content ".env.production"
```

Muc dich: frontend build production se goi backend IIS qua HTTPS.

Neu muon test HTTP truoc khi cau hinh certificate, co the tam dung:

```powershell
Set-Content -Path ".env.production" -Value "VITE_API_URL=http://10.197.246.231:8081"
```

Sau khi hoan thanh HTTPS o cac buoc ben duoi, doi lai thanh `https://tess:8443` va build frontend lai.

Noi dung mong muon:

```env
VITE_API_URL=https://tess:8443
```

## 19. Build Frontend

Chay o PowerShell trong thu muc `MProjectFrontend`:

```powershell
yarn.cmd build
```

Kiem tra:

```powershell
Test-Path ".\dist\index.html"
Get-ChildItem ".\dist" | Select-Object Name,Mode,Length
```

Muc dich: tao static files production trong thu muc `dist`.

## 20. Copy Frontend Vao IIS

Chay o PowerShell Administrator trong thu muc `MProjectFrontend`:

```powershell
Remove-Item "C:\inetpub\wwwroot\*" -Recurse -Force
Copy-Item ".\dist\*" "C:\inetpub\wwwroot" -Recurse -Force
```

Ghi chu: `Remove-Item` phu hop o lan deploy dau tien khi `wwwroot` chi co file test `IIS OK`. Sau khi da tao `web.config` cho React SPA, deploy lai nen dung `Copy-Item` truc tiep de khong lam mat `web.config`.

Kiem tra:

```powershell
Test-Path "C:\inetpub\wwwroot\index.html"
Get-ChildItem "C:\inetpub\wwwroot" | Select-Object Name,Mode,Length
```

Muc dich: thay file test IIS bang frontend React da build.

## 21. Test Frontend

Mo trinh duyet:

```txt
http://localhost
http://10.197.246.231
```

Muc dich: kiem tra frontend da hien dung tren IIS.

Sau do mo DevTools, tab Network, login va kiem tra API:

```txt
Neu dang test HTTP:
http://10.197.246.231:8081/api/auth/login

Neu da chuyen sang HTTPS:
https://tess:8443/api/auth/login
```

Ket qua da dat:

```txt
Status Code: 200 OK
```

## 22. Cai Rewrite Cho React Router

Kiem tra URL Rewrite Module:

```powershell
Get-WebGlobalModule | Where-Object { $_.Name -match "Rewrite" }
```

Muc dich: React SPA can rewrite route con ve `index.html` de refresh khong bi 404.

Tao file `web.config` cho frontend:

```powershell
Set-Content -Path "C:\inetpub\wwwroot\web.config" -Value @'
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="React SPA Routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
'@
```

Restart IIS:

```powershell
iisreset
```

Test: vao mot route con trong frontend, bam `F5`. Ket qua mong muon: khong bi `404`.

## 23. Tao Certificate HTTPS Noi Bo Cho `tess`

Phan nay dung cho moi truong dev/LAN noi bo. Certificate la self-signed, khong phai certificate public production.

Chay o PowerShell Administrator:

```powershell
$cert = New-SelfSignedCertificate `
  -Subject "CN=tess" `
  -DnsName "tess","localhost" `
  -FriendlyName "MProject tess SChannel HTTPS certificate" `
  -CertStoreLocation "Cert:\LocalMachine\My" `
  -Provider "Microsoft RSA SChannel Cryptographic Provider" `
  -KeyAlgorithm RSA `
  -KeyLength 2048 `
  -HashAlgorithm SHA256 `
  -KeySpec KeyExchange `
  -KeyUsage DigitalSignature,KeyEncipherment `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
  -KeyExportPolicy Exportable `
  -NotAfter (Get-Date).AddYears(3)

Export-Certificate -Cert $cert -FilePath "$env:TEMP\mproject-tess.cer" | Out-Null
Import-Certificate -FilePath "$env:TEMP\mproject-tess.cer" -CertStoreLocation Cert:\LocalMachine\Root | Out-Null

$cert | Select-Object Subject,FriendlyName,Thumbprint,NotAfter,HasPrivateKey,DnsNameList,EnhancedKeyUsageList | Format-List
```

Muc dich:

- Tao certificate `CN=tess` co SAN `tess` va `localhost`.
- Dung provider `Microsoft RSA SChannel Cryptographic Provider` de phu hop IIS/HTTP.sys.
- Them EKU `Server Authentication`.
- Import vao `Trusted Root` cua chinh may nay de trinh duyet tin cay certificate.

Ket qua da dung:

```txt
Thumbprint: 1E041C6D09A23BBE6028DA04FE0CF40BA5F53CF4
SAN: tess, localhost
EKU: Server Authentication
```

Neu tao lai cert tren may khac, dung thumbprint moi cua may do cho cac buoc binding ben duoi.

## 24. Them Hostname `tess`

Chay o PowerShell Administrator:

```powershell
$hostsPath = "C:\Windows\System32\drivers\etc\hosts"
$entry = "10.197.246.231 tess"
$hosts = Get-Content $hostsPath
if (-not ($hosts | Where-Object { $_ -match "^\s*10\.197\.246\.231\s+tess\s*$" })) {
  Add-Content -Path $hostsPath -Value $entry
}

ping tess
```

Muc dich: de `https://tess` tro ve may dev hien tai.

Ket qua mong muon:

```txt
Pinging tess [10.197.246.231] ...
```

Ghi chu: may khac trong LAN cung can resolve duoc `tess`. Co the sua file `hosts` tren tung may client, hoac dung DNS noi bo.

## 25. Them HTTPS Binding Cho IIS

Chay o PowerShell Administrator:

```powershell
Import-Module WebAdministration

$thumbprint = "1E041C6D09A23BBE6028DA04FE0CF40BA5F53CF4"

function Ensure-HttpsBinding {
  param(
    [string]$SiteName,
    [int]$Port,
    [string]$HostName,
    [string]$Thumbprint
  )

  $binding = Get-WebBinding -Name $SiteName -Protocol "https" -ErrorAction SilentlyContinue |
    Where-Object { $_.bindingInformation -eq "*:$Port`:$HostName" }

  if (-not $binding) {
    New-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $HostName -SslFlags 1
    $binding = Get-WebBinding -Name $SiteName -Protocol "https" |
      Where-Object { $_.bindingInformation -eq "*:$Port`:$HostName" }
  }

  $binding.AddSslCertificate($Thumbprint, "My")
}

Ensure-HttpsBinding -SiteName "Default Web Site" -Port 443 -HostName "tess" -Thumbprint $thumbprint
Ensure-HttpsBinding -SiteName "MProjectApi" -Port 8443 -HostName "tess" -Thumbprint $thumbprint

Get-WebBinding | Where-Object { $_.bindingInformation -like "*tess" } |
  Select-Object protocol,bindingInformation,sslFlags,certificateHash,certificateStoreName
```

Muc dich:

- Bind frontend HTTPS: `https://tess`.
- Bind backend HTTPS: `https://tess:8443`.
- Dung SNI voi `sslFlags=1`, tranh anh huong cac HTTPS binding khac.

Ket qua mong muon:

```txt
https *:443:tess  -> cert 1E041C6D09A23BBE6028DA04FE0CF40BA5F53CF4
https *:8443:tess -> cert 1E041C6D09A23BBE6028DA04FE0CF40BA5F53CF4
```

## 26. Cap Nhat HTTPS Cho Backend Va Frontend

Backend da publish can co origin HTTPS:

```json
"AllowedOrigins": [
  "http://localhost",
  "http://10.197.246.231",
  "https://tess"
]
```

Neu can sua truc tiep file publish:

```powershell
notepad "C:\inetpub\MProject\Api\appsettings.json"
Restart-WebAppPool MProjectApiPool
```

Frontend `.env.production` can tro ve backend HTTPS:

```env
VITE_API_URL=https://tess:8443
```

Sau khi doi `.env.production`, can build va copy lai frontend:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject\MProjectFrontend"
yarn.cmd build
Copy-Item ".\dist\*" "C:\inetpub\wwwroot" -Recurse -Force
```

Ghi chu: lenh copy tren giu lai `C:\inetpub\wwwroot\web.config` neu file nay da ton tai.

## 27. Test HTTPS

Test bang trinh duyet:

```txt
https://tess
https://tess:8443/health/live
https://tess:8443/health/ready
```

Test bang Node neu PowerShell/curl tren Windows bi loi Schannel:

```powershell
node -e "const https=require('https'); https.get('https://tess/', {rejectUnauthorized:false}, r=>{console.log('status', r.statusCode); let n=0; r.on('data', d=>n+=d.length); r.on('end',()=>console.log('bytes', n));}).on('error', e=>{console.error(e); process.exit(1);});"

node -e "const https=require('https'); https.get('https://tess:8443/health/ready', {rejectUnauthorized:false}, r=>{console.log('status', r.statusCode); let s=''; r.on('data', d=>s+=d); r.on('end',()=>console.log(s));}).on('error', e=>{console.error(e); process.exit(1);});"
```

Ket qua da dat:

```txt
https://tess                  -> 200
https://tess:8443/health/live -> Healthy
https://tess:8443/health/ready -> Healthy, database Available
```

## 28. Mo Firewall Khi Test Tu May Khac Trong LAN

Neu may khac trong LAN khong truy cap duoc, chay PowerShell Administrator:

```powershell
New-NetFirewallRule -DisplayName "MProject Frontend HTTP 80" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
New-NetFirewallRule -DisplayName "MProject Backend API 8081" -Direction Inbound -Protocol TCP -LocalPort 8081 -Action Allow
New-NetFirewallRule -DisplayName "MProject Frontend HTTPS 443" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
New-NetFirewallRule -DisplayName "MProject Backend HTTPS 8443" -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow
```

Muc dich: cho may khac trong LAN truy cap frontend va backend qua HTTP/HTTPS.

## 29. Setup Lan Dau Tu Dong Bang Script

Neu dung dung mac dinh cua may dev hien tai, co the dung script setup lan dau:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\setup-iis-local.ps1"
```

Chay lenh tren trong PowerShell Administrator.

Script nay se tu dong:

- Tu detect IPv4 LAN cua may dang chay script neu khong truyen `-LanIp`.
- Bat/kiem tra IIS va `W3SVC`.
- Backup IIS config truoc khi thay doi.
- Tao thu muc deploy/data.
- Tao hoac tai su dung certificate `CN=tess` co SAN `tess`, `localhost` va EKU `Server Authentication`.
- Import certificate vao `LocalMachine\Root`.
- Them/cap nhat hosts entry `10.197.246.231 tess`.
- Tao/cap nhat `Default Web Site`, `MProjectApi`, `MProjectApiPool`.
- Tao HTTP binding `80`, `8081`.
- Tao HTTPS binding `443`, `8443`.
- Cap quyen ghi `C:\MProjectData` cho `IIS AppPool\MProjectApiPool`.
- Tao `appsettings.json` neu chua co; mac dinh khong ghi de file dang ton tai.
- Tao `web.config` frontend neu chua co.
- Mo firewall `80`, `443`, `8081`, `8443`.
- Goi tiep `scripts/deploy-local.ps1` de deploy app lan dau.

Mot so tham so huu ich:

```powershell
# Chi setup IIS/cert/config, khong deploy app
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\setup-iis-local.ps1" -SkipDeploy

# Tao lai certificate tess
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\setup-iis-local.ps1" -RecreateCertificate

# Ghi de appsettings.json deploy bang config mac dinh cua script
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\setup-iis-local.ps1" -OverwriteAppSettings

# Override IP/hostname neu may co nhieu card mang, VPN, Hyper-V, Docker...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\setup-iis-local.ps1" -LanIp "10.197.246.231" -HostName "tess"
```

Ghi chu:

- `-LanIp` la tuy chon. Neu khong truyen, script se chon IPv4 cua adapter dang `Up`, co default gateway, bo qua `127.x` va `169.254.x`.
- Neu may co nhieu network adapter va script chon nham IP, chay lai voi `-LanIp`.
- Script nay idempotent theo muc tieu local dev, nghia la chay lai se cap nhat/giu cac cau hinh da co, khong xoa site/app pool mac dinh.

## 30. Deploy Lai Tu Dong Bang Script

Sau khi setup IIS/certificate/hostname xong, co the deploy lai bang script:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\deploy-local.ps1"
```

Chay lenh tren trong PowerShell Administrator.

Script se tu dong:

- Kiem tra `dotnet`, `node`, `yarn.cmd`.
- Backup `C:\inetpub\MProject\Api\appsettings.json`.
- Dat `app_offline.htm` va stop `MProjectApiPool` de tranh file DLL bi lock.
- `dotnet publish` backend vao `C:\inetpub\MProject\Api`.
- Restore lai `appsettings.json` da deploy.
- Start lai `MProjectApiPool`.
- Ghi `.env.production` voi `VITE_API_URL=https://tess:8443`.
- Build frontend bang `yarn.cmd build`.
- Copy `dist` vao `C:\inetpub\wwwroot` ma khong xoa `web.config`.
- Test backend health qua `https://tess:8443/health/live` va `/health/ready`.

Mot so tham so huu ich:

```powershell
# Chi deploy frontend
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\deploy-local.ps1" -SkipBackend

# Chi deploy backend
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\deploy-local.ps1" -SkipFrontend

# Cai lai dependency frontend truoc khi build
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\deploy-local.ps1" -InstallFrontendDependencies

# Dung API URL khac
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\deploy-local.ps1" -ApiUrl "https://tess:8443" -HealthBaseUrl "https://tess:8443"
```

Ghi chu: mac dinh script se bao toan `appsettings.json` da deploy. Chi dung `-OverwritePublishedAppSettings` neu that su muon file `appsettings.json` tu source publish de len config hien tai.

## 31. Quy Trinh Deploy Lai Thu Cong Sau Khi Sua Code

### Backend

Chay o PowerShell:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject\MProjectBackend"
dotnet publish MProject.Api\MProject.Api.csproj -c Release -o "C:\inetpub\MProject\Api"
Restart-WebAppPool MProjectApiPool
```

Muc dich: publish backend moi va restart app pool.

### Frontend

Chay o PowerShell Administrator:

```powershell
cd "C:\Users\Administrator\Desktop\New folder\MProject\MProjectFrontend"
yarn.cmd build
Copy-Item ".\dist\*" "C:\inetpub\wwwroot" -Recurse -Force
```

Muc dich: build frontend moi va copy vao IIS.

Khong nen xoa toan bo `C:\inetpub\wwwroot` trong quy trinh deploy lai, vi co the lam mat `web.config` cua React SPA. Neu bat buoc clean truoc khi copy, hay backup/tao lai `web.config` sau do.

Neu can restart IIS:

```powershell
iisreset
```

Hoac chi restart backend app pool neu chi sua backend:

```powershell
Restart-WebAppPool MProjectApiPool
```

## 32. Checklist Ket Qua Cuoi

Can dat cac muc sau:

- `http://localhost` hien frontend.
- `http://10.197.246.231` hien frontend.
- `https://tess` hien frontend.
- `http://localhost:8081/health/live` tra `Healthy`.
- `http://localhost:8081/health/ready` tra `Healthy`, database `Available`.
- `http://10.197.246.231:8081/health/live` tra `Healthy`.
- `http://10.197.246.231:8081/health/ready` tra `Healthy`, database `Available`.
- `https://tess:8443/health/live` tra `Healthy`.
- `https://tess:8443/health/ready` tra `Healthy`, database `Available`.
- Login frontend HTTPS goi `https://tess:8443/api/auth/login` va tra `200 OK`.
- Refresh route con frontend khong bi `404`.
