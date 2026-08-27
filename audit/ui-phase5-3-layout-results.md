# UI Phase 5.3 Native Layout Results

~~~text
Task=DesktopHubPhase5_3NativeDesktopLayoutFirst
Status=PASS

Branch=develop
CommitBefore=e4e0b4c19f3fcd0e5965e422e76478cbd2633896
CheckpointTag=pre-ui-phase5-3-native-layout-first

LayoutPlanner=PASS
Classifier=ReferenceGroupingClassifier (reused)
DesktopWorkArea=0,0 2560x1440
CurrentDpi=96
IconGrid=75x100
PrimaryMonitorOnly=true

AutoArrangeEnabled=false
AlignToGridEnabled=true
AutoArrangeModified=false

StableIconCount=81
AmbiguousIconCount=2
ShellNamespaceCount=2

ResearchCount=12
AIDevCount=13
OfficeDocumentsCount=8
DailyToolsCount=13
GamesCount=15
SystemTempCount=2
UnassignedCount=22

BatchAGroup=OFFICE_DOCUMENTS
BatchAItems=8
BatchAResult=PASS
BatchBGroup=AI_DEV
BatchBItems=13
BatchBResult=PASS
BatchCGroup=RESEARCH_STUDY
BatchCItems=12
BatchCResult=PASS
BatchDGroup=DAILY_TOOLS
BatchDItems=0
BatchDResult=DEFERRED_USER_REVIEW
BatchEGroup=GAMES
BatchEItems=0
BatchEResult=DEFERRED_USER_REVIEW

ItemsMovedVisually=33
DuplicateTargetCoordinates=0
TargetOutsideWorkArea=0
OtherUnmanagedIconsChanged=false

Snapshot=PASS
SnapshotDryRun=CanResolve:81 Missing:0 Ambiguous:0
Restore=PASS
RestoreAccuracy=Exact (Restored:81 Failed:0)

FilesMoved=0
FilesDeleted=0
FilesRenamed=0
FilesCopied=0
FoldersMoved=0
FoldersDeleted=0
FoldersRenamed=0

SearchRegression=PASS (path-based modules unchanged; Release build passed)
AutoOrganizeManagerModified=false
ProductionRulesChanged=false
FrameBehaviorChanged=false

ExplorerCrash=false
ExplorerRestart=false
DesktopIconsDisappear=false
BackgroundPolling=false
BeforeWorkingSetMB=192.71
BeforePrivateMemoryMB=96.44
AfterWorkingSetMB=192.12
AfterPrivateMemoryMB=95.20
AfterIdleCPU=0.0076%
NoContinuousMemoryGrowth=true

BuildSucceeded=true
NewPhase53Warnings=0
NewNuGetPackages=0
NewExternalLibraries=0
~~~

## Result

The three requested primary areas are applied and grid-idempotent. A repeated
preview reports Eligible=0 for Office Documents, AI/Development, and
Research/Study. Daily Tools and Games remain preview-only for user review.

All 33 baseline coordinate changes belong to those three managed groups. The
remaining 48 stable icons and all four unresolved/shell items retained their
baseline grid slots. Desktop filesystem fingerprints were identical before and
after the final convergence apply, and the Explorer process identity did not
change.
