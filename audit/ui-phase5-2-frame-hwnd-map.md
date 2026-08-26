# UI Phase 5.2 — Frame HWND map

Date: 2026-08-26

## Production Frame construction

```text
FrameHwnd=one top-level WPF HwndWrapper per NonActivatingWindow
WindowStyle=WindowStyle.None; ResizeMode is CanResizeWithGrip or NoResize
ExtendedStyle=WS_EX_NOACTIVATE plus WPF-owned layered/tool-window flags
AllowsTransparency=true
WindowChrome=none
HwndSourceHook=NonActivatingWindow.WndProc
ShowInTaskbar=false
```

`Framemanager.CreateFrame` builds the visual tree dynamically:

```text
NonActivatingWindow
└─ Border (Frame outer visual)
   └─ DockPanel
      ├─ Grid titleGrid (top, 32 DIP after Phase 3)
      └─ content ScrollViewer / item panel
```

The title grid owns the existing WPF title interaction, including `DragMove`, roll-up, lock, rename, hover controls, and the Frame context menu. Resize is controlled by `ResizeMode`; lock switches it to `NoResize`.

## Pre-prototype hit-test path

```text
HeaderRect=full Frame width, top 32 DIP
ContentRect=remaining area below the header
ResizeBorder=the WPF top-level HWND resize surface
CurrentHitTestPath=screen point -> complete WPF HwndWrapper rectangle
CurrentWMNCHITTESTHandling=none beyond WPF defaults
```

An element-level transparent background, `Opacity=0`, or `IsHitTestVisible=false` does not remove the top-level HWND from `WindowFromPoint`; this was the Phase 5.1 blocker.

## TEST-only Plan A path

The prototype is opt-in through `NonActivatingWindow.NativeSurfacePrototypeEnabled`. `Framemanager` does not set this flag, so production Frames retain their prior behavior.

```text
HeaderRect=0..Width x 0..32 DIP
ContentRect=8 DIP..Width-8 DIP x 32 DIP..Height-8 DIP
ResizeBorder=8 DIP logical hit rim; visual border remains unchanged
CurrentHitTestPath=
  header/rim -> WPF HwndWrapper
  central hole -> next real HWND in z-order (Explorer SysListView32 when Desktop is exposed)
CurrentWMNCHITTESTHandling=
  header -> HTCLIENT, existing WPF DragMove
  left -> HTLEFT
  right -> HTRIGHT
  bottom -> HTBOTTOM
  bottom-left -> HTBOTTOMLEFT
  bottom-right -> HTBOTTOMRIGHT
```

Measured prototype HWND state at 100% DPI:

```text
WindowRect=988,202 to 1438,522 (450 x 320 device pixels)
WindowStyle=0x160F0000
ExtendedStyle=0x08080100
HeaderHeightPx=32
ResizeBorderPx=8
ContentHole=8,32 to 442,312
```

After a test move and resize, the region was rebuilt for a 520 x 360 window. Lock changed style `0x160F0000` to `0x16080000`; unlock restored `0x160F0000`.

## Lifecycle

`NativeFrameSurfaceManager` attaches after `SourceInitialized`, applies once at dispatcher-loaded priority, and schedules a single 200 ms debounced rebuild after size or DPI changes. It removes handlers and stops its timer when the test window closes. It does not poll and creates no worker thread.
