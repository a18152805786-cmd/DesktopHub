# DesktopHub Production Auto Organize V1

This document records the first production ruleset for the private DesktopHub fork.
It is intentionally extension-based and conservative. It does not classify shortcuts,
URLs, folders, executables, or unknown files.

## Rules

```text
RuleSetName=DesktopHub Production V1
RuleSetVersion=1
DefaultEnabled=false (application default; this local rollout is explicitly enabled)
ConflictAction=Skip
OverwriteNever=true
DeleteNever=true
AutoCreateFrame=false
```

| Rule | Extensions | Destination |
|---|---|---|
| 文档 | `.doc`, `.docx`, `.txt`, `.rtf`, `.md` | `Desktop\文档` |
| PPT | `.ppt`, `.pptx`, `.pps`, `.ppsx` | existing `Desktop\ppt` (reused as `PPT`) |
| PDF | `.pdf` | `Desktop\PDF` |
| 数据表格 | `.xls`, `.xlsx`, `.xlsm`, `.csv`, `.dta` | `Desktop\数据表格` |
| 图片 | `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp` | `Desktop\图片` |
| 压缩包 | `.zip`, `.rar`, `.7z` | `Desktop\压缩包` |
| 安装包 | `.msi`, `.msix` | `Desktop\安装包` |

The runtime JSON is stored by the existing profile system at:

```text
Code\Desktop Frames\bin\Release\net8.0-windows7.0\Profiles\Default\auto_organize.json
```

The schema does not provide a ruleset-name/version field, so those identifiers are
documented here rather than added to the upstream model.

## Explicit exclusions

`.exe`, `.lnk`, `.url`, folders, files without a supported extension, unknown
extensions, hidden/system objects, `desktop.ini`, and Windows shell namespace items
remain in place. Temporary and Office lock files remain excluded:

```text
*.tmp
*.temp
*.crdownload
*.part
*.partial
*.download
*.opdownload
*.aria2
~$*
```

The watcher is non-recursive and monitors only the Desktop root. Existing folders,
including the DesktopHub project directory, are not traversed or moved.

## Safety behavior

The rollout uses the existing guarded AutoOrganizeManager:

```text
FileSystemWatcher (Created/Changed/Renamed)
→ debounce/deduplication
→ exclusion checks
→ extension rule match
→ destination existence check
→ two stable length/write-time observations
→ safe read open
→ File.Move(overwrite:false)
```

Destination collisions are skipped. The UI and runtime do not enable overwrite.
Files that remain active, locked, or unstable are deferred or safely skipped. The
existing enable/disable switch stops the watcher; a one-shot `--initial-organize`
argument was added to invoke the same guarded sweep for files already present before
the production rollout. Normal startup behavior is unchanged.

## Validation record

```text
ProductionCheckpointTag=pre-production-auto-organize-v1
ExistingFilesMoved=8
StageA=PASS (4 existing files: images/archives)
StageB=PASS (4 existing files: documents/data)
StageC=PASS (0 existing files; installer watcher samples passed)
NewFileWatcher=PASS (PDF/DOCX/PPTX/CSV/PNG/ZIP/MSI/MSIX)
ExeControl=PASS (PROD_TEST.exe stayed excluded)
CollisionBehavior=Skip
OverwriteOccurred=false
FilesDeleted=0 (production files)
DirectoriesMoved=0
ShortcutsMoved=0
TemporaryFilesSkipped=true
ActiveFileHandling=DEFER_THEN_MOVE
KillSwitchValidated=true
AutoOrganizeEnabled=true
```

The explicit test artifacts were removed after their results were recorded. The
production files moved by the initial sweep were not moved back.

## Performance observation

After approximately five minutes idle with the complete ruleset enabled:

```text
WorkingSetMB=174.87..175.06
PrivateMemoryMB=93.55..93.71
IdleCPUPercentApprox=0.0049..0.0275 (average 0.0078)
NoContinuousMemoryGrowth=true
```

## Local audit artifacts

The inventory, preview, rollback manifest, initial-organize log, new-file results,
options backup, and performance CSV are kept locally under `audit/`. They are excluded
from Git because they contain personal Desktop paths and filenames. The rollback
manifest is the authoritative source for reversing the eight production moves; test
entries are marked as test artifacts and cleaned.
