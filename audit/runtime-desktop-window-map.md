# Runtime Desktop Window Map

Status: `STATIC_ONLY — runtime launch not performed`

## DesktopIconManager classification

| Classification | Operation | Evidence / effect |
|---|---|---|
| `READ` | `FindWindow("Progman")`, `FindWindowEx` for `SHELLDLL_DefView`, `WorkerW`, and `SysListView32` | Locates the desktop icon list-view window; no file path is read or changed |
| `READ` | `IsWindowVisible` | Saves the initial visibility state of the desktop icon window |
| `READ` | `GetClassName`, `WindowFromPoint`, and `SendMessage(LVM_GETSELECTEDCOUNT)` in `DesktopMouseHook` | Identifies desktop windows and whether an icon is selected |
| `SHOW_HIDE` | `ShowWindow(listView, SW_SHOW/SW_HIDE)` | Shows or hides the desktop icon window; this is a window operation, not a file operation |
| `SHOW_HIDE` | `RestoreOriginalState` on WPF `Application.Exit` and `AppDomain.ProcessExit` | Restores the icon-window visibility and hides the optional dot on exit |
| `POSITION` | WPF dot overlay `Left`/`Top` placement | Positions Desktop Hub's visual dot near the bottom center of the screen; it does not reposition desktop icons |
| `OTHER` | `SetWindowLong`/`GetWindowLong` adds `WS_EX_NOACTIVATE` to the dot overlay | Changes only the overlay window style |
| `MOVE_FILE` | No call found in `DesktopIconManager.cs` | Not implemented by this manager |
| `DELETE_FILE` | No call found in `DesktopIconManager.cs` | Not implemented by this manager |
| `RENAME` | No call found in `DesktopIconManager.cs` | Not implemented by this manager |

## Startup behavior

`FrameManager.LoadAndCreateFrames` initializes `DesktopIconManager` and calls `SetDesktopIconsVisible(!SettingsManager.HideDesktopElementsOnStart)`. The default is `HideDesktopElementsOnStart=false`, so a fresh default profile requests visible desktop icons. If a user has enabled hiding, the manager changes the desktop list-view window visibility while running and has explicit restore logic on exit.

`DesktopMouseHook` listens for desktop clicks and invokes `Framemanager.WakeUpFrames()` or the Win+D event. This is a UI/frame visibility action; it is not a file move, delete, rename, or icon-coordinate rewrite.

## Window layer versus file-system layer

The desktop window manager itself operates on HWNDs and window visibility only. Other upstream modules have separate file-system capabilities that are outside this manager:

- `AutoOrganizeManager` can move matched files and send conflicts to the Recycle Bin when its setting is enabled; its default is `EnableAutoOrganize=false`.
- Portal/frame context actions can explicitly delete, rename, or cut target paths when the user invokes those actions.
- Startup migration can delete legacy `.lnk` files, as documented in `runtime-registry-map.md`.

Those capabilities are not inferred from the presence of a desktop HWND and must be audited independently. No runtime interaction was performed in the real user session, so no actual desktop-window or file-system side effect is claimed here.

## Source evidence

- `Code/Desktop Frames/DesktopIconManager.cs`: desktop HWND discovery, visibility, restore, and dot overlay.
- `Code/Desktop Frames/DesktopMouseHook.cs`: desktop-window classification and selected-icon query.
- `Code/Desktop Frames/FrameManager.cs`: manager initialization and initial visibility call.
- `Code/Desktop Frames/SettingsManager.cs`: default `HideDesktopElementsOnStart=false`.
- `Code/Desktop Frames/AutoOrganizeManager.cs` and portal managers: separate file-system behavior.

