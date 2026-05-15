@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Starting POOC...

dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] dotnet not found. Please install .NET SDK.
    pause & exit /b
)

if not exist "POOC.csproj" (
    echo [ERROR] POOC.csproj not found. Place this .bat in the project folder.
    pause & exit /b
)

start /b dotnet run --launch-profile http
echo Waiting for app to start...
timeout /t 8 /nobreak >nul
start "" http://localhost:7000
echo App running at http://localhost:7000
echo Press Ctrl+C to stop.
pause