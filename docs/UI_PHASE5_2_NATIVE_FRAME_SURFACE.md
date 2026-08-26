# UI Phase 5.2 — Native Frame hit-test surface prototype

## Outcome

Plan A establishes a technically sound hollow-HWND prototype: the header and resize rim remain part of the WPF Frame, while the center is removed from the HWND region and naturally exposes the next real native window. It is not yet production-enabled because the available Computer Use controller did not return Explorer Desktop or the no-activate test Frame as targetable windows, so native single-click, double-click, context menu, and drag were not certified with real pointer input.

```text
Status=PARTIAL
ChosenPlan=A_PROVISIONAL
WindowRegionUsed=true
DualWindowUsed=false
NativeVisualOwnershipFeasible=UNCONFIRMED_PENDING_POINTER_CERTIFICATION
```

## Architecture

```text
NonActivatingWindow (existing single HWND)
├─ header region: WPF visual + input
├─ 8 DIP resize rim: WPF HWND + native resize hit codes
└─ central content: removed from HWND region
                     ↓
               real underlying HWND
               (Explorer SysListView32 when Desktop is exposed)
```

The implementation is isolated in:

- `NativeFrameRegionBuilder`: pixel geometry and GDI region ownership.
- `NativeFrameSurfaceManager`: TEST-only attach, apply, debounced resize/DPI refresh, and cleanup.
- `NonActivatingWindow`: opt-in flag and minimal prototype-only `WM_NCHITTEST` mapping.

`FrameManager.cs` is unchanged. No production Frame enables the prototype flag.

## Why transparency was insufficient

WPF transparency controls rendering and element hit testing; it does not remove the top-level `HwndWrapper` from the operating system's window geometry. The central `HRGN` subtraction changes native window geometry itself. Consequently, `WindowFromPoint` over the hole can return Explorer `SysListView32` directly without any input forwarding.

## Geometry and DPI

The region builder converts the 32 DIP header and 8 DIP resize rim using WPF's current `DpiScaleX`/`DpiScaleY` and uses physical pixels for `SetWindowRgn`. The runtime test used 100% scaling.

```text
CurrentDpiRuntimeTest=100% PASS
125PercentRuntimeTest=NOT_RUN
150PercentRuntimeTest=NOT_RUN
MultiMonitorSupport=NOT_CERTIFIED
PrimaryMonitorPrototype=true
```

No system DPI setting was changed. Before production adoption, 125%, 150%, negative monitor origins, and production Frame z-order need direct validation.

## Input ownership

```text
Header=Frame WPF HWND
LeftRightBottomRim=Frame WPF HWND with HTLEFT/HTRIGHT/HTBOTTOM/corner codes
CentralContent=underlying native HWND
```

The header continues to use the existing WPF `DragMove` path. Lock removes resize style and suppresses prototype resize hit codes. A native-reference Frame should disable RollUp in V1 because no icon parking behavior exists.

## Safety

```text
InputForwarding=false
SendInput=false
ExplorerInjection=false
ExplorerHook=false
SecondWindow=false
FilesystemMove=false
FilesystemDelete=false
FilesystemCopy=false
AutoArrangeModified=false
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
BackgroundPolling=false
```

The Phase 5.1 snapshot/restore implementation remains unchanged and restored both test icons to exact baseline coordinates.

## Why Plan B was not attempted

Plan A did not fail technically. Its region, hit-test routing, resize codes, lock behavior, snapshot/restore, GDI ownership, and Explorer stability all passed. The missing evidence is real GUI pointer certification, so moving to a more complex two-HWND design would violate the agreed sequencing and add unnecessary synchronization risk.

## Production adoption gate

Before enabling `VisualOwnershipMode=NativeDesktopIcon`, complete one quiet-session test in which Explorer Desktop and the no-activate test Frame are available to the GUI controller:

1. Native TEST icon single-click selects in Explorer.
2. Double-click opens only a disposable target.
3. Right-click opens the native shell context menu.
4. Drag is handled by Explorer.
5. Header drag and all retained resize rims work.
6. Lock blocks Frame movement/resize.
7. Production Frame z-order keeps the chrome on Desktop rather than above foreground apps.

Only after those checks should the renderer stop drawing a duplicate reference icon and Phase 5 high-confidence Batch 1 begin.
