@echo off
setlocal

echo Closing any running instances...
taskkill /IM VibeSkua.exe /F >nul 2>&1
taskkill /IM VibeSkua.Manager.exe /F >nul 2>&1
taskkill /IM Skua.exe /F >nul 2>&1
taskkill /IM Skua.Manager.exe /F >nul 2>&1

echo Preparing Build folder...
if not exist "%~dp0Build" mkdir "%~dp0Build"

if exist "%~dp0Skua.AS3\skua\bin\skua.swf" (
    echo Skipping AS3 build ^(skua.swf already exists^). Delete the file to force a rebuild.
) else (
    echo Building AS3 Client...
    set "MXMLC_PATH="
    if exist "%USERPROFILE%\Downloads\apache-flex-sdk-4.16.1-bin\bin\mxmlc.bat" set "MXMLC_PATH=%USERPROFILE%\Downloads\apache-flex-sdk-4.16.1-bin\bin\mxmlc.bat"
    if defined MXMLC_PATH (
        powershell -ExecutionPolicy Bypass -File "%~dp0Skua.AS3\compile-as3.ps1" -MxmlcPath "%MXMLC_PATH%"
    ) else (
        powershell -ExecutionPolicy Bypass -File "%~dp0Skua.AS3\compile-as3.ps1"
    )
    if not exist "%~dp0Skua.AS3\skua\bin\skua.swf" (
        echo Failed to build AS3 client.
        pause
        exit /b 1
    )
)

echo Building App (Fast Dev Build)...
dotnet build .\Skua.App.Avalonia\Skua.App.Avalonia.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Failed to build App.
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)

echo Building Manager (Fast Dev Build)...
dotnet build .\Skua.Manager.Avalonia\Skua.Manager.Avalonia.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Failed to build Manager.
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)

echo Building DailyTracker Plugin (Fast Dev Build)...
dotnet build .\Skua.Plugin.DailyTracker\Skua.Plugin.DailyTracker.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Failed to build DailyTracker Plugin.
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)

echo Building ButlerV5 Plugin (Fast Dev Build)...
dotnet build .\Skua.Plugin.ButlerV5\Skua.Plugin.ButlerV5.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Failed to build ButlerV5 Plugin.
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)

echo Building Cosmetics Plugin (Fast Dev Build)...
dotnet build .\Skua.Plugin.Cosmetics\Skua.Plugin.Cosmetics.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Failed to build Cosmetics Plugin.
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)

echo Packaging and organizing output to match WPF release structure...
powershell -ExecutionPolicy Bypass -File "%~dp0package-build.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo Failed to package build output.
    if "%~1"=="" pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==========================================================
echo Fast Build complete! The application is located in the Build folder.
echo You can run VibeSkua.Manager.exe or VibeSkua.exe from there.
echo ==========================================================
if "%~1"=="" pause
