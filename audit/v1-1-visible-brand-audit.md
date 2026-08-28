# DesktopHub V1.1 Pure — Visible Brand Audit

Full sweep for `Desktop Frames`, `Desktop Frames +`, `DesktopFramesPlus`,
`BirdyFences`, `PayPal`, `Donate`, `Donation`, `Hand Water Pump` across source,
resources, resx, csproj, and repository documents.

Every hit is placed in exactly one of three buckets:

| Bucket | Rule |
|---|---|
| **A — User-visible branding/promotion** | Cleaned in this phase |
| **B — Internal technical identity** | Deliberately retained (renaming needs a migration this phase is not doing) |
| **C — Licence / copyright / attribution** | Must be retained by the MIT terms |

---

## Bucket A — cleaned

### A1. PayPal donation, Options window

`OptionsFormManager.cs` had a live donation band pinned to the bottom of the
Options dialog: an 80 px grid row, an amber banner, `Strings.LblDonate`, and a
button opening
`https://www.paypal.com/donate/?hosted_button_id=PPLWC66UC8Q42`.

Removed: `CreateDonationSection()`, its call site, and its `RowDefinition`. The
window height dropped 850 → 770 to absorb the row, so no empty band or dead grid
row is left behind.

### A2. Remote PayPal announcement feed — the one a string sweep cannot find

`RemoteInfoManager` fetched, 25 s after every start:

```
https://raw.githubusercontent.com/limbo666/DesktopFramesPlus/refs/heads/main/ngdfcs/getversion.json
```

The manifest live at that URL contains:

```json
{ "Title": "Support Desktop Frames +",
  "Body":  "If this tool helps your workflow, please consider supporting the project...",
  "Link":  "https://www.paypal.com/donate/?hosted_button_id=PPLWC66UC8Q42",
  "MaxDisplayCount": 3 }
```

DesktopHub rendered that into its own notification window via
`NotificationFormManager.Show(msg)` — **a PayPal solicitation for the upstream
author, shown up to three times, arriving over the network.** No amount of
static string cleaning would have removed it.

The same channel also carried:

- `Meta.ForceKillSwitch` → `Application.Current.Shutdown()`, letting the remote
  manifest owner terminate the user's application;
- `Settings.EnableBackgroundLogging` → silently flips
  `SettingsManager.EnableBackgroundValidationLogging`;
- an update prompt whose fallback link pointed at the **upstream** releases page.

Changed: there is now **no built-in manifest URL**. `Initialize()` resolves a URL
only from an operator-supplied `developer_repo.txt` beside the executable; with
no such file there is no manifest, no network call, and no announcement. The
version-comparison and notification code is left intact and reachable, so an
operator can point it at their own manifest. The fallback download link now
points at the DesktopHub repository.

`ngdfcs/getversion.json` and `ngdfcs/dev.json` (copies of the upstream promo
manifest carrying the same PayPal link) were emptied to `{"Announcements": []}`.

### A3. "Desktop Frames +" wordmark in every context menu

`Resources/DesktopFramesVertical.png` is a 24×120 vertical **"Desktop Frames +"**
wordmark painted into the dark sidebar of every frame context menu
(`FrameManager.cs:1152`, `PortalFrameManager.cs:119`). It rendered on every
right-click.

Replaced with `Resources/DesktopHubVertical.png`, a DesktopHub wordmark drawn
locally at the same dimensions and colour weight. No third-party logo was
downloaded.

### A4. Legacy brand strings in localisation

`zh-Hans` was already clean. English and the seven other languages still carried
the old product name in live UI strings:

| Key | Before (EN) | After |
|---|---|---|
| `SearchTitle` | Desktop Frames Search | DesktopHub Search |
| `TrayExportDoneTitle` | Desktop Frames Plus | DesktopHub |
| `TrayExportFailedTitle` | Desktop Frames Plus - Error | DesktopHub - Error |
| `MsgHotkeysSavedRestartNeeded` | …restart Desktop Frames… | …restart DesktopHub… |
| `StartupTipsContent` | WELCOME TO DESKTOP FRAMES + | WELCOME TO DESKTOPHUB |

Applied per-language (de/es/fr/it/pl/pt/ru equivalents), 4–5 tokens each.
`AboutCreditsBody` was explicitly excluded from this rewrite — see bucket C.

### A5. Dead donation and easter-egg strings

Eleven keys were reachable from no code path at all (verified against both
`Strings.<Key>` accessors and `Get("<Key>")` literals). Removed from
`Strings.cs` and from all nine `.resx` files:

`BtnDonate` (“♥ Donate via PayPal”), `LblDonate`, `AboutDonateBig`
(“Donate 999,00 €”), `AboutPleaseDonate`, `AboutDontFeedDragons`,
`AboutThinkAgain`, `AboutWhat`, `AboutAnd`, `AboutGreatToHaveYou`,
`AboutSoundCredits` (“Using free sound files from …”), `AboutSupportDevelopment`.

### A6. Promotional and legacy-brand assets

Unreferenced by any `GetManifestResourceStream` literal or `pack://` URI:

| Asset | MB | Note |
|---|---:|---|
| `avtr.png` | 4.10 | upstream author avatar |
| `Feed.png` | 2.05 | upstream promo art |
| `dragon.png` | 1.45 | removed About easter egg |
| `logo_blue.ico` | 0.09 | legacy brand icon |
| `HWP_Logo.png` | 0.04 | **Hand Water Pump** promo logo |
| `donate.png` | 0.005 | donation button art |
| `DesktopFramesVertical.png` | 0.002 | legacy wordmark, replaced |

### A7. Repository README

`README.md` was still the upstream project's page in full: upstream `<h1>Desktop
Frames +</h1>`, upstream download/stars/forks badges, the rebranding notice, a
**Liberapay** recurring-donation widget, a **PayPal** donate button, upstream
release/manual/tips/issue links, and a starchart. Since the new About window
links here, leaving it would have simply relocated the promotion.

Rewritten as a DesktopHub README describing what DesktopHub does and its Auto
Organize safety model, with the licence and attribution preserved (bucket C).

### A8. Hand Water Pump

`Hand Water Pump` returns **zero** hits in source, resx, and csproj at the
baseline — the footer and its link were removed earlier in `3b763aa`. What
remained was the orphaned `HWP_Logo.png` still compiled into the assembly; that
is removed in A6. Nothing named Hand Water Pump remains anywhere in the tree.

### A9. Pixabay / Pexels

Zero hits in source and resx. The `AboutSoundCredits` string (“Using free sound
files from …”) was the last remnant and was orphaned; removed in A5.

---

## Bucket B — internal identity, deliberately retained

Per the phase constraint against renaming anything that would need a
configuration or registry migration:

| Location | Value | Why retained |
|---|---|---|
| `Desktop Frames.csproj` | assembly/exe name, `RootNamespace=Desktop_Frames` | rename needs a migration |
| `App.xaml.cs:21` | `Global\DesktopFramesPlus_Mutex_UniqueId_v2` | single-instance identity |
| `TrayManager.cs:34` | `APP_NAME = "Desktop Frames +"` | registry `Run` value name |
| `TrayManager.cs:655` | `Desktop Frames +.lnk` | legacy startup-shortcut migration |
| `RegistryHelper.cs:99-100` | `…\Shell\DesktopFrames` | existing context-menu keys |
| `RegistryHelper.cs:589` | `runKey.SetValue("Desktop Frames +", …)` | startup migration target |
| `RegistryHelper.cs:428,432` | registry-export filename and header | diagnostic file, not product chrome |
| `StartupTipsManager.cs:42-43` | `Desktop Frames + Startup Tips` / `使用提示` | **historical migration signatures** — must stay byte-exact or an edited Note stops matching |
| `Localization/Strings.cs:192` | `Desktop Frames.resources.dll` | satellite assembly filename |
| `Plugins/CustomTerminalPlugin.cs:34` | `DesktopFrames_TerminalHistory.txt` | existing user data file |

None of these is rendered as a product name to the user.

---

## Bucket C — licence and attribution, retained

| Location | Content |
|---|---|
| `License.md` | MIT License, `Copyright (c) 2025-2026 LIMBO666`, full permission notice, and the BirdyFences/HakanKokcu origin note — **untouched** |
| `Code/LICENSE` | untouched |
| `Strings.resx` → `AboutCreditsBody` | “DesktopHub is a modified build of Desktop Frames + (DesktopFramesPlus), originally created by HakanKokcu as BirdyFences and maintained by Nikos Georgousis. / The upstream project and its components are used under their MIT License.” |
| `README.md` → Licence and attribution | same statement, plus a link to `License.md` |

These are the only places `Desktop Frames` / `DesktopFramesPlus` / `BirdyFences`
still appear as text a user can read, and they appear there **because the licence
requires it**.

### Attribution improved, not just preserved

The seven non-English `AboutCreditsBody` values were upstream marketing copy
(“…has extended it and made it more stable and pleasant to use”) that **omitted
both** the modified-build statement and the MIT notice the EN and zh-Hans
versions carried. All seven were rewritten to the neutral attribution form, so
the licence notice is now stated in all nine languages rather than two.

---

## Result

| Gate | Value |
|---|---|
| VisibleProductName | DesktopHub |
| PayPalRemoved | true (static UI **and** remote feed) |
| DonationBannerRemoved | true |
| DonationLinksRemoved | true |
| HandWaterPumpRemoved | true |
| LegacyPromotionRemoved | true |
| LegacyMainBrandingRemoved | true |
| OldLogoRemoved | true (replaced with DesktopHub wordmark) |
| DesktopHubGitHubLinkCorrect | true — `https://github.com/a18152805786-cmd/DesktopHub` |
| MITAttributionPreserved | true |
| CopyrightNoticePreserved | true |

## Pre-existing observation (not changed)

`pack://application:,,,/Resources/steam-White.png` is referenced in source, but
`steam-White.png` is registered only as an `EmbeddedResource`, never as a WPF
`Resource`. It is therefore absent from `Desktop Frames.g.resources` — **in the
frozen baseline as well as after this phase**. Verified by diffing the resource
tables of both built assemblies. Pre-existing and out of scope; recorded here so
it is not mistaken for a V1.1 regression.
