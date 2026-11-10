@echo off
setlocal
set "DOTNET=dotnet"
where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "D:\Tools\dotnet\dotnet.exe" (
    set "DOTNET=D:\Tools\dotnet\dotnet.exe"
  ) else (
    echo dotnet was not found on PATH. Install the .NET 8 SDK and make sure dotnet is on PATH.
    exit /b 1
  )
)
"%DOTNET%" publish "%~dp0src\StripView\StripView.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0publish\win-x64"
