# UI Phase 5.1 — Native icon test results

Date: 2026-08-26

## Safety gate

```text
BuildBeforePositionTest=PASS
AutoArrangeEnabled=false
AutoArrangeModified=false
AlignToGridEnabled=true
FilesystemMoveUsed=false
FilesystemDeleteUsedForDesktopItems=false
RegistryModified=false
ExplorerRestartRequested=false
```

The complete pre-mutation Desktop icon snapshot is `audit/ui-phase5-1-native-icon-layout-before.json`. A separate test baseline is `audit/ui-phase5-1-native-icon-test-baseline.json`.

## Enumeration, snapshot, and restore

```text
DesktopIconEnumeration=PASS
PretestIconCount=85
PretestStableIdentityCount=81
PretestAmbiguousCount=2
PretestUnmanagedShellItemCount=2

Snapshot=PASS
RestoreBeforeFirstMove=PASS
RestoredStableItems=81
RestoreFailures=0
PostRestoreCoordinateDifferences=0
```

Restore is path-based and idempotent. It skips missing, ambiguous, and unmanaged entries. It does not touch icons created after the snapshot.

## TEST icon position tests

The test used only these disposable items:

- `NATIVE_ICON_TEST_A.lnk`
- `NATIVE_ICON_TEST_B.lnk`
- `NATIVE_ICON_TEST_FOLDER`
- `NATIVE_ICON_TEST_NEW.lnk` for new/missing-item behavior

```text
TEST_A_Before=(163,2)
TEST_B_Before=(163,102)
TEST_FOLDER_Before=(163,202)

TEST_A_Requested=(1013,502)
TEST_A_Actual=(988,502)
TEST_A_MoveDifferenceCount=1
FilesystemPathUnchanged=true
ExplorerStable=true

SingleRestore=PASS
SingleRestoreDifferenceCount=0

MultipleMove=PASS
MovedItems=3
OtherDesktopIconsChanged=false
MultipleRestore=PASS
MultipleRestoreDifferenceCount=0

RepeatedMoveRestoreCycles=10
RepeatedRestoreFailures=0
RepeatedRestoreDifferenceCount=0
```

The 25-pixel X adjustment is Explorer's Align-to-Grid snapping. Ten repeated apply/restore cycles left all tracked stable positions equal to their baseline.

## Missing and new item behavior

```text
NewItemUnaffectedByOlderSnapshot=true
MissingSnapshotItem=SKIP_MISSING
MissingRestoreFailures=0
```

The explicitly created test items were removed after testing. The Desktop root contained 64 filesystem objects before and after the test. No object was added or removed. `Desktop\ppt` received independent file updates during the run; this task did not move or modify that directory's contents.

## Explorer and process stability

```text
ExplorerPidBefore=33676
ExplorerPidAfter=33676
ExplorerResponding=true
ExplorerCrash=false
ExplorerRestart=false
DesktopIconsDisappear=false
DesktopIconsCorrupted=false
```

## Z-order and hit testing

The native icons are visually visible through the semi-transparent Fluent Frame. At a native test icon's screen-center coordinate, `WindowFromPoint` returned the WPF `HwndWrapper[Desktop Frames;...]`, not Explorer's `SysListView32`.

```text
NativeIconVisible=true
FrameContentHitTestSafe=false
NativeIconClickable=false
NativeIconContextMenu=false
```

`HTTRANSPARENT` cannot safely forward these events to Explorer because that documented behavior applies to underlying windows in the same thread. Making the complete Frame window click-through would also disable its header drag, hover controls, lock, roll-up, and context menu. No click-forwarding, injection, full-frame hit-test disabling, or global icon hiding workaround was introduced.

## Performance sample

```text
WorkingSetMB=164.17
PrivateMemoryMB=76.37
IdleCPUPercentApprox=0
FiveSecondWorkingSetDeltaMB=0
FiveSecondPrivateMemoryDeltaMB=0
BackgroundPolling=false
NoContinuousMemoryGrowth=true
```

## Result

```text
LowLevelReadPosition=PASS
LowLevelSetPosition=PASS
SnapshotRestore=PASS
FilesystemDiff=SAFE
ProductionApply=NOT_RUN
VisualDuplicateResolved=false
Status=PARTIAL
```

Production grouping was deliberately stopped before Batch 1 because native icons cannot yet receive natural mouse interaction through the current single WPF Frame window.
