@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo.
echo ==============================================================
echo  SSD Health Inspector - Self-contained single-file build
 echo ==============================================================
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 8 SDK is required only on this BUILD machine.
  echo Install from https://dotnet.microsoft.com/download/dotnet/8.0
  echo No .NET runtime or third-party app is required on TARGET PCs.
  pause
  exit /b 1
)
if exist "publish" rmdir /s /q "publish"
dotnet publish "%~dp0SsdHealthInspector.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o "%~dp0publish"
if errorlevel 1 (
  echo.
  echo ERROR: The .NET build failed. See error details above.
  pause
  exit /b 1
)
if not exist "%~dp0publish\SsdHealthInspector.exe" (
  echo ERROR: The publish executable is missing.
  pause
  exit /b 1
)
echo.
echo SUCCESS: "%~dp0publish\SsdHealthInspector.exe"
echo Copy only this single executable to target Windows 10/11 x64 PCs.
echo No installed .NET runtime, smartctl, PowerShell, or vendor DLL is needed.
echo.
echo CLI examples:
echo   publish\SsdHealthInspector.exe --json
 echo   publish\SsdHealthInspector.exe --json --output health.json
 echo.
pause
exit /b 0
