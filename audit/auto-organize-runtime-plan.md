# Auto Organize Runtime Plan

审计对象：`Code/Desktop Frames/AutoOrganizeManager.cs`、`AutoOrganizeForm.cs`、`OptionsFormManager.cs`、`SettingsManager.cs`、`ProfileManager.cs`。

审计基线：`develop`，安全加固提交 `6dd7d3b`。

## Capability map

```text
MonitoringMechanism=FileSystemWatcher with Created, Changed and Renamed events
MonitoringPath=%USERPROFILE%\\Desktop (Environment.SpecialFolder.Desktop)
MonitoringPathConfigurable=false

RuleStorage=ProfileManager.GetProfileFilePath("auto_organize.json")
RuleSchema=OrganizeRule JSON objects: Id, Name, Extensions, NameStartsWith, NameContains, TargetFolderPath, ConflictAction, AutoCreateFrame, IsEnabled, Priority, LastRun

ExtensionMatchingSupported=true
FilenameMatchingSupported=true (strict NameStartsWith and optional NameContains)
RegexSupported=false
FolderMatchingSupported=false (watcher is non-recursive and processes files only)
ShortcutMatchingSupported=false (.lnk and .url are explicitly skipped)
RulePrioritySupported=true (lower Priority first)

DestinationModel=explicit TargetFolderPath; portal selection in the rule editor resolves to a folder path
DestinationSubfolderSupported=false (no automatic destination subfolder model)

FileMoveImplementation=File.Move(sourcePath, destPath, overwrite: false)
OverwriteBehavior=never overwrite; legacy Overwrite enum values fail closed as Skip
CollisionBehavior=Rename generates a unique name; Skip/legacy Overwrite preserves both source and destination

FileStabilityCheck=two consecutive equal Length and LastWriteTimeUtc observations, 2 seconds apart, then FileShare.Read open
LockedFileHandling=fail closed and skip
RetryBehavior=maximum 5 stability checks; cancellation-aware; no infinite retry

TemporaryFileHandling=skip .tmp, .temp, .crdownload, .part, .partial, .download, .opdownload, .aria2 and names beginning ~$
UnknownFileHandling=no matching enabled rule means no move; hidden/system files and desktop.ini are skipped

AutoOrganizeDefaultEnabled=false
UserOptInRequired=true (Options/Tray toggle calls Start or Stop)
```

## Execution chain

```text
Detected
  -> FileSystemWatcher Created/Changed/Renamed
  -> one-second per-path queue de-duplication
  -> ProcessFileAsync
  -> existence, opt-in, shortcut/temp/hidden/system/desktop.ini safety checks
  -> enabled rules ordered by Priority
  -> extension AND strict NameStartsWith AND optional NameContains
  -> DestinationResolve (TargetFolderPath)
  -> Directory.CreateDirectory only for the configured target
  -> stability/lock check (2-second interval, maximum 5 checks)
  -> final opt-in/cancellation check
  -> collision check
  -> File.Move(..., overwrite: false)
  -> LastRun/rule persistence and optional notification
```

The Auto Organize execution path no longer calls `File.Delete`, `File.Replace`, or the Recycle Bin API. The previous `RuleConflictAction.Overwrite` value remains readable for old JSON, but it now preserves the source and destination and records a skip. The editor displays that legacy choice as disabled.

## Minimal safety changes made

1. Added `NameStartsWith` so a test rule can require `TEST_` at the beginning of the filename while retaining extension matching as an AND condition.
2. Added the requested temporary/download exclusions and fail-closed hidden/system metadata handling.
3. Replaced the single unlock probe with bounded stability checks using file length and UTC last-write time.
4. Added Changed-event handling and a bounded per-path debounce/de-duplication queue.
5. Added cancellation and setting checks before and after waiting, so Stop/disable prevents a queued operation from moving a file.
6. Made the physical move explicitly non-overwriting and made collisions fail closed for all non-Rename actions.

## Residual scope

The application contains other file-management paths for Portal, shortcut, backup, profile and frame operations. This document covers only the Auto Organize execution chain. The first real-desktop test must create rules with `NameStartsWith=TEST_`, `AutoCreateFrame=false`, and `ConflictAction=Skip`, and must leave `EnableAutoOrganize=false` after the test.
