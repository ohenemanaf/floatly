@echo off
setlocal
cd /d "%~dp0"
echo ==================================================
echo FLOATLY  ^|  Build Windows portable release files
 echo ==================================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
dotnet publish "Floatly.App\Floatly.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "out\Floatly-win-x64"
if errorlevel 1 (
  echo PUBLISH FAILED. Please copy the error text.
  pause
  exit /b 1
)
echo.
echo SUCCESS! The release is in:
echo %CD%\out\Floatly-win-x64
start "" "%CD%\out\Floatly-win-x64"
echo Run Floatly.exe from that folder.
pause
endlocal
