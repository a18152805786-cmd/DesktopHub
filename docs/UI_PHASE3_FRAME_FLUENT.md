# UI Phase 3 — Frame Fluent Restyle

## Scope

```text
DesignGoal=Minimal Fluent Desktop Widget
ResourceDictionary=Code/Desktop Frames/Themes/DesktopHubFrame.xaml
FrameStyleHelper=Code/Desktop Frames/FrameVisualStyleHelper.cs
FrameStates=Normal,Hover,Locked,RolledUp
HeaderHeight=32 DIPs
CornerRadius=10 DIPs (0 when the existing sharp-corners setting is enabled)
BorderThickness=1 DIP
BackgroundMode=Theme-aware translucent neutral solid
Mica=false
Acrylic=false
WebView=false
```

The change is presentation-only. `NonActivatingWindow`, `Framemanager`, Frame data, icon membership, drag/drop, Portal handling, profile loading, and `frames.json` remain the existing architecture.

## Visual resources

`DesktopHubFrame.xaml` owns the shared brushes, geometry, typography, and control states:

- light and dark neutral palettes;
- high-contrast aliases selected by `FrameVisualStyleHelper`;
- a one-pixel low-contrast border;
- a 10-DIP internal corner radius;
- a compact 32-DIP header;
- Segoe UI Variable Text with Segoe UI fallback;
- 140 ms opacity transitions for header controls;
- title ellipsis for long Chinese, English, mixed, numeric, and emoji titles.

No bitmap shadow or continuously running animation was added.

## Frame states

### Normal / idle

- Translucent neutral background with low border contrast.
- Title remains readable.
- Menu and lock controls have `Opacity=0` and `IsHitTestVisible=false`.
- The persisted default title is displayed compactly as `新建分组`/`New Frame`; the original persisted title remains unchanged and is retained as the tooltip and rename value.

### Hover

- Background and border become slightly stronger.
- Menu and lock controls fade to full opacity in 140 ms.
- Hidden controls become hit-testable only while the Window-level hover trigger is active.
- The trigger observes the whole Window, avoiding child-to-child mouse enter/leave loops.

### Locked

- Existing `ResizeMode.NoResize` behavior is unchanged.
- The lock control uses a restrained theme-aware accent only while controls are visible.

### Rolled up

- Rolled height now uses the shared 32-DIP header resource instead of the former scattered 28-pixel literals.
- Existing animation, `IsRolled`, `UnrolledHeight`, and persistence paths are unchanged.

### Edit/customize

The existing Ctrl-based rename/reorder and Customize flows remain unchanged. Phase 3 does not add persistent edit chrome or new appearance fields.

## Theme and accessibility behavior

- The current Windows app theme is read from the existing per-user theme setting without writing the registry.
- Light and dark palettes are both compiled into the ResourceDictionary.
- Windows High Contrast selects system brushes at runtime.
- WPF device-independent units, layout rounding, and pixel snapping are used; no 100%-DPI-only pixel-region hack was added.

## Compatibility

```text
FrameSchemaChanged=false
ProfileSchemaChanged=false
FramesJsonMigration=false
AutoOrganizeBehaviorChanged=false
ProductionRulesChanged=false
NewNuGetPackages=0
NewExternalLibraries=0
NewFontFiles=0
```

The project file’s existing XML declaration said `utf-16BE` while the file bytes were UTF-8. Adding a second XAML compilation pass exposed this mismatch, so only the declaration was corrected to `utf-8`; project references and build settings are unchanged.

## Performance impact

The implementation adds one shared ResourceDictionary and lightweight WPF styles. It does not add blur, bitmap effects, continuous timers, or layout-property animations.

Observed with five Frames after approximately seven minutes:

```text
WorkingSetMB=185.09
PrivateMemoryMB=101.14
IdleCPUPercentApprox=0.0097
NoContinuousMemoryGrowth=true
```

## Known visual gaps

- Empty-Frame helper text was styled but not injected into the production visual tree in V1, avoiding a risky content-layout change.
- Per-Frame custom title size/color and border color remain compatible; a broader appearance-settings redesign is deferred.
- Acrylic, Mica, native clipping regions, and custom shadows are intentionally deferred.
- Computer Use cannot target `WS_EX_NOACTIVATE` Frame windows. Actual Frame screenshots, Win32 properties, persisted state, compiled resources, and a disposable WPF style harness were used instead of changing the Window type.

