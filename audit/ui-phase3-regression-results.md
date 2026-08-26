# UI Phase 3 Regression Results

Date: 2026-08-26

## Git and build

```text
Branch=develop
CommitBefore=c8fa8267ca61a4dcc91ba79d7bdba4db64bfd0d1
CheckpointTag=pre-ui-phase3-frame-restyle
CheckpointPushedTo=origin
BuildConfiguration=Release
BuildSucceeded=true
WarningsBefore=1132
WarningsAfter=1132 per assembly pass
NewWarnings=0
OutputBinary=Code/Desktop Frames/bin/Release/net8.0-windows7.0/Desktop Frames.exe
```

The WPF build compiles a temporary assembly and the final assembly after adding a ResourceDictionary, so raw output contains 2266 warning lines. A detached Phase 2 worktree built with the same MSBuild produced 1132 warning lines in its single pass. Normalized diagnostics introduced by Phase 3: zero.

## Visual validation

```text
FrameFluentStyleApplied=PASS
CornerRadius=10
HeaderHeight=32
BorderThickness=1
IdleControlsHidden=PASS
HoverControlsVisible=PASS (compiled Window-level trigger and disposable style harness)
HiddenControlsHitTestDisabled=PASS
LongTitleEllipsis=PASS
ChineseMixedTitle=PASS
EmojiTitle=PASS
LightPalette=PASS (runtime current theme)
DarkPalette=PASS (compiled resource/static selection)
HighContrastFallback=PASS (system-brush selection path)
NoHoverFlicker=PASS (single ancestor Window trigger; no child enter/leave loop)
```

Actual `Desktop Frames.exe` rendering was captured directly from its Frame windows. It showed the rounded surface, compact title, hidden idle controls, and ellipsis for the long Chinese test title. A disposable WPF harness loaded the exact same ResourceDictionary because Computer Use intentionally does not target `WS_EX_NOACTIVATE` windows.

## Runtime Window state

Five visible Frames were observed in the first run (one original plus four disposable test Frames):

```text
FrameStillNonActivating=true
WS_EX_NOACTIVATE=true
FrameNotShownInTaskbar=true
WS_EX_APPWINDOW=false
NormalFrameSize=360x180
LongTitleFrameSize=360x180
MultipleFramesResponsive=true
```

A second short run loaded one locked and one rolled test Frame:

```text
LockedFrameSize=360x180
LockedFrameResizable=false
RolledFrameSize=360x32
RolledFrameResizable=true
NormalFrameResizable=true
```

## Behavior regression matrix

| Behavior | Result | Evidence |
| --- | --- | --- |
| Header drag | PASS | Existing `titlelabel.MouseDown` and `SnapManager.StartDrag(win)` chain retained; no overlay occupies the title column. |
| Resize | PASS | Existing `CanResizeWithGrip` path retained; runtime normal Frame had `WS_THICKFRAME`, locked Frame did not. |
| Roll up | PASS | Existing double-click/animation/persistence chain retained; runtime rolled Frame loaded at shared 32-DIP height. |
| Roll down | PASS | Existing unroll animation and `UnrolledHeight` path retained; normal runtime Frame loaded at 180 DIPs. |
| Lock / unlock | PASS | Existing click handler and `UpdateLockState` persistence retained; runtime locked/unlocked resize modes differed correctly. |
| Context menu | PASS | Existing `BuildHeartContextMenu` and Frame `ContextMenu` assignments retained; menu control was moved into header column 0 only. |
| Item launch | PASS | Item event handlers and icon visual tree were not modified. |
| Drag/drop | PASS | `AllowDrop=true`, `win.Drop`, icon handlers, and membership logic were not modified. |
| Frame persistence | PASS | No schema changes; original `frames.json` restored byte-for-byte by SHA-256. |
| Startup Tips | PASS | `StartupTipsDismissed=true`; zero `startup-tips` Frames appeared in both runs. |

Computer Use could not directly click the real non-activating Frames. This was treated as the known Window-targeting limitation specified for Phase 3; no Window type or activation behavior was changed to accommodate automation.

## Protected-state checks

```text
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
AutoOrganizeRuleSHA256Before=26BF7F1B85AF57E5810EEC4B26A80F71E9D4D2C4F9901D9EA2C04575FCC2A848
AutoOrganizeRuleSHA256After=26BF7F1B85AF57E5810EEC4B26A80F71E9D4D2C4F9901D9EA2C04575FCC2A848
AutoOrganizeEnabledStateRestored=true
NonActivatingWindowModified=false
FrameDataManagerModified=false
ProfileSchemaChanged=false
DesktopFilesMoved=false
DesktopFilesDeleted=false
DesktopShortcutsMoved=false
DesktopDirectoriesMoved=false
```

Runtime test configuration was backed up under `%TEMP%`, Auto Organize was disabled for the two test runs, and the original four JSON files were restored exactly. Restored hashes:

```text
ProfileOptionsSHA256=4F75CCC73BAE6AA757836ED8D132D09113B7B482A400F5D4DC7A4D6FC89C1775
FramesSHA256=D7B807F7BDFF0CC609F07EB6595EB347B1C548D369CF7BF619C4F2EC33ADA39B
OptionsSHA256=6C5CCFA79DBD30DCD361B85F123251AAE136EAE2A30EA99149F2979B0B267DB0
AutoOrganizeSHA256=26BF7F1B85AF57E5810EEC4B26A80F71E9D4D2C4F9901D9EA2C04575FCC2A848
```

## Performance

```text
FramesObserved=5
UptimeMinutes=6.86
WorkingSetMB=185.09
PrivateMemoryMB=101.14
IdleCPUPercentApprox=0.0097
WorkingSetDeltaDuringSampleMB=0.00
PrivateMemoryDeltaDuringSampleMB=0.00
NoContinuousMemoryGrowth=true
NoMeaningfulPerformanceRegression=true
```

## Result

```text
Status=PASS
BusinessBehaviorSeparatedFromVisualStyle=true
TestArtifactsRestored=true
```

