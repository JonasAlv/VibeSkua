@echo off
setlocal EnableExtensions

rem Run a temporary copy so git pull can safely update this script on disk.
if defined BUILDPROJECT_RUNNING goto :BuildRunner

set "BUILDPROJECT_ROOT=%~dp0"
if "%~1"=="" set "BUILDPROJECT_PAUSE=1"
set "BUILDPROJECT_RUNNING=1"
set "BUILDPROJECT_TEMP=%TEMP%\BuildProject-%RANDOM%-%RANDOM%.bat"

copy /Y "%~f0" "%BUILDPROJECT_TEMP%" >nul
if errorlevel 1 (
    echo Failed to create a temporary copy of the build script.
    pause
    exit /b 1
)

call "%BUILDPROJECT_TEMP%"
set "BUILDPROJECT_EXIT=%ERRORLEVEL%"
del "%BUILDPROJECT_TEMP%" >nul 2>&1
exit /b %BUILDPROJECT_EXIT%

:BuildRunner
pushd "%BUILDPROJECT_ROOT%" || goto :Failure

echo Updating repository...
git pull --ff-only
if errorlevel 1 (
    echo Failed to pull updates. Resolve any Git issues before building.
    goto :Failure
)

echo Closing any running instances...
for %%P in (VibeSkua.exe VibeSkua.Manager.exe Skua.exe Skua.Manager.exe) do taskkill /IM "%%P" /F >nul 2>&1

if not exist "Build" (
    echo Preparing Build folder...
    mkdir "Build"
    if errorlevel 1 goto :Failure
)

if exist "Skua.AS3\skua\bin\skua.swf" (
    echo Skipping AS3 build because skua.swf already exists. Delete it to force a rebuild.
) else (
    echo Building AS3 Client...
    set "MXMLC_PATH="
    if exist "%USERPROFILE%\Downloads\apache-flex-sdk-4.16.1-bin\bin\mxmlc.bat" set "MXMLC_PATH=%USERPROFILE%\Downloads\apache-flex-sdk-4.16.1-bin\bin\mxmlc.bat"

    if defined MXMLC_PATH (
        powershell -NoProfile -ExecutionPolicy Bypass -File "Skua.AS3\compile-as3.ps1" -MxmlcPath "%MXMLC_PATH%"
    ) else (
        powershell -NoProfile -ExecutionPolicy Bypass -File "Skua.AS3\compile-as3.ps1"
    )

    if errorlevel 1 (
        echo Failed to build AS3 client.
        goto :Failure
    )
    if not exist "Skua.AS3\skua\bin\skua.swf" (
        echo AS3 build completed without producing skua.swf.
        goto :Failure
    )
)

echo Restoring solution once for faster project builds...
dotnet restore "Skua.sln"
if errorlevel 1 goto :Failure

call :BuildProject "Skua.App.Avalonia\Skua.App.Avalonia.csproj" "App" || goto :Failure
call :BuildProject "Skua.Manager.Avalonia\Skua.Manager.Avalonia.csproj" "Manager" || goto :Failure
call :BuildProject "Skua.Plugin.DailyTracker\Skua.Plugin.DailyTracker.csproj" "DailyTracker Plugin" || goto :Failure
call :BuildProject "Skua.Plugin.ButlerV5\Skua.Plugin.ButlerV5.csproj" "ButlerV5 Plugin" || goto :Failure
call :BuildProject "Skua.Plugin.Cosmetics\Skua.Plugin.Cosmetics.csproj" "Cosmetics Plugin" || goto :Failure

echo Packaging and organizing output to match WPF release structure...
powershell -NoProfile -ExecutionPolicy Bypass -File "package-build.ps1"
if errorlevel 1 goto :Failure
if not exist "Build\VibeSkua.exe" (
    echo Build completed without producing Build\VibeSkua.exe.
    goto :Failure
)

echo.
echo ==========================================================
echo Fast Build complete! The application is located in the Build folder.
echo You can run VibeSkua.Manager.exe or VibeSkua.exe from there.
echo ==========================================================
echo Launching VibeSkua...
start "" "Build\VibeSkua.exe"
if errorlevel 1 goto :Failure
set "BUILD_EXIT_CODE=0"
goto :Finish

:BuildProject
echo Building %~2 (Fast Dev Build)...
dotnet build "%~1" -c Release --no-restore
if errorlevel 1 exit /b 1
exit /b 0

:Failure
set "BUILD_EXIT_CODE=1"
echo.
echo Fast Build failed. Check the output above for details.

:Finish
if defined BUILDPROJECT_PAUSE pause
popd
exit /b %BUILD_EXIT_CODE%
