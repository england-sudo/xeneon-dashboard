@echo off
setlocal
cd /d "%~dp0XeneonDash"

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
  echo [XeneonDash] .NET 8 SDK not found.
  echo Get it here: https://dotnet.microsoft.com/download
  echo Then re-run this file.
  pause
  exit /b 1
)

if /i "%1"=="installer" (
  echo [XeneonDash] Publishing self-contained app...
  dotnet publish -c Release -r win-x64 --self-contained true -o "%~dp0publish"
  if %errorlevel% neq 0 (
    echo [XeneonDash] Publish failed.
    pause
    exit /b 1
  )
  where makensis >nul 2>nul
  if %errorlevel% neq 0 (
    echo [XeneonDash] NSIS not found — app published to publish\ but no installer built.
    echo Install NSIS 3 from https://nsis.sourceforge.io/ then re-run: build.bat installer
    pause
    exit /b 1
  )
  echo [XeneonDash] Building installer...
  makensis "%~dp0XeneonDash.nsi"
  echo.
  echo Done: XeneonDash-Setup.exe
) else if /i "%1"=="publish" (
  echo [XeneonDash] Building portable single-file exe (no .NET install needed to run^)...
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0publish"
  echo.
  echo Done: publish\XeneonDash.exe
) else (
  echo [XeneonDash] Building...
  dotnet build -c Release
  echo.
  echo Done: XeneonDash\bin\Release\net8.0-windows\XeneonDash.exe
  echo (needs the .NET 8 Desktop Runtime on the PC that runs it)
)

pause
