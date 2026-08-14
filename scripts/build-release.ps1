# Publish DshDesktop for Windows x64.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $root "app"
Set-Location $appDir

dotnet publish -c Release -r win-x64 --self-contained false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$out = Join-Path $appDir "bin\Release\net10.0-windows\win-x64\publish"
Write-Host ""
Write-Host "Published -> $out" -ForegroundColor Green
Write-Host "Copy the whole folder (or zip it) to distribute. Requires .NET 10 Desktop Runtime."
