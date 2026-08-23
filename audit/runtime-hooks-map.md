# Runtime Hooks Map

Status: `STATIC_ONLY — runtime launch not performed`

## Keyboard hook

| Field | Finding |
|---|---|
| `HookType` | `WH_KEYBOARD_LL` (`13`) |
| `HookAPI` | `user32!SetWindowsHookEx`, `CallNextHookEx`, `UnhookWindowsHookEx`; `kernel32!GetModuleHandle` |
| `HookScope` | `dwThreadId=0`: low-level hook for the current interactive desktop/session |
| `HookLifetime` | Started from `App.Application_Startup` after frames/tray initialization; one in-process hook handle |
| `Purpose` | Detect Win+D, configured profile hotkeys, search/focus shortcuts, and other application shortcuts. Some matching events may be swallowed by returning `1`; unrelated events are passed to `CallNextHookEx`. |
| `UnhookLocation` | `App.OnExit` → `GlobalHotkeyManager.StopMonitoring()` → `UnhookWindowsHookEx(_hookID)`; handle is then zeroed |
| `PersistsAfterExit` | `false` on normal exit according to source. Windows also removes process-owned hooks when the process terminates, but that abnormal-exit property was not runtime-tested. |

## Mouse hook

| Field | Finding |
|---|---|
| `HookType` | `WH_MOUSE_LL` (`14`) |
| `HookAPI` | `user32!SetWindowsHookEx`, `CallNextHookEx`, `UnhookWindowsHookEx`; `kernel32!GetModuleHandle` |
| `HookScope` | `dwThreadId=0`: low-level hook for the current interactive desktop/session |
| `HookLifetime` | Started from `App.Application_Startup` after initialization; one in-process hook handle |
| `Purpose` | Detect the taskbar Show Desktop click and double-clicks on the bare desktop, then wake/recover frames. It also queries the desktop window class and selected-icon count. It does not move or delete files. |
| `UnhookLocation` | `App.OnExit` → `DesktopMouseHook.Stop()` → `UnhookWindowsHookEx(_hookHandle)`; handle and delegate are cleared |
| `PersistsAfterExit` | `false` on normal exit according to source. Abnormal-exit cleanup was not runtime-tested. |

## Non-hook polling

`AutomationManager` is a separate foreground-window/process polling loop using `GetForegroundWindow`, `OpenProcess`, and `QueryFullProcessImageName`. It starts only when `SettingsManager.EnableProfileAutomation` is true; the default is `false`. It is not a global hook and was not enabled for this baseline.

## Safety conclusion

The source has explicit normal-exit unhook calls for both global hooks and no service, driver, or separate hook host was found. The hooks are still system-wide within the interactive desktop while the process is running, and the keyboard hook can intercept configured shortcut events; these are runtime risks to validate only inside an isolated account/session.

## Source evidence

- `Code/Desktop Frames/GlobalHotkeyManager.cs`: `WH_KEYBOARD_LL`, installation, callback, and `StopMonitoring`.
- `Code/Desktop Frames/DesktopMouseHook.cs`: `WH_MOUSE_LL`, desktop click detection, installation, and `Stop`.
- `Code/Desktop Frames/App.xaml.cs`: startup and `OnExit` ownership of both hooks.
- `Code/Desktop Frames/AutomationManager.cs`: optional foreground polling.

