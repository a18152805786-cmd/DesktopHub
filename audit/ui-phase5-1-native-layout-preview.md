# UI Phase 5.1 — Native layout preview

Date: 2026-08-26

## Eligibility

```text
ReferencePreviewCandidates=63
HighConfidenceCandidates=37
AI_DEV=12
RESEARCH_STUDY=7
WORK_TOOLS=3
GAMES=11
DAILY_TOOLS=4

AutoArrangeEnabled=false
IdentityStableEnoughForKnownUniquePaths=true
NativeIconCoordinatesSupported=true
FrameHitTestCompatible=false
```

## Proposed first production batch

The Phase 5 plan contains 12 high-confidence AI/development references. A production Batch 1 would be limited to at most five unique `.lnk` items whose `OriginalPath` maps to a single native Desktop icon. Folder and URL references would remain deferred until shortcut interaction passes.

No production item was selected or positioned in this phase because the mandatory hit-test gate failed.

```text
ProductionBatch1Count=0
ProductionBatch1Result=BLOCKED_FRAME_CONTENT_HIT_TEST
ShortcutsMovedFilesystem=0
FoldersMovedFilesystem=0
```

## Required next design proof

The next experiment must preserve the existing interactive header while making only the visual content surface transparent to Explorer input. The safest candidate is an architectural split between a click-through background/content surface and a small interactive header/border surface. It must be prototyped using TEST icons and must preserve non-activation, drag, resize, lock, roll-up, and context-menu behavior before any production reference is applied.

Rejected approaches:

- hiding all Desktop icons;
- copying or moving shortcuts;
- setting file hidden attributes;
- click forwarding or input simulation;
- Explorer injection or hooks;
- disabling Auto Arrange or Align to Grid;
- making the whole Frame window click-through.
