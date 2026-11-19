# Floatly functional and release checklist

## Build
- [ ] dotnet build succeeds on Windows (x64) with no errors
- [ ] Included logic tests pass
- [ ] dotnet publish produces Floatly.exe
- [ ] App starts offline, with no API keys and no accounts

## Overlay
- [ ] Floatly button appears after launch
- [ ] Click opens the ring with all ten tools; Esc or a click outside closes it
- [ ] Button stretches on press and springs back; Reduce motion turns all animation off
- [ ] Central button collapses the menu
- [ ] Drag button to both sides; restart preserves its location
- [ ] Menu remains inside work area on main monitor
- [ ] Button appears over maximized Notepad, Chrome, Explorer and VS Code
- [ ] Right-click settings, hide, restore from tray, exit all work
- [ ] Ctrl+Alt+Space works when not already claimed by another program
- [ ] Theme/opacity/size changes reflect on floating button

## Tools
- [ ] Tasks: create, mark completed, filter, remove, save and reload
- [ ] Notes: multiple notes, search, edit, autosave, delete, save/reload
- [ ] Launcher: launch Notepad, Explorer, chosen local file/folder/URL
- [ ] Timer: start, pause, change preset; finish and notify when hidden
- [ ] Stopwatch: start, pause, reset
- [ ] Clipboard: manual save, recopy, delete, clear; opt-in monitor off on fresh launch
- [ ] Screenshot: primary screen, all monitors, primary region, Escape cancel
- [ ] Calculator: keypad, 2+3*4=14, (2+3)*4=20, division by zero error
- [ ] Settings: startup opt-in/out, backups, hotkey, reduce motion
- [ ] System: volume keys and Windows Settings shortcuts
- [ ] Focus: focus/break times, session completion count and notification

## Environment
- [ ] Mixed display scaling (100%, 150%, 200%) works without clipping
- [ ] Multi-monitor drag and screenshot work
- [ ] Sleep/wake while timer active behaves acceptably
- [ ] Non-admin Windows user installation works
- [ ] On shutdown, no stray Floatly icon/process remains
- [ ] Uninstall stops auto-launch and preserves user data

Status: **NOT YET RUN ON WINDOWS.** The developer must check these items on a Windows PC. Mixed-DPI and secure-desktop limitations are known risk areas.
