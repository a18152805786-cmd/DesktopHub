# UI Phase 5.2 final GUI certification

Date: 2026-08-27
Status: `PARTIAL`

## Gate output

```text
Task=DesktopHubPhase5_2FinalNativeOwnership
RealGUIValidationAuthorized=true
PlanARealGuiCertification=PARTIAL

NativeIconSingleClick=NOT_CERTIFIED
NativeIconDoubleClick=NOT_CERTIFIED
NativeIconContextMenu=NOT_CERTIFIED
NativeIconDrag=NOT_CERTIFIED

HeaderDrag=PASS
Resize=PASS
Lock=PASS
Unlock=PASS
ContentAreaPassThrough=true

VisualOwnershipModeImplemented=true
FrameRenderedDuplicate=false (TEST native item)
VisualDuplicateResolved=TEST_PASS;PRODUCTION_NOT_RUN
NativeModeRollUp=DISABLED
FrameMoveRelayout=PASS
FrameResizeRelayout=PASS
RelayoutDebounceMs=200

CurrentDpi=96
DpiConversionUsed=true
PrimaryMonitorOnly=true
SnapshotRestoreStillPass=true
```

## Observations

The test Frame used one disposable `.lnk` whose target was the disposable test
folder. At the Frame content center, `WindowFromPoint` returned Explorer's
desktop `SysListView32`; header and border points returned the Frame HWND.
The Frame moved from `(140,120)` to `(200,128)`. Real rim drags changed right,
bottom, bottom-right, left, and bottom-left geometry. Locking changed the
accessible state to `分组已锁定（点击解锁）`, set `NoResize`, and left the
geometry unchanged when a drag was attempted; unlock restored the unlocked
state and grip resizing.

The Computer Use controller did not expose the Explorer Desktop ListView as a
valid target. It rejected hollow-region coordinates as belonging to
`explorer.exe` `FolderView`, so native click/double-click/context-menu/drag
actions cannot be certified from this session. One screenshot showed a
selection highlight, but the wrapper reported an unknown outcome and there was
no observable Explorer launch or native menu. This is intentionally recorded
as `NOT_CERTIFIED`, not promoted to `PASS`.

## Safety evidence

```text
SyntheticInputUsed=false
ClickForwardingUsed=false
MessageProxyUsed=false
ExplorerInjectionUsed=false
FilesystemChanged=false
AutoArrangeEnabled=false
AutoArrangeModified=false
AlignToGridEnabled=true
ObservedGrid=75x100
OtherStableIconCoordinateDiffs=0
ExplorerCrash=false
ExplorerRestart=false
```

The test snapshot was restored and the disposable TEST Frame and objects were
cleaned up. No production apply was run.
