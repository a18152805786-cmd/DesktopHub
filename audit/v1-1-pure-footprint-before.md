# DesktopHub V1.1 Pure — Footprint Baseline (BEFORE)

Measured on the frozen baseline, before any V1.1 change.

- BaseTag: `desktop-hub-core-v1`
- BaseCommit: `98184ff8ce46aa61cde4ce09567c5ef35b997633`
- Branch: `cleanup/v1.1-pure`
- Measured: 2026-08-28
- Machine: Windows 11 Pro 10.0.26200, 32 logical cores

## 1. Release directory

Path: `Code/Desktop Frames/bin/Release/net8.0-windows7.0`

| Metric | Value |
|---|---|
| ReleaseDirectorySizeMB | **17.45** |
| ReleaseFileCount | 34 |
| ExeSizeMB | 0.255 |
| MainAssemblySizeMB (`Desktop Frames.dll`) | **15.124** |

The directory total includes live runtime user data that the application writes
into its own folder (`ProfileOptions.json`, `Profiles/`, `Desktop_Frames.log`,
`audit/`). Those are **not** build output and are never touched by this phase.
Build output alone is therefore tracked separately:

| Metric | Value |
|---|---|
| BuildOutputSizeMB (dll + exe + pdb + deps/runtimeconfig) | **17.14** |
| UserDataInReleaseDirMB (not build output, untouched) | 0.31 |

## 2. Top 20 largest files

| # | File | MB | Purpose | RequiredAtRuntime | CandidateForRemoval |
|---|---|---|---|---|---|
| 1 | `Desktop Frames.dll` | 15.124 | Main assembly + embedded resources | Yes | Shrink (dead embedded assets) |
| 2 | `Newtonsoft.Json.dll` | 0.663 | JSON persistence (frames, profiles, layout) | Yes | No |
| 3 | `Desktop Frames.pdb` | 0.350 | Debug symbols | No (debug only) | Deploy package only |
| 4 | `Desktop Frames.exe` | 0.255 | Apphost launcher | Yes | No |
| 5 | `NAudio.Core.dll` | 0.179 | Audio stack (notification sounds, VU plugin) | Yes | No |
| 6 | `NAudio.Wasapi.dll` | 0.171 | Audio stack | Yes | No |
| 7 | `audit/ui-phase5-5-layout-plan.json` | 0.094 | App-written audit output (user data) | No | Not build output |
| 8 | `NAudio.WinMM.dll` | 0.055 | Audio stack | Yes | No |
| 9 | `ru/Desktop Frames.resources.dll` | 0.053 | Russian satellite | Yes | Shrink (dead strings) |
| 10 | `zh-Hans/Desktop Frames.resources.dll` | 0.047 | Chinese satellite | Yes | Shrink (dead strings) |
| 11 | `NAudio.Midi.dll` | 0.044 | Audio stack | Yes | No |
| 12 | `fr/Desktop Frames.resources.dll` | 0.044 | French satellite | Yes | Shrink |
| 13 | `it/Desktop Frames.resources.dll` | 0.043 | Italian satellite | Yes | Shrink |
| 14 | `de/Desktop Frames.resources.dll` | 0.043 | German satellite | Yes | Shrink |
| 15 | `pl/Desktop Frames.resources.dll` | 0.043 | Polish satellite | Yes | Shrink |
| 16 | `es/Desktop Frames.resources.dll` | 0.043 | Spanish satellite | Yes | Shrink |
| 17 | `pt/Desktop Frames.resources.dll` | 0.043 | Portuguese satellite | Yes | Shrink |
| 18 | `NAudio.WinForms.dll` | 0.033 | Audio stack | Yes | No |
| 19 | `NAudio.Asio.dll` | 0.033 | Audio stack | Yes | No |
| 20 | `audit/ui-phase5-5-desktop-before.json` | 0.022 | App-written audit output (user data) | No | Not build output |

### File-type breakdown

| Extension | Count | MB |
|---|---|---|
| `.dll` | 17 | 16.668 |
| `.pdb` | 1 | 0.350 |
| `.exe` | 1 | 0.255 |
| `.json` | 10 | 0.145 |
| `.svg` | 1 | 0.013 |
| `.md` | 2 | 0.010 |
| `.csv` | 1 | 0.009 |
| `.log` | 1 | 0.003 |

**Finding:** 86.7% of the whole release directory is a single 15.1 MB assembly.
The C# in that assembly is nowhere near 15 MB — the weight is embedded binary
resources compiled into the DLL.

## 3. Embedded resource audit (the actual cause)

Every `EmbeddedResource` / `Resource` in `Desktop Frames.csproj` was cross-checked
against every `GetManifestResourceStream(...)` literal and every
`pack://application:,,,/Resources/...` URI in the source tree.

The `NotificationSound` enum (`SettingsManager.cs:15`) has exactly 7 members and
`MessageBoxesManager.PlayDingSound` maps all 7 to a wav; `InterCore.cs:951` uses
one more. Eight wavs are reachable; the rest are not.

### Referenced (keep)

`36505577-smooth-simple-notification-274738.wav`, `dragon-studio-new-notification-444814.wav`,
`notification_message-best-notification-1-286672.wav`, `notification_message-nada-1-326000.wav`,
`soundshelfstudio-ui-notification-pop-513564.wav`, `sweep-sound-effect-240243.wav`,
`ui-8-warning-sound-effect-336254.wav`, `universfield-soft-notice-146623.wav`,
`logo1.png`, `logo2.ico`, `steam-White.png`, `spotify-White.png`, `portal.png`,
`placeholder.png`, `dot.png`, `empty.png`, `folder*.png`, `file*.png`, `link-White.png`.

### Unreferenced (zero occurrences anywhere in source)

| Asset | Bytes | MB | Why it is dead |
|---|---:|---:|---|
| `Resources/avtr.png` | 4,303,179 | 4.10 | Upstream author avatar, About easter egg removed in `3b763aa` |
| `Resources/Feed.png` | 2,149,664 | 2.05 | Upstream promo art, no reference |
| `Resources/dragon.png` | 1,520,133 | 1.45 | Dragon easter egg art, removed |
| `Resources/universfield-bright-notification-352449.wav` | 589,902 | 0.56 | Not mapped by any `NotificationSound` |
| `Resources/koiroylers-slow-ding-354125.wav` | 543,822 | 0.52 | Not mapped by any `NotificationSound` |
| `Resources/universfield-new-notification-062-494544.wav` | 202,830 | 0.19 | Not mapped by any `NotificationSound` |
| `Resources/logo_blue.ico` | 97,785 | 0.09 | Legacy brand icon, no reference |
| `Resources/HWP_Logo.png` | 43,041 | 0.04 | **Hand Water Pump promo logo** |
| `Resources/ding.wav` | 22,085 | 0.02 | Superseded, only a stale doc comment mentions it |
| `Resources/donate.png` | 4,944 | 0.005 | **Donation button art** |
| **Total** | **9,477,385** | **9.04** | |

`Resources/DesktopFramesVertical.png` is *referenced* — but it is a vertical
**"Desktop Frames +"** wordmark painted into the sidebar of every frame context
menu (`FrameManager.cs:1152`, `PortalFrameManager.cs:119`). That is live legacy
branding, handled as a branding item rather than a footprint item.

## 4. Runtime baseline

Process `Desktop Frames` PID 10560, started 09:40:17, built from the frozen tag.

| Metric | Value |
|---|---|
| WorkingSetAfterStartupMB | 201.31 |
| PrivateMemoryAfterStartupMB | 139.96 |
| HandleCount | 812 |
| ThreadCount | 18 |
| CPUCores | 32 |

Idle numbers from the matched 300 s sample are in section 6.

## 5. Architecture observations

- **CurrentPublishMode: framework-dependent.** `Desktop Frames.runtimeconfig.json`
  declares `Microsoft.NETCore.App` 8.0.0 and `Microsoft.WindowsDesktop.App` 8.0.0
  as *frameworks*, and no runtime pack is present in the output directory. The
  build is already in the smaller of the two modes; there is no self-contained
  payload to trim. No change proposed.
- **No `PublishTrimmed`, no `PublishReadyToRun`, no NativeAOT** in the csproj.
  All three stay off, per phase constraints.
- **BackgroundPollingDetected: none.** All timer constructions across the tree are
  one-shot or debounce timers created in response to an event
  (`NativeFrameSurfaceManager` 200 ms debounce, `App.xaml.cs` 800 ms/10 s
  one-shots that `Stop()` in their first tick, launch/animation effects). There is
  no resident polling loop for the desktop, mouse, or icons.
- **DispatcherTimer inventory (declaration sites):** InterCore 16, LaunchEffects 7,
  FrameManager 8, ProfileManager 2, PortalFrameManager 2, SystemIpPlugin 2,
  NativeFrameSurfaceManager 2, App 2, plus 1 each in TargetChecker, SmartToast,
  VUMeter, SystemQueueSaturation, SystemPerformance, PictureSlideshow, Calculator,
  NotificationForm, NoteFrame, NonActivatingWindow, MessageBoxes, BackupManager,
  AutomationManager. None is an always-on poller.
- **SpotSearch** performs no permanent scan while closed. Unchanged.
- **Window lifetime:** About, Options, Customize and SpotSearch are all
  constructed on demand inside their `Show...` entry points and are not held
  after close. There is no pre-created hidden window pool to make lazy.

## 6. Idle measurement (5-minute window)

Sampled live with the app resident and untouched: 30 samples at 10 s, PID 10560.

| Metric | Value |
|---|---|
| SampleWindowSec | 300 |
| CPUSecondsConsumed | 1.03 |
| IdleCPUPercent (all 32 cores) | 0.0107 |
| IdleCPUPercent (single-core equivalent) | 0.343 |
| WorkingSetIdleMB min / avg / max | 200.64 / 200.93 / 200.98 |
| PrivateMemoryIdleMB min / avg / max | 139.87 / 139.92 / 139.95 |
| HandleCount | 821 |
| ThreadCount | 17 |

Idle CPU is at the noise floor (1.03 CPU-seconds over 5 minutes) and idle memory
is flat to within 0.34 MB across the window. There is no leak, drift, or resident
polling cost to chase on this baseline — which sets the honest expectation that
this phase cannot deliver a meaningful *runtime* memory win.

> **These runtime figures are NOT the before/after baseline.** They were taken
> from a process with ~11 minutes of uptime that the user had been actively
> driving (a group drag is in its log), so they are not comparable to a fresh
> start. The before/after pair in `v1-1-pure-footprint-after.md` comes from a
> separate matched run: both builds deployed to the same directory against the
> same profile, launched fresh, left untouched, sampled identically. Use that
> pair. These numbers are kept only as a record of the baseline as first found.
