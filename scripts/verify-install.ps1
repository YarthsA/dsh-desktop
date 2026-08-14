<#
.SYNOPSIS
  One-shot verification of a dsh-desktop install. Prints a compact PASS/FAIL
  table and exits 0 when everything is healthy, 1 otherwise.

.DESCRIPTION
  Checks prerequisites (node, pnpm, .NET 10 Desktop Runtime, WebView2 Runtime),
  resolves the dsh source dir from config.json / DSH_DIR, verifies the
  directory-picker repair patch, then makes sure the dsh Web UI is reachable on
  127.0.0.1:3080 (starting DshDesktop.exe if nothing is listening) and reports
  whether the service is owned by DshDesktop.

.PARAMETER AppDir
  Directory containing DshDesktop.exe (the extracted/published app). Auto-
  resolved from the current directory or the dev publish output when omitted.

.PARAMETER DshDir
  Override the dsh source checkout path. Otherwise read from config.json next
  to DshDesktop.exe, then DSH_DIR.

.PARAMETER WaitSec
  How long to wait for the service to become ready after starting the app.
#>
param(
    [string]$AppDir = "",
    [string]$DshDir = "",
    [int]$WaitSec = 90
)

$ErrorActionPreference = "Stop"
$script:anyFail = $false

function Pass([string]$msg) { Write-Host ("  [PASS] " + $msg) -ForegroundColor Green }
function Fail([string]$msg) { Write-Host ("  [FAIL] " + $msg) -ForegroundColor Red; $script:anyFail = $true }
function Warn([string]$msg) { Write-Host ("  [WARN] " + $msg) -ForegroundColor Yellow }
function Section([string]$name) { Write-Host ""; Write-Host "== $name ==" -ForegroundColor Cyan }

function Get-Listener3080 {
    Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
}

Section "Prerequisites"
$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) { Pass ("node " + (& node --version)) } else { Fail "node not found in PATH" }

$pnpm = Get-Command pnpm -ErrorAction SilentlyContinue
if ($pnpm) { Pass ("pnpm " + (& pnpm --version)) } else { Fail "pnpm not found in PATH" }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $runtimes = (& dotnet --list-runtimes | Out-String)
    if ($runtimes -match "Microsoft.WindowsDesktop.App 10\.") { Pass ".NET 10 Desktop Runtime installed" }
    else { Fail ".NET 10 Desktop Runtime NOT installed (winget install Microsoft.DotNet.DesktopRuntime.10)" }
} else { Fail "dotnet not found in PATH" }

$wvRoot = "C:\Program Files (x86)\Microsoft\EdgeWebView\Application"
if (Test-Path $wvRoot) {
    $wvVer = Get-ChildItem $wvRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d' } | Select-Object -First 1
    Pass ("WebView2 Runtime " + $wvVer.Name)
} else { Fail "WebView2 Runtime not found (install from https://developer.microsoft.com/microsoft-edge/webview2/)" }

Section "Config / dsh source"
if (-not $AppDir) {
    foreach ($candidate in @(
        (Join-Path (Get-Location) "DshDesktop.exe"),
        (Join-Path (Split-Path $PSScriptRoot -Parent) "app\bin\Release\net10.0-windows\win-x64\publish\DshDesktop.exe")
    )) {
        if (Test-Path $candidate) { $AppDir = Split-Path $candidate -Parent; break }
    }
}
$appExe = Join-Path $AppDir "DshDesktop.exe"
if (Test-Path $appExe) { Pass ("DshDesktop.exe: " + $appExe) } else { Fail "DshDesktop.exe not found (pass -AppDir or build first)" }

if (-not $DshDir) {
    $cfg = Join-Path $AppDir "config.json"
    if (Test-Path $cfg) {
        $json = Get-Content $cfg -Raw | ConvertFrom-Json
        if ($json.dshDir) { $DshDir = $json.dshDir }
    }
}
if (-not $DshDir) { $DshDir = $env:DSH_DIR }

if ($DshDir) {
    if (Test-Path (Join-Path $DshDir "node_modules")) { Pass ("dshDir: " + $DshDir + " (node_modules ok)") }
    else { Fail ("dshDir missing node_modules: " + $DshDir) }
} else {
    Warn "dshDir not resolved (attach mode only is fine; config.json / DSH_DIR needed for managed mode)"
}

Section "Directory picker repair"
if ($DshDir) {
    $picker = Join-Path $DshDir "packages\host\directory-picker-native\lib\index.js"
    if (Test-Path $picker) {
        if ((Get-Content $picker -Raw).Contains("dsh-desktop: powershell fallback")) {
            Pass "directory picker PS fallback patch applied"
        } else {
            Warn "picker patch NOT applied (run: powershell -File scripts\fix-directory-picker.ps1 -DshDir <dshDir>)"
        }
    } else { Warn "dsh source layout not found; picker repair not applicable" }
} else { Warn "dshDir unknown; picker repair check skipped" }

Section "Service on :3080"
$listener = Get-Listener3080
if ($listener) {
    Pass "service already listening on 127.0.0.1:3080 (attach)"
} elseif (Test-Path $appExe) {
    Write-Host "  starting DshDesktop.exe, waiting for :3080 (up to ${WaitSec}s)..."
    Start-Process -FilePath $appExe -WorkingDirectory (Split-Path $appExe -Parent)
    $deadline = (Get-Date).AddSeconds($WaitSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 1500
        $listener = Get-Listener3080
        if ($listener) { break }
    }
    if ($listener) { Pass "service became ready on 127.0.0.1:3080 (managed)" }
    else { Fail "service did not become ready within ${WaitSec}s (check %LOCALAPPDATA%\DshDesktop\app.log)" }
} else {
    Fail "cannot start service: DshDesktop.exe missing"
}

if ($listener) {
    try {
        $resp = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:3080/" -TimeoutSec 5
        if ($resp.StatusCode -eq 200) { Pass "Web UI returns HTTP 200" }
        else { Fail ("Web UI returned HTTP " + $resp.StatusCode) }
    } catch { Fail "Web UI not reachable: $($_.Exception.Message)" }
}

Section "Service ownership"
if ($listener) {
    $pid3080 = $listener.OwningProcess
    $cur = $pid3080; $seen = @{}; $owned = $false
    while ($cur -and -not $seen.ContainsKey($cur)) {
        $seen[$cur] = $true
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$cur" -ErrorAction SilentlyContinue
        if (-not $proc) { break }
        if ($proc.Name -eq "DshDesktop.exe") { $owned = $true; break }
        $cur = $proc.ParentProcessId
    }
    if ($owned) { Pass "service is owned by DshDesktop (tray exit stops it)" }
    else { Warn "listener PID $pid3080 is NOT a DshDesktop child — attach/orphan; tray exit won't stop it" }
}

Write-Host ""
if ($script:anyFail) { Write-Host "RESULT: FAIL" -ForegroundColor Red; exit 1 }
Write-Host "RESULT: PASS" -ForegroundColor Green
exit 0
