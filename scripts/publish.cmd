@echo off
setlocal
cd /d "%~dp0.."
dotnet test JPCFIX.Optimizer.slnx -c Release
if errorlevel 1 exit /b 1
dotnet publish src\JPCFIX.Optimizer.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\portable
if errorlevel 1 exit /b 1
dotnet publish src\JPCFIX.Optimizer.Runner -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\portable\Runner
if errorlevel 1 exit /b 1
echo Portable: artifacts\portable\JPCFIX.Optimizer.Desktop.exe
