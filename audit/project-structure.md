# Project structure and technology audit

## Structure tree

```text
DesktopHub/
├─ .github/ISSUE_TEMPLATE/
├─ Code/
│  ├─ Desktop Frames.sln
│  └─ Desktop Frames/
│     ├─ Desktop Frames.csproj
│     ├─ App.xaml / App.xaml.cs
│     ├─ FrameManager.cs / FrameDataManager.cs
│     ├─ ProfileManager.cs / SettingsManager.cs
│     ├─ PortalFrameManager.cs / AutoOrganizeManager.cs
│     ├─ BackupManager.cs / CopyPasteManager.cs
│     ├─ TrayManager.cs / GlobalHotkeyManager.cs
│     ├─ DesktopMouseHook.cs / DesktopIconManager.cs
│     ├─ RegistryHelper.cs / InterCore.cs
│     ├─ Plugins/
│     ├─ Localization/
│     └─ Resources/
├─ Images/
├─ ngdfcs/
├─ README.md, License.md, VersionHistory.md, TIPS.md, ...
├─ audit/
├─ build/
└─ docs/
```

## Project inventory

| Item | Finding |
|---|---|
| `.sln` | `Code/Desktop Frames.sln` |
| `.slnx` | none |
| `.csproj` | one: `Code/Desktop Frames/Desktop Frames.csproj` |
| `.vcxproj` | none |
| Installer project | none found |
| Updater project | none found |
| Tests | no test project found |
| Shared libraries | no separate solution project; plugins are source folders in the primary project |

## Technology stack

- C# desktop application.
- WPF application with `UseWPF=true`.
- Windows Forms support with `UseWindowsForms=true`, primarily for tray and interop UI.
- Target framework: `net8.0-windows7.0`.
- `OutputType=WinExe`; namespace/root namespace is `Desktop_Frames`.
- Win32 P/Invoke through `user32.dll`, `kernel32.dll`, and `shell32.dll`.
- Windows Shell/COM shortcut handling through `IWshRuntimeLibrary`.
- JSON persistence through Newtonsoft.Json. No SQLite reference was found.
- NAudio is used for notification/audio behavior.
- No WinUI, Windows App SDK, native C++ project, Electron, Flutter, or database project was found.

The main runtime flow is centered on `App` → `ProfileManager`/`SettingsManager` → `TrayManager`/`Framemanager` → frame, portal, profile, backup, hook, and plugin managers. Configuration is portable and stored beside the executable rather than under a central database; details are in the filesystem audit.
