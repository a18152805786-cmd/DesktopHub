# SpotSearch visible Desktop regression

GeneratedUtc=2026-08-28T01:20:21.5848788+00:00
CandidateCount=123
UserDesktop=C:\Users\71966\Desktop
PublicDesktop=C:\Users\Public\Desktop
SteamExplorerLabel=Steam
SteamOriginalPath=C:\Users\Public\Desktop\Steam.lnk
SteamItemType=.lnk
SteamPhysicalLocation=C:\Users\Public\Desktop
SteamTargetPath=D:\steam\Steam.exe
CodexExplorerLabel=Codex++
CodexOriginalPath=C:\Users\71966\Desktop\Codex++.lnk
CodexItemType=.lnk
CodexPhysicalLocation=C:\Users\71966\Desktop
CodexTargetPath=C:\Users\71966\AppData\Local\Programs\Codex++\codex-plus-plus.exe
UserDesktopIndexed=true
PublicDesktopIndexed=true
ShortcutDisplayNameIndexed=true
ShortcutSearchableFieldsIndexed=true
UrlSearchableFieldsIndexed=true
SearchAliasesPreserved=true
VisibleDuplicateResults=false
Ranking=exact>prefix>substring
BackgroundScanWhenClosed=false

| Query | Provider | MatchedField | DisplayName | OriginalPath | LaunchTarget | Rank |
|---|---|---|---|---|---|---:|
| Steam | DesktopRoot | DisplayName | Steam | C:\Users\Public\Desktop\Steam.lnk | C:\Users\Public\Desktop\Steam.lnk | 100 |
| Codex | DesktopRoot | DisplayName | Codex++ | C:\Users\71966\Desktop\Codex++.lnk | C:\Users\71966\Desktop\Codex++.lnk | 80 |
| 论文 | DesktopRoot | TargetFileNameWithoutExtension | 论文 - 快捷方式 | C:\Users\71966\Desktop\论文 - 快捷方式.lnk | C:\Users\71966\Desktop\论文 - 快捷方式.lnk | 90 |
| PDF | DesktopRoot | DisplayName | PDF | C:\Users\71966\Desktop\PDF | C:\Users\71966\Desktop\PDF | 100 |
| Chrome | DesktopRoot | TargetFileNameWithoutExtension | Google Chrome | C:\Users\Public\Desktop\Google Chrome.lnk | C:\Users\Public\Desktop\Google Chrome.lnk | 90 |

## Root-cause audit

SteamBaselineSearchProviderIncluded=false  
SteamWhyMissing=DesktopRootSearchProvider previously enumerated only Environment.SpecialFolder.DesktopDirectory; the visible Steam.lnk is in CommonDesktopDirectory (Public Desktop).  
CodexBaselineSearchProviderIncluded=true  
CodexWhyMissing=The current production service baseline already returned Codex++ from User Desktop; the earlier GUI miss was not reproducible at provider level. Canonical dedup nevertheless discarded lower-priority display aliases, so alias merging was hardened without a Codex-specific rule.  
Fix=Enumerate the deterministic User/Public Desktop root set and preserve every shortcut display/file/target/argument/URI alias while keeping one canonical visible result.  
DeepScanAdded=false  
FileSystemWatcherAdded=false  
PollingAdded=false  
TimerAdded=false  
NewBackgroundIndexer=false

## Scope and GUI audit

SpotSearchHotkey=PASS_GUI_FINAL_RELEASE  
SpotSearchPopupVisible=true  
SpotSearchGuiQueryInput=NOT_AUTOMATED_TRANSPARENT_WINDOW_NOT_EXPOSED_AS_SAFE_TARGET  
SpotSearchDeterministicQueries=PASS_ALL_FIVE  
SpotSearchOpenResultCodeModified=false  
IdentityClassificationRegression=PASS_DETERMINISTIC_AND_LIVE  
ZoneCodeModified=false  
AutoOrganizeModified=false  
ExplorerPidStable=true  
RealUserFilesMoved=0  
RealUserFilesDeleted=0  
RealUserFilesRenamed=0  
RealUserFilesCopied=0
