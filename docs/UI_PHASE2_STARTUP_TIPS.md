# UI Phase 2 — Startup Tips cleanup

Status: implemented on `develop` (2026-08-26)

## Lifecycle contract

```text
StartupTipsIdentity=SystemFrameId=startup-tips
DismissStateKey=StartupTipsDismissed
DismissStateStorage=application-level ProfileOptions.json
DismissScope=global
DefaultValueWhenMissing=false
```

Startup Tips remain a normal Note Frame from the rendering system's point of
view, but new built-in instances carry `SystemFrameId=startup-tips`. This is a
human-readable, additive JSON property. Older Frames without this property
continue to load as ordinary Frames.

## Creation and dismissal

For an empty/missing/invalid profile, the existing default initialization still
creates the ordinary Data Frame. It creates the Tips Note only when
`StartupTipsDismissed=false`.

The normal confirmed frame-delete action is the user dismissal action. When
the deleted Frame has the stable identity, or is an exact high-confidence
legacy Tips template, `ProfileManager.SetStartupTipsDismissed(true)` persists
the global state. Load failures, crashes, hidden Frames, and unrelated Frame
operations do not set the flag.

When the flag is true, a loaded stable Tips Frame or one exact legacy Tips
Frame is removed during reconciliation. If there are multiple candidates,
the migration preserves them and logs a warning rather than guessing.

## Legacy detection and migration

`StartupTipsManager` recognizes legacy Tips conservatively:

```text
ItemsType == Note
AND exact known title
AND exact normalized known content template
```

Known English and Phase 1 Chinese templates are included. Normalization
canonicalizes line endings and line-trailing whitespace only. These cases are
not high confidence and are preserved:

```text
ordinary Note with the words “Startup Tips”
title-only match
content-only match
user-edited Tips content
multiple matching candidates
```

On a non-dismissed profile, one exact legacy candidate receives the stable
marker and is saved. On a dismissed profile, one exact candidate is removed.
The migration is idempotent.

## Compatibility

Old `ProfileOptions.json` files without `StartupTipsDismissed` load with a safe
default of `false`; the field is added on the normal config save path. Existing
`frames.json`, `options.json`, and Auto Organize configuration remain
compatible. No profile-wide schema migration or new database was introduced.

## Restore behavior

There is no Settings UI entry in Phase 2. Restore is intentionally deferred to
avoid an Options-window refactor. The persisted state is reversible through
the existing `ProfileManager.SetStartupTipsDismissed(false)` API or an
explicit administrative edit of the application-level
`ProfileOptions.json`; the latter should be done only while the application is
closed and after a backup. A future Settings action can call the same API.

## Verification summary

```text
FirstRunCreatesTips=true
ClosingTipsSetsDismissed=true
SecondLaunchDoesNotRecreateTips=true
DuplicateTipsPrevented=true
LegacyEnglishDetectedSafely=true
LegacyChineseDetectedSafely=true
RandomNotesPreserved=true
AmbiguousNotesPreserved=true
OldConfigCompatible=true
MigrationIdempotent=true
RealProfileTipsRemoved=true
RealProfileOtherFramesPreserved=true
ProductionRulesUnchanged=true
AutoOrganizeBehaviorUnchanged=true
NewDependencies=0
NewWarnings=0
```

The real Default profile was backed up before migration under
`audit/backups/ui-phase2-startup-tips/`. After migration it contains the
original Data Frame only, and `ProfileOptions.json` records
`StartupTipsDismissed=true`.
