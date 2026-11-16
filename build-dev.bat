@echo off
setlocal

pushd "%~dp0"
if errorlevel 1 exit /b 1

echo Building development package...
dotnet build "ADOFAI.EditorTweaks.csproj" -c Debug /p:CreateModPackage=true
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
    echo.
    echo Development build complete.
) else (
    echo.
    echo Development build failed with exit code %EXIT_CODE%.
)

popd
exit /b %EXIT_CODE%
