<#
  Builds self-contained release packages (no .NET runtime needed on the server).

    powershell -ExecutionPolicy Bypass -File tools\publish.ps1                 # win-x64 + linux-x64
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Runtime linux-x64

  Output: publish\<rid>\  (the application + content root)
          publish\hearthcms-<rid>.zip / .tar.gz
#>
param([string[]]$Runtime = @('win-x64', 'linux-x64'))

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\HearthCms\HearthCms.csproj'

foreach ($rid in $Runtime) {
    $out = Join-Path $root "publish\$rid"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Write-Host "== publishing $rid -> $out"
    & dotnet publish $proj -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:DebugType=none -o $out -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    # Runtime state that must never ship from a dev box.
    Remove-Item (Join-Path $out 'App_Data\logs'), (Join-Path $out 'App_Data\page_cache') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $out 'App_Data\reset_admin.txt') -Force -ErrorAction SilentlyContinue

    if ($rid -like 'win-*') {
        Copy-Item (Join-Path $root 'deploy\windows\web.config') $out -Force
        $zip = Join-Path $root "publish\hearthcms-$rid.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    }
    else {
        $tgz = Join-Path $root "publish\hearthcms-$rid.tar.gz"
        if (Test-Path $tgz) { Remove-Item $tgz -Force }
        & tar.exe -czf $tgz -C $out .
        if ($LASTEXITCODE -ne 0) { throw "tar failed" }
    }
}
Write-Host "done."
