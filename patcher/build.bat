@echo off
setlocal
echo [Witch-Rus-Patcher] Building single-file release...
dotnet publish "%~dp0Patcher.csproj" -c Release -o "%~dp0bin"
if errorlevel 1 (
    echo [ERROR] Build failed!
    pause
    exit /b 1
)
echo.
echo [OK] Built successfully: %~dp0bin\Witch-Rus-Patcher.exe
pause
