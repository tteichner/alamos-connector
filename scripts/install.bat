@ECHO off
setlocal enabledelayedexpansion

:: BatchGotAdmin
:-------------------------------------
REM  --> Check for permissions
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"

REM --> If error flag set, we do not have admin.
if '%errorlevel%' NEQ '0' (
    echo Requesting administrative privileges...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    set params = %*:"=""
    echo UAC.ShellExecute "cmd.exe", "/c %~s0 %params%", "", "runas", 1 >> "%temp%\getadmin.vbs"

    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    pushd "%CD%"
    CD /D "%~dp0"
:--------------------------------------

SET mypath=%~dp0
SET mypath2=!mypath!printit\PrintIt.ServiceHost.exe
SET mypath=!mypath!AlamosConnector.exe

ECHO Installiere Service:
ECHO %mypath%
ECHO %mypath2%
ECHO -----------------------
SET /p DUMMY=Hit ENTER to continue...

sc create AlamosConnector BinPath=%mypath%
sc create PrintIt binPath=%mypath2% start=auto
timeout /T 10 