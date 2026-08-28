<h1 align="center">DesktopHub</h1>
<p align="center"><i>Sort your desktop, keep it tidy, find anything fast.</i></p>

DesktopHub is a Windows desktop organiser. It groups your desktop icons
automatically, keeps the layout tidy, and lets you find files and apps instantly
— while your icons stay drawn by Windows Explorer itself, not repainted by a
third-party shell.

---

## What it does

- **Auto Organize** — sorts ordinary desktop files into folders by type
  (documents, slides, PDFs, spreadsheets, images, archives, installers).
  Folders, `.lnk` and `.url` items are never physically moved; they are managed
  visually instead.
- **Native desktop layout** — DesktopHub does not redraw your desktop icons.
  Windows Explorer still owns them; DesktopHub reads and arranges their
  positions through the desktop list view. No Explorer injection.
- **Interactive group zones** — named groups with a header and bounds. Drag a
  header to move a whole group, resize a zone on the icon grid, lock a group, or
  reset it back to automatic position and size.
- **Manual overrides that stick** — a position, size, or group assignment you
  set by hand persists and outranks the automatic classifier. A refresh will not
  overwrite your choice.
- **SpotSearch** — press <kbd>Ctrl</kbd>+<kbd>`</kbd> for instant search across
  your User Desktop, the Public Desktop, managed folders, and frame items. No
  background indexing service runs while the search is closed.

## Safety model

DesktopHub is deliberately conservative with your files:

- Auto Organize is **off until you enable it**.
- It never deletes and never overwrites — every production rule is
  `Collision=Skip`, and moves use `File.Move(..., overwrite: false)`.
- It only touches ordinary files in the Desktop root; it does not recurse.
- `desktop.ini`, hidden and system files, temporary downloads, and Office lock
  files are skipped.
- Files are stability- and lock-checked before a move, and it fails closed when
  stability cannot be confirmed.

Group zones, resizing, header drags, and manual classification change only
metadata or Explorer icon coordinates — never your files.

## Requirements

- Windows 10 or Windows 11
- .NET 8 Desktop Runtime (the build is framework-dependent)
- A user-writable install location

## Building

Open `Code/Desktop Frames.sln` in Visual Studio 2022, or build from the command
line:

```bash
msbuild "Code/Desktop Frames.sln" -p:Configuration=Release -p:Platform="Any CPU"
```

The executable is still named `Desktop Frames.exe` for now. That internal name,
along with the assembly name, root namespace, registry keys, and configuration
paths, is intentionally unchanged so existing installations keep their settings.

## Documentation

- [Core V1 baseline](docs/DESKTOPHUB_CORE_V1.md) — the frozen feature set and its
  acceptance record
- [Version history](VersionHistory.md)
- [Tips](TIPS.md)

## Licence and attribution

DesktopHub is released under the [MIT License](License.md).

DesktopHub is a modified build of Desktop Frames + (DesktopFramesPlus),
originally created by HakanKokcu as BirdyFences and maintained by Nikos
Georgousis. The upstream project and its components are used under their MIT
License, and the original copyright and permission notices are retained in
[License.md](License.md).
