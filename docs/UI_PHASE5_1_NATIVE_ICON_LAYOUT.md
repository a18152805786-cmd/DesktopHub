# UI Phase 5.1 — Native Desktop icon layout

## Outcome

Phase 5.1 implements and verifies the reversible low-level foundation for native Desktop icon positioning. It does not enable production native visual grouping because the current top-level WPF Frame intercepts mouse input over its content area.

```text
Status=PARTIAL
DesktopIconEnumeration=PASS
StableIconIdentity=PASS_WITH_AMBIGUOUS_ITEMS_SKIPPED
ReadPosition=PASS
SetPosition=PASS
Snapshot=PASS
Restore=PASS
VisualDuplicateResolved=false
ProductionBatch1=NOT_RUN
```

## Architecture

```text
DesktopIconManager
  └─ existing Progman/WorkerW/SysListView32 discovery

NativeDesktopIconIdentityResolver
  └─ Explorer display label -> unique user/Public Desktop OriginalPath

NativeDesktopIconLayoutService
  ├─ enumerate native ListView items
  ├─ detect Auto Arrange and Align to Grid
  ├─ read and set icon coordinates
  ├─ capture a complete layout snapshot
  └─ restore stable, still-existing items only
```

The runtime ListView index is transient and is never persisted. `OriginalPath` remains the source of truth. Duplicate labels and shell namespace items fail closed.

## Snapshot guarantees

- Snapshot files are human-readable JSON.
- `SaveSnapshot` uses create-new semantics and cannot overwrite an existing backup.
- Restore only addresses paths present in the snapshot and still uniquely resolvable now.
- Missing items are skipped.
- New items created after a snapshot are untouched.
- Ambiguous display names and shell namespace items are skipped.
- Auto Arrange is detected and causes position writes/restores to fail closed; it is never modified.

The validated baseline snapshot is `audit/ui-phase5-1-native-icon-layout-before.json`. The future product backup location should be the application backup/config area as `native_desktop_layout_backup.json`, but it is intentionally not created until production Apply exists and passes the interaction gate.

## Coordinate behavior

Desktop ListView coordinates are view/device-pixel coordinates. Phase 5.1 validates direct native position operations and Explorer grid snapping. WPF-DIP-to-Desktop coordinate mapping, multi-monitor frame placement, frame-move relayout, and frame-resize relayout are intentionally not connected while production grouping is blocked.

```text
DpiHandling=Native ListView coordinates verified; WPF mapping deferred
MultiMonitorHandling=Not production-enabled
FrameMoveRelayout=Not connected
FrameResizeRelayout=Not connected
BackgroundPolling=false
StartupAutoApply=false
```

## Visual ownership blocker

The native icon can be seen through the Fluent Frame, but the Frame is a separate top-level WPF window above Explorer. The content surface receives hit testing before the Desktop ListView, so native single-click, double-click, drag, and context-menu input cannot naturally reach the icon.

The service therefore remains a low-level, opt-in foundation. No `VisualOwnershipMode=NativeDesktopIcon` migration was added, and existing Phase 5 references continue using their compatible Frame-rendered path. This avoids hiding the original icon, duplicating it into the profile, or silently breaking interaction.

## Safety invariants

```text
DesktopFilesystemMove=false
DesktopFilesystemDelete=false
DesktopFilesystemRename=false
ShortcutCopy=false
RegistryAutoArrangeModification=false
DesktopIconGlobalHide=false
ExplorerInjection=false
ExplorerRestart=false
BackgroundPolling=false
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
```

## Recommended next step

Pause production native grouping. Build a TEST-only prototype that separates the Frame into a click-through visual content surface and an independently interactive header/border surface. Proceed only if native icon click, double-click, drag, and context menu work while Frame drag, resize, lock, roll-up, and non-activation remain intact. If that cannot be achieved without fragile input forwarding or shell integration, keep Phase 5 reference rendering and accept that native visual ownership is not suitable for this WPF window architecture.
