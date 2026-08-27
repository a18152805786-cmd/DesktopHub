# DesktopHub Phase 5.2 final native ownership certification

Date: 2026-08-27
Status: `PARTIAL`

This run used the existing Plan A single-window hollow Frame. Real desktop
validation was authorized and performed. Production rollout was stopped at the
required gate when the Computer Use controller could not target the Explorer
Desktop ListView that is exposed through the hollow region.

## Checkpoint and scope

```text
Repository=C:\Users\71966\Desktop\DesktopHub
Branch=develop
CommitBefore=5d306a83c931d8012a8f7babc01b7d69eff03f4f
CheckpointTag=pre-ui-phase5-2-final-certification
CheckpointPushedTo=origin
ChosenPlan=A_PROVISIONAL
RealGUIValidationAuthorized=true
SyntheticInputUsed=false
PlanBUsed=false
ExplorerInjectionUsed=false
InputForwardingUsed=false
```

The annotated checkpoint was created before code changes and pushed to
`origin`; `upstream` was not modified.

## Real GUI result

The Frame was created as `NATIVE FINAL TEST` over a disposable shortcut. The
central point resolved to Explorer's `SysListView32`, while header and rim
points resolved to the Frame HWND. The observed Frame rect began at
`(140,120)-(780,560)`, moved to `(200,128)`, and then changed size through
right, bottom, bottom-right, left, and bottom-left rim drags.

```text
ContentAreaPassThrough=true
HeaderInteractive=true
BorderInteractive=true
HeaderDrag=PASS
Resize=PASS
Lock=PASS
Unlock=PASS
FrameRenderedDuplicate=false (TEST native item)
VisualDuplicateResolved=TEST_PASS;PRODUCTION_NOT_RUN
FrameMoveRelayout=PASS
FrameResizeRelayout=PASS
RelayoutDebounceMs=200
```

Locking changed the accessibility description to `分组已锁定（点击解锁）`,
set `ResizeMode=NoResize`, and blocked a header drag without changing the
Frame position. Unlock restored `分组未锁定（点击锁定）` and
`CanResizeWithGrip`.

The four native icon actions remain deliberately uncertified:

```text
NativeIconSingleClick=NOT_CERTIFIED
NativeIconDoubleClick=NOT_CERTIFIED
NativeIconContextMenu=NOT_CERTIFIED
NativeIconDrag=NOT_CERTIFIED
```

Computer Use returned only application windows. It rejected a hollow-content
coordinate because the underlying point belonged to `explorer.exe`'s desktop
`FolderView`, which was not a returned target window. A screenshot showed a
selection highlight after one attempted click, but the action result was
unknown and no Explorer launch or native context menu was observable. This is
not treated as proof. No `SendInput`, click forwarding, message proxy, DLL
injection, Explorer injection, or full desktop-icon hiding was used.

## Ownership implementation

`ReferenceFrameItem` now persists an explicit `VisualOwnershipMode`. Existing
items default to `FrameRendered`; only items explicitly marked
`NativeDesktopIcon` suppress the WPF icon, label, and clickable item renderer.
A Frame is considered native only when all of its reference items are native,
so mixed legacy Frames retain their previous rendering behavior.

Native Frames keep the Plan A hollow region: header and resize rim remain the
Frame's HWND, and the content hole exposes the real Desktop. The TEST native
item rendered only once through Explorer (`FrameRenderedDuplicate=false`).
Native mode
disables RollUp and supplies localized tooltips:

```text
中文=原生桌面图标分组暂不支持折叠
English=Native desktop icon grouping does not support roll-up yet
NativeModeRollUp=false
```

Move and resize events feed a `DispatcherTimer` debounce of approximately
200 ms, which calls `LayoutReferencesInsideFrame()`. There is no native-grouping
background polling. The layout service reads the current ListView spacing,
uses the actual `75x100` spacing observed on this Desktop as runtime data, and
falls back only when Windows does not report spacing. Header, border, and
padding are excluded from the content rectangle. If capacity is insufficient,
the service skips the layout without partially forcing icons outside the Frame.

The kill switch is exposed as `Framemanager.NativeVisualGroupingEnabled` and
`NativeDesktopVisualGroupingService.NativeVisualGroupingEnabled`. Disabling it
stops active repositioning; the existing snapshot restore path remains
available.

## Snapshot and filesystem safety

The test snapshot is [ui-phase5-2-final-layout-before.json](../audit/ui-phase5-2-final-layout-before.json).
The TEST Frame cleanup restored the snapshot without a restore error. Comparing
the 81 stable, non-test Desktop objects before and after cleanup found zero
coordinate differences. The three disposable test objects were removed after
the run; no production Desktop object was moved, deleted, renamed, or copied.

```text
SnapshotRestoreStillPass=true
RestoreAccuracy=Exact for retained stable items
UnexpectedOtherIconPositionDiffCount=0
AutoArrangeEnabled=false
AlignToGridEnabled=true
ObservedGrid=75x100
ShortcutsMoved=0
ShortcutsDeleted=0
ShortcutsRenamed=0
ShortcutsCopied=0
FoldersMoved=0
FoldersDeleted=0
FoldersRenamed=0
AutoArrangeModified=false
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
```

The system DPI probe reported `96` DPI (`100%`); existing DPI-aware conversion
was retained. This run was restricted to the primary monitor.

## Production preview and stop gate

The refreshed preview is [ui-phase5-final-production-preview.md](../audit/ui-phase5-final-production-preview.md):

```text
ProductionCandidateCount=63
ProductionHighConfidenceCount=37
HighConfidenceShortcutCount=22
HighConfidenceAiDevCount=12
```

No production Frame was created and no project was started. Because the native
single-click, double-click, context-menu, and drag gate was not certified,
Batch 1 was not attempted. Consequently there is no production-before
snapshot, restart-persistence result, production search-dedup result, or
production restore result in this run. See
[ui-phase5-final-batch1-results.md](../audit/ui-phase5-final-batch1-results.md).

## Build and stability

Release Rebuild through Visual Studio 2022 MSBuild succeeded. The project
continues to emit its existing nullable/COM warnings; no warning was added by
the native ownership changes. No NuGet package, external library, or font was
added.

A short idle baseline of the normal Desktop Hub process measured working set
`164.99 -> 165.48 MB`, private memory `89.88 -> 89.98 MB`, GDI handles
`35 -> 35`, and USER handles `44 -> 44`. This is not a five-minute grouped
Batch 1 measurement because Batch 1 was intentionally not run. Explorer kept
the same process and the Desktop remained visible; no Explorer crash or
restart occurred. A historical Desktop Hub crash at 15:50 was the stale
`ApplyNow` callback found during the first cleanup attempt; the callback was
made disposal-safe and no subsequent final-run crash occurred.

## Next step

Keep production at zero. Re-run only the native icon interaction gate with a
Computer Use target that can safely address the Explorer Desktop ListView (or
with an explicitly authorized physical-pointer session). After all four native
actions pass, execute one bounded `AI · 开发` Batch 1 of at most five `.lnk`
items, then perform the requested visual, search, restart, and restore checks.
