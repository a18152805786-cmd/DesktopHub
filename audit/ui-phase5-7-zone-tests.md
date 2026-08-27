# UI Phase 5.7 — Zone Interaction Tests

Every result below was produced on the live desktop with synthetic mouse input
driving the real windows, not by calling the services directly.

## Geometry the tests ran against

```text
DesktopWorkArea=2560x1440
IconSpacingX=75   IconSpacingY=100
SafeColumns=33    SafeRows=14
StableIconCount=83
AmbiguousCount=0
```

## Zone visibility

```text
ZoneBoundsVisibleInNormalMode=true    ~10% outline, no capacity badge, click-through
ZoneBoundsVisibleOnHeaderHover=true   outline brightens to ~40% for that group only
ZoneBoundsVisibleInEditMode=true      outline plus a "columns × rows" badge on every zone
ZoneClickThroughInNormalMode=true     WS_EX_TRANSPARENT set; desktop icons unaffected
```

Edit mode showed all seven zones simultaneously labelled
`2 × 1`, `4 × 3`, `4 × 2`, `5 × 3`, `5 × 3`, `5 × 4`, `6 × 4`.

## Group move

```text
Test        drag 游戏 header 2 grid columns left
Result      Group GAMES moved by (-2,0); icons=21; filesystemUnchanged=True
HeaderMoved=true
AllGroupIconsMoved=true            21 of 21
InternalRelativeSlotsPreserved=true
ManualPositionSaved=true           Column=24 Row=10 IsManualPosition=true
```

Persistence after a restart **and** a full re-plan:

```text
4 (GAMES) col=24 row=10 6x4 manualPos=True
1 (AI_DEV)      col=20 ... manualPos=False   flowed around the pinned group
3 (DAILY_TOOLS) col=20 ... manualPos=False
```

Rejection path, verified separately:

```text
Test    drag 游戏 up one row, which puts its reserved header row inside 日常工具
Result  Group drag rejected: HEADER_ROW_COLLISION / GROUP_COLLISION at slot (c,r)
        held by '<icon name>'; header snapped back; desktop untouched
```

## Resize

```text
Test         drag 待整理 bottom-right corner one column right  (5x3 -> 6x3)
Result       Zone UNASSIGNED resized to 6x3
ResizeAccepted=true
ResizeGridSnapping=true            preview snapped whole cells only
ManualSizeSaved=true               ManualColumns=6 ManualRows=3 IsManualSize=true
IconsReflowed=true                 12 items relaid into 6 columns, order preserved
```

Persistence after a restart and a fresh re-plan:

```text
6 (UNASSIGNED) 6x3 manualSize=True     every other group kept its automatic shape
```

Minimum capacity:

```text
Test     drag 游戏 corner to 3x3 while the group holds 20 icons
Result   Zone resize rejected for GAMES: CAPACITY_TOO_SMALL (requested 3x3, needs 20 slots)
ResizeRejected=true
OutlineTurnedRedDuringDrag=true
SizeUnchanged=true
```

## Reset

```text
恢复自动大小 on 待整理   -> zone entry removed, group back to the planner's 5x3
恢复自动位置 available   -> enabled only while a manual position exists
```

The context menu correctly greys out each reset until the matching manual value
exists, so the menu states what has been overridden.

## Drag an icon into a zone

```text
Test    drag GROUP_TEST_UNKNOWN_FOLDER from 待整理 into a free cell of 游戏,
        then Refresh desktop layout
Prompt  将“GROUP_TEST_UNKNOWN_FOLDER”归类到“游戏”？   [x] ... -> 游戏
Confirm ManualLayoutGroupId=GAMES persisted
Replan  GROUP_TEST group=GAMES manual=True
```

Cancelling writes nothing; the icon keeps its previous classification.

## New desktop items

```text
Created  GROUP_TEST_UNKNOWN_FOLDER (empty folder, name matches no rule)
Refresh  classified UNASSIGNED, reason "No high-confidence rule matched"
         placed in 待整理
FolderPhysicallyMoved=false      still at C:\Users\71966\Desktop after the refresh
```

## Filesystem

```text
FilesMovedByGrouping=0
FilesDeletedByGrouping=0
FilesRenamedByGrouping=0
FilesCopiedByGrouping=0
FoldersMovedByGrouping=0
FoldersRenamedByGrouping=0
```

Verified by comparing the Desktop entry set (name + kind + size) before and after
every drag and resize in this phase.

Test artefacts created and then removed by this phase, recorded separately as
required:

```text
created  GROUP_TEST_UNKNOWN_FOLDER   removed after the test
removed  its ManualLayoutGroupId record
```

No user file was created, moved, renamed or deleted.

## Coordinate validation

```text
ExactMatches=0
SameGridSlotMatches=83
ExplorerClampFailures=0
```

## Two defects found and fixed during these tests

1. **A group blocked its own members.** Group membership and obstacle-hood were
   decided by identity *status* (`IdentityStatus == StablePath`) rather than by
   whether the plan actually places the icon. Once icons sharing a display name
   became placeable, they still failed that status test, so the two game icons
   counted as obstacles against the very group they belong to: the drag was
   refused with `GROUP_COLLISION at slot (27,13)`, and a stored manual position
   was silently discarded on the next re-plan. Both now ask the real question —
   "will this plan place this icon" — instead of a proxy for it.

2. **A rejected move said nothing useful.** `GROUP_COLLISION` alone gave neither
   the user nor the log anything to act on. The rejection now names the slot and
   the icon holding it, which is what made the first defect findable at all.

## Performance

Ten minutes idle with all 7 zone outlines and 6 headers live, logging off, on a
32-core machine.

```text
WallSeconds=600
CpuSeconds=2.453
IdleCPUPercentApprox=0.409% of one core   (0.0128% of total capacity)
WorkingSetMB=138.0 -> 150.1
PrivateMemoryMB=74.6 -> 78.0
HandleCount=631 -> 757
```

For comparison, Phase 5.6 measured 0.328% with headers only and 0.477% with the
headers suppressed entirely. This phase's 0.409% sits between those two numbers,
so adding seven zone windows did not move idle CPU outside the run-to-run spread
already established on this machine.

```text
BackgroundPolling=false
ContinuousRendering=false
DispatcherTimerInZoneOrHeaderPath=none
AnimationInZoneOrHeaderPath=none
CompositionTarget.Rendering=none
```

The zone windows cost roughly 8 MB of private bytes between them. Private bytes
grew 3.4 MB over the ten minutes and then flattened, comparable to the 2.9 MB
Phase 5.6 measured without any zones, so the growth is not zone-related.

```text
NoContinuousMemoryGrowth=true (within the same band as the previous phase)
```
