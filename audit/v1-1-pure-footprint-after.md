# DesktopHub V1.1 Pure — Footprint After

- BaseTag: `desktop-hub-core-v1` (`98184ff`)
- Measured commit: `bc44017` on `cleanup/v1.1-pure`
- Date: 2026-08-28
- Machine: Windows 11 Pro 10.0.26200, 32 logical cores
- CurrentPublishMode: **framework-dependent** (unchanged)

## 1. Disk — real reduction

Two measurements are reported because the deployed folder also contains live
user data the application writes into itself (`Profiles/`,
`ProfileOptions.json`, `Desktop_Frames.log`, `audit/`). That data was never
touched; the clean-build comparison isolates what actually changed.

### Deployed Release directory (as the user runs it)

| Metric | Before | After | Delta |
|---|---:|---:|---:|
| ReleaseDirectorySizeMB | **17.45** | **8.40** | **−9.05** |
| ReleaseFileCount | 34 | 34 | 0 |

**ReleaseSizeReductionMB = 9.05  →  ReleaseSizeReductionPercent = 51.86 %**

### Clean build output (matched worktree builds, no user data)

| Metric | Before | After | Delta |
|---|---:|---:|---:|
| BuildOutputMB | **17.283** | **8.228** | **−9.055** |
| File count | 21 | 21 | 0 |
| `Desktop Frames.dll` | **15.124** | **6.076** | **−9.048 (−59.8 %)** |
| `Desktop Frames.exe` | 0.255 | 0.255 | 0 |
| `Desktop Frames.pdb` | 0.350 | 0.350 | 0 |

**Percent reduction on matched clean builds = 52.39 %**

The reduction lands almost entirely in the main assembly and matches the
9.04 MB of dead embedded resources identified in the baseline audit, to within
8 KB — which is the new DesktopHub wordmark (612 B) replacing the old one
(1935 B) plus resource-table bookkeeping.

### Satellite assemblies

Removing eleven unreachable strings shrank every language satellite slightly:

| Culture | Before (B) | After (B) | Delta |
|---|---:|---:|---:|
| de | 45,568 | 44,544 | −2.2 % |
| es | 45,056 | 44,544 | −1.1 % |
| fr | 46,080 | 45,056 | −2.2 % |
| it | 45,568 | 44,544 | −2.2 % |
| pl | 45,056 | 44,032 | −2.3 % |
| pt | 45,056 | 44,544 | −1.1 % |
| ru | 55,808 | 54,784 | −1.8 % |
| zh-Hans | 49,152 | 48,640 | −1.0 % |

### What was removed

Ten assets, every one verified unreachable from any
`GetManifestResourceStream` literal or `pack://` URI:

| Asset | MB | Category |
|---|---:|---|
| `avtr.png` | 4.10 | upstream author avatar |
| `Feed.png` | 2.05 | upstream promo art |
| `dragon.png` | 1.45 | removed easter egg |
| `universfield-bright-notification-352449.wav` | 0.56 | unmapped sound |
| `koiroylers-slow-ding-354125.wav` | 0.52 | unmapped sound |
| `universfield-new-notification-062-494544.wav` | 0.19 | unmapped sound |
| `logo_blue.ico` | 0.09 | legacy brand icon |
| `HWP_Logo.png` | 0.04 | Hand Water Pump promo logo |
| `ding.wav` | 0.02 | unmapped sound |
| `donate.png` | 0.005 | donation button art |
| **Total** | **9.04** | |

Plus `DesktopFramesVertical.png` (1935 B) replaced by `DesktopHubVertical.png`
(612 B).

Resource tables of the two built assemblies were diffed directly: the only
differences are these ten manifest streams removed, `donate.png` removed from
`g.resources`, and the wordmark swap. Nothing else moved.

## 2. Runtime — measured under matched conditions

The baseline figure first captured (WorkingSet 201 MB / Private 140 MB) came
from a process with ~11 minutes of uptime that the user had been actively
driving. It is **not** comparable to a fresh start, and is not used here.

Both builds were therefore re-measured under identical conditions: deployed to
the same directory against the same profile, launched fresh, left completely
untouched, then sampled 30 times at 10 s.

Protocol for both: stop the app → deploy that build's output over the same
folder (user data untouched) → launch → wait 20 s → sample `WorkingSet64` and
`PrivateMemorySize64` 30 times at 10 s, and total CPU time across the window.

## 3. Matched before/after

| Metric | BEFORE (`98184ff`) | AFTER (`bc44017`) | Delta |
|---|---:|---:|---:|
| **WorkingSetIdleMB** (avg) | **148.59** | **138.54** | **−10.05  (−6.8 %)** |
| WorkingSetIdleMB (min–max) | 147.96 – 149.27 | 137.87 – 139.27 | ranges do not overlap |
| **PrivateMemoryIdleMB** (avg) | **75.82** | **73.84** | **−1.98  (−2.6 %)** |
| PrivateMemoryIdleMB (min–max) | 75.32 – 76.38 | 72.96 – 74.40 | ranges do not overlap |
| IdleCPUPercent (32 cores) | 0.0085 | 0.0060 | −0.0025 pp |
| IdleCPUPercent (1-core equiv.) | 0.271 | 0.192 | −0.079 pp |
| CPU seconds per 5 min | 0.81 | 0.58 | −0.23 s |
| HandleCount | 745 | 634 | −111 |
| ThreadCount | 18 | 18 | 0 |

### Reading these numbers honestly

**WorkingSet fell 10.05 MB (−6.8 %) and this is a real measurement, not noise** —
the two sample ranges do not overlap at all (before 147.96–149.27, after
137.87–139.27 across 30 samples each). The most plausible mechanism is simply
that the assembly is 9 MB smaller: a managed assembly is memory-mapped, so a
smaller image means less resident image in the working set.

**Private memory barely moved: −1.98 MB (−2.6 %).** That is consistent with the
same explanation — the deleted resources were never *loaded* into the private
heap, they only sat in the mapped image. This is the number that would have to
move for anyone to claim a real reduction in what the application allocates, and
it essentially did not.

**Idle CPU was already at the noise floor and remains there.** 0.81 → 0.58 CPU
seconds over five minutes, on a 32-core machine. Part of that difference is the
upstream manifest fetch no longer happening. It is not a meaningful improvement
and is not claimed as one.

**Handle count fell by 111.** Not investigated in depth; the removed `HttpClient`
and its TLS machinery plausibly account for some of it. Recorded, not claimed.

### Verdict

```
DiskFootprintImproved             = true      (-9.05 MB, -51.9 % deployed)
RuntimeWorkingSetImproved         = true      (-10.05 MB, -6.8 %, measured, ranges disjoint)
RuntimeMemoryMeaningfullyImproved = false     (private memory -2.6 %, effectively flat)
IdleCPUImproved                   = NEGLIGIBLE (both at noise floor)
RuntimeMemoryImprovement          = MODEST
```

This phase was a branding cleanup and a dead-resource removal. It did not
restructure allocation, window lifetime, caching, or timers — no evidence
justified doing so, and the phase's priority order put Core V1 stability first.
The disk result is large and the working-set result is a real but modest
side-effect of it. Nobody should describe this as a memory optimisation.

## 4. Decisions taken and not taken

| Option | Decision | Reason |
|---|---|---|
| `PublishTrimmed` | **not enabled** | WPF depends on XAML, `ResourceManager`, and reflection; aggressive trimming needs its own compatibility campaign. Out of scope. |
| `TrimMode=full` | **not enabled** | same |
| NativeAOT | **not attempted** | out of scope |
| `PublishReadyToRun` | **not enabled** | it *increases* deployment size, which works against this phase's goal |
| Third-party packer (UPX etc.) | **not used** | the goal is a clean, stable build, not an opaque compressed executable |
| Self-contained → framework-dependent | **no change needed** | already framework-dependent; `runtimeconfig.json` declares `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App` as frameworks and no runtime pack ships. There is no self-contained payload to convert. |
| PDB in deploy package | **left in place** | 0.350 MB. This is the developer Release build and keeping symbols is correct here. A user-facing deploy package could omit it, taking the build output to 7.878 MB — but that is a packaging decision, not a build change, and no packaging step exists yet. |
| Window lazy-creation | **not changed** | About, Options, Customize and SpotSearch are already constructed on demand inside their `Show…` entry points and released on close. There was no eagerly-created window pool to make lazy. |
| Timer removal | **not changed** | all timers are one-shot or debounce, created in response to an event. No resident poller was found to remove. |
| Icon cache limits | **not changed** | no evidence of unbounded growth; idle memory is flat across the sample window. |
| New background polling | **none added** | explicitly forbidden, and nothing here needed it |
| SpotSearch background index | **none added** | `BackgroundScanWhenClosed=false` still holds |

## 5. One behavioural reduction worth noting

Disabling the built-in upstream manifest fetch removes, per application start:

- one outbound HTTPS request to a third-party host, 25 s after launch;
- the `HttpClient` and JSON deserialisation that serviced it;
- the notification window it could raise.

This was done for branding and control reasons rather than footprint, but it
does mean the idle process no longer performs any network I/O of its own.
