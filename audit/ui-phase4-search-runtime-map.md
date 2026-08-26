# UI Phase 4 — SpotSearch Runtime Map

Audit baseline: `32394134a82f485fc96aa18efb8dbbd85c8d5fc8`

```text
SearchWindowType=Desktop_Frames.SearchFormManager : System.Windows.Window
SearchLifecycle=Singleton-like static instance; Ctrl+` toggles a new window or closes the loaded instance
SearchActivationBehavior=Activating, topmost, focusable WPF popup; ShowInTaskbar=false; closes on deactivation
CurrentDataLoadPath=Framemanager.GetFrameData() -> Data frames/tabs -> JArray Items
CurrentResultModel=Nested SearchFormManager.SearchResult(Name, Path, IsFolder, frameId, frameTitle)
CurrentLaunchMethod=ProcessStartInfo(UseShellExecute=true), with Explorer for folders and Store-app shortcuts
CurrentKeyboardNavigation=Esc closes; Enter opens the first substring match; no Up/Down selection
CurrentCloseBehavior=Toggle, deactivation, successful launch, or Esc calls SafeClose(); OnClosed clears the static instance
```

## Existing execution path

```text
GlobalHotkeyManager low-level keyboard hook
  -> configured SpotSearch modifier/key (default Ctrl + VK 192)
  -> Application dispatcher
  -> SearchFormManager.ToggleSearch()
  -> SearchFormManager constructor
  -> LoadAllShortcuts()
  -> Framemanager.GetFrameData()
  -> Name.ToLower().Contains(query)
  -> dynamically-created icon tiles
  -> LaunchResult()
  -> Shell execution
```

## Constraints confirmed before implementation

- `GlobalHotkeyManager` does not need to change; the current `Ctrl + \`` route can be preserved.
- `FrameDataManager` exposes the already-loaded frame list through `Framemanager.GetFrameData()`; search can remain read-only.
- Shortcut target and argument parsing already exist in `Utility.GetShortcutTarget()` and `Utility.GetShortcutArguments()`.
- Shell icons already come from `Utility.GetShellIcon()`.
- Current Profile paths are available through `ProfileManager.GetProfileFilePath()`.
- Production managed folder paths are stored in the active Profile's `auto_organize.json` as `TargetFolderPath` values. Search can read those paths without modifying `AutoOrganizeManager` or the rules.
- The production PPT directory is actually `Desktop\ppt`; search must preserve that casing/path and must not rename it.
- No background search watcher is needed. Refreshing providers whenever SpotSearch opens is sufficient for the bounded scope.

## Identified Phase 4 gaps

- Only saved Frame items are searchable.
- Results are modeled as shortcuts even when they represent folders or files.
- Matching is only a direct substring over the display name.
- There is no ranking, canonical-path deduplication, provider fault isolation, or missing-item removal.
- Enter ignores visual ordering and always launches the first raw substring match.
- There is no Up/Down selection.
- The tile layout does not present category or friendly path context.
- Shell icons are extracted for every matched tile without a bounded per-popup cache.
