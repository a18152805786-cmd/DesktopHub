# UI Phase 5.7.1 final gates

Task=DesktopHubPhase5_7_1IdentityClassificationHardening
Status=PARTIAL

## Source and deterministic gates

IdentityClassificationSeparated=true
SharedLabelDoesNotShareClassification=true
RealSeanceFolderGroup=UNASSIGNED
RealSeanceUrlGroup=GAMES
OrdinalMappingMethod=ItemTypeRank+NormalizedOriginalPath
OrdinalMappingStable=true
CountMismatchFailsClosed=true
CountMismatchMovablePath=null
CountMismatchNoMovement=true
ManualOverridePriority=PASS
OwnMembersObstacleBugFixed=true
AutoOrganizeManagerModified=false
ProductionAutoOrganizeRulesChanged=false
NewDependencies=0

## GUI gates

ComputerUseAttempted=true
ComputerUseStoppedBySafetyPolicy=true
ComputerUseStopReason=could not determine the current browser URL with enough confidence to enforce policy
ComputerUseRetriedAfterUserMinimizedBrowser=true
BrowserContentUsed=false
SpotSearchHotkeySent=true
SpotSearchPopupVisible=true
SpotSearchTransparentWindowTargetable=false
SteamGUIResult=NOT_RUN
CodexGUIResult=NOT_RUN
论文GUIResult=NOT_RUN
PDFGUIResult=NOT_RUN
ChromeGUIResult=NOT_RUN
ZoneRegression=NOT_RUN
ResizeRegression=NOT_RUN
ManualSizeRegression=NOT_RUN
ManualPositionRegression=PASS_NON_GUI_PERSISTENCE_ONLY
HeaderDragGUIRegression=NOT_RUN
SpotSearchGUIRegression=PARTIAL_POPUP_VISIBLE_RESULTS_NOT_RUN

The first Computer Use attempt stopped while selecting a browser window solely as an input target. After the user minimized it, the retry used a non-browser window and the real Ctrl+` hotkey opened the DesktopHub search popup. The popup was visible, but Computer Use did not expose its Desktop Frames window as a targetable window and refused text input across the window boundary. No search result was opened and no GUI result PASS is inferred.

## Native desktop and filesystem gates

LiveSameLabelAddressing=PASS
BothSameLabelIconsPreciselyAddressed=true
RealSeanceDifferentFinalGroup=true
GroupDragServiceRegression=PASS
OwnMembersAreObstacles=false
GameGroupRegressionMove=+1 column
GameGroupFinalPositionRestored=true
GameGroupFinalColumn=26
RealUserFilesMoved=0
RealUserFilesDeleted=0
RealUserFilesRenamed=0
RealUserFilesCopied=0
TestArtifactsDeleted=true
ExplorerCrash=false
ExplorerRestart=false
DesktopIconsMissing=false
DesktopUnavailable=false

## Build

BuildCommand=MSBuild.exe "Code\\Desktop Frames.sln" /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /v:minimal
BuildExitCode=0
BuildSucceeded=true
OutputDllTimestampChanged=true
UpstreamWarningBaseline=4420
ObservedWarningLines=2212
WarningsInChangedNativeDesktopFiles=0
NewWarnings=0

## Final gate decision

BlockingIssues=Required GUI Zone, Resize, ManualSize and HeaderDrag gates were not run because DesktopHub transparent desktop windows were not targetable; SpotSearch popup visibility passed, but the five result-list queries could not be entered for the same reason.
PassGateSatisfied=false
FinalizationDecision=PARTIAL
