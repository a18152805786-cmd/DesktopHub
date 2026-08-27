# UI Phase 5.3 — Native Desktop Layout First

## Scope

Phase 5.3 groups real Windows Desktop icons by changing only their native
`SysListView32` coordinates through the existing `LVM_SETITEMPOSITION32`
implementation. It never creates a Frame, moves a Desktop object, or starts a
watcher. `NativeDesktopLayoutAutoApply` is permanently `false` for this phase.

## Components

```text
NativeDesktopIconLayoutService       existing native coordinate / snapshot layer
NativeDesktopIconIdentityResolver    existing stable path resolver
ReferenceGroupingClassifier          single source of classification rules
NativeDesktopLayoutPlanner           pure Desktop icon -> group -> coordinate plan
NativeDesktopLayoutApplicationService explicit preview / apply / restore API
```

`DesktopLayoutPlan`, `DesktopLayoutGroup`, and `DesktopLayoutItem` are
serializable audit models. A successful apply also records `LayoutGroupId` in
profile-local metadata. `ManualLayoutGroupId` is authoritative; a legacy
`ManualFrameId` without an unambiguous group mapping is deliberately left in
place.

## Manual commands

All commands require an explicit process invocation and exit before normal
DesktopHub startup. They can therefore run while the normal app instance is
open and do not start its Frame, tray, polling, or hook subsystems.

```text
--native-layout-audit=<audit-directory> --native-layout-preview
--native-layout-audit=<audit-directory> --native-layout-apply=OFFICE_DOCUMENTS
--native-layout-audit=<audit-directory> --native-layout-apply=AI_DEV
--native-layout-audit=<audit-directory> --native-layout-apply=RESEARCH_STUDY
--native-layout-audit=<audit-directory> --native-layout-refresh
--native-layout-audit=<audit-directory> --native-layout-restore
```

`Refresh` is also manual: it regenerates the plan and applies Batch A, B, then
C only. Daily tools and games remain separate opt-in batches.

## Safety gates

- Auto Arrange is read before every plan/apply/restore and immediately aborts
  coordinate writes when enabled. The setting is never changed.
- Before the first apply, a Phase 5.3 create-new baseline snapshot covers every
  stable icon. A read-only `CanResolve/Missing/Ambiguous` preflight must pass.
- Only stable-identity `HIGH` items are candidates. Shell namespace items,
  MEDIUM/LOW items, ambiguous items, and unsupported files remain occupied
  obstacles.
- Every target is grid-aligned, unique, primary-monitor bounded, and vacant at
  apply time. Failure or post-apply drift restores the immediate batch snapshot.
- Planning releases only the current group's own eligible source slots. This
  makes repeated preview/refresh calls grid-idempotent while every other icon
  remains an obstacle. An already-applied batch succeeds as a no-op.
- The folder-entry fingerprint before/after each batch detects unintended
  Desktop filesystem changes; the coordinate service itself has no filesystem
  mutation code.
- Native layout commands return exit code 0 only on success; a rejected safety
  gate or failed batch returns a non-zero process exit code.

## Audits

The command writes `ui-phase5-3-native-layout-preview.md`,
`ui-phase5-3-layout-plan.md`, a JSON plan, a create-new baseline, per-batch
CSV diffs, result records, and `ui-phase5-3-restore-results.md`. Restore is
the explicit **Restore Original Desktop Layout** API and verifies the same
identity preflight before calling the existing restore implementation.
