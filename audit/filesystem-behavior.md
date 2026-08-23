# Filesystem behavior audit

This is a static source audit. The product executable was not launched because its startup path has forbidden registry and desktop side effects. The statements below are based on call sites in `Code/Desktop Frames`.

## Storage model

The application uses the executable directory as its data root:

```text
<exe directory>/
├─ Profiles/<profile>/frames.json
├─ Profiles/<profile>/options.json
├─ Profiles/<profile>/Shortcuts/
├─ Profiles/<profile>/Backups/
├─ ProfileOptions.json
├─ MasterOptions.json                 optional global settings override
├─ auto_organize.json                 per-profile rules
├─ Desktop_Frames.log                 only when logging is enabled
└─ plugin state files                  e.g. *.dat
```

`ProfileManager` creates `Profiles\Default`, `frames.json`, and `options.json` when the directory is empty. `SettingsManager.LoadSettings()` writes the active JSON settings file after loading, so first launch is not read-only even in a fresh folder.

## Default-state safety switches

For a fresh `{}` options file, the effective values are established by `SettingsManager.ApplyJsonToProperties`:

| Behavior | Effective fresh value | Evidence |
|---|---:|---|
| Auto-organize desktop watcher | `false` | `SettingsManager.cs`; `AutoOrganizeManager.Initialize()` only calls `Start()` when enabled |
| Delete original shortcut on drop | `false` | `SettingsManager.cs` |
| Export shortcuts to real Desktop when deleting a frame | `false` | `SettingsManager.cs` |
| Profile automation | `false` | `SettingsManager.cs` |
| Context-menu registration | `false` | `SettingsManager.cs` |
| Hide desktop elements on start | `false` | `SettingsManager.cs` |
| Portal deletion uses Recycle Bin | `true` | `SettingsManager.cs`; can be changed to permanent deletion |
| Automatic backup | missing-value fallback is `false` in `ApplyJsonToProperties` | property initializer is `true`, but the missing-value fallback wins after settings load |

These defaults reduce the chance of automatic real-file movement, but they do not make the application side-effect-free.

## Real Desktop and user-file operations

### Automatic organization: high risk when enabled

`AutoOrganizeManager` watches `Environment.SpecialFolder.Desktop` with `FileSystemWatcher`. For enabled rules it can:

- create the configured target folder;
- wait for a file to unlock;
- move a matched file with `File.Move`;
- send an existing conflict to the Recycle Bin when overwrite is selected;
- generate a Portal frame for the target folder when the rule requests it.

The watcher also has a manual `ProcessDesktopNow()` sweep. It skips `.lnk`, `.url`, and common temporary-download extensions, but it is still a physical relocation engine. This feature is disabled by default, and no rule was enabled or executed here.

### Portal frames: user-triggered operations on real targets

`PortalFrameManager` mirrors a real folder and exposes context actions that operate on the target path:

- Rename: `Directory.Move` or `File.Move`.
- Delete with `UseRecycleBin=true`: native `SHFileOperation` with `FOF_ALLOWUNDO`.
- Delete with `UseRecycleBin=false`: recursive `Directory.Delete` or `File.Delete`.
- Cut: places a move-effect file list on the clipboard; Explorer performs the move when pasted.
- Open With / launch: delegates to the Windows shell.

These operations require an explicit user action, but they are incompatible with a future “reference, not relocation” safety contract unless separately gated.

### Data-frame drag and drop

Dragging a file or folder into a Data frame generally creates/copies a shortcut into the app-owned `Shortcuts` folder. For a raw file or folder the code creates a `.lnk` through `IWshRuntimeLibrary`; for an existing `.lnk` or `.url` it copies the shortcut file. A separate setting can delete the original dropped shortcut when it came from the Desktop; that setting defaults to `false`.

Deleting a frame can optionally copy its shortcuts back to the real Desktop through `CopyPasteManager.SendToDesktop`; `ExportShortcutsOnFrameDeletion` defaults to `false`.

### Frame/profile management

- Deleting a frame backs up frame metadata and shortcut copies, then removes the frame from JSON. The backup is app-owned, but `BackupManager.CleanLastDeletedFolder()` clears the previous deletion-backup files during frame loading.
- Profiles can be renamed with `Directory.Move` and deleted recursively with `Directory.Delete(profileDir, true)`. These paths are under the app's `Profiles` directory.
- Legacy migration can move `frames.json`, `fences.json`, `options.json`, `Shortcuts`, `Temp Shortcuts`, `Last Fence Deleted`, `CopiedItem`, and `Backups` from the executable directory into `Profiles\Default`.
- `FrameDataManager` can migrate `fences.json` to `frames.json` and delete the legacy file.
- Dead shortcut cleanup deletes missing-target shortcut files from app-owned `Shortcuts`/`Temp Shortcuts` paths; it does not delete the target file that the shortcut points to.

## First-run conclusion

On a fresh isolated application directory, default startup is not expected to move or delete real Desktop files because the auto-organize and destructive shortcut-export settings are off. It will create and rewrite app-local JSON/profile state and may clear app-local deletion-backup state. The source still contains direct real-file move/delete/rename paths, so `FilesystemBehaviorRisk=HIGH` for unrestricted long-term use.

No real Desktop file was moved, deleted, renamed, or copied by this task. The application was not launched.
