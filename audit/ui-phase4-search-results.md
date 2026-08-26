# UI Phase 4 — SpotSearch Results

Validation date: 2026-08-26

## Build and change boundaries

```text
Branch=develop
CommitBefore=32394134a82f485fc96aa18efb8dbbd85c8d5fc8
CheckpointTag=pre-ui-phase4-spotsearch
CheckpointPushedTo=origin
GlobalHotkeyManagerModified=false
FrameDataManagerModified=false
AutoOrganizeManagerModified=false
FrameStyleResourceModified=false
NewNuGetPackages=0
NewExternalLibraries=0
NewFontFiles=0
```

## Candidate/provider harness

The built Release assembly was loaded against the real Desktop and active production Profile. Providers were refreshed read-only; one existing `Codex++.lnk` path was also represented as a Frame item to exercise cross-provider deduplication.

```text
TotalCandidates=86
FileCandidates=20
FolderCandidates=30
ShortcutCandidates=28
UrlCandidates=7
FrameItemCandidates=1

FrameSearchProvider=PASS
DesktopRootSearchProvider=PASS
ManagedFolderSearchProvider=PASS
FolderSearch=PASS
ShortcutSearch=PASS
UrlSearch=PASS
FrameItemSearch=PASS
```

Validated managed directories:

```text
C:\Users\71966\Desktop\文档
C:\Users\71966\Desktop\ppt
C:\Users\71966\Desktop\PDF
C:\Users\71966\Desktop\数据表格
C:\Users\71966\Desktop\图片
C:\Users\71966\Desktop\压缩包
C:\Users\71966\Desktop\安装包
```

## Matching and ranking

```text
ChineseQuery_论文=2 results
ChineseQuery_数据=4 results
ChineseQuery_图片=3 results
EnglishQuery_Desktop=20 results (capped)
EnglishQuery_Codex=3 results
MixedQuery_AI=11 results
ChineseUnicodeSearch=PASS
EnglishSearch=PASS
MixedSearch=PASS
```

Test ordering for query `SEARCH_TEST`:

```text
1. SEARCH_TEST
2. SEARCH_TEST_ALPHA
3. SEARCH_TEST_MISSING
4. MY_SEARCH_TEST_FILE
```

```text
ExactFirst=PASS
PrefixBeforeContains=PASS
Ranking=PASS
CodexCanonicalResultCount=1
FrameMetadataWinsDuplicate=PASS
DuplicateResultsDetected=false
```

## Real popup interaction

The production `SearchFormManager` was exercised through a disposable WPF host that changed only host-level window chrome so Windows automation could target the popup. Search providers, rendering, query handling, result launch, localization, and lifecycle came from the built production assembly. The disposable host was not retained in the repository.

```text
SearchInputAutoFocused=PASS
ChineseQuery_论文=PASS
EnglishQuery_Codex=PASS
UpNavigation=PASS
DownNavigation=PASS
EnterOpenFolder=PASS
ClickOpenFolder=PASS
EscClose=PASS
SearchRefreshOnOpen=PASS
FriendlyMissingMessage=PASS
MissingResultRemoved=PASS
CrashOnMissingItem=false
```

Runtime Chinese query `论文` returned an existing shortcut and folder. Runtime English query `Codex` returned the expected Codex entries. Creating `SEARCH_TEST_REFRESH.searchtest` while Search was closed and reopening the popup made the item immediately searchable.

The missing-item check deleted only the disposable `SEARCH_TEST_REFRESH.searchtest` after it had been indexed. Pressing Enter then showed the localized `项目不存在或已被移动。` message, left the popup responsive, and removed the stale result.

## Lifecycle and memory

Twenty popup create/close cycles ran in one WPF process:

```text
Cycles=20
OpenWindowsAfterTest=0
ZombieWindows=false
FirstPrivateMemoryMB=58.33
PeakPrivateMemoryMB=93.64
FinalPrivateMemoryMB=88.41
FirstWorkingSetMB=124.00
PeakWorkingSetMB=174.98
FinalWorkingSetMB=170.13
LastFivePrivateMemoryDeltaMB=-2.49
LastFiveWorkingSetDeltaMB=-1.53
ContinuousGrowth=false
```

Production idle samples after startup:

| Elapsed | Working set MB | Private MB | Approx. CPU % |
| ---: | ---: | ---: | ---: |
| 30 s | 165.20 | 78.16 | 0.1253 |
| 60 s | 164.29 | 76.43 | 0.0065 |
| 90 s | 164.34 | 76.44 | 0.0049 |
| 120 s | 164.34 | 76.41 | 0.0098 |

Search-closed production process after another five minutes:

```text
FinalWorkingSetMB=164.45
FinalPrivateMemoryMB=76.10
FinalIdleCPUPercentApprox=0.0163
SearchClosedBackgroundScanning=false
NoContinuousMemoryGrowth=true
```

Provider-backed popup host while open:

```text
SearchOpenWorkingSetMB=125.66
SearchOpenPrivateMemoryMB=65.21
SearchOpenIdleCPUPercentApprox=0.0488
```

## Regression and safety result

```text
HotkeyConfiguration=Ctrl + VK 192
CurrentHotkeyPreserved=true
SearchDoesMoveFiles=false
SearchDoesDeleteFiles=false
SearchDoesRenameFiles=false
FrameBehaviorChanged=false
StartupTipsRecreated=false
ProductionRulesChanged=false
ProviderErrorsFatal=false
SearchClosedNoBackgroundScanning=true
```

The Computer Use keyboard injector did not trigger the application's low-level global keyboard hook, so hotkey preservation is established by the unchanged `GlobalHotkeyManager`, unchanged `Ctrl + VK 192` configuration, and unchanged dispatch path. The same `SearchFormManager` lifecycle was validated directly. This is a test-tool limitation, not a product hotkey change.

## Final Release build

```text
BuildCommand=MSBuild.exe "Code\Desktop Frames\Desktop Frames.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU
BuildSucceeded=true
MSBuildReportedWarnings=2228
ExistingWarningBaselineApprox=1115 unique source diagnostics (WPF temporary and final projects report them twice)
SearchSourceWarnings=0
NewWarnings=0
Errors=0
OutputBinary=C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0\Desktop Frames.exe
OutputBinarySHA256=74A36090E8D52BDE282D94E145CAC086C91D65A7622F50B1B3097824E6DF7F76
```
