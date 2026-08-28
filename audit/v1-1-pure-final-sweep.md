# DesktopHub V1.1 Pure — Final Sweep

Last work before the `desktop-hub-v1.1-pure` tag decision.

- Branch: `cleanup/v1.1-pure`
- CommitBefore: `2a2f63f`
- Date: 2026-08-28

---

## 1. Upstream author easter eggs — removed

Three trigger words fired hidden behaviour from the **frame title** field.

| Trigger | Behaviour | Where |
|---|---|---|
| `limbo666` | full-screen fireworks overlay, then **silently reverted the title the user typed** | `InterCore.ProcessTitleChange` → `ActivateSparkleEffect` |
| `Nikos`, `Nikos Georgousis` | persistent "Legendary Mode" — animated gradient border and glow, reapplied on every frame load | `ActivateLegendaryMode` |
| any title *containing* `>:` | same Legendary Mode | same |

The `>:` one was the most defensible reason to remove all three: it is a
substring test, so an ordinary title like `工作 >: 紧急` would silently acquire a
permanent visual effect the user never asked for.

### Deleted from `InterCore.cs`

- `ProcessTitleChange` — the whole method; its only purpose was these triggers
- `#region Legendary Mode (Nikos)` — `ActivateLegendaryMode`, `DeactivateLegendaryMode`
- `#region Private Methods - Epic Fireworks (limbo666)` — `ActivateSparkleEffect` and helpers
- orphaned fields: `_sparkleOverlay`, `_legendaryEffects`, `_originalBorders`,
  `_originalBorderThicknesses`, `_originalEffects`
- the `_sparkleOverlay?.Close()` line in `Cleanup()`

File went 964 → 480 lines.

### Deleted from `FrameManager.cs`

- the rename path now commits the typed title directly:
  `string finalTitle = newTitle;` (was routed through `ProcessTitleChange`)
- the "Cleanup Legendary Effects" block on frame reload
- the "Check for persistent Legendary Mode" block on frame creation — **and the
  bare `{ ... }` scope immediately after it**, which called `ProcessTitleChange`
  unconditionally on *every* frame created, not just triggered ones

### Deliberately kept

`Dance Party` (Ctrl+Alt+D), `Gravity Drop` (Ctrl+Shift+G) and `Lighthouse Sweep`
are generic effects not keyed to any person's name, and Lighthouse Sweep is used
by the real single-instance notification path. `PlaySweepSound` is retained — it
is called by Lighthouse Sweep at `InterCore.cs:536`, not by the fireworks.

### Result

```
Limbo666EasterEggRemoved      = true
NikosEasterEggRemoved         = true
GreaterColonEasterEggRemoved  = true
```

`grep -rn "Nikos|limbo666|Contains(\">:\")" --include=*.cs` over the whole
product source returns **zero matches**. Frame / header / group rename are
untouched; a title is now just a title.

---

## 2. Remote manifest control chain — removed, not merely disabled

The previous round removed the built-in URL but left the machinery in place. This
round deletes the production chain outright.

### Files deleted

| File | What it was |
|---|---|
| `RemoteInfoManager.cs` | the fetch/parse/dispatch chain |
| `RemoteDataModel.cs` | `RemoteManifest`, `RemoteMeta`, `RemoteAnnouncement`, `RemoteSettings` |
| `NotificationFormManager.cs` | the window that rendered remote announcements — its only caller was `RemoteInfoManager`, and its only input type was `RemoteAnnouncement` |
| `ngdfcs/getversion.json`, `ngdfcs/dev.json` | the inherited upstream promo manifests (directory now gone) |

### Code removed

- `TrayManager.cs` — the `RemoteInfoManager.Initialize()` startup call
- `RegistryHelper.cs` — the entire `#region Messaging State Management (Remote
  Info System)`: `IsMessageDismissed`, `GetMessageDisplayCount`,
  `IncrementMessageCount`, `SetMessageDismissed`, and the
  `SOFTWARE\Desktop_Frames_Plus\Messaging` key path. These existed only to
  throttle remote announcements.
- `Strings.cs` + all nine `.resx` — `UpdateAvailable`, `NotificationTitle`,
  `NotificationDontShowAgain`, now unreachable

### Gate

```
BuiltInRemoteManifest            = false
DeveloperRepoOverride            = false   (developer_repo.txt no longer read anywhere)
RemoteKillSwitch                 = false   (ForceKillSwitch gone with RemoteMeta)
RemotePromotionInjection         = false   (no window can render a remote message)
RemoteLoggingToggle              = false
DefaultNetworkAccessFromRemoteInfo = false
```

`SettingsManager.EnableBackgroundValidationLogging` is **kept** — it is a genuine
local option, surfaced in the Options window and read by `FilePathUtilities` and
`TargetChecker`. What is gone is any remote party's ability to flip it.

A future DesktopHub update check should be written fresh: explicit, transparent,
and DesktopHub's own. Nothing from the old design was carried forward.

---

## 3. Desktop shortcut

```
DesktopShortcutCreated          = true
DesktopShortcutPath             = C:\Users\71966\Desktop\DesktopHub.lnk
DesktopShortcutName             = DesktopHub
DesktopShortcutTarget           = ...\bin\Release\net8.0-windows7.0\Desktop Frames.exe
DesktopShortcutWorkingDirectory = ...\bin\Release\net8.0-windows7.0
DesktopShortcutArguments        = (none)
DesktopShortcutIcon             = <exe>,0
ShortcutTargetExists            = true
ShortcutLaunch                  = PASS
PublicDesktopShortcutCreated    = false
```

**Conflict check first.** No `DesktopHub.lnk` existed on either desktop before
this round, so nothing was overwritten. The creation logic is idempotent: it
re-reads an existing `DesktopHub.lnk` and only updates it if `TargetPath` already
matches our exe; a same-named shortcut pointing anywhere else aborts the write
and reports a conflict.

**Launch test.** Shell-invoked the `.lnk` as a double-click would:
`DesktopHubProcessStarted=true`, PID 37368, running from the expected path.
Invoked a second time to check single-instance: still exactly **1** instance,
same PID — the existing single-instance mechanism works, no conflicting process.

**No extra shortcuts created**: no Start Menu entry, no taskbar pin, no startup
entry, no Public Desktop copy. The exe was not copied to the desktop — only a
`.lnk` was created.

### Icon — the exe carried the wrong brand

`IconLocation=<exe>,0` was the intended route, but the exe's embedded icon was
`logo2.ico`, and all four bundled logos (`logo1.ico`, `logo2.ico`, `logo3.ico`,
`logo1.png`) turned out to be the **same upstream heart-and-card mark** in
different colours. None was DesktopHub.

So a DesktopHub icon was generated locally — `Resources/DesktopHubIcon.ico`, a
multi-resolution icon (16/24/32/48/64/128/256) carrying the exact mark already
used in the About window: a rounded square in DesktopHub blue `#1F518B` with a
white "DH". Nothing was downloaded and no upstream artwork was reused.

- `csproj` `ApplicationIcon` → `Resources\DesktopHubIcon.ico`
- `logo1.png` (tray and message-box mark) regenerated with the same DH mark,
  filename unchanged so no code needed touching
- `logo1.ico`, `logo2.ico`, `logo3.ico` deleted — unreferenced upstream brand

Windows had cached the old icon against the `.lnk`, so the shortcut was recreated
and `SHChangeNotify(SHCNE_ASSOCCHANGED)` issued. Verified on screen afterwards:
the desktop icon renders as the blue DH mark.

```
ShortcutUsesDesktopHubBranding = true
```

### Auto Organize and classification

`DesktopHub.lnk` is a `.lnk`, and Core V1 never physically moves `.lnk` or
`.url` items — `DoNotPhysicallyMoveShortcut=true` holds with no change required.
No special-case classification rule was added for it; the classifier treats it
like any other shortcut, and `待整理` is an acceptable outcome the user can
reclassify by hand.

---

## 4. Build

| Gate | Value |
|---|---|
| BuildSucceeded | true |
| Errors | 0 |
| Warnings | **2124** (was 2212) |
| NewWarnings | **0** |
| NewDependencies | **0** |

The 88-warning drop is the three deleted files, not a cleanup campaign. Warning
*code* sets before and after are identical — 19 codes, no new kind introduced:

```
CS0105 CS0162 CS0168 CS0219 CS0414 CS4014 CS8600 CS8601 CS8602 CS8603
CS8604 CS8605 CS8612 CS8618 CS8619 CS8622 CS8625 MSB3284 MSB3290
```

Deployed: `Desktop Frames.dll` 6.08 → **5.94 MB**; Release directory 8.40 →
**8.16 MB**.

---

## 5. Final binary scan

Against the deployed `Desktop Frames.dll` (5.94 MB), searching both UTF-16 and
UTF-8 encodings:

| Term | Present |
|---|---|
| `paypal.com` | **false** |
| `hosted_button_id` | **false** |
| `raw.githubusercontent.com` | **false** |
| `limbo666` | **false** |
| `ForceKillSwitch` | **false** |
| `developer_repo.txt` | **false** |
| `RemoteInfoManager` / `RemoteManifest` | **false** |
| `Hand Water Pump` / `HWP_Logo` | **false** |
| `liberapay` | **false** |
| `DesktopFramesVertical` / `logo2.ico` | **false** |
| `Nikos` / `Georgousis` | **true — licence attribution only** |

`Nikos Georgousis` survives **only** inside `AboutCreditsBody`, the MIT
attribution shown in the About window's 开源许可 section, in all nine languages.
Zero occurrences in any `.cs` file. `License.md` and `Code/LICENSE` are
byte-identical to the frozen tag.

```
MITAttributionPreserved   = true
CopyrightNoticePreserved  = true
```

---

## 6. Regressions

Re-run against the final deployed build. Both are the project's own harnesses and
throw on any failed assertion.

| Test | Result |
|---|---|
| SpotSearch (`--spotsearch-regression=`) | **PASS**, exit 0 |
| Steam / Codex / 论文 / PDF / Chrome | **all PASS** |
| `UserDesktopIndexed` / `PublicDesktopIndexed` | true / true |
| `SearchAliasesPreserved` | true |
| `VisibleDuplicateResults` | false |
| `BackgroundScanWhenClosed` | false |
| Identity (`--native-layout-identity-tests`) | **PASS**, exit 0 |
| `SteamUrlClassifiedAsGames` | true |
| `SameNameFolderDoesNotInheritSteamClassification` | true |
| `ManualOverrideStillHighestPriority` | true |
| `OrdinalMappingDeterministic` | true |
| `CountMismatchFailsClosed` | true |
| Auto Organize | unchanged — `git diff` on `AutoOrganizeManager.cs` / `AutoOrganizeForm.cs` vs the frozen tag is empty |

Group zones, header drag, resize, manual position and manual size were confirmed
working by the user in their own session. **No code behind any of them was
modified this round** — the changed files are `InterCore.cs`, `FrameManager.cs`
(rename path only), `TrayManager.cs`, `RegistryHelper.cs`, the localisation
files, the csproj and icon resources.

---

## 7. Explorer restarted — reported, not attributed

**`ExplorerRestart = true.`** Explorer's start time changed from
`2026-08-27 22:55:54` to `2026-08-28 10:41:06` during this round.

The Windows event log shows what happened:

```
10:41:01  System Restore            8216  (restore point created)
10:41:02  AppXDeploymentServer      2562  (package deployment)
10:41:03  Application Error         1000
          Faulting application: Explorer.EXE 10.0.26100.9168
          Faulting module:      SystemTray.dll 2606.29002.0.0
          Path: C:\WINDOWS\SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy\SystemTray.dll
          Exception: 0xc0000409
10:41:06  Winlogon                  1002  (shell restarted)
```

The crash is inside **Microsoft's own `SystemTray.dll`**, part of the
`MicrosoftWindows.Client.Core` system app, during what the surrounding events
show to be a Windows shell package deployment — restore point, then AppX deploy,
then the fault, one second apart.

Honest position: this is a Windows shell component faulting inside its own
update, at a moment when DesktopHub was idle and no V1.1 code was executing. It
is **not attributable to these changes**, and it happened *before* the final
sweep build was even deployed (10:49). But DesktopHub does own a tray icon
registered with that exact component, so a contributing interaction cannot be
positively ruled out from the evidence available.

Recovery was clean: the desktop came back with every icon present, both rails
populated and all six group headers rendering.

```
ExplorerCrash   = true  (SystemTray.dll, Windows component, during AppX deploy)
ExplorerRestart = true
DesktopIconLoss = false
```

---

## 8. Filesystem safety

Desktop inventory compared against the snapshot taken before any of this work:

```
BeforeCount = 85     NowCount = 86
UnexpectedUserFileMoves   = 0
UnexpectedUserFileDeletes = 0
UnexpectedUserFileRenames = 0
UnexpectedUserFileCopies  = 0
```

The single addition is `DesktopHub.lnk`, this round's intended deliverable. The
`DesktopHub` repo folder's mtime moved, which is expected — builds and git ran
inside it. Nothing else on either desktop changed.
