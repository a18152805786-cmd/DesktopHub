# UI Phase 3 Frame baseline

Recorded: 2026-08-26

Repository baseline:

```text
Branch=develop
Commit=c8fa8267ca61a4dcc91ba79d7bdba4db64bfd0d1
CheckpointTag=pre-ui-phase3-frame-restyle
WorkingTreeCleanBefore=true
```

Runtime profile:

```text
Profile=Default
FrameCount=1
FrameType=Data
X=20
Y=20
Width=360
Height=180
Title=New Frame - Drop your shortcuts here
ItemsCount=0
LockedState=false
RolledState=false
Opacity=TintValue 85
```

Visual construction before Phase 3:

```text
Background=SelectedColor Gray at TintValue 85, applied by Utility.ApplyTintAndColorToFrame
Border=FrameBorderThickness 2 with default Gray when no custom border color exists
CornerRadius=6 unless FramesWithNoRoundCorners is enabled
TitleHeight=implicit layout; roll-up target 28
TitleFont=per-frame Medium (12px), normal weight
ControlVisibility=menu and lock TextBlocks always present at MenuTintValue 30%
MenuControl=heart/menu glyph overlaid directly on the DockPanel
HeaderBackground=20/255 black
```

Behavior freeze:

```text
WindowType=NonActivatingWindow : WPF Window
AllowsTransparency=true
ShowInTaskbar=false
WindowStyle=None
ResizeMode=CanResizeWithGrip when unlocked; NoResize when locked
HeaderDrag=Label.MouseDown -> SnapManager.StartDrag -> Window.DragMove
RollUp=title double-click; persisted in IsRolled/UnrolledHeight
ContextMenu=existing Frame context menu
FramePersistence=Profiles/Default/frames.json
```

Safety hashes:

```text
frames.json=D7B807F7BDFF0CC609F07EB6595EB347B1C548D369CF7BF619C4F2EC33ADA39B
options.json=6C5CCFA79DBD30DCD361B85F123251AAE136EAE2A30EA99149F2979B0B267DB0
AutoOrganizeEnabled=true
StartupTipsDismissed=true
```
