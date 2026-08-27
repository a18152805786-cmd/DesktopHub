# UI Phase 5.6 — Known Issues Fix, Visual Polish, Interaction Cleanup

Scope: fix the problems found in real use of the Phase 5.5 build. No core architecture
was redesigned: Auto Organize, SpotSearch, native Desktop enumeration, snapshot/restore
and Explorer icon positioning are untouched apart from the corrections listed here.

## Layout geometry after the change

```text
DesktopWorkArea=2560x1440
IconSpacingX=75
IconSpacingY=100
SafeColumns=33
SafeRows=14

LeftRailX=75            (column 1)
RightRailX=1950         (column 26)
LeftRailAligned=true
RightRailAligned=true
GroupSpacingConsistent=true
HeaderToFirstIconGap=14px
GroupSeparationGap=60px
LeftRailSpareRows=1
RightRailSpareRows=1

CentralClearZone=450,0 1500x1400
CentralClearZonePreserved=true
DuplicateTargetCoordinates=0
TargetOutsideWorkArea=0
```

| Group | Rail | Column | Icon row | Shape | Items | Header drawn |
|---|---|---:|---:|---|---:|---|
| 系统 · 临时 | Left | 1 | 1 | 2x1 | 2 | no |
| 科研 · 学习 | Left | 1 | 3 | 4x3 | 12 | yes |
| 办公 · 文档 | Left | 1 | 7 | 4x2 | 8 | yes |
| 待整理 | Left | 1 | 10 | 5x3 | 16 | yes |
| AI · 开发 | Right | 26 | 1 | 5x3 | 12 | yes |
| 日常工具 | Right | 26 | 5 | 5x4 | 17 | yes |
| 游戏 | Right | 26 | 10 | 6x3 | 18 | yes |

Every group in a rail now shares one X baseline by construction. The rail baseline is the
outermost grid column whose whole band is free of icons DesktopHub is not allowed to move;
groups may only be pushed **down** from there, never sideways, so they cannot drift apart.

## Header visual

Before: an opaque dark bar the full width of the icon block, with centred white text.
After: a left-aligned text chip (12% black wash, 5px radius, 1px hairline border) followed
by a 1px rule that fades out to the right.

```text
Height=26 DIP
FontSize=13
FontWeight=SemiBold
TextAlignment=Left
Background=transparent, except a light neutral wash behind the label
Border=1px subtle
CornerRadius=5
Animation=none
Acrylic/Mica=none
ContinuousRendering=false
```

## Coordinate validation

The old check accepted any read-back within one whole grid cell
(`|dx| <= IconSpacingX && |dy| <= IconSpacingY`), which passes an icon that has actually
landed in the neighbouring slot. It now compares **logical grid slots**: the requested and
the actual coordinate must resolve to the same slot of the Explorer grid.

```text
ExactMatches=0
SameGridSlotMatches=81
ExplorerClampFailures=0
```

Every managed icon read back at exactly `requested + (13, 2)` — a single constant Explorer
grid-origin offset, not per-icon drift. `ExactMatches=0` is the honest number: Explorer's
grid origin is not at (0,0), so a pixel-exact match is not achievable, while every icon is
provably in the slot that was requested.

## Group header drag

- `WS_EX_TRANSPARENT` removed from the header window; `WS_EX_NOACTIVATE` and
  `WS_EX_TOOLWINDOW` are kept, so the header is grabbable but never steals focus.
- Drag preview snaps grid cell by grid cell and moves **only the header window**.
  Explorer is not called while the pointer moves.
- On drop the whole native icon group is repositioned once, then the anchor is stored.
- Validation before any icon moves: inside the safe grid, no collision with another group,
  a shell icon or a loose icon, and the reserved header row must be clear too.
- Validation after: no icon outside the group changed position, every member landed in its
  intended logical slot, and the set of Desktop entries is unchanged. Any failure rolls the
  whole move back.
- Moves are ordered in the direction of travel so a destination cell is always vacated
  before it is written to. Writing into a cell Explorer still considers occupied makes it
  shove the occupant somewhere arbitrary; that is now impossible.

## Idle CPU

See `ui-phase5-6-idle-cpu.md`.

## Verification on the real desktop

```text
HeaderVisualPolish=PASS          heavy bar removed, left-aligned chip + hairline rule
LeftRailAlignment=PASS           all left groups at x=75
RightRailAlignment=PASS          all right groups at x=1950
GroupSpacing=PASS                14px header gap, 60px group gap, everywhere

GroupHeaderDraggable=PASS        游戏 dragged one row down: 17 icons moved together,
                                 internal arrangement preserved, no other icon moved
GroupDragPreview=PASS            header snaps cell by cell, Explorer untouched until drop
GroupDragPersistence=PASS        anchor Column=25 Row=11 survived a restart AND a full
                                 re-plan, which placed the group at the manual position
                                 and flowed the automatic groups around it
GroupLock=PASS                   锁定分组位置 present and enabled on an anchored group
ResetAutoPosition=PASS           恢复自动位置 cleared the anchor and returned 游戏 to its
                                 planned Dual-Rail position
GroupDragRejection=PASS          a drag onto an occupied cell was refused, the header
                                 snapped back and the desktop was untouched

ManualClassification=PASS        整理待分类项目 listed all 待整理 items with a group dropdown
ManualOverridePersistence=PASS   太吾公式 → 游戏 written as ManualLayoutGroupId, still GAMES
                                 after refresh and after restart

LogicalGridValidation=PASS       ExactMatches=0, SameGridSlotMatches=81, ClampFailures=0
```

## Regression

```text
SpotSearchRegression=PASS   (Codex / 论文 / PDF / Chrome / Steam, output byte-identical to Phase 5.5)
AutoOrganizeManagerModified=false
AutoOrganizeFormModified=false
ProductionRulesChanged=false
FilesMoved=0
FilesDeleted=0
FilesRenamed=0
FilesCopied=0
FoldersMoved=0
FoldersDeleted=0
FoldersRenamed=0
ExplorerCrash=false
ExplorerRestart=false
DesktopIconsDisappear=false
```

## Two defects found and fixed during real-machine testing

1. **The application exited when the headers were rebuilt.** WPF's default
   `ShutdownMode` is `OnLastWindowClose`. Rebuilding the headers closes them all for an
   instant, and with no Frame open those were the only windows the process owned, so a
   successful group drag terminated DesktopHub. `ShutdownMode` is now
   `OnExplicitShutdown`, which is what a tray-resident application should always have used,
   and the rebuild is deferred until the mouse event has finished unwinding.

2. **A group move could displace an unrelated icon.** Members were repositioned in
   enumeration order, so an icon could be written into a cell another member had not yet
   left; Explorer then relocated the occupant to an arbitrary free cell. Moves are now
   ordered so destinations are always vacated first, and the result is verified and rolled
   back if anything outside the group moved.

A third, smaller one: the interactive path used the strict filesystem fingerprint, which
also hashes every entry's size and last-write time. An unrelated background write inside a
Desktop folder made it differ and silently rolled back a move the user had asked for. The
drag path now compares the set of entry names and kinds, which is precisely the
"nothing was moved, deleted, renamed or copied" property.
