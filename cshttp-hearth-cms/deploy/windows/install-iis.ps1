<#
  Registers cshttp Hearth CMS as an IIS website (IIS = reverse proxy + process manager).
  Run from an ELEVATED PowerShell:

    powershell -ExecutionPolicy Bypass -File deploy\windows\install-iis.ps1 [-Port 8092] [-SiteName HearthCms] [-Target C:\inetpub\HearthCms]

  Requirements: IIS with the HttpPlatformHandler module installed
  (https://www.iis.net/downloads/microsoft/httpplatformhandler). No ASP.NET
  feature, no .NET hosting bundle - the published app is self-contained.

  Re-running is safe: binaries are replaced, the existing App_Data (database,
  config.txt, logs) and uploaded media are kept.
#>
param(
    [string]$SiteName = 'HearthCms',
    [int]$Port = 8092,
    [string]$HostName = '',
    [string]$Target = 'C:\inetpub\HearthCms',
    [string]$Source = ''
)

$ErrorActionPreference = 'Stop'
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
if (-not (Test-Path $appcmd)) { throw 'IIS is not installed (appcmd.exe not found).' }
if (-not (& $appcmd list modules | Select-String 'httpPlatformHandler')) {
    throw 'The IIS HttpPlatformHandler module is not installed.'
}
if (-not $Source) { $Source = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'publish\win-x64' }
if (-not (Test-Path (Join-Path $Source 'HearthCms.exe'))) { throw "Published app not found in $Source - run tools\publish.ps1 first." }

$pool = $SiteName

# 1. Stop the running site/pool so HearthCms.exe releases its files.
if (& $appcmd list site /name:$SiteName) { & $appcmd stop site /site.name:$SiteName | Out-Null }
if (& $appcmd list apppool /name:$pool) { & $appcmd stop apppool /apppool.name:$pool | Out-Null }
Get-Process HearthCms -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Target*" } | Stop-Process -Force
Start-Sleep -Seconds 2

# 2. Copy files. First install: everything. Upgrade: keep App_Data + media + favicon.
New-Item -ItemType Directory -Force $Target | Out-Null
$firstInstall = -not (Test-Path (Join-Path $Target 'App_Data'))
$excludeDirs = @()
$excludeFiles = @()
if (-not $firstInstall) {
    $excludeDirs = @((Join-Path $Source 'App_Data'), (Join-Path $Source 'media'))
    $excludeFiles = @('favicon.ico')
}
$rc = @($Source, $Target, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:2', '/W:2')
if ($excludeDirs) { $rc += '/XD'; $rc += $excludeDirs }
if ($excludeFiles) { $rc += '/XF'; $rc += $excludeFiles }
& robocopy @rc | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
New-Item -ItemType Directory -Force (Join-Path $Target 'App_Data\logs') | Out-Null

# 3. Application pool: no managed code, always running, never idles out
#    (sessions are in process memory; a recycle signs users out unless they
#    ticked "remember me").
if (-not (& $appcmd list apppool /name:$pool)) { & $appcmd add apppool /name:$pool | Out-Null }
& $appcmd set apppool /apppool.name:$pool /managedRuntimeVersion:"" /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00 /recycling.periodicRestart.time:00:00:00 | Out-Null

# 4. Site.
$binding = "http/*:${Port}:$HostName"
if (-not (& $appcmd list site /name:$SiteName)) {
    & $appcmd add site /name:$SiteName /physicalPath:$Target /bindings:$binding | Out-Null
}
else {
    & $appcmd set site /site.name:$SiteName /bindings:$binding | Out-Null
    & $appcmd set vdir "$SiteName/" /physicalPath:$Target | Out-Null
}
& $appcmd set app "$SiteName/" /applicationPool:$pool | Out-Null

# 5. File permissions: the app writes to App_Data, media/, assets/themes, css/theme
#    and regenerates /favicon.ico, so the pool identity gets Modify on the site root.
& icacls $Target /grant "IIS AppPool\${pool}:(OI)(CI)M" /T /Q | Out-Null

# 6. Firewall (LAN testing).
if (-not (Get-NetFirewallRule -DisplayName "HearthCms $Port" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "HearthCms $Port" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

& $appcmd start apppool /apppool.name:$pool | Out-Null
& $appcmd start site /site.name:$SiteName | Out-Null

Write-Host "Site '$SiteName' -> http://localhost:$Port/  (files: $Target)"
