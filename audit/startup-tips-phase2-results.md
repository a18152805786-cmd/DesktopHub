# UI Phase 2 Startup Tips results

Audit date: 2026-08-26

## Scope

This phase implements only the Startup Tips lifecycle. It does not change
Frame styling, SpotSearch, shortcuts, folder grouping, product identity, or
Auto Organize execution/rules.

## Implementation result

```text
StartupTipsIdentity=SystemFrameId=startup-tips
DismissStateStorage=application-level ProfileOptions.json
DismissScope=global
LegacyDetection=exact normalized title + exact normalized content template
RestoreMethod=manual/config API only; Settings UI deferred
```

The integration points are deliberately small:

1. `Framemanager.InitializeDefaultFrame()` adds the stable marker to newly
   generated Tips and omits that Note when the global dismissal flag is true.
2. `Framemanager.LoadAndCreateFrames()` reconciles loaded data after the
   existing generic migration and before windows are created.
3. The confirmed frame-delete action calls
   `StartupTipsManager.HandleUserFrameDeletion()`; an explicit user deletion
   of the identified Tips frame persists the global dismissal flag.
4. `ProfileManager` reads a missing flag as `false` and writes the new field
   without changing the existing profile/options schema.

## Harness scenario results

The file-based reflection harness used the Release output in
`C:\Users\71966\AppData\Local\Temp\DesktopHub-phase2-release-20260826` and
wrote only to temporary JSON directories. It did not launch the real
application or touch the real Desktop.

```text
KnownEnglishStartupTips=HIGH_CONFIDENCE
KnownChineseStartupTips=HIGH_CONFIDENCE
RandomNote=NOT_MATCH
NoteContainingWordsStartupTipsOnly=NOT_MATCH
ModifiedStartupTipsContent=LOW_CONFIDENCE

DismissedFalseShouldCreate=true
DismissedTrueShouldCreate=false
FreshProfileTipsCreated=1
FreshDismissedProfileTipsCreated=0

LegacyWithDismissedFalse=stable identity added; normal Note preserved
SecondReconcile=no duplicate; PASS
StableTipsWithDismissedTrue=removed; normal Note preserved
LegacyTipsWithDismissedTrue=removed; normal Note preserved
TwoLegacyCandidates=preserved; PASS
ModifiedTipsCandidate=preserved; PASS
NormalNoteDeletionLeavesDismissedFalse=true

OldConfigMissingDismissKey=defaults false; key added; PASS
UserDeletionPersistsDismissState=true
```

## Real Profile migration

Before the first real-profile write, these files were backed up:

```text
audit/backups/ui-phase2-startup-tips/ProfileOptions.json.20260826_121157.bak
audit/backups/ui-phase2-startup-tips/Default.options.json.20260826_121157.bak
audit/backups/ui-phase2-startup-tips/Default.frames.json.20260826_121157.bak
```

Runtime root:

```text
C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0
```

The initial profile contained two Frames: one Data Frame and one historical
English Startup Tips Note. The exact normalized detector found one
high-confidence legacy candidate. The migration then:

```text
StartupTipsDismissed=true
LegacyFrameRemoved=true
RemainingFrameCount=1
RemainingFrames=New Frame - Drop your shortcuts here (Data)
OtherNoteFramesBefore=0
OtherNoteFramesAfter=0
DataFramePreserved=true
```

The second migration run made no further change, confirming idempotence. The
resulting `ProfileOptions.json` contains `StartupTipsDismissed=true`. The
Default profile `options.json` SHA-256 remained:

```text
6C5CCFA79DBD30DCD361B85F123251AAE136EAE2A30EA99149F2979B0B267DB0
```

`EnableAutoOrganize` remained `true`; `AutoOrganizeManager.cs` was not
modified.

## Safety observations

```text
OrdinaryNoteDeletedByMigration=false
AmbiguousNoteDeletedByMigration=false
ModifiedTipsDeletedByMigration=false
AutoOrganizeFilesMovedByThisPhase=false
ProductionRulesChanged=false
RegistryModifiedByThisPhase=false
RealDesktopFilesTouchedByHarness=false
```

The migration path uses exact normalized templates. Normalization only
canonicalizes line endings and removes trailing whitespace from each line;
it does not use `Contains()` or a fuzzy text match. A title-only, content-only,
or edited template is not high confidence.

## Build

```text
Command=MSBuild Desktop Frames.csproj /t:Rebuild /p:Configuration=Release /p:OutDir=C:\Users\71966\AppData\Local\Temp\DesktopHub-phase2-release-20260826\
Result=0 errors
Warnings=1132 existing project diagnostics
NewWarnings=0 in the new Startup Tips code and changed call sites
Output=C:\Users\71966\AppData\Local\Temp\DesktopHub-phase2-release-20260826\Desktop Frames.exe
```

No NuGet package, external library, font, or runtime dependency was added.

## Performance / runtime note

No real application process was launched in this phase because the real
release binary would run the pre-change code and application startup can
perform unrelated Windows integration. The lifecycle behavior was validated
through the compiled assembly and temporary profile JSON. The change adds one
boolean, one bounded reconciliation pass, and no watcher, timer, or new UI
surface; a meaningful performance regression is therefore not indicated.
The previous Phase 1 idle reference remains approximately 175 MB working set,
80 MB private memory, and 0.02% CPU.
