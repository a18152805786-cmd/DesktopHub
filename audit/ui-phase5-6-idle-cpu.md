# UI Phase 5.6 Idle CPU Investigation

Phase 5.5 reported `IdleCPU≈0.21%` against an earlier `≈0.01%`, and the group headers
were the obvious suspect. They are not the cause.

## Method

Two 10-minute idle runs on the same Phase 5.6 binary, on the real desktop, with nothing
else driven. CPU is measured as the delta of the process's `TotalProcessorTime` over
wall-clock time. The machine has 32 logical cores, which is why the "one core" and
"all cores" columns differ by a factor of 32 — the historical single figure was a
one-core percentage.

- **Test A** — headers running (the shipping configuration).
- **Test B** — identical binary, identical desktop, headers suppressed by hiding the
  layout plan the restore path reads, so `RestoreOnStartup` returns false and no header
  window is ever created.

## Result

| | Test A (headers) | Test B (no headers) |
|---|---:|---:|
| Wall time | 600 s | 600 s |
| CPU time consumed | 1.969 s | 2.859 s |
| Idle CPU, one core | **0.328 %** | **0.477 %** |
| Idle CPU, all 32 cores | 0.0103 % | 0.0149 % |
| Working set, start → end | 131.0 → 143.3 MB | 92.6 → 98.9 MB |
| Private bytes, start → end | 67.0 → 69.9 MB | 29.1 → 31.4 MB |
| Handles, start → end | 618 → 749 | 494 → 540 |

**The run without headers used more CPU than the run with them.** The headers cannot be
responsible for a cost that is larger when they are absent; the spread between the two
runs is background noise from the rest of the application, not a header cost.

For reference, the same measurement against the Phase 5.5 build with headers gave
0.953 s / 600 s = 0.159 % of one core, so the run-to-run spread on this machine is of the
same order as the difference between any two of these numbers.

## Code inspection

```text
DispatcherTimer in the header path        = none
Animation / Storyboard / BeginAnimation   = none
CompositionTarget.Rendering               = none
InvalidateVisual / layout invalidation    = none
Background thread or polling loop         = none
Mouse polling                             = none
Icon position polling                     = none
ContinuousHeaderRendering                 = false
```

The only `CompositionTarget` reference in `DesktopGroupHeaderWindow` is
`CompositionTarget.TransformFromDevice`, a one-off DPI matrix read taken while a drag is in
progress — not the per-frame `CompositionTarget.Rendering` event. Every brush is frozen and
the content is static, so a header renders once and is then composited by the GPU without
further CPU work. Drag detection is not a watcher: group positions are only ever examined on
an explicit refresh or a user gesture.

One header cost is real and worth recording: six WPF windows carry about 38 MB of private
bytes and roughly 40 MB of working set. That is the price of WPF top-level windows rather
than a leak — both runs grew by a comparable 2–3 MB of private bytes over ten minutes and
then flattened.

## Conclusion

```text
IdleCPUInvestigated=true
HeaderCausesContinuousRender=false
BackgroundPolling=false
IdleCPU10Min=0.328% of one core (0.0103% of total capacity)
IdleCPUWithoutHeaders=0.477% of one core (0.0149% of total capacity)
```

The residual idle cost belongs to machinery that predates this phase — the 1 Hz
`TargetChecker`, `DesktopMouseHook`, `WallpaperColorManager` and the startup manifest
fetch. Reducing it would mean touching parts this phase was told not to redesign, and at
a third of one percent of a single core it does not justify that. It is recorded here as
a candidate for a later phase rather than optimised now.
