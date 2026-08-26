# UI Phase 5.1 — Desktop icon positioning map

Date: 2026-08-26

```text
DesktopListViewHandleDiscovery=Progman/WorkerW -> SHELLDLL_DefView -> SysListView32
DesktopIconEnumerationMethod=LVM_GETITEMCOUNT + LVM_GETITEMTEXTW + LVM_GETITEMPOSITION

IconIdentityMethod=Exact unique Explorer display label mapped to a top-level user/Public Desktop filesystem path
IconNameAvailable=true
IconIndexAvailable=true (runtime only; never persisted)
IconPositionReadAPI=LVM_GETITEMPOSITION
IconPositionWriteAPI=LVM_SETITEMPOSITION32

UsesLVM_GETITEMPOSITION=true
UsesLVM_SETITEMPOSITION=false
UsesLVM_SETITEMPOSITION32=true

CrossProcessMemoryRequired=true (message buffers only)
ExplorerInjectionRequired=false

AutoArrangeDetectionAvailable=true (GWL_STYLE / LVS_AUTOARRANGE)
AlignToGridDetectionAvailable=true (LVM_GETEXTENDEDLISTVIEWSTYLE / LVS_EX_SNAPTOGRID)

ExplorerRestartRequired=false
```

## Existing implementation

Before Phase 5.1, `DesktopIconManager` could discover the Desktop `SysListView32` and show or hide the whole ListView. It did not enumerate individual icons or read/write positions. Phase 5.1 reuses its handle discovery and adds the low-level position operations in `NativeDesktopIconLayoutService`; it does not create a second handle-discovery implementation.

## Identity and fail-closed behavior

The resolver indexes only visible, top-level filesystem entries from the current user's Desktop and the Public Desktop. It accepts an Explorer label only when that label maps to exactly one canonical path. Both the filename and Explorer's common extension-hidden alias are considered.

- Unique mapping: `StablePath`; the canonical `OriginalPath` is the persistent identity.
- Duplicate display label: `AmbiguousDisplayName`; position writes and restores are skipped.
- Shell namespace item or no matching path: `UnmanagedShellItem`; position writes and restores are skipped.

The current enumeration found 85 icons before tests: 81 stable filesystem identities, 2 ambiguous labels, and 2 unmanaged shell items (`此电脑` and `回收站`). The duplicate label was `The Seance of Blake Manor` (a folder and a `.url` with the same Explorer display label).

## Cross-process boundary

ListView messages containing pointers require memory valid in Explorer's process. The service opens Explorer with the minimum VM/query rights it needs, allocates small temporary buffers with `VirtualAllocEx`, and uses `ReadProcessMemory`/`WriteProcessMemory`. No executable code is written, no remote thread is created, and there is no DLL injection, Explorer hook, shell extension, or Explorer restart. Every message uses a two-second `SendMessageTimeout` fail-safe.

## Runtime state observed

```text
DesktopListViewHandle=0x10146
AutoArrangeEnabled=false
AlignToGridEnabled=true
GridSpacingX=75
GridSpacingY=100
AutoArrangeModified=false
AlignToGridModified=false
```

With Align to Grid enabled, Explorer snapped requested X coordinates to its grid. This is expected and is why restore correctness is verified against Explorer's post-restore coordinates rather than the unobserved requested value.
