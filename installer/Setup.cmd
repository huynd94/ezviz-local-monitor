@echo off
setlocal EnableExtensions
chcp 65001 >nul
set /p choice="Cai vao thu muc mac dinh D:\EZVIZ-Local-Monitor? [Y/n]: "
if /I "%choice%"=="n" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-EzvizLocalMonitor.ps1" -ChooseLocation
) else (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-EzvizLocalMonitor.ps1"
)
endlocal
