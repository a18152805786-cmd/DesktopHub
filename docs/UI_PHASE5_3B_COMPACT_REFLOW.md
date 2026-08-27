# UI Phase 5.3b - Compact Visual Reflow

## Scope

Phase 5.3b keeps the Phase 5.3 classifier, stable identity, native coordinate,
snapshot, and restore layers. It changes only group packing: the production
layout mode is CompactGrouped, while unassigned items remain PRESERVE.

No Frame is required for grouping. No Desktop filesystem object is moved,
renamed, copied, or deleted, and Auto Arrange / Align to Grid are never changed.

## Compact planning

Each semantic group has a ratio-based, grid-aligned home region and stable
anchor:

| Group | Anchor | Preferred shape | Reserved area |
|---|---|---|---|
| Research / Study | TopLeft | 4 x 3 | upper-left clean zone |
| AI / Development | TopRight | 4 x 4 | upper-right clean zone |
| Office / Documents | MiddleLeft | 4 x 2 | left-center clean zone |
| Daily Tools | MiddleRight | 4 x 4 | reserved, not applied in 5.3b |
| Games | BottomLeft | 5 x 3 | reserved, not applied in 5.3b |

The planner scores compact candidate dimensions, then searches for one wholly
vacant rectangle nearest the stable anchor. It moves the whole rectangle when
obstacles are present instead of fragmenting the group. Unassigned, ambiguous,
shell namespace, and all other groups remain occupied obstacles.

Targets use deterministic semantic priority, normalized display name, and
canonical path ordering. Apply first stages one batch in vacant grid slots so
targets may safely reuse slots previously occupied by another item in that same
batch. Any staging, apply, filesystem, target, or unmanaged-icon verification
failure restores the immediate batch snapshot.

## Manual commands

~~~text
--native-layout-audit=<dir> --native-layout-compact-preview
--native-layout-audit=<dir> --native-layout-compact-refresh
--native-layout-audit=<dir> --native-layout-compact-restore
--native-layout-audit=<dir> --native-layout-search-smoke
--native-layout-audit=<dir> --native-layout-clean-empty-default-frame --native-layout-frames=<path>
~~~

Compact preview creates ui-phase5-3b-before.json once and never overwrites it.
It also writes the JSON plan, Markdown preview, unassigned simulation, and SVG
layout map. Compact refresh applies only Office, AI/Development, and Research.

## Empty default Frame

Cleanup accepts exactly one built-in default title, ItemsType=Data, empty Items
and Tabs, and no SystemFrameId. It backs up frames.json before removal. A valid
empty profile with globally dismissed Startup Tips is respected on startup,
preventing the obsolete empty default Frame from being recreated. All other
Frames and onboarding state are unchanged.
