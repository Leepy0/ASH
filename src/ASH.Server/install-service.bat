@echo off
rem ASH.Server: register as a Windows service, lock folders, open the firewall port, start, check.
rem Run it from the folder that holds ASH.Server.exe (normally C:\Program Files\ASH.Server). Double-click is fine: it asks for administrator rights.
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
if not exist "ASH.Server.exe" (echo ASH.Server.exe is not in %~dp0& if not defined NOPAUSE pause& exit /b 1)
if not exist "appsettings.json" (echo appsettings.json is not in %~dp0& if not defined NOPAUSE pause& exit /b 1)

rem port = first port in "Urls" of appsettings.json (default 8082)
set "PORT=8082"
for /f "usebackq delims=" %%p in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $u = (Get-Content -Raw -LiteralPath 'appsettings.json' | ConvertFrom-Json).Urls; if ($u -match ':(\d+)') { $Matches[1] } } catch { }"`) do set "PORT=%%p"

echo [1/5] Locking the install folder to Administrators and SYSTEM (the service runs as SYSTEM)
icacls "%~dp0." /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" >nul
if errorlevel 1 (echo icacls failed on the install folder& if not defined NOPAUSE pause& exit /b 1)

echo [2/5] Creating and locking %ProgramData%\ASH.Server (release files) before the service exists
if not exist "%ProgramData%\ASH.Server" mkdir "%ProgramData%\ASH.Server"
icacls "%ProgramData%\ASH.Server" /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" >nul
if errorlevel 1 (echo icacls failed on %ProgramData%\ASH.Server& if not defined NOPAUSE pause& exit /b 1)

echo [3/5] Registering the service
sc query ASH.Server >nul 2>&1
if errorlevel 1 (
    "%~dp0ASH.Server.exe" --install
    if errorlevel 1 (echo Service registration failed& if not defined NOPAUSE pause& exit /b 1)
) else (
    echo Service ASH.Server already exists, keeping it. To re-register run uninstall-service.bat first.
)
sc failure ASH.Server reset= 86400 actions= restart/60000 >nul

echo [4/5] Opening firewall TCP %PORT% (domain and private networks only)
netsh advfirewall firewall delete rule name="ASH.Server" >nul 2>&1
netsh advfirewall firewall add rule name="ASH.Server" dir=in action=allow protocol=TCP localport=%PORT% profile=domain,private >nul
if errorlevel 1 echo WARNING: firewall rule was not created. Other PCs may not reach the server.

echo [5/5] Starting the service
sc query ASH.Server | find "RUNNING" >nul 2>&1 || sc start ASH.Server >nul
set /a N=0
:wait
curl.exe -fsS -m 2 "http://127.0.0.1:%PORT%/healthz" >nul 2>&1 && goto up
set /a N+=1
if %N% GEQ 20 goto down
ping -n 2 127.0.0.1 >nul
goto wait

:up
echo.
echo OK. ASH.Server is running: http://localhost:%PORT%/
echo From another PC open http://%COMPUTERNAME%:%PORT%/ (if it does not open: Get-NetConnectionProfile must not say Public).
echo The server fetches new releases from GitHub by itself and updates itself. Settings: %~dp0appsettings.json
if not defined NOPAUSE pause
exit /b 0

:down
echo.
echo The service was registered but did not answer on port %PORT% within 20 seconds.
echo See Event Viewer - Windows Logs - Application - source ASH.Server.
if not defined NOPAUSE pause
exit /b 1
