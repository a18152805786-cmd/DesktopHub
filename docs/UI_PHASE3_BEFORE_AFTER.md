# UI Phase 3 — Before / After

## Before

- Heavy gray surface driven by the global tint value.
- 6-pixel or square corners with optional 2-pixel custom border.
- Long default title: `New Frame - Drop your shortcuts here`.
- Centered 12-pixel title.
- Heart/menu and lock controls permanently visible at a reduced opacity.
- Separate per-control hover timers and opacity animations.
- Rolled-up height duplicated as the literal `28` in several paths.

## After

- Light, theme-aware translucent grouping surface.
- 10-DIP internal rounded corner and one-DIP low-contrast border.
- Compact 32-DIP header.
- Semi-bold 13.5-DIP title with left alignment and safe ellipsis.
- Persisted default title displayed compactly without rewriting `frames.json`.
- Menu and lock controls hidden and non-hit-testable while idle, visible on whole-Frame hover.
- Shared 140 ms opacity animation; no blur, zoom, or layout animation.
- Shared rolled-up height from the ResourceDictionary.

## Behavior intentionally unchanged

- Non-activating, no-taskbar Window style.
- Header drag and snapping.
- Resize and locked-state resize prevention.
- Roll up/down, Auto Roll, and persistence.
- Context menus, item launch, drag/drop, icon grid, and Portal behavior.
- Profiles, Frame membership, Startup Tips dismissal, and Auto Organize.

