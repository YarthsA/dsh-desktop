# Publish DshDesktop for Windows x64.
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 -SelfContained
#
# Default (framework-dependent) needs .NET 10 Desktop Runtime on the target
# machine. -SelfContained bundles the runtime (bigger zip, zero prerequisites).
param(
    [switch]$SelfContained
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $root "app"
Set-Location $appDir

$sc = if ($SelfContained) { "true" } else { "false" }
dotnet publish -c Release -r win-x64 --self-contained $sc
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$out = Join-Path $appDir "bin\Release\net10.0-windows\win-x64\publish"
Write-Host ""
Write-Host "Published -> $out" -ForegroundColor Green
if ($SelfContained) {
    Write-Host "Self-contained build: no .NET runtime needed on the target machine (larger zip)."
    $zip = Join-Path $appDir "bin\dsh-desktop-self-contained-win-x64.zip"
    Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
    Write-Host "Zip -> $zip" -ForegroundColor Green
} else {
    Write-Host "Framework-dependent build: requires .NET 10 Desktop Runtime (winget install Microsoft.DotNet.DesktopRuntime.10)."
    Write-Host "Copy the whole folder (or zip it) to distribute."
}
