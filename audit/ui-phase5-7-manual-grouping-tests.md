# UI Phase 5.7 — Manual Grouping Tests

Two ways to group something by hand, and the rule that neither may be undone by a
refresh.

## Priority

```text
ManualLayoutGroupId  >  ReferenceGroupingClassifier
```

`NativeDesktopLayoutPlanner.ToLayoutItem` consults the manual store before it
classifies anything, so an item with a manual group never reaches the classifier.
`OriginalPath` remains the identity; no ListView index is ever persisted.

Storage: `Profiles/<profile>/native-desktop-layout-metadata.json`

```json
{ "C:\\Users\\...\\Desktop\\太吾公式": { "LayoutGroupId": "GAMES",
  "IsManualOverride": true, "UpdatedUtc": "..." } }
```

## Entry point 1 — drag an icon into a zone

The one the user asks for, because it is what they already tried to do.

```text
Setup    GROUP_TEST_UNKNOWN_FOLDER classified UNASSIGNED, placed in 待整理
Action   dragged into a free cell of the 游戏 zone, then Refresh desktop layout
Detect   coordinates compared against zone bounds; no hook, no polling
Prompt   将“GROUP_TEST_UNKNOWN_FOLDER”归类到“游戏”？
         [x] GROUP_TEST_UNKNOWN_FOLDER → 游戏     [取消] [确定]
Confirm  ManualLayoutGroupId=GAMES written
Refresh  GROUP_TEST group=GAMES manual=True
```

```text
DragIntoZoneDetection=PASS
DragIntoZoneConfirmation=PASS
SilentReclassification=false        nothing is written before the user confirms
CancelWritesNothing=PASS
```

With several icons out of place the same dialog lists them all with a tick box
each, rather than asking once per icon.

## Entry point 2 — the triage list

`整理待分类项目...` on the 待整理 header lists everything unassigned with a group
dropdown per row. Carried over from Phase 5.6 and still working; it is the way to
group something without moving it first.

## Persistence

```text
太吾公式                   -> GAMES   survived refresh and restart (Phase 5.6, still held)
GROUP_TEST_UNKNOWN_FOLDER  -> GAMES   survived a full re-plan
```

## Icons that share a display name

`The Seance of Blake Manor` exists twice on this desktop: a Steam shortcut
(`steam://rungameid/1395520`) and a working folder of the same name. Explorer
never shows the `.url` extension, so both ListView items carry one identical
label and neither could be identified — which is why both sat unmanaged in the
shell column for two phases, and why Explorer kept displacing them.

They are both games, and DesktopHub now agrees. When every candidate behind a
shared label classifies into the same group, the ambiguity cannot change the
answer, so the group is applied and the k-th ListView item is paired with the
k-th candidate path.

```text
The Seance of Blake Manor        -> GAMES  "Every entry sharing the display name
The Seance of Blake Manor.url    -> GAMES   resolves to GAMES"
StableIconCount   81 -> 83
AmbiguousCount     2 -> 0
```

A label whose candidates disagree, or where only one is classifiable at low
confidence, stays exactly as unmanaged as before.

## New desktop items

```text
New folder with an unrecognised name  -> 待整理, and left where it is on disk
New shortcut                          -> classified on the next refresh
Watcher                               -> none; classification happens on refresh
```

## Filesystem

```text
FilesystemMovedByGrouping=false
```

Grouping is metadata. Assigning `太吾公式` to 游戏 moves its icon and writes one
JSON entry; the folder itself never leaves the Desktop.
