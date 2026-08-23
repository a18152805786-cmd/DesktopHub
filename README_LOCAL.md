# Desktop Hub — local baseline record

This directory is the long-term private maintenance clone of `limbo666/DesktopFramesPlus`. The upstream product source has not been renamed or refactored in this phase. The source layout remains the upstream layout under `Code/` so the first baseline stays easy to compare and revert.

## Current state

```text
Task=DesktopHubBaselineAdoption
Status=BLOCKED
Reason=Release build succeeds, but safe launch is gated because the upstream startup path writes HKCU registry state, installs global input hooks, and touches the desktop icon window.
SourceChanges=none
Origin=https://github.com/a18152805786-cmd/DesktopHub.git (PRIVATE; configured and pushed)
Upstream=https://github.com/limbo666/DesktopFramesPlus.git
Branches=main and develop created; upstream/main retained as the public remote-tracking branch
```

The executable was not launched. Therefore this baseline deliberately does not claim runtime validation or a baseline tag. `main` and `develop` currently contain the local audit commit; `main` remains the default branch.

## Actual layout

```text
DesktopHub\
├─ Code\                         upstream solution and product source
├─ Images\                       upstream image assets
├─ ngdfcs\                       upstream remote manifest files
├─ audit\                        source and environment audit
├─ build\                        build notes only; generated bin/obj stay ignored
├─ docs\                         upstream sync and Desktop Hub route
├─ README.md                     upstream README
├─ License.md                    upstream license
└─ README_LOCAL.md               this local baseline record
```

`src/` was not introduced: moving the upstream `Code/` tree would add no safety or reproducibility value and would make upstream comparison noisier.

## Safety decision

The audit found high-impact behavior in the original code. Important defaults such as `EnableAutoOrganize=false`, `DeleteOriginalShortcutsOnDrop=false`, `ExportShortcutsOnFrameDeletion=false`, `EnableContextMenu=false`, and `HideDesktopElementsOnStart=false` reduce first-run risk, but they do not remove unconditional startup side effects:

- `FrameManager.LoadAndCreateFrames` calls `RegistryHelper.PerformRenameMigration()` and `SetProgramManagementValues()`.
- `TrayManager` performs startup-migration checks and writes a `StartupMigrated` value.
- `App` initializes low-level keyboard and mouse hooks.
- `FrameManager` initializes `DesktopIconManager` and calls `ShowWindow` on the Windows desktop list view.
- `RemoteInfoManager` starts a delayed GitHub manifest request from the tray initialization path.

Because this phase forbids registry and real desktop changes, no GUI launch was attempted. See [audit/system-integration.md](audit/system-integration.md) and [audit/build-environment.md](audit/build-environment.md).

## Local files

- [audit/upstream-metadata.md](audit/upstream-metadata.md)
- [audit/project-structure.md](audit/project-structure.md)
- [audit/filesystem-behavior.md](audit/filesystem-behavior.md)
- [audit/system-integration.md](audit/system-integration.md)
- [audit/network-behavior.md](audit/network-behavior.md)
- [audit/build-environment.md](audit/build-environment.md)
- [build/BUILD_NOTES.md](build/BUILD_NOTES.md)
- [docs/UPSTREAM_SYNC.md](docs/UPSTREAM_SYNC.md)
- [docs/DESKTOP_HUB_VISION.md](docs/DESKTOP_HUB_VISION.md)
