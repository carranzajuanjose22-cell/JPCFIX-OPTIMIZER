@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -File "%~dp0publish.ps1" %*
exit /b %errorlevel%
