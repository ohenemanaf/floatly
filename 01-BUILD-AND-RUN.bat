@echo off
setlocal
cd /d "%~dp0"
echo =====================================================
echo  FLOATLY  ^|  Build and Run
 echo =====================================================
where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 10 SDK was not found.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/10.0
  echo Open this file again after installing .NET.
  pause
  exit /b 1
)
echo.
echo Building Floatly...
dotnet build "Floatly.App\Floatly.App.csproj" -c Debug
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Please copy the error text from this window.
  pause
  exit /b 1
)
echo.
echo Starting Floatly. Look for the floating button at the edge of your screen.
echo Closing this terminal does not necessarily exit Floatly. Use its tray icon to exit.
dotnet run --no-build -c Debug --project "Floatly.App\Floatly.App.csproj"
if errorlevel 1 (
  echo Run failed. Please copy this error message.
  pause
  exit /b 1
)
endlocal
