# System integration audit

## Registry

The code uses `Microsoft.Win32.Registry` under the current user and writes the following areas:

| Area | Use | Startup or opt-in |
|---|---|---|
| `HKCU\Software\Desktop_Frames_Plus\InstanceTrigger` | inter-instance trigger value `TriggerEffect` | used by startup/IPC paths |
| `HKCU\Software\Desktop_Frames_Plus\ProgramManagement` | executable path, first/current version, `DVC`, `UnitID`, `EnPU` | `FrameManager.LoadAndCreateFrames()` calls `SetProgramManagementValues()` unconditionally |
| `HKCU\Software\Desktop_Frames_Plus\Settings` | `StartupMigrated` migration flag | `TrayManager` checks and writes it during construction |
| `HKCU\Software\Desktop_Frames_Plus\Messaging` | remote announcement dismissal/count state | written when remote notifications are displayed/dismissed |
| `HKCU\Software\Classes\DesktopBackground\Shell\DesktopFrames` | optional desktop background context menu and `-create` command | enabled from Options; startup can auto-heal an existing key |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `Desktop Frames +` startup value | only when the user enables it or a legacy startup shortcut is migrated |

`RegistryHelper.PerformRenameMigration()` also reads and may migrate/delete legacy `Desktop_Fences_Plus` registry data, old startup values, and old context-menu keys. `App.xaml.cs` calls `DeleteTrigger()` and `RefreshContextMenuPath()` during startup. The latter is conditional on an existing context-menu key, but the program-management and startup-migration writes are not conditional on a user opting into the context menu.

### Safety consequence

The first-run path is not registry-neutral. It reaches:

```text
App.Startup
  → TrayManager constructor
      → PerformStartupMigration() / SetStartupMigrated()
  → Framemanager.LoadAndCreateFrames()
      → RegistryHelper.PerformRenameMigration()
      → RegistryHelper.SetProgramManagementValues()
```

This is the reason the executable was not launched in this baseline phase. The audit itself only read registry-installation information; it did not write registry values. `RegistryModified=false` for this task.

## Input hooks and shell integration

- `GlobalHotkeyManager.StartMonitoring()` installs a `WH_KEYBOARD_LL` low-level keyboard hook through `user32.dll` during normal startup. It observes Win+D, SpotSearch, focus, profile, and other hotkeys.
- `DesktopMouseHook.Start()` installs a `WH_MOUSE_LL` low-level mouse hook during normal startup. It observes desktop double-clicks and taskbar Show Desktop clicks.
- `DesktopIconManager` finds the `Progman`/`WorkerW` desktop list view and calls `ShowWindow`; it restores the recorded state on exit. `FrameManager` calls `SetDesktopIconsVisible(!HideDesktopElementsOnStart)` during frame loading, so the running program can change desktop icon visibility even when no file is moved.
- A named global mutex `Global\\DesktopFramesPlus_Mutex_UniqueId_v2` provides single-instance protection.
- `TrayManager` creates a Windows Forms `NotifyIcon` and tray menus.
- `IWshRuntimeLibrary` COM automation creates/reads `.lnk` shortcuts. `shell32.dll` is used for Recycle Bin operations and `rundll32.exe` is used for an Open With fallback.
- `AutomationManager` can poll the foreground process and switch profiles when profile automation is enabled; it is off by default.

## What was not found

- No Task Scheduler registration.
- No native shell extension DLL or COM registration project.
- No file-association registration code found.
- No Explorer replacement or Explorer process injection.
- No service/driver installation.

The desktop background context-menu registry entry is still Explorer-visible shell integration and should not be enabled during baseline work.

## Side-effect record

```text
RegistryModified=false
StartupModified=false
ShellExtensionInstalled=false
ExplorerModified=false
GlobalHooksInstalled=false
DesktopIconStateChanged=false
```

The last four values are task-side effects, not claims that the source lacks those capabilities. The executable was not run.
