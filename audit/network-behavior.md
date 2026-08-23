# Network behavior audit

The source search found one background HTTP client path and several user-triggered external URI launches. No local file was uploaded by this task or by any audited code path.

## Required classification

| Category | Finding | Evidence / behavior |
|---|---|---|
| `AUTO_UPDATE` | Not found | No updater package, installer, download-to-disk, replacement, or self-update operation was found. |
| `UPDATE_CHECK` | Present | `RemoteInfoManager` starts from `TrayManager` and performs a delayed `HttpClient.GetStringAsync()` request. |
| `TELEMETRY` | No outbound telemetry found; review marker remains | `RegistryHelper` stores `DVC` and `UnitID` values under `ProgramManagement`, but no code path was found sending them over HTTP. |
| `ANALYTICS` | Not found | No analytics SDK, event endpoint, or analytics request was found. |
| `CRASH_REPORT` | Not found | No crash-reporting SDK or crash upload path was found. |
| `OTHER_NETWORK` | Present, mostly user initiated | Shell launches for GitHub/PayPal links and user-saved web/Steam/Spotify URI shortcuts. |

## Remote manifest behavior

`RemoteInfoManager` uses this default URL:

```text
https://raw.githubusercontent.com/limbo666/DesktopFramesPlus/refs/heads/main/ngdfcs/getversion.json
```

It waits 25 seconds in a normal build (3 seconds when a local `developer_repo.txt` exists), adds a timestamp cache-buster query parameter, and sends a `DesktopFrames/2.7` user-agent. If `developer_repo.txt` is present, its validated HTTP(S) content becomes the target URL.

The manifest can contain:

- a newer version and a user-facing download link;
- announcements and links;
- a `ForceKillSwitch` that shuts down the application;
- a setting that enables background validation logging.

This is a remote control/announcement path rather than an automatic updater. Announcement display counts and dismissals are written to the registry. No remote check was triggered because the executable was not launched.

The checked-in manifest currently contains a support announcement with a PayPal link. This is an external link opened by the UI if the user clicks it, not an automatic upload.

## Network risk conclusion

`NetworkBehaviorRisk=MEDIUM`: there is no evidence of telemetry, analytics, crash reporting, or automatic update installation, but the delayed remote manifest check is always initialized through the tray path and can influence shutdown, logging, and UI announcements. A private Desktop Hub fork should later make update-check consent and the manifest endpoint explicit before changing product identity or adding features.
