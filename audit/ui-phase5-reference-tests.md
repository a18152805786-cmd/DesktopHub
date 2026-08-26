# UI Phase 5 reference tests

Date: 2026-08-26

## Static and build results

```text
Branch=develop
CommitBefore=bcc3c64bdbe61c02df91f8d01df481141bb42799
BuildConfiguration=Release|AnyCPU
BuildSucceeded=true
ReferenceGroupingWarnings=0
NewDependencies=0
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
FrameStyleResourceChanged=false
ReferenceAutoGroupingEnabled=false
AutoOrganizeManagerSHA256=3FB4098E6751C2EB32A0FAA0C7B5ECF8A937074B3E9A05E281599946F917DD42
ProductionRulesSHA256=26BF7F1B85AF57E5810EEC4B26A80F71E9D4D2C4F9901D9EA2C04575FCC2A848
RuntimeFramesSHA256=D7B807F7BDFF0CC609F07EB6595EB347B1C548D369CF7BF619C4F2EC33ADA39B
```

## Isolated harness

The harness used a unique directory below `%TEMP%`, a generated Notepad shortcut, a Steam `.url`, and a test folder. `FrameDataManager.JsonFilePath` pointed only to a temporary `frames.json`. The validated temporary directory was removed after the run.

```text
ShortcutReferenceAdded=true
UrlReferenceAdded=true
FolderReferenceAdded=true
PersistedItemCount=3

OriginalShortcutStillExists=true
OriginalUrlStillExists=true
OriginalFolderStillExists=true
CopyDirectoryCreated=false

RemoveMembershipSucceeded=true
RemovePreservedOriginal=true
ManualOverridePreserved=true

ShortcutClassification=AiDev/High
UrlClassification=Games/High
FolderClassification=ResearchStudy/High

FrameSearchReferenceCount=2
SearchCanonicalDedupCount=1
ProductionPreviewCount=63
ProductionApplyBlockedWithoutVisualOwnership=true
```

## Preview totals

| Group | High | Medium | Unassigned |
|---|---:|---:|---:|
| AI · 开发 | 12 | 0 | 0 |
| 科研 · 学习 | 7 | 0 | 0 |
| 工作工具 | 3 | 0 | 0 |
| 游戏 | 11 | 2 | 0 |
| 日常工具 | 4 | 0 | 0 |
| 未分组 | 0 | 0 | 24 |

`HIGH` entries were previewed but not applied to the real profile because visual duplication cannot yet be prevented safely.

## Filesystem safety

The real Desktop was read only. No test item was created on the real Desktop during the harness. Before/after snapshots are compared separately.

```text
ShortcutsMoved=0
ShortcutsDeleted=0
ShortcutsRenamed=0
FoldersMoved=0
FoldersDeleted=0
FoldersRenamed=0
```

The only snapshot delta was `Desktop\ppt` directory `LastWriteTime` (`18:39:46` → `18:52:50`) while separate PPT work was active. Its path, type, and membership were unchanged; Phase 5 performed no operation against it.

## GUI status

Computer Use was user-authorized after core tests. The running release build remained responsive; the existing Fluent Frame was visible, `Ctrl + \`` opened the localized SpotSearch popup, and the second hotkey closed it. No search result or real shortcut, URL, folder, Office app, game, or browser was launched.

```text
FrameFluentCompatibility=PASS
SpotSearchHotkey=PASS
ReferenceItemGuiApply=NOT_RUN
Reason=Production apply is blocked by unresolved per-icon visual ownership.
PowerPointWindowTargeted=false
```

## Runtime sample

```text
WorkingSetMB=180.60
PrivateMemoryMB=85.59
IdleCPUPercentApprox=0.0000
Responding=true
GroupedRuntimeSample=NOT_AVAILABLE
```

The ordinary runtime sample remains within the Phase 5 preferred limits. Continuous-growth and five-Frame grouped measurements cannot be certified until reference groups can be displayed without native-icon duplication.
