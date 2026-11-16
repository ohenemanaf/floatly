# FLOATLY — Offline AssistiveTouch for Windows

**Native desktop application source code. No Codex, no login, no API key, no account, no web backend.**

Floatly is a small movable floating button that sits over ordinary Windows applications. Clicking it opens a polished radial menu with two pages and 10 useful tools. Data is stored in local JSON files. The UI is built in C# / WPF with .NET 10.

> **Build status:** Builds with 0 warnings and 0 errors on Windows 11 with .NET SDK 10.0.404, and the 13 logic tests pass. The items in `docs/TEST-CHECKLIST.md` still need to be ticked off by hand on a real desktop.

## What works in the source

| Tool | Implemented functionality |
|---|---|
| To-do List | Add, complete, delete; optional due date, priority and status filters; saved locally |
| Quick Notes | Create multiple notes; edit titles and content, search, autosave, delete; saved locally |
| App Launcher | Launch Windows basics; add/remove file, folder and web shortcuts; choose from dialogs |
| Timer & Stopwatch | Start, pause, reset; presets; shared clock service continues while tool panel is hidden |
| Clipboard History | Save copied text manually, copy again, remove, clear; optional *session-only* automatic text history (OFF by default) |
| Screenshot Tool | Save PNG of primary display or full virtual desktop; drag-select a region on primary display |
| Calculator | On-screen arithmetic keypad, keyboard expressions, safe calculation parser, last 15 results |
| Settings | Accent theme, opacity, button size, animation preference, hotkey and login startup, backup export |
| System Controls | Volume up/down/mute, Windows Wi-Fi/Bluetooth/Display/Sound/Power settings, Task Manager |
| Focus Sessions | Work/break countdowns, configurable duration, completed session count, desktop tray notification |

Other features: system tray (show, hide, exit), drag with edge snapping, always-on-top over ordinary windows, optional Ctrl+Alt+Space hotkey, single-instance enforcement, local backups and recovery.

## What YOU need to do manually

1. **Use a Windows 10/11 computer** (64-bit Windows recommended). Windows is required to compile and run the WPF interface.
2. **Install .NET 10 SDK** (free, one-time): https://dotnet.microsoft.com/download/dotnet/10.0 — choose the SDK, *not only the desktop runtime*. Restart PowerShell after installing.
3. **Unzip this project.** Open the folder named `Floatly`.
4. **Double-click `01-BUILD-AND-RUN.bat`.** It builds the code and opens Floatly. The purple circle should appear near the right edge of the screen. No Codex needed.
5. Once it works, **double-click `03-RUN-LOGIC-TESTS.bat`** to run calculation, storage and timer checks.
6. **Double-click `02-CREATE-PORTABLE-EXE.bat`** to build a self-contained Windows release in `out\Floatly-win-x64`.
7. Optional: **double-click `04-INSTALL-LOCALLY.bat`** to copy the published files into your local Apps folder and create a Windows Start shortcut. No administrator account is needed.
8. If you want the floating button after each reboot, open **Floatly → Settings → Start automatically after Windows sign-in**. This uses your account's Windows startup registry entry and is OFF until you enable it.

If Windows blocks `.bat` files from an internet ZIP, extract the archive first, inspect the scripts, and run the documented commands directly in Windows PowerShell. You do not need to disable antivirus or Windows security protections.

## Commands you can run in PowerShell instead

From the extracted `Floatly` folder:

```powershell
dotnet --info
dotnet build .\Floatly.App\Floatly.App.csproj
dotnet run --project .\Floatly.App\Floatly.App.csproj
dotnet run --project .\Floatly.LogicTests\Floatly.LogicTests.csproj
dotnet publish .\Floatly.App\Floatly.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\out\Floatly-win-x64
```

`dotnet build` might download the required .NET build references using official NuGet feeds during the first restore; the **finished Floatly app itself has no network dependency**, and there are no third-party NuGet packages in this project.

## How to use

- **Click** the floating button to expand its menu; select a tool.
- Select **More** for the other five tools.
- **Drag** the button to reposition it (it snaps to the closest left/right edge).
- **Right-click** the button for Settings, Hide and Exit.
- **Ctrl+Alt+Space** toggles the menu (when enabled and not claimed by another program).
- **×** on the tools panel hides the panel but keeps Floatly running.
- Use the **system tray icon** to restore the button, open Tasks/Notes, or fully exit.

## User data & privacy

- Tasks, notes, bookmarks, manually/optionally captured clipboard text and preferences are stored as readable JSON at `%LOCALAPPDATA%\Floatly\state.json`. These files are **not encrypted**; never save passwords or authentication codes.
- Prior state is backed up to `state.backup.json` when a newer state is saved. Damaged JSON is moved aside for recovery; there is an **Export backup** button in Settings.
- Clipboard **auto-capture is session-only and OFF at launch**. Turning it on captures copied text as you work, which can contain sensitive data. Floatly cannot reliably tell which text is a password, so leave it off unless you accept that risk. Use Clear saved entries to delete history.
- Screenshots are user-initiated, stored locally at `Pictures\Floatly Captures`, and never uploaded.
- No AI, telemetry, analytics, account sign-in, cloud service or API keys.
- Screenshots and on-screen overlays cannot appear on secure Windows desktops and may not appear over exclusive-fullscreen games. **Always on top** is for ordinary Windows applications.

## Known limitations & honest boundaries

- The UI, multi-monitor display-scaling edge cases, fullscreen behavior, keyboard shortcut conflicts, installer workflow, and hardware-specific Windows controls need real Windows testing.
- Drag-select screenshot regions support the **primary monitor**; full-desktop screenshot capture includes all monitors.
- A brightness slider is not implemented because it is device-dependent; the System page opens Windows Display settings.
- `Topmost` is not a security bypass and does not guarantee visibility over a protected or exclusive-fullscreen surface.
- The project outputs a Windows `.exe` on your computer after `dotnet publish`; an executable cannot be prebuilt or validated in this Linux environment.
- The optional install scripts create user-local shortcuts; this is not a signed MSIX/MSI installer. Before public distribution, consider code signing and a formal installer.
- Startup from an unpublished Debug build is deliberately disallowed. Publish and install first.

## Folder structure

```text
Floatly/
  01-BUILD-AND-RUN.bat          One-click build and launch
  02-CREATE-PORTABLE-EXE.bat    Build standalone Windows release
  03-RUN-LOGIC-TESTS.bat        Run included tests
  04-INSTALL-LOCALLY.bat        Optional user-local install
  05-UNINSTALL-LOCAL.bat        Optional uninstall, preserving your data
  Floatly.App/
    Assets/Floatly.ico         App icon
    Models/                    Persistent task, notes and preference models
    Services/                  Storage, calculator, screenshot, timer, startup
    Views/                     Floating radial menu, tool panel, region selector
    App.xaml                   Appearance resources
    App.xaml.cs                Application lifecycle and tray
  Floatly.LogicTests/          Logic checks without external test packages
  scripts/                     User-local installation helpers
  docs/                        Windows testing and troubleshooting
```
