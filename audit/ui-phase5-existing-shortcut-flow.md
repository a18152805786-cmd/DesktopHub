# UI Phase 5 — Existing Shortcut Flow

Audit baseline: `bcc3c64bdbe61c02df91f8d01df481141bb42799`

```text
ExternalShortcutCopied=true
CopyDestination=working-directory Shortcuts folder (normally the active Profile Shortcuts directory at runtime)
OriginalShortcutPreserved=true by default
OptionalOriginalDeletion=SettingsManager.DeleteOriginalShortcutsOnDrop; default false

FrameMembershipStorage=active Profile frames.json, under Data frame Items or the active tab Items
FrameItemSchema=Filename, IsFolder, IsLink, IsNetwork, DisplayName, AlwaysRunAsAdmin, DisplayOrder (plus optional runtime/custom fields)

FolderDropBehavior=creates a new .lnk in Shortcuts and stores that generated shortcut as Filename
UrlDropBehavior=copies the original .url byte-for-byte into Shortcuts and stores the copy as Filename

LaunchUsesCopiedShortcut=true for existing external-drop items
LaunchCanUseOriginalPath=true; existing renderer and launcher already accept absolute file/folder paths, but the old drop pipeline does not persist them as reference metadata
```

## Existing execution chain

```text
Desktop shortcut/folder/.url
  -> Framemanager window FileDrop handler
  -> validate source path
  -> ensure Shortcuts directory
  -> .lnk/.url: File.Copy into Shortcuts
     raw file/folder: IWshShortcut.CreateShortcut in Shortcuts
  -> create item with Filename=<copied/generated shortcut>
  -> append to frame.Items or active tab.Items
  -> FrameDataManager.SaveFrameData()
  -> AddIcon + ClickEventAdder
  -> optional DeleteOriginalShortcutsOnDrop branch
```

The original is preserved only because `DeleteOriginalShortcutsOnDrop` defaults to false. The model itself still points at the copied/generated Profile shortcut, so this is not reference-only membership.

## Existing removal behavior

The ordinary icon context-menu `Remove` handler removes the matching item from the current `JArray`, saves `frames.json`, and removes the WPF child. It does not delete `Filename`. However, the label is generic and other commands such as Edit, Copy Item, Send to Desktop, and exported shortcuts were designed around Profile-owned shortcut files, so reference items need a restricted context-menu surface.

## Desktop visual architecture

```text
FrameIconRendering=independent WPF Image/TextBlock controls inside NonActivatingWindow
WindowsDesktopIconMembership=false
DesktopIconPositioningAPI=false
PerDesktopIconHideAPI=false
DesktopIconManagerCapability=show/hide the complete SysListView32 only
```

DesktopFramesPlus does not position individual Windows Desktop icons into Frame bounds. A reference item rendered by WPF while native Desktop icons remain visible therefore produces two visual representations of the same filesystem object. The only existing suppression mechanism hides every Desktop icon through `ShowWindow(SysListView32)`, which is global rather than per-item and is not suitable for automatic Phase 5 activation.

Consequently, Phase 5 may safely add the reference model, persistence, drop path, search support, classification Preview, and explicit apply planning, but must not auto-apply real Desktop grouping until a user-approved visual ownership strategy exists.
