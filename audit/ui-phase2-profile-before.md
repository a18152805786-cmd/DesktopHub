# UI Phase 2 real profile snapshot (before migration)

Snapshot date: 2026-08-26

Runtime profile root:

`C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0`

Active profile from `ProfileOptions.json`: `Default`

Profile data:

`Profiles\Default\frames.json`

```text
Profile=Default
FrameCount=2
StartupTipsCandidates=1 (initial heuristic candidate; final detector is exact/template based)
HighConfidenceMatches=1 after exact normalized title/content verification
AmbiguousMatches=0
OtherNoteFramesBefore=0
```

Observed frames:

1. Data frame: `New Frame - Drop your shortcuts here`
2. Note frame: `Desktop Frames + Startup Tips`, with the historical English built-in Startup Tips content.

The Note frame has no `SystemFrameId`/built-in marker in the pre-migration JSON. No files were changed while taking this snapshot.

The following exact files were backed up before any real-profile write:

`audit/backups/ui-phase2-startup-tips/`

- `ProfileOptions.json.20260826_121157.bak`
- `Default.options.json.20260826_121157.bak`
- `Default.frames.json.20260826_121157.bak`

Backup hashes are recorded in the command audit and can be recalculated from the backup files. The source-code repository was clean at the start of this phase, at commit `4b824d082011a7ff7ff0b9988ff7aa2a41c35f41`.
