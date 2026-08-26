# UI Phase 5 — Reference-only visual grouping

## Scope

Phase 5 adds an additive reference item format for Desktop `.lnk`, `.url`, and root folders. `OriginalPath` remains the source of truth. Creating or removing membership does not move, copy, rename, or delete the referenced object.

```text
ReferenceAutoGroupingEnabled=false
DesktopScan=top-level only
BackgroundWatcher=false
AutomaticApply=blocked until per-icon visual ownership exists
```

## Existing upstream flow

The upstream Data Frame drop path imports an external object into the profile `Shortcuts` directory:

```text
Desktop item
→ external drop
→ copy .lnk/.url, or create a new .lnk for a file/folder
→ Profile\Shortcuts
→ Frame item Filename points to imported shortcut
```

That is not reference-only. The original normally survives because `DeleteOriginalShortcutsOnDrop=false`, but the Frame launches the copied/generated shortcut.

## Reference model

New items use `ItemKind=Reference` and store readable additive metadata in `frames.json`:

```json
{
  "ItemKind": "Reference",
  "ReferenceType": "Shortcut | Url | Folder",
  "OriginalPath": "C:\\Users\\...\\Desktop\\item.lnk",
  "DisplayName": "item",
  "TargetPath": "...",
  "Arguments": "...",
  "WorkingDirectory": "...",
  "IconLocation": "...",
  "Uri": "...",
  "FrameId": "...",
  "ManualFrameId": "..."
}
```

Compatibility aliases such as `Filename`, `IsFolder`, and `DisplayOrder` remain present so the existing icon renderer can load the item. Old items without `ItemKind` continue through the old path and are not migrated.

## Add, open, remove, and missing items

- Dropping an existing Desktop `.lnk`, `.url`, or folder into a Data Frame creates only JSON membership.
- `.lnk` and `.url` are opened through the original path with Shell execution, preserving the shortcut's own arguments, working directory, Steam URI, and Store-app behavior.
- Folders open in Explorer.
- `从分组中移除` removes only the JSON membership.
- The context menu does not expose Profile-copy or send-to-Desktop actions for references.
- A missing `OriginalPath` produces the localized “项目已移动或不存在” message instead of launching or deleting membership.

## Manual override

Moving a reference between Frames records both `ManualFrameId` and `FrameId`. A manual assignment is authoritative and automatic classification returns `SKIP_MANUAL`; later refreshes do not overwrite it.

## Classification

Inputs are evaluated in this order:

1. `ManualFrameId`
2. `DisplayName`
3. target filename (not parent directories)
4. `Arguments`
5. `Uri`
6. `UNASSIGNED`

Groups are `AI_DEV`, `RESEARCH_STUDY`, `WORK_TOOLS`, `GAMES`, `DAILY_TOOLS`, and `UNASSIGNED`. Only `HIGH` is eligible for assignment. `MEDIUM`, `LOW`, and `UNASSIGNED` remain untouched. Research terms precede general AI-tool terms for mixed names such as `科研相关Skills-KimiWork`.

The classifier and preview are on-demand. Phase 5 installs no new `FileSystemWatcher` and performs no background scanning.

## Search compatibility

`FrameSearchProvider` resolves a reference from `OriginalPath`, carries target/arguments/URI metadata into `SearchResult`, and leaves canonical Phase 4 de-duplication intact. The harness confirmed one result when `Codex++.lnk` was visible to both Desktop and Frame providers.

## Visual ownership limitation

DesktopFramesPlus renders Frame items as WPF icons. Its Desktop integration currently exposes only whole-list show/hide for `SysListView32`; it has no per-icon coordinate/ownership API. Therefore a reference icon in a Frame and the original native Desktop icon would both remain visible.

Phase 5 does not hide all Desktop icons and does not move the shortcut into `Profile\Shortcuts` to mask the duplicate. Production apply is guarded by `visualOwnershipConfirmed`; it fails closed until a safe per-icon ownership design is approved. This is why the current result is core implementation PASS but overall `PARTIAL`.

## Compatibility and safety

- `AutoOrganizeManager.cs` is unchanged.
- Production `auto_organize.json` is unchanged.
- Frame Fluent resources are unchanged.
- No database, watcher, package, font, or external library was added.
- Reference auto grouping defaults to disabled.
- Existing Frame and profile schemas remain load-compatible.

## Next decision

Before production grouping, choose and validate a visual ownership mechanism that does not move files and does not hide every Desktop icon. Do not enable automatic apply until that design is approved.
