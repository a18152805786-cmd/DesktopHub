# DesktopHub V1.1 Pure — Regression Record

- BaseTag: `desktop-hub-core-v1` (`98184ff`)
- Branch: `cleanup/v1.1-pure`
- Tested commit: `bc44017`
- Date: 2026-08-28
- Deployed build: `Desktop Frames.dll` 6.08 MB, run from
  `Code/Desktop Frames/bin/Release/net8.0-windows7.0`

## 0. What was and was not exercised

The interactive GUI suite (SpotSearch keystrokes, header drag, zone resize,
edit mode) could not be driven from this session: the DesktopHub executable is
not a Start-menu-registered application, so it could not be added to the
computer-control allowlist, and Windows Explorer was granted only at *click*
tier — no typing, no right-click, no drag. Those specific interactive gestures
are listed as **NOT EXERCISED** below rather than claimed as passes.

Everything that could be verified without those gestures was verified against
the real deployed build, using the project's own built-in regression harnesses
where they exist. Nothing below is inferred from source reading alone unless it
says so.

## 1. Change scope (what could possibly have regressed)

`git diff --name-only desktop-hub-core-v1..HEAD`, filtered for core logic:

```
identity / classifier / NativeDesktop / layout / AutoOrganize / Search
→ NO FILE MATCHED
```

The only `.cs` files touched are:

| File | Change |
|---|---|
| `OptionsFormManager.cs` | donation section removed, window 850 → 770 |
| `RemoteInfoManager.cs` | built-in upstream manifest URL removed, made opt-in |
| `FrameManager.cs` | **one string**: context-menu image pack URI |
| `PortalFrameManager.cs` | **one string**: context-menu image pack URI |
| `Localization/Strings.cs` | 11 unreachable accessors removed |

No identity resolver, classifier, layout planner, Explorer interop, Auto
Organize, or Search file was modified.

## 2. Build

| Gate | Baseline (`98184ff`) | After (`bc44017`) |
|---|---|---|
| BuildSucceeded | true | true |
| Errors | 0 | 0 |
| Warnings | **2212** | **2212** |
| NewWarnings | — | **0** |
| NewDependencies | — | **0** (`PackageReference` set unchanged: NAudio 2.2.1, Newtonsoft.Json 13.0.1) |

Both built with the identical invocation:
`msbuild "Code/Desktop Frames.sln" -p:Configuration=Release -p:Platform="Any CPU" -restore`
from separate `git worktree` checkouts, so the warning count is a like-for-like
comparison. (The raw log shows 4424 warning lines because MSBuild prints each
warning inline and again in the summary — 4424 / 2 = 2212, which reconciles the
two historical figures 2212 and 4420.)

## 3. SpotSearch — PASS (built-in harness, real desktop)

Run against the deployed build via the project's own
`--spotsearch-regression=` entry point, which exits non-zero and throws
`SPOTSEARCH_REGRESSION_FAILED` on any assertion failure. **Exit code 0.**

Full report: [`v1-1-spotsearch-regression.md`](v1-1-spotsearch-regression.md)

| Query | Result | Source | Rank |
|---|---|---|---:|
| Steam | `Steam` | `C:\Users\Public\Desktop\Steam.lnk` | 100 |
| Codex | `Codex++` | `C:\Users\71966\Desktop\Codex++.lnk` | 80 |
| 论文 | `论文 - 快捷方式` | `C:\Users\71966\Desktop\论文 - 快捷方式.lnk` | 90 |
| PDF | `PDF` | `C:\Users\71966\Desktop\PDF` | 100 |
| Chrome | `Google Chrome` | `C:\Users\Public\Desktop\Google Chrome.lnk` | 90 |

```
SteamSearch=PASS   CodexSearch=PASS   论文Search=PASS
PDFSearch=PASS     ChromeSearch=PASS
UserDesktopIndexed=true
PublicDesktopIndexed=true      <- the Steam-from-Public-Desktop fix holds
ShortcutSearchableFieldsIndexed=true
UrlSearchableFieldsIndexed=true
SearchAliasesPreserved=true
VisibleDuplicateResults=false
BackgroundScanWhenClosed=false <- no background indexing service introduced
CandidateCount=123
```

## 4. Identity — PASS (built-in deterministic harness)

Run via `--native-layout-identity-tests`. The harness builds its cases in a
fresh temp directory (`%TEMP%\DesktopHub-Phase5-7-1-<guid>`) and deletes it in a
`finally` block — it never touches the real Desktop. **Exit code 0.**

Full report: [`v1-1-identity-regression.md`](v1-1-identity-regression.md)

```
DeterministicHarness=PASS
SharedLabelDoesNotShareClassification=true
SteamUrlClassifiedAsGames=true
SameNameFolderDoesNotInheritSteamClassification=true
ManualOverrideStillHighestPriority=true
OrdinalMappingDeterministic=true
CountMismatchFailsClosed=true
TestArtifactsDeleted=true
```

The Steam case uses `steam://rungameid/1395520` — the exact Seance URI — and
asserts:

| Candidate | Type | FinalGroup |
|---|---|---|
| same-label folder | Directory | **UNASSIGNED** |
| same-label `.url` | InternetShortcut | **GAMES** |

`SeanceFolderGroup=UNASSIGNED` and `SeanceSteamUrlGroup=GAMES` therefore hold at
the classifier level. The bug fixed in `1a7c619` has not regressed.

### Live persisted state — an honest note

`Profiles/Default/native-desktop-layout-metadata.json` holds the two real Seance
items as **two separate entries with distinct paths** — the identity separation
is intact and they are not collapsed into one:

```
C:\Users\71966\Desktop\The Seance of Blake Manor      LayoutGroupId=GAMES
C:\Users\71966\Desktop\The Seance of Blake Manor.url  LayoutGroupId=GAMES
```

The real folder is persisted as `GAMES`, not `UNASSIGNED`. This is **not** a
change from this phase, and is stated rather than glossed:

- the file's last write is `2026-08-28 08:02:55`, which is before this branch
  existed, and it has not been rewritten since (verified after two restarts);
- no classification or identity code was modified in this phase;
- the harness proves the folder does not *inherit* the URL's classification from
  a shared display name, which was the actual defect. A folder whose own name
  matches a game can still be classified `GAMES` on its own evidence.

If the user expects the live folder to read `UNASSIGNED`, that is a separate
question about the classifier's treatment of the folder on its own merits, and
predates V1.1.

## 5. Auto Organize — PASS (unchanged)

```
git diff desktop-hub-core-v1..HEAD -- AutoOrganizeManager.cs AutoOrganizeForm.cs
→ empty
```

Live production rules in `Profiles/Default/auto_organize.json` re-read after
testing: all rules carry `"ConflictAction": 2` (Skip) and are unmodified
(`LastRun` still `2026-08-26T10:36:08`, i.e. it did not run during this phase).
`DeleteNever` / `OverwriteNever` / `Collision=Skip` are untouched, as are the
temporary-file filter, stability checks, debounce and kill switch.

## 6. Filesystem safety — PASS

Full inventory of `C:\Users\71966\Desktop` and `C:\Users\Public\Desktop` taken
before any testing and again after two full application restarts and a
redeployment:

```
BeforeCount = 85        AfterCount = 85
UnexpectedUserFileMoves    = 0
UnexpectedUserFileDeletes  = 0
UnexpectedUserFileRenames  = 0
UnexpectedUserFileCopies   = 0
ModifiedInPlace            = 0
```

Compared on `(root, name, length, lastWriteUtc)`. Snapshots retained as
`desktop-before.csv` / `desktop-after.csv` in the session scratch directory.

## 7. Explorer stability — PASS

```
ExplorerCrash   = false
ExplorerRestart = false
DesktopIconLoss = false
```

`explorer.exe` StartTime was `2026-08-27 22:55:54` before the work and remained
`2026-08-27 22:55:54` after both application restarts and the redeployment —
the same process instance throughout, never restarted. Process count stayed 1.

A full-desktop screenshot after redeployment shows both rails populated, all
group headers rendered (游戏 / 日常工具 / AI · 开发 / 待整理 / 办公 · 文档 /
科研 · 学习), the centre wallpaper clear space intact, and no missing icons.

## 8. About window — PASS

`audit/v1-1-pure-about-final.png` is an offscreen render of the **real**
production window: a host process references the built `Desktop Frames.dll` and
calls `AboutFormManager.ShowAboutForm()` — the same entry point the tray menu
uses — then captures the live visual tree at 2x. It is not a mock-up. It renders
in zh-Hans because the machine's UI culture is zh-CN and the language setting is
"follow Windows", so it is what this user sees.

```
PayPalVisible        = false
DonationVisible      = false
HandWaterPumpVisible = false
OldBrandVisible      = false   (as a product name; upstream names appear only
                                in the 开源许可 attribution section)
VisibleProductName   = DesktopHub
```

## 9. Not exercised

These require the interactive gestures this session could not perform. They are
**unverified**, not failed — no evidence of regression, and no code behind them
was modified:

| Item | Status | Note |
|---|---|---|
| SpotSearch via `Ctrl+\`` keystroke | NOT EXERCISED | the search *engine* passed via harness; only the hotkey path is unverified |
| GroupZonesRegression | NOT EXERCISED | zone bounds render correctly in the screenshot |
| HeaderDragRegression | NOT EXERCISED | needs drag |
| ResizeRegression (grow / reject-below-capacity) | NOT EXERCISED | needs drag |
| ManualPositionPersistence | NOT EXERCISED | store unmodified |
| ManualSizePersistence | NOT EXERCISED | store unmodified |
| ManualClassificationPersistence | PARTIAL | `ManualOverrideStillHighestPriority=true` proven by the identity harness; the drag-to-zone UI path itself not exercised |
| Options window layout after donation removal | NOT EXERCISED | grid verified statically: rows are now 0 header / 1 content / 2 footer, no `Grid.SetRow(_, 3)` remains |

Recommended manual pass before tagging: open Options and confirm no empty band
at the bottom, press `Ctrl+\`` and search the five terms, then drag and resize
one group header.
