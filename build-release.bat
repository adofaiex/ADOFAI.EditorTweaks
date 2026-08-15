@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1

echo Building ADOFAI.EditorTweaks release package...
dotnet build "ADOFAI.EditorTweaks.csproj" -c Release /p:CreateModPackage=true /p:BumpModVersion=false
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
  echo Release build complete.
) else (
  echo Release build failed with exit code %EXIT_CODE%.
)

popd
exit /b %EXIT_CODE%
