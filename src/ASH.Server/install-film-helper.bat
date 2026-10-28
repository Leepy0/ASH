@echo off
rem ASH.Server: register the AutoView film helper (task ASH.FilmHelper) for the account that double-clicks this file, and start it now.
rem Log on to the server PC with the account that should render films, then double-click (it asks for administrator rights).
rem Another account: install-film-helper.bat DOMAIN\user. Remove: ASH.Server.exe --uninstall-film-helper
rem ASCII only on purpose: cmd mangles UTF-8 without a BOM.
setlocal
cd /d "%~dp0"

rem the account is taken before elevation: an elevated prompt may run as another (administrator) account
set "ACCOUNT=%~1"
if "%ACCOUNT%"=="" set "ACCOUNT=%USERDOMAIN%\%USERNAME%"

net session >nul 2>&1
if errorlevel 1 (
    echo Asking for administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '\"%ACCOUNT%\"' -Verb RunAs" >nul 2>&1
    if errorlevel 1 (echo Could not elevate. Right-click this file and choose "Run as administrator".& if not defined NOPAUSE pause)
    exit /b 1
)
if not exist "ASH.Server.exe" (echo ASH.Server.exe is not in %~dp0& if not defined NOPAUSE pause& exit /b 1)

echo Registering the film helper for %ACCOUNT%
"%~dp0ASH.Server.exe" --install-film-helper "%ACCOUNT%"
if errorlevel 1 (echo.& echo Film helper registration failed.& if not defined NOPAUSE pause& exit /b 1)

echo.
echo OK. The film helper starts whenever %ACCOUNT% logs on. Films render only while that account is logged on.
if not defined NOPAUSE pause
exit /b 0
