@echo off
rem ASH.Server: stop and remove the Windows service and its firewall rule. Release files (%ProgramData%\ASH.Server) and appsettings.json are kept.
rem ASCII only on purpose: cmd mangles UTF-8 without a BOM.
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    echo Asking for administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
    if errorlevel 1 (echo Could not elevate. Right-click this file and choose "Run as administrator".& if not defined NOPAUSE pause)
    exit /b 1
)

sc query ASH.Server >nul 2>&1
if errorlevel 1 (
    echo Service ASH.Server is not registered.
    goto firewall
)
sc stop ASH.Server >nul 2>&1
set /a N=0
:stopwait
sc query ASH.Server | find "STOPPED" >nul 2>&1 && goto stopped
set /a N+=1
if %N% GEQ 30 goto stopped
ping -n 2 127.0.0.1 >nul
goto stopwait
:stopped
"%~dp0ASH.Server.exe" --uninstall

:firewall
netsh advfirewall firewall delete rule name="ASH.Server" >nul 2>&1
echo Done. Release files in %ProgramData%\ASH.Server were not deleted.
if not defined NOPAUSE pause
exit /b 0
