# Interactive Group Zones

Before this phase a group was a title and a cluster of icons. The boundary
existed only inside the layout plan, so the desktop could not answer the
questions people actually asked of it:

- how far does 游戏 reach?
- is this icon inside the group or just near it?
- how big is the group, and can I make it bigger?
- if I drag something in, will DesktopHub remember?

A group is now a **zone**: a rectangle you can see, measure, move and resize,
and whose contents DesktopHub remembers.

## What a zone is

```text
DesktopGroupZone
├── Header            title chip; drag handle for the whole group
├── Bounds            columns × rows of the Explorer icon grid
└── Native icons      still drawn and owned by Explorer
```

DesktopHub never renders a desktop icon. It positions them and draws a boundary
around them. A zone is layout metadata and nothing more: growing 游戏 from 5×3
to 6×4 changes where icons are placed and creates, moves, renames and deletes
exactly zero files.

## Sizes are stored in grid slots, never pixels

Explorer's icon cell is 75 × 100 on this desktop, but that is a runtime value
that changes with DPI, icon size and resolution. So a zone stores

```text
ManualColumns = 6
ManualRows    = 4
```

and never `Width = 376`. Pixels are recomputed at run time from the live
`IconSpacingX` / `IconSpacingY`, so a stored size stays correct across a DPI or
resolution change.

## Two modes

| | Normal | Edit |
|---|---|---|
| Zone outline | ~10% opacity, brightens to ~40% while the header is hovered | clearly visible |
| Capacity badge (`6 × 4`) | hidden | shown |
| Resize handles | hidden | right, bottom, bottom-right |
| Clicks on desktop icons | pass straight through | go to the zone |

Normal mode keeps the desktop uncluttered, and the zone window carries
`WS_EX_TRANSPARENT`, so it is *physically incapable* of swallowing a click meant
for an icon. Edit mode drops that style to make the handles grabbable; while you
are editing you are arranging rather than launching, so that is the right trade.

Edit mode is toggled from the tray menu or any group header's context menu.

## Moving a group

The header is the handle. While the pointer moves, only the lightweight header
window is repositioned, snapping cell by cell — Explorer is not called. On drop
the whole icon group is repositioned exactly once.

A move is refused, leaving the desktop untouched, unless it lands inside the safe
grid and every destination cell — the reserved header row included — is free of
icons outside the group and outside every other group's zone, that zone's
reserved header row included. A header row holds no icon, so zones are checked
against the plan: the planner refuses a pin whose icons sit in another group's
header row, and the drag must not accept one either. The rejection names the
blocking icon or group.

## Resizing a group

Drag the right edge, the bottom edge or the bottom-right corner. The preview
snaps to whole grid cells, so a zone is never three-and-a-half columns wide, and
the outline turns red the moment the proposed size can no longer hold the group's
own icons. Nothing is written until you release.

```text
Columns × Rows >= managed item count
```

A zone that would drop its own icons is refused, and the toast says how many
slots the group needs.

A size is also refused when it would cost a pinned group its place. The planner
pins groups first, in a fixed order, and silently lets a pin go when the group's
icons would reach into the block of a group pinned before it, header row
included; that group then flows automatically, often into the middle of the
screen. So every proposed size is planned in full, and each group pinned on
screen must still be pinned at the same slot. A pin the next refresh would drop
anyway, after new icons or a drop the planner does not honour, does not count
against the resize. The toast names the group that would have moved.

## What outranks what

```text
ManualLayoutGroupId   >  ReferenceGroupingClassifier
ManualPosition        >  automatic Dual-Rail placement
ManualSize            >  automatic sizing
```

A refresh re-reads all three before it plans, so it can pick up new desktop items
without ever undoing something you did on purpose. A manual size is kept even as
the group grows; only when the item count exceeds the manual capacity do rows
grow, and the manual column count — the part you actually see — is preserved.

`恢复自动位置` and `恢复自动大小` hand each decision back to the planner
independently.

## Dragging an icon into a zone

Drop a desktop icon inside another group's boundary, then refresh. DesktopHub
compares stored coordinates against stored bounds and asks:

```text
将“不心动才有鬼”归类到“游戏”？
```

Confirming writes `ManualLayoutGroupId`; cancelling writes nothing at all and the
icon keeps its existing classification. There is no mouse hook, no polling and no
Explorer injection — the check runs on an explicit refresh.

## Icons that share a display name

Explorer never shows the `.url` or `.lnk` extension, so a game shortcut and a
folder of the same name are two ListView items with one identical label. There is
no way to ask Explorer which item is which.

Failing closed on that is right when the answer depends on *which* file an icon
is. It is needlessly strict for grouping: if every candidate behind a label
belongs in the same group, the ambiguity does not change the answer. So the
candidates are classified, and when exactly one confident group comes out of
them, all of them take it and the k-th ListView item is paired with the k-th
candidate path. The pairing is arbitrary but stable and one-to-one, which is all
that is needed to address each icon exactly once.

Anything less certain stays an untouchable obstacle, exactly as before.

## Performance

Everything here is event-driven: mouse down, mouse move *while a drag is in
progress*, mouse up, an explicit refresh, and startup. There is no timer, no
polling, no animation and no continuous rendering. Zone outlines are static
frozen brushes, so a zone renders once and is then composited without further CPU
work.
