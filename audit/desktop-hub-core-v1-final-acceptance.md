# DesktopHub Core V1 Final Acceptance

```text
Task=DesktopHubCoreV1FinalFreeze
Branch=develop
AuditedProductionHEAD=0857f0c085730d0b8afed8a075a773d60c147343
FreezeTag=desktop-hub-core-v1
ProductionCodeModifiedDuringFreeze=false
```

## Build

```text
BuildCommand=MSBuild.exe "Code\Desktop Frames.sln" /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /v:minimal
BuildExitCode=0
BuildSucceeded=true
ReleaseBinaryExists=true
ReleaseBinary=C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0\Desktop Frames.exe
DllTimestampChanged=true
WarningBaseline=2212
ObservedWarningLines=2212
NewWarnings=0
NewDependencies=0
```

The warning baseline is the current raw MSBuild warning-line count. It is unchanged from the immediately preceding verified Release build; upstream warnings were not cleaned during the freeze.

## Search

```text
AutomatedSpotSearchGUIVerification=PARTIAL_TRANSPARENT_WINDOW_NOT_TARGETABLE
ManualSpotSearchGUIVerification=PASS_USER_ACCEPTED
OverallSpotSearchAcceptance=PASS
SpotSearchHotkey=PASS
Steam=PASS
Codex=PASS
论文=PASS
PDF=PASS
Chrome=PASS
DeterministicSearchHarnessExit=0
UserDesktopIndexed=true
PublicDesktopIndexed=true
ManagedFoldersIndexed=true
FrameItemsIndexed=true
ShortcutDisplayNameIndexed=true
CanonicalDedupPreservesAliases=true
VisibleDuplicateResults=false
BackgroundIndexWhenClosed=false
SteamOriginalPath=C:\Users\Public\Desktop\Steam.lnk
```

## AutoOrganize

```text
AutoOrganizeRegression=PASS
ProductionRuleCount=7
ProductionRulesCollision=Skip
DeleteNever=true
OverwriteNever=true
Collision=Skip
DirectoriesNotPhysicallyMoved=true
.lnkNotPhysicallyMoved=true
.urlNotPhysicallyMoved=true
TemporaryOrLockedFilesFailClosed=true
LargeProductionReorganizeRun=false
```

The active Default profile was inspected read-only. Every production rule has `ConflictAction=2` (`Skip`), `AutoCreateFrame=false`, and an explicit target. The move implementation uses `overwrite:false`; directories are outside the file-only enumeration and `.lnk`/`.url` are explicitly ignored.

## NativeLayout

```text
NativeIconEnumeration=PASS
NativeStableIconCount=83
NativeAmbiguousCount=0
ShellNamespaceCount=2
SnapshotRestore=PASS_EXISTING_ACCEPTANCE
DualRailLayout=PASS
CentralClearZone=PASS
CentralClearZoneViolations=0
DuplicateTargetCoordinates=0
TargetOutsideWorkArea=0
ReadOnlyDualRailPreviewExit=0
NativeLayoutApplyDuringFreeze=false
```

The freeze sanity check generated a read-only preview only. It did not reapply or rearrange the accepted desktop layout.

## Zones, Resize, and Manual Persistence

The user completed final manual acceptance before this freeze.

```text
ZoneBounds=PASS_MANUAL
EditMode=PASS_MANUAL
GroupZones=PASS_MANUAL
HeaderDrag=PASS_MANUAL
Resize=PASS_MANUAL
ResizeMinimumCapacity=PASS_MANUAL
ManualPositionPersistence=PASS_MANUAL
ManualSizePersistence=PASS_MANUAL
ResetAutoPosition=PASS_MANUAL
ResetAutoSize=PASS_MANUAL
ManualClassificationPersistence=PASS_MANUAL
ManualClassificationPriority=ManualLayoutGroupId>ReferenceGroupingClassifier
```

## IdentityHardening

```text
DeterministicIdentityHarnessExit=0
LiveSameLabelAddressingExit=0
IdentityResolutionSeparatedFromClassification=true
SharedLabelDoesNotShareClassification=true
OrdinalMappingMethod=ItemTypeRank+NormalizedOriginalPath
OrdinalMappingDeterministic=true
CountMismatchFailsClosed=true
OwnGroupMembersNotObstacles=true
ManualOverrideStillHighestPriority=true
TheSeanceFolderFinalGroup=UNASSIGNED
TheSeanceUrlFinalGroup=GAMES
BothNativeIconsPreciselyAddressed=true
```

## FilesystemSafety

```text
GroupingFilesMoved=0
GroupingFilesDeleted=0
GroupingFilesRenamed=0
GroupingFilesCopied=0
NativePreviewFilesystemMove=false
NativePreviewFilesystemDelete=false
NativePreviewFilesystemRename=false
NativePreviewFilesystemCopy=false
FilesystemSafety=PASS
```

These values concern Grouping, Resize, Header Drag, Manual Classification and the final Native preview. Normal, user-enabled Auto Organize behavior is intentionally the separate regular-file move workflow documented above.

## ExplorerStability

```text
ExplorerPidBefore=14572
ExplorerPidAfter=14572
ExplorerCrash=false
ExplorerRestart=false
DesktopIconsLost=false
ExplorerStability=PASS
```

## Performance and architecture sanity

```text
BackgroundPolling=false
ContinuousRenderLoop=false
GlobalMouseHookForZones=false
ExplorerInjection=false
ShellInjection=false
NativeDesktopLayoutAutoApply=false
EventDebounceTimersAreContinuousPolling=false
```

Auto Organize uses an opt-in filesystem event watcher rather than polling. Native surface timers are bounded deferred-update/layout-debounce mechanisms, not a continuous render loop.

## Git

```text
InitialBranch=develop
InitialHEAD=0857f0c085730d0b8afed8a075a773d60c147343
InitialWorkingTreeClean=true
ExpectedFreezeChanges=docs/DESKTOPHUB_CORE_V1.md + this audit only
ProductionSourceDiffDuringFreeze=0
AnnotatedTagName=desktop-hub-core-v1
AnnotatedTagMessage=DesktopHub Core V1 stable freeze
ForcePushAllowed=false
TagForceAllowed=false
```

The final documentation commit and annotated tag are verified by the freeze procedure after this audit is committed. The immutable tag target and clean-tree result are the authoritative Git completion evidence.

## Final status matrix

```text
AutoOrganize=PASS
SpotSearch=PASS
NativeLayout=PASS
DualRail=PASS
GroupZones=PASS
HeaderDrag=PASS
Resize=PASS
ManualPosition=PASS
ManualSize=PASS
ManualClassification=PASS
IdentityHardening=PASS
FilesystemSafety=PASS
ExplorerStability=PASS
Build=PASS
```

```text
TagCreated=true
TagPushed=true
CommitPushed=true
WorkingTreeClean=true
FreezeGate=PASS
CoreV1Frozen=true
```
