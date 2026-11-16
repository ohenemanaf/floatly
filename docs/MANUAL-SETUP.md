# Things to do yourself (no Codex login required)

1. Download and extract Floatly ZIP on a Windows 10 or 11 x64 computer.
2. Install the **.NET 10 SDK**, found at https://dotnet.microsoft.com/download/dotnet/10.0. Select the x64 SDK installer.
3. Open extracted folder and double-click **01-BUILD-AND-RUN.bat**.
4. If Windows shows a build error: copy the full error message; paste it into ChatGPT. Do not change C# files at random.
5. Test click, drag, app launcher, all 10 tool pages, save notes, restart, and system tray on your actual PC.
6. Run **03-RUN-LOGIC-TESTS.bat** and review PASS results.
7. Run **02-CREATE-PORTABLE-EXE.bat**. A release is made under `out\Floatly-win-x64\`.
8. Optionally run **04-INSTALL-LOCALLY.bat** to create a Start Menu shortcut without admin permissions.
9. Open Floatly → Settings and optionally enable **start after Windows sign-in**.
10. Test reboot, sleep/wake, 100/150/200% scaling, multiple monitors (if available), maximized apps, and Windows permissions.

No API key, Windows Store account, Visual Studio, GitHub, or Codex installation is required for these steps. An internet connection may be needed once to download the .NET SDK/build references; Floatly does not require an internet connection to function.

If double-clicking `.bat` is blocked, open PowerShell in the extracted directory and type:

```
dotnet build .\Floatly.App\Floatly.App.csproj
dotnet run --project .\Floatly.App\Floatly.App.csproj
```

If you see a build error, send a screenshot or paste the error text. Windows-only testing still has to happen on your computer, and any issues can then be patched in a new source ZIP.
