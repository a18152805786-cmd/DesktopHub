# Upstream metadata

Audit date: 2026-08-23 (Asia/Taipei)

| Field | Result |
|---|---|
| Repository | `https://github.com/limbo666/DesktopFramesPlus` exists and is public |
| UpstreamURL | `https://github.com/limbo666/DesktopFramesPlus.git` |
| DefaultBranch | `main` |
| UpstreamHEAD | `b9eb0471264538448549c49f487c96b9c53fb03a` |
| LatestTag | `2.7.7.294` |
| LatestRelease | `2.7.7.294`, published 2026-06-08; non-draft, non-prerelease |
| License | MIT in `License.md` and `Code/LICENSE`; GitHub API reported SPDX `NOASSERTION`, so the checked-in license file is the authority |
| Current source version | `2.7.8.358` in the `.csproj`; this is ahead of the latest published tag and HEAD is not itself tagged |
| README platform statement | Windows 10/11, portable distribution |

The clone preserved the full Git history. The local `main` branch initially pointed at the upstream HEAD and has `upstream/main` as its remote-tracking branch.

The repository page identifies the project as an open-source Windows desktop icon-management utility and describes the portable release flow. The local source audit below is based on the actual files, not only that description:

- GitHub repository: <https://github.com/limbo666/DesktopFramesPlus>
- Latest release: <https://github.com/limbo666/DesktopFramesPlus/releases/tag/2.7.7.294>
- License file: <https://github.com/limbo666/DesktopFramesPlus/blob/main/License.md>

## Packaging and distribution

No installer project, updater project, MSIX/AppInstaller manifest, WiX/Inno/NSIS file, or release-packaging project was present in the clone. The README describes extracting a release package and running `Desktop Frames.exe`; the source tree does not contain the release archive itself.

There is no test project. `.github/` contains issue-template files only; no build or release workflow was found.

## Third-party dependencies

Top-level NuGet package references in `Code/Desktop Frames/Desktop Frames.csproj`:

- `NAudio` `2.2.1`
- `Newtonsoft.Json` `13.0.1`

The project also declares COM references for `IWshRuntimeLibrary` and a `System_Drawing` type library. No new dependencies were added during this baseline task. The resolved transitive package list is recorded in [build/BUILD_NOTES.md](../build/BUILD_NOTES.md).
