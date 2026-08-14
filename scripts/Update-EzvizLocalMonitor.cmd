@echo off
setlocal
chcp 65001 >nul
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-EzvizLocalMonitor.ps1"
if errorlevel 1 (
  echo.
  echo Cap nhat khong thanh cong. Xem thong bao trong cua so PowerShell.
  pause
)
endlocal
