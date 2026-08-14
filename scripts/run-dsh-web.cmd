@echo off
rem Standalone host for the dsh web service (used when running the app
rem without DshDesktop, or as the source of truth for the launch command).
rem Usage: run-dsh-web.cmd [dshDir]   (default: ..\..\deepseek-harness)

set "DSH_DIR=%~1"
if "%DSH_DIR%"=="" set "DSH_DIR=%~dp0..\..\deepseek-harness"

title DeepSeek Harness (dsh)
cd /d "%DSH_DIR%"

rem git-bash exports its session CWD as PWD; cmd's cd does not update it,
rem so pnpm may read a stale PWD and resolve the wrong importer dir.
rem (no setlocal in the parent batch: pnpm.cmd uses its own SETLOCAL/endLocal,
rem  and nested setlocal + cd /d makes pnpm resolve the wrong workspace dir)
set PWD=
set INIT_CWD=
pnpm dsh web
exit /b %errorlevel%
