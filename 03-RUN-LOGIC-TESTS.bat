@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (echo Install .NET 10 SDK first. & pause & exit /b 1)
dotnet run --project "Floatly.LogicTests\Floatly.LogicTests.csproj"
if errorlevel 1 (echo LOGIC TESTS FAILED. & pause & exit /b 1)
pause
endlocal
