# Runtime Registry Map

Status: `STATIC_ONLY — runtime launch not performed`

This map was produced from the upstream source at commit `b9eb0471264538448549c49f487c96b9c53fb03a`. The program was not launched in the real user session.

## Startup write chain

1. `App.Application_Startup` loads the profile and settings, deletes a stale instance trigger, and refreshes an existing context-menu path.
2. `TrayManager` construction runs the one-time startup migration and writes the `StartupMigrated` marker.
3. `FrameManager.LoadAndCreateFrames` runs the legacy rename migration and writes program-management values.
4. Optional notification display/dismissal writes message state under the product key.

## Registry write map

| RegistryHive | RegistryKey | ValueName | ValueType | Value | Trigger | Purpose |
|---|---|---|---|---|---|---|
| `HKCU` (`Registry.CurrentUser`) | `SOFTWARE\Desktop_Frames_Plus\Settings` | `StartupMigrated` | `REG_SZ` | `1` | First `TrayManager` construction when the marker is absent | One-time migration marker; not Windows autostart |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `ProgramPath` | `REG_SZ` | Executable path | Every normal frame-load path | Product management/configuration value |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `FirstRunDate` | `REG_SZ` | Current timestamp, only when absent | First normal frame-load path | Product management/configuration value |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `FirstVersion` | `REG_SZ` | Current application version, only when absent | First normal frame-load path | Product management/configuration value |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `CurrentVersion` | `REG_SZ` | Current application version when missing/newer | Normal frame-load path | Product management/configuration value |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `DVC` | `REG_SZ` | Fixed opaque product value, only when absent | First normal frame-load path | Product-management identifier; no upload caller was found statically |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `UnitID` | `REG_SZ` | `df001`, only when absent | First normal frame-load path | Product-management identifier; no upload caller was found statically |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\ProgramManagement` | `EnPU` | `REG_DWORD` | `0`, only when absent | First normal frame-load path | Product-management/configuration flag |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\InstanceTrigger` | `TriggerEffect` | `REG_SZ` | Timestamp ticks, or `CMD_DRAW\|<GUID>` | Second instance / context-menu create command | Single-instance IPC trigger; main instance deletes the value on startup/processing |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\Messaging` | `<msgId>_Count` | `REG_DWORD` | Incremented count | Announcement/notification display | Local notification display state |
| `HKCU` | `SOFTWARE\Desktop_Frames_Plus\Messaging` | `<msgId>_Dismissed` | `REG_SZ` | `1` | User dismisses a notification | Local notification dismissal state |
| `HKCU` | `SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | `Desktop Frames +` | `REG_SZ` | Quoted executable path | User enables Start with Windows, or a legacy startup entry is detected during migration | **Autostart** for the current user |
| `HKCU` | `Software\Classes\DesktopBackground\Shell\DesktopFrames` | `(Default)` | `REG_SZ` | `Create New Frame` | User enables context menu, or an existing enabled key is refreshed at startup | Explorer desktop context-menu integration |
| `HKCU` | `Software\Classes\DesktopBackground\Shell\DesktopFrames` | `Icon` | `REG_SZ` | Executable path | Same as above | Explorer context-menu icon |
| `HKCU` | `Software\Classes\DesktopBackground\Shell\DesktopFrames\command` | `(Default)` | `REG_SZ` | `"<exe>" -create` | Same as above | Explorer context-menu command |

## Migration and deletion paths

These are conditional paths in `RegistryHelper.PerformRenameMigration` and `TrayManager.PerformStartupMigration`; they are not evidence that the fresh test profile contains the legacy keys.

| RegistryHive | RegistryKey / operation | Trigger | Purpose / risk |
|---|---|---|---|
| `HKCU` | Reads `SOFTWARE\Desktop_Fences_Plus`; if present, recursively copies it to `SOFTWARE\Desktop_Frames_Plus`, then deletes the old tree | Legacy installation detected | Product rename migration; destructive to the legacy registry tree but not a file operation |
| `HKCU` | Under the user Run key, deletes `Desktop Fences +`, `DesktopFences`, and `Desktop Fences`; if any existed, writes `Desktop Frames +` | Legacy Run values detected | Autostart migration |
| `HKCU` | Deletes legacy context-menu paths `Software\Classes\DesktopBackground\Shell\DesktopFences`, `Directory\Background\shell\Desktop Fences +`, and `Directory\Background\shell\Desktop Fences`; then rebuilds the new HKCU context-menu path | Legacy context-menu entry detected | Explorer integration migration |
| `HKCR` (`Registry.ClassesRoot`) | Reads and, if a legacy context-menu path exists, calls `DeleteSubKeyTree` for the same legacy paths | Legacy class-root context-menu entry detected | Conditional Explorer integration cleanup; `HKCR` is a merged view and can represent machine-backed class registration. This is the unresolved machine-hive boundary to verify before any real-user launch. |

The startup-folder migration also reads `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Desktop Fences.lnk`; if present, it deletes that shortcut after writing the HKCU Run value. The user-facing Start with Windows toggle can delete `Desktop Fences.lnk` and `Desktop Frames +.lnk`. These are file-system side effects, not registry writes.

## Hive and elevation conclusion

- Direct source references to `Registry.LocalMachine`, `HKEY_LOCAL_MACHINE`, `HKLM`, `requireAdministrator`, `highestAvailable`, and `requestedExecutionLevel` were not found.
- The normal fresh-profile writes are HKCU and do not inherently require administrator rights.
- `Registry.ClassesRoot.DeleteSubKeyTree` is a conditional legacy cleanup path. Because HKCR is a merged view, it must be treated as a potential machine-backed Explorer-registration write until a disposable profile snapshot proves that no legacy key exists.
- The source does not install a shell extension. The context-menu feature is a registry-based Explorer entry and is disabled by the default `EnableContextMenu=false` setting.
- No runtime launch was performed, so actual registry writes and any UAC prompt remain unvalidated.

## Source evidence

- `Code/Desktop Frames/App.xaml.cs`: startup trigger cleanup and context-menu refresh; hook startup/shutdown.
- `Code/Desktop Frames/TrayManager.cs`: startup migration, Run-key toggle, startup-folder cleanup.
- `Code/Desktop Frames/RegistryHelper.cs`: product-management, messaging, trigger, context-menu, and legacy migration paths.
- `Code/Desktop Frames/FrameManager.cs`: calls to `PerformRenameMigration` and `SetProgramManagementValues`.
- `Code/Desktop Frames/SettingsManager.cs`: safe defaults, including `EnableContextMenu=false`.

