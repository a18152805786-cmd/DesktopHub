# UI Phase 5.2 — Plan A hollow region results

Date: 2026-08-26

## Result

```text
PlanATested=true
PlanAResult=PARTIAL
RegionConstruction=PASS
ContentAreaPassThrough=PASS_OS_HIT_TEST
HeaderPointHitsFrame=true
BorderPointHitsFrame=true
NativeInteractionCertification=INCOMPLETE
PlanAFailureReason=NONE_TECHNICAL; GUI target unavailable and active PowerPoint protected
PlanBTested=false
```

Plan A did not technically fail, so Plan B was not started. The remaining gate is real pointer interaction on the Desktop, not region construction.

## Region implementation

The prototype creates one outer rectangular `HRGN`, creates the central content rectangle, and uses `CombineRgn(..., RGN_DIFF)` to subtract it. `SetWindowRgn` applies the result to the existing single Frame HWND.

Ownership is explicit:

- Temporary content regions are deleted by the builder.
- On successful `SetWindowRgn`, Windows owns the final region and the application does not delete it.
- On failure before ownership transfer, the application deletes the region.
- No second HWND, input forwarding, Explorer hook, injection, or synthetic mouse input is used.

## `WindowFromPoint` and `WM_NCHITTEST`

At 450 x 320 device pixels and 100% DPI:

| Point | Native target | `WM_NCHITTEST` |
| --- | --- | --- |
| Header | test WPF `HwndWrapper` | `HTCLIENT` (1) |
| Content center | Explorer `SysListView32` | `HTCLIENT` (1) |
| Left rim | test WPF `HwndWrapper` | `HTLEFT` (10) |
| Right rim | test WPF `HwndWrapper` | `HTRIGHT` (11) |
| Bottom rim | test WPF `HwndWrapper` | `HTBOTTOM` (15) |
| Bottom-left | test WPF `HwndWrapper` | `HTBOTTOMLEFT` (16) |
| Bottom-right | test WPF `HwndWrapper` | `HTBOTTOMRIGHT` (17) |

This proves that the central area is absent from the Frame HWND region and that the retained chrome has correct native resize semantics. When PowerPoint later covered the same coordinates, the hole correctly exposed PowerPoint rather than the Frame. That confirms genuine pass-through, but also means final integration must preserve the production Frame's Desktop z-order behavior.

## Region update and lock tests

```text
MoveGeometryUpdated=true
ResizeGeometryUpdated=true
ContentHolePreservedAfterResize=true
LockRemovedResizeStyle=true
UnlockRestoredResizeStyle=true
ActualPointerHeaderDrag=NOT_CERTIFIED
ActualPointerResize=NOT_CERTIFIED
```

Actual pointer tests were not simulated. On 2026-08-26 the Computer Use surface could not enumerate the Explorer Desktop or the no-activate test Frame, and PowerPoint became active during final validation; no focus was taken from it. On 2026-08-27 PowerPoint was no longer running, but a fresh Computer Use enumeration still returned only ChatGPT, WorkBuddy, and Claude—not Explorer Desktop or the test Frame. The controller therefore had no valid returned target window for safe input.

## GDI and lifecycle stability

```text
RegionUpdates=100
GdiHandlesBefore=20
GdiHandlesAfter=20
GdiDelta=0
UserHandlesBefore=26
UserHandlesAfter=26
UserDelta=0
GdiLeak=false

SurfaceLifecycleCycles=10
ExplorerPidBefore=33676
ExplorerPidAfter=33676
ExplorerCrash=false
ExplorerRestart=false
```

The ten lifecycle cycles created a test Frame, applied the region 100 times, and destroyed the Frame. Every cycle reported zero GDI and USER handle delta.

## Native TEST icon and filesystem safety

Only these disposable Desktop objects were used:

- `NATIVE_SURFACE_TEST_A.lnk`
- `NATIVE_SURFACE_TEST_B.lnk`

The native layout service placed both icon coordinates inside the test content rectangle. Their center points resolved to Explorer `SysListView32`, not the Frame. The Phase 5.1 snapshot service then restored both exactly:

```text
TEST_A_Before=(163,2)
TEST_A_AfterRestore=(163,2)
TEST_B_Before=(163,102)
TEST_B_AfterRestore=(163,102)
RestoreAccuracy=Exact
RestoreFailures=0
```

After exact test cleanup, the Desktop filesystem inventory remained 64 objects with no additions, removals, or metadata changes.

```text
FilesystemMoved=false
FilesystemDeleted=false
FilesystemCopied=false
OtherDesktopIconsChanged=false
SnapshotRestoreStillPass=true
```

## Interaction certification boundary

```text
NativeIconVisible=PASS
NativeIconSingleClick=NOT_CERTIFIED
NativeIconDoubleClick=NOT_CERTIFIED
NativeIconContextMenu=NOT_CERTIFIED
NativeIconDrag=NOT_CERTIFIED
```

The OS hit-test route is correct, but the four user-input operations were not claimed as PASS without real pointer evidence. Computer Use rules prohibit reconstructing a missing target window or using stale/guessed handles. No `SendInput`, message proxy, click forwarding, Explorer injection, or real application launch was used.

## Performance

```text
PrototypeWorkingSetMB=109.21
PrototypePrivateMemoryMB=52.01
PrototypeIdleCPUPercentApprox=0
FiveSecondWorkingSetDeltaMB=0
FiveSecondPrivateMemoryDeltaMB=0
BackgroundPolling=false
NoContinuousMemoryGrowth=true
```

## Production boundary

```text
ProductionProfileModified=false
ProductionReferencesApplied=0
FrameRenderedDuplicate=true
VisualDuplicateResolved=false
VisualOwnershipModeNotEnabled=true
```

The test-only flag remains disabled for all production Frames. No renderer was suppressed and no high-confidence reference group was applied.
