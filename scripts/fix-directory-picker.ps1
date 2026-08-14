<#
.SYNOPSIS
  Restore a PowerShell fallback for dsh's native Win32 directory picker.

.DESCRIPTION
  dsh's native picker spawns a koffi-driven child process for the Win32
  folder dialog; under the desktop shell that worker can exit without a
  result ("win32 folder dialog worker exited before reporting a result"),
  and dsh no longer ships a fallback tier. This script patches the built
  picker lib in a dsh source checkout to add one: when the native pick
  fails (and the caller did not abort), it falls back to a PowerShell
  FolderBrowserDialog.

  Idempotent: exits 0 immediately when the marker is already present.
  Safe: when dsh's built shape no longer matches the expected anchors it
  prints a warning and exits 1 without touching the file.

.PARAMETER DshDir
  Path to the deepseek-harness source checkout. Falls back to DSH_DIR.
#>
param(
    [string]$DshDir = ""
)

$ErrorActionPreference = "Stop"

function Fail([string]$msg) {
    Write-Host "[fix-directory-picker] $msg" -ForegroundColor Yellow
    exit 1
}

if (-not $DshDir) { $DshDir = $env:DSH_DIR }
if (-not $DshDir) { Fail "no dshDir: pass it as the first argument or set DSH_DIR" }

$target = Join-Path $DshDir "packages\host\directory-picker-native\lib\index.js"
if (-not (Test-Path $target)) {
    Fail "picker lib not found: $target (is this a dsh source checkout?)"
}

$content = [System.IO.File]::ReadAllText($target)

$marker = "/* dsh-desktop: powershell fallback */"
if ($content.Contains($marker)) {
    Write-Host "[fix-directory-picker] already patched: $target"
    exit 0
}

# Anchors that must exist for a safe patch. The win32 anchor spans the whole
# `if (platform === "win32")` line (minus leading whitespace) so the wrapped
# replacement does not leave a duplicated condition behind.
$anchors = @(
    'import { spawn } from "node:child_process";',
    'if (platform === "win32") return await (internals.pickWin32Dialog ?? pickWin32Directory)(signal);',
    'async function pickNativeDirectory(signal, internals = {}) {'
)
foreach ($a in $anchors) {
    if (-not $content.Contains($a)) {
        Fail "anchor not found: $a - dsh built shape changed; skipping"
    }
}

# Match the file's dominant line ending so the insert does not create a mix.
$nl = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }

# 1) Add execFile to the child_process import.
$content = $content.Replace(
    'import { spawn } from "node:child_process";',
    'import { spawn, execFile } from "node:child_process";')

# 2) Insert the PowerShell fallback helper right before pickNativeDirectory.
$fallbackFn = @'
/* dsh-desktop: powershell fallback */
async function pickPowerShellDirectory() {
  const script = "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = 'Select Workspace Directory'; $f.ShowNewFolderButton = $true; $r = $f.ShowDialog(); if ($r -eq 'OK') { $f.SelectedPath }";
  return await new Promise((resolve, reject) => {
    execFile("powershell.exe", ["-NoProfile", "-STA", "-Command", script], { windowsHide: true, timeout: 60000, maxBuffer: 1048576 }, (error, stdout) => {
      if (error) return reject(error);
      const path = stdout.replace(/[\r\n]+$/, "").trim();
      resolve(path === "" ? null : path);
    });
  });
}

'@
$fallbackFn = ($fallbackFn -replace "`r?`n", $nl)
$content = $content.Replace($anchors[2], $fallbackFn + $anchors[2])

# 3) Wrap the win32 branch with the fallback. Built output indents with tabs;
#    build the block from tab-prefixed lines so it matches the surrounding code.
#    (Interpolate `${tab}` instead of using `($tab*N) + '...'` — PowerShell's
#    array literal splits a trailing binary `+` into a separate element.)
$tab = "`t"
$branchNew = @(
    'if (platform === "win32") {'
    "${tab}${tab}try {"
    "${tab}${tab}${tab}return await (internals.pickWin32Dialog ?? pickWin32Directory)(signal);"
    "${tab}${tab}${tab}} catch (error) {"
    "${tab}${tab}${tab}${tab}if (signal.aborted) throw error;"
    "${tab}${tab}${tab}${tab}/* dsh-desktop: powershell fallback */"
    "${tab}${tab}${tab}${tab}return await pickPowerShellDirectory();"
    "${tab}${tab}${tab}}"
    "${tab}${tab}}"
) -join $nl
$content = $content.Replace($anchors[1], $branchNew)

# Write back UTF-8 without BOM, preserving the dominant line ending.
[System.IO.File]::WriteAllText($target, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[fix-directory-picker] patched: $target"
exit 0
