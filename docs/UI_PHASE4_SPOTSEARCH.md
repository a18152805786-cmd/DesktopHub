# UI Phase 4 — SpotSearch

```text
Phase=UI Phase 4
BaselineCommit=32394134a82f485fc96aa18efb8dbbd85c8d5fc8
SearchArchitecture=Provider refresh -> unified SearchResult list -> in-memory ranking/deduplication -> WPF popup
ResultModel=Desktop_Frames.Search.SearchResult
RefreshStrategy=Refresh providers whenever SpotSearch opens; no persistent search watcher
MaximumVisibleResults=20
```

## Architecture

`SearchFormManager` owns only the popup lifecycle, keyboard/mouse interaction, result rendering, and launch actions. `SearchService` refreshes isolated providers, contains candidates in memory, ranks matches, and deduplicates them before the UI receives the first 20 results.

```text
Ctrl + `
  -> GlobalHotkeyManager (unchanged)
  -> SearchFormManager.ToggleSearch()
  -> SearchService.Refresh()
     -> FrameSearchProvider
     -> DesktopRootSearchProvider
     -> ManagedFolderSearchProvider
  -> SearchService.Search(query)
  -> SearchResult rows
  -> Shell launch
```

Provider failures are caught independently and logged through the existing logging path. One unavailable folder therefore does not prevent other providers from returning results.

## Providers and scope

### FrameSearchProvider

Reads the already-loaded Frame data and projects each saved Frame item into a `SearchResult`. It does not modify `frames.json` or Frame membership.

### DesktopRootSearchProvider

Enumerates only direct filesystem children of the Windows Desktop root. It includes visible files, directories, `.lnk`, and `.url` entries. It skips `desktop.ini` and hidden/system entries and never recurses into Desktop folders.

### ManagedFolderSearchProvider

Reads enabled `TargetFolderPath` values from the active Profile's existing `auto_organize.json`. It accepts only paths that are direct children of Desktop, and enumerates only each directory's direct children. It does not call or modify `AutoOrganizeManager`.

Actual production paths validated on 2026-08-26:

```text
DesktopRoot=C:\Users\71966\Desktop
DocumentsFolder=C:\Users\71966\Desktop\文档
PPTFolder=C:\Users\71966\Desktop\ppt
PDFFolder=C:\Users\71966\Desktop\PDF
DataFolder=C:\Users\71966\Desktop\数据表格
ImagesFolder=C:\Users\71966\Desktop\图片
ArchivesFolder=C:\Users\71966\Desktop\压缩包
InstallersFolder=C:\Users\71966\Desktop\安装包
```

The lowercase production `ppt` path is intentionally preserved. Search does not rename production directories.

## Result model

The common model carries only reference and launch metadata:

```text
Id
DisplayName
FullPathOrUri
ResultType
CategoryKey
SourceProvider
IconSource
Subtitle
LaunchTarget
OriginalPath
TargetPath
LaunchArguments
Uri
FrameId
FrameTitle
```

Supported V1 types are `File`, `Folder`, `Shortcut`, `Url`, and `FrameItem`. Search does not copy shortcuts or persist search results.

## Matching, ranking, and deduplication

Matching is culture-aware, case-insensitive string matching over display name, with target/subtitle metadata as secondary searchable text. Direct Unicode Chinese input is supported.

```text
Exact display name=100
Display name starts with query=80
Display name contains query=50
Secondary metadata match=lower priority
```

Canonical Windows paths are normalized case-insensitively, without trailing separators. URIs and shortcut target/argument pairs are also normalized. If a filesystem item also belongs to a Frame, the richer Frame result wins and the duplicate path is not shown twice.

## Popup and interaction

The popup remains an activating WPF window because text input needs keyboard focus. It is centered, 640 DIP wide, at most 540 DIP high, not shown in the taskbar, and uses the Phase 3 theme direction without Acrylic, Mica, WebView, blur, or a third-party UI toolkit.

```text
Up/Down=change selection
Enter=open selected result
Esc=close
Mouse click=open selected result
Open success=close popup
Missing item=localized message and remove stale result
```

Files and URLs use the existing shell association. Folders open in Explorer. `.lnk` launch preserves the existing target and arguments behavior, including Store-app handling.

## Icons

Only currently rendered top results request Shell icons. A bounded in-memory cache holds at most 64 entries for the popup lifetime and is cleared when the popup closes. There is no disk icon cache and no startup-wide icon preload.

## Localization

New text continues to use `.resx + ResourceManager + Strings.cs`. English fallback and `zh-Hans` resources include the placeholder, empty state, keyboard hint, missing-item message, and result category labels.

## Safety and compatibility

```text
SearchIsReadOnly=true
SearchMovesFiles=false
SearchDeletesFiles=false
SearchRenamesFiles=false
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
FramePersistenceChanged=false
GlobalHotkeyChanged=false
NewLongLivedWatcher=false
```

## Performance impact

Production process after two minutes idle:

```text
WorkingSetMB=164.34
PrivateMemoryMB=76.41
IdleCPUPercentApprox=0.0098
```

After another five minutes with Search closed:

```text
FinalWorkingSetMB=164.45
FinalPrivateMemoryMB=76.10
FinalIdleCPUPercentApprox=0.0163
SearchClosedBackgroundScanning=false
NoContinuousMemoryGrowth=true
```

The real provider-backed popup, measured in an isolated WPF runtime host to make the non-taskbar popup observable, used approximately 125.66 MB working set and 65.21 MB private memory. That host value is not additive to the production-process figure and is recorded only as a popup-level runtime check.

## Known limitations

```text
NoWholeDiskSearch=true
NoFullTextSearch=true
NoPinyinSearch=true
NoDeepFolderRecursion=true
NoSemanticSearch=true
NoRecentHistory=true
```

These limits are deliberate V1 scope decisions. They keep refresh bounded and leave idle CPU near zero.
