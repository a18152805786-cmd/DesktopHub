using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Deterministic check that resizing a zone never costs a pinned group its
    /// slot. Every desktop here is synthetic and is planned through the planner's
    /// replay entry point, and DesktopGroupZoneService.Resize is handed its zones,
    /// grid and planner directly, so neither Explorer nor the zone store is read
    /// and nothing on the real desktop moves. No test framework dependency.
    /// </summary>
    internal static class NativeDesktopGroupResizeTestHarness
    {
        private sealed record Display(string Key, int Width, int Height, int Dpi, int CellX, int CellY)
        {
            public NativeDesktopSafeGrid Grid => NativeDesktopIconLayoutService.SafeGridFor(Width, Height, CellX, CellY);
            public Rectangle WorkArea => new(0, 0, Width, Height);
        }

        /// <summary>
        /// What a resize meets: the plan on screen, the zones in the store, and the
        /// desktop the planner will see, which need not be the one on screen.
        /// </summary>
        private sealed record Scenario(string Name, Display Display, DesktopLayoutPlan OnScreen,
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> Zones,
            (NativeDesktopLayoutGroupId Group, int Count)[] Members)
        {
            public DesktopLayoutPlan PlanWith(IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones) =>
                Plan(Display, zones, Members);

            /// <summary>The production decision, with only its environment replaced.</summary>
            public DesktopGroupResizeResult ResizeTo(NativeDesktopLayoutGroupId group, int columns, int rows) =>
                DesktopGroupZoneService.Resize(OnScreen, group, columns, rows, () => Zones, () => Display.Grid, PlanWith);
        }

        /// <summary>Every size one scenario allows, checked against the planner.</summary>
        private sealed class SweepTally
        {
            public string Scenario { get; init; } = string.Empty;
            /// <summary>Groups pinned on screen that the next refresh drops even without a resize.</summary>
            public List<DesktopLayoutGroup> StalePins { get; init; } = new();
            /// <summary>Sizes that pass Resize's own checks: lock, minimum, capacity and safe grid.</summary>
            public int Sizes { get; set; }
            /// <summary>What Resize accepted before it looked at pins: the layout plans and passes ValidateDualRailPlan.</summary>
            public int Valid { get; set; }
            /// <summary>The reported defect: a valid layout in which the resize costs a pinned group its slot.</summary>
            public int ValidUnpins { get; set; }
            public int Accepted { get; set; }
            /// <summary>Accepted although the resize costs a pinned group its slot. Must stay zero.</summary>
            public int AcceptedUnpins { get; set; }
            /// <summary>Accepted, and a pin on screen goes, but one the next refresh drops with or without the resize.</summary>
            public int AcceptedStalePinOnly { get; set; }
            public int WrongVerdicts { get; set; }
            public List<string> Examples { get; } = new();
        }

        private static readonly Display Laptop = new("2560x1600@144", 2560, 1600, 144, 115, 154);
        private static readonly Display Monitor = new("2560x1440@96", 2560, 1440, 96, 75, 100);

        /// <summary>Managed icons per group on the real desktop on 2026-09-25.</summary>
        private static readonly (NativeDesktopLayoutGroupId Group, int Count)[] RealMembers =
        {
            (NativeDesktopLayoutGroupId.RESEARCH_STUDY, 17),
            (NativeDesktopLayoutGroupId.AI_DEV, 10),
            (NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, 11),
            (NativeDesktopLayoutGroupId.DAILY_TOOLS, 17),
            (NativeDesktopLayoutGroupId.GAMES, 17),
            (NativeDesktopLayoutGroupId.SYSTEM_TEMP, 3),
            (NativeDesktopLayoutGroupId.UNASSIGNED, 12)
        };

        public static void Run(string auditDirectory)
        {
            var failures = new List<string>();
            var body = new StringBuilder();
            Scenario stored = StoredLaptop();
            LiveLaptopCase(body, failures, stored);
            ReportedCase(body, failures, stored);
            SweepCases(body, failures, new List<SweepTally>
            {
                Sweep(stored),
                Sweep(DroppedLaptop()),
                Sweep(GrownLaptop()),
                Sweep(StoredMonitor())
            });

            var report = new StringBuilder();
            report.AppendLine("# Zone resize and Dual-Rail planner: pinned groups");
            report.AppendLine();
            report.AppendLine($"GroupResizeAnchorHarness={(failures.Count == 0 ? "PASS" : "FAIL")}");
            report.AppendLine($"Failures={failures.Count}");
            report.AppendLine("ExplorerRead=false");
            report.AppendLine("ZoneStoreRead=false");
            report.AppendLine("DesktopMoved=false");
            report.AppendLine();
            report.Append(body);
            Directory.CreateDirectory(auditDirectory);
            File.WriteAllText(Path.Combine(auditDirectory, "ui-group-resize-anchor-tests.md"),
                report.ToString(), new UTF8Encoding(false));
            if (failures.Count > 0)
                throw new InvalidOperationException("GROUP_RESIZE_ANCHOR_HARNESS_FAILED " + string.Join(" | ", failures));
        }

        /// <summary>
        /// The synthetic laptop is only evidence if it plans like the real one:
        /// the live plan of 2026-09-25 00:16 in bin\Release\...\audit.
        /// </summary>
        private static void LiveLaptopCase(StringBuilder body, List<string> failures, Scenario scenario)
        {
            body.AppendLine("## The synthetic laptop reproduces the live plan");
            body.AppendLine();
            Check(body, failures, Laptop.Grid.SafeColumns == 22 && Laptop.Grid.SafeRows == 10,
                $"safe grid {Laptop.Grid.SafeColumns}x{Laptop.Grid.SafeRows}");
            foreach ((NativeDesktopLayoutGroupId id, int column, int row, int columns, int rows, bool pinned) in
                     new (NativeDesktopLayoutGroupId, int, int, int, int, bool)[]
                     {
                         (NativeDesktopLayoutGroupId.SYSTEM_TEMP, 5, 5, 3, 1, true),
                         (NativeDesktopLayoutGroupId.RESEARCH_STUDY, 0, 5, 4, 5, true),
                         (NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, 1, 1, 5, 3, true),
                         (NativeDesktopLayoutGroupId.UNASSIGNED, 5, 7, 5, 3, true),
                         (NativeDesktopLayoutGroupId.AI_DEV, 14, 1, 8, 2, true),
                         (NativeDesktopLayoutGroupId.DAILY_TOOLS, 13, 4, 9, 2, true),
                         (NativeDesktopLayoutGroupId.GAMES, 16, 7, 6, 3, false)
                     })
            {
                DesktopLayoutGroup group = GroupOf(scenario.OnScreen, id);
                Check(body, failures, group.HasManualAnchor == pinned && group.RailColumn == column &&
                    group.RailRow == row && group.Columns == columns && group.Rows == rows,
                    $"{id}: {Describe(group)}");
            }
            body.AppendLine();
        }

        /// <summary>
        /// The size that exposed the defect. 系统 · 临时, pinned 3x1 at (5,5), is
        /// resized to 1x3: its icons then fill rows 5 to 7 of column 5, and row 7
        /// is the first icon row of 待整理, pinned 5x3 at (5,7). The planner lets
        /// 待整理's pin go and the group flows to (6,4), yet the layout is valid,
        /// which was all Resize used to ask of it.
        /// </summary>
        private static void ReportedCase(StringBuilder body, List<string> failures, Scenario scenario)
        {
            body.AppendLine("## 系统 · 临时 resized to 1x3 on the laptop");
            body.AppendLine();
            DesktopLayoutPlan candidate = scenario.PlanWith(
                WithSize(scenario.Zones, NativeDesktopLayoutGroupId.SYSTEM_TEMP, 1, 3));
            string? invalid = ValidationFailure(candidate, scenario.Display.Grid);
            DesktopLayoutGroup unassigned = GroupOf(candidate, NativeDesktopLayoutGroupId.UNASSIGNED);
            Check(body, failures, invalid == null, $"the whole layout is valid: {invalid ?? "valid"}");
            Check(body, failures, !unassigned.HasManualAnchor,
                $"待整理 does not keep its pin at (5,7): {Describe(unassigned)}");

            // Where the group flows is the planner's business; that it is named is Resize's.
            DesktopGroupResizeResult result = scenario.ResizeTo(NativeDesktopLayoutGroupId.SYSTEM_TEMP, 1, 3);
            Check(body, failures, !result.Accepted && result.Reason == "LAYOUT_INVALID" &&
                result.Detail == "ANCHOR_LOST '待整理' (UNASSIGNED) is pinned at (5,7) but would flow automatically to " +
                    $"({unassigned.RailColumn},{unassigned.RailRow})",
                $"Resize refuses and names 待整理: {Verdict(result)}");
            body.AppendLine();
        }

        private static void SweepCases(StringBuilder body, List<string> failures, List<SweepTally> tallies)
        {
            body.AppendLine("## Every size, checked against the planner");
            body.AppendLine();
            body.AppendLine("Each laid-out group is resized to every size that passes Resize's own checks (lock,");
            body.AppendLine("minimum, capacity, safe grid), its current size excepted. A size unpins a group when a");
            body.AppendLine("group pinned on screen is not pinned at the same slot in the resulting plan, although");
            body.AppendLine("a refresh without the resize keeps that pin; a pin the refresh drops anyway is stale.");
            body.AppendLine("A valid layout is all Resize asked for before it looked at pins.");
            body.AppendLine();
            body.AppendLine("| Scenario | Stale pins on screen | Sizes | Valid layout | Valid, unpins a group | " +
                "Accepted by Resize | Accepted, unpins a group | Accepted, only a stale pin goes | Wrong verdicts |");
            body.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (SweepTally tally in tallies)
            {
                body.AppendLine($"| {tally.Scenario} | {Names(tally.StalePins)} | {tally.Sizes} | {tally.Valid} | " +
                    $"{tally.ValidUnpins} | {tally.Accepted} | {tally.AcceptedUnpins} | {tally.AcceptedStalePinOnly} | " +
                    $"{tally.WrongVerdicts} |");
            }
            body.AppendLine();
            foreach (SweepTally tally in tallies)
            {
                Check(body, failures, tally.AcceptedUnpins == 0, $"{tally.Scenario}: no accepted size unpins a group");
                Check(body, failures, tally.WrongVerdicts == 0,
                    $"{tally.Scenario}: every size is refused exactly when its layout is invalid or it unpins a group");
                if (tally.StalePins.Count > 0)
                    Check(body, failures, tally.AcceptedStalePinOnly > 0,
                        $"{tally.Scenario}: sizes are still accepted although {Names(tally.StalePins)} loses its pin, " +
                        "as it does on any refresh");
            }
            Check(body, failures, tallies.Sum(tally => tally.ValidUnpins) > 0,
                "the sweep does find valid sizes that unpin a group, so the zeros above are not vacuous");
            body.AppendLine();
            foreach (SweepTally tally in tallies.Where(tally => tally.Examples.Count > 0))
            {
                body.AppendLine($"Examples, {tally.Scenario}:");
                body.AppendLine();
                foreach (string example in tally.Examples) body.AppendLine($"- {example}");
                body.AppendLine();
            }
        }

        private static SweepTally Sweep(Scenario scenario)
        {
            NativeDesktopSafeGrid grid = scenario.Display.Grid;
            // What the next refresh lays out if nothing is resized: which pins survive anyway.
            DesktopLayoutPlan unchanged = scenario.PlanWith(scenario.Zones);
            var tally = new SweepTally { Scenario = scenario.Name, StalePins = PinsNotKept(scenario.OnScreen, unchanged) };
            int refusedExamples = 0, staleExamples = 0, ownPinExamples = 0;
            foreach (DesktopLayoutGroup group in scenario.OnScreen.Groups.Where(group => group.Columns > 0 && group.Rows > 0))
            {
                int required = DesktopGroupZoneService.ManagedItemCount(scenario.OnScreen, group.GroupId);
                for (int rows = DesktopGroupZoneService.MinimumRows; group.RailRow + rows <= grid.SafeRows; rows++)
                for (int columns = DesktopGroupZoneService.MinimumColumns; group.RailColumn + columns <= grid.SafeColumns; columns++)
                {
                    if (columns * rows < required || (columns == group.Columns && rows == group.Rows)) continue;
                    tally.Sizes++;
                    string size = $"{group.GroupId} {columns}x{rows}";

                    DesktopLayoutPlan? candidate = TryPlan(scenario, WithSize(scenario.Zones, group.GroupId, columns, rows));
                    bool valid = candidate != null && ValidationFailure(candidate, grid) == null;
                    List<DesktopLayoutGroup> gone = candidate == null
                        ? new List<DesktopLayoutGroup>()
                        : PinsNotKept(scenario.OnScreen, candidate);
                    List<DesktopLayoutGroup> unpinned = gone.Where(pin => UnpinnedByResize(pin, unchanged, candidate!)).ToList();
                    bool expected = valid && unpinned.Count == 0;
                    if (valid)
                    {
                        tally.Valid++;
                        if (unpinned.Count > 0) tally.ValidUnpins++;
                    }

                    DesktopGroupResizeResult result = scenario.ResizeTo(group.GroupId, columns, rows);
                    if (result.Accepted)
                    {
                        tally.Accepted++;
                        if (unpinned.Count > 0) tally.AcceptedUnpins++;
                        else if (gone.Count > 0) tally.AcceptedStalePinOnly++;
                    }

                    string? wrong = null;
                    if (result.Accepted != expected)
                        wrong = $"{(result.Accepted ? "accepted" : "refused")}, expected {(expected ? "accepted" : "refused")}";
                    else if (!result.Accepted && result.Reason != "LAYOUT_INVALID")
                        wrong = $"refused with {result.Reason}, which the sweep's own checks exclude";
                    else if (!result.Accepted && valid && !result.Detail.StartsWith(
                                 $"ANCHOR_LOST '{unpinned[0].GroupDisplayName}' ({unpinned[0].GroupId}) ", StringComparison.Ordinal))
                        wrong = $"refused without naming {unpinned[0].GroupId}";

                    if (wrong != null)
                    {
                        tally.WrongVerdicts++;
                        if (tally.WrongVerdicts <= 10)
                            tally.Examples.Add($"WRONG {size}: {wrong}; unpins {Names(unpinned)}; {Verdict(result)}");
                    }
                    else if (!result.Accepted && valid && unpinned[0].GroupId == group.GroupId && ownPinExamples++ < 2)
                    {
                        tally.Examples.Add($"refused, the resized group loses its own pin: {size}: {result.Detail}");
                    }
                    else if (!result.Accepted && valid && refusedExamples++ < 3)
                    {
                        tally.Examples.Add($"refused: {size}: {result.Detail}");
                    }
                    else if (result.Accepted && gone.Count > 0 && staleExamples++ < 3)
                    {
                        tally.Examples.Add($"accepted, only a stale pin goes: {size}: {Names(gone)} " +
                            $"{string.Join(", ", gone.Select(pin => Describe(GroupOf(candidate!, pin.GroupId))))}, " +
                            $"as without the resize: {string.Join(", ", gone.Select(pin => Describe(GroupOf(unchanged, pin.GroupId))))}");
                    }
                }
            }
            return tally;
        }

        /// <summary>Groups pinned on screen that the candidate plan does not keep pinned at the same slot.</summary>
        private static List<DesktopLayoutGroup> PinsNotKept(DesktopLayoutPlan onScreen, DesktopLayoutPlan candidate) =>
            onScreen.Groups
                .Where(pin => pin.HasManualAnchor && pin.Columns > 0 &&
                    !IsPinnedAt(GroupOf(candidate, pin.GroupId), pin.RailColumn, pin.RailRow))
                .ToList();

        /// <summary>
        /// Whether the resize itself costs a pin on screen its slot: the plan without
        /// the resize keeps the group pinned, and the candidate does not keep it there.
        /// </summary>
        private static bool UnpinnedByResize(DesktopLayoutGroup pin, DesktopLayoutPlan unchanged, DesktopLayoutPlan candidate)
        {
            DesktopLayoutGroup kept = GroupOf(unchanged, pin.GroupId);
            return kept.HasManualAnchor && !IsPinnedAt(GroupOf(candidate, pin.GroupId), kept.RailColumn, kept.RailRow);
        }

        /// <summary>The laptop's scheme as stored on 2026-09-25, native-desktop-group-anchors.2560x1600@144.json.</summary>
        private static Scenario StoredLaptop() => new("laptop, anchors as stored", Laptop,
            Plan(Laptop, LaptopZones(), RealMembers), LaptopZones(), RealMembers);

        /// <summary>
        /// The laptop right after the drop of 2026-09-24, before any refresh. 游戏,
        /// pinned 3x6 at (10,2), was dropped with its first icon at (9,1), and the
        /// drag, which then checked icons only, took it: the plan on screen and the
        /// store both have 游戏 pinned there. Its last icon row is the header row of
        /// 待整理, pinned at (5,7), so the next refresh lets the pin go whatever
        /// size is chosen - nothing a resize should be blamed for.
        /// </summary>
        private static Scenario DroppedLaptop()
        {
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = LaptopZones();
            zones[NativeDesktopLayoutGroupId.GAMES] = Anchor(10, 2, -2, Laptop.Grid);
            DesktopLayoutPlan onScreen = Plan(Laptop, zones, RealMembers);
            DesktopLayoutGroup games = GroupOf(onScreen, NativeDesktopLayoutGroupId.GAMES);
            DropOnScreen(onScreen, games, 9, 1);
            zones[NativeDesktopLayoutGroupId.GAMES] = Anchor(9, 1, games.ExtraColumns, Laptop.Grid);
            return new Scenario("laptop, 游戏 just dropped at (9,1): stale pin", Laptop, onScreen, zones, RealMembers);
        }

        /// <summary>
        /// The laptop after four icons joined 系统 · 临时 since the last refresh.
        /// Sized 3x1 by hand, the group now needs three rows, and its third is the
        /// first icon row of 待整理, pinned at (5,7), so the next refresh lets that
        /// pin go whatever size is chosen. The plan on screen still shows it pinned.
        /// </summary>
        private static Scenario GrownLaptop()
        {
            (NativeDesktopLayoutGroupId Group, int Count)[] grown = RealMembers
                .Select(pair => pair.Group == NativeDesktopLayoutGroupId.SYSTEM_TEMP ? (pair.Group, pair.Count + 4) : pair)
                .ToArray();
            return new Scenario("laptop, 系统 · 临时 grew by 4 icons since the last refresh: stale pin", Laptop,
                Plan(Laptop, LaptopZones(), RealMembers), LaptopZones(), grown);
        }

        /// <summary>The monitor's scheme as stored on 2026-09-25: two manual sizes, no pins, so nothing to lose.</summary>
        private static Scenario StoredMonitor()
        {
            var zones = new Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>
            {
                [NativeDesktopLayoutGroupId.SYSTEM_TEMP] = new() { ManualColumns = 3, ManualRows = 1, IsManualSize = true },
                [NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS] = new() { ManualColumns = 5, ManualRows = 2, IsManualSize = true }
            };
            return new Scenario("monitor, sizes as stored", Monitor, Plan(Monitor, zones, RealMembers), zones, RealMembers);
        }

        private static Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> LaptopZones()
        {
            NativeDesktopSafeGrid grid = Laptop.Grid;
            return new Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>
            {
                [NativeDesktopLayoutGroupId.DAILY_TOOLS] = Anchor(13, 4, 5, grid),
                [NativeDesktopLayoutGroupId.AI_DEV] = Anchor(14, 1, 4, grid),
                [NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS] = Anchor(1, 1, 0, grid, 5, 2),
                [NativeDesktopLayoutGroupId.UNASSIGNED] = Anchor(5, 7, 0, grid),
                [NativeDesktopLayoutGroupId.SYSTEM_TEMP] = Anchor(5, 5, 0, grid, 3, 1),
                [NativeDesktopLayoutGroupId.RESEARCH_STUDY] = Anchor(0, 5, -1, grid, 4, 5)
            };
        }

        private static DesktopGroupZone Anchor(int column, int row, int extraColumns, NativeDesktopSafeGrid grid,
            int manualColumns = 0, int manualRows = 0) => new()
        {
            Column = column,
            Row = row,
            ExtraColumns = extraColumns,
            GridColumns = grid.SafeColumns,
            GridRows = grid.SafeRows,
            IsManualPosition = true,
            ManualColumns = manualColumns,
            ManualRows = manualRows,
            IsManualSize = manualColumns > 0 && manualRows > 0
        };

        /// <summary>The zones with one group sized by hand, as Resize proposes them.</summary>
        private static Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> WithSize(
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones,
            NativeDesktopLayoutGroupId group, int columns, int rows)
        {
            var result = zones.ToDictionary(pair => pair.Key, pair => pair.Value);
            zones.TryGetValue(group, out DesktopGroupZone? previous);
            result[group] = new DesktopGroupZone
            {
                Column = previous?.Column ?? 0,
                Row = previous?.Row ?? 0,
                GridColumns = previous?.GridColumns ?? 0,
                GridRows = previous?.GridRows ?? 0,
                IsManualPosition = previous?.IsManualPosition ?? false,
                ExtraColumns = previous?.ExtraColumns ?? 0,
                Locked = previous?.Locked ?? false,
                IsManualSize = true,
                ManualColumns = columns,
                ManualRows = rows
            };
            return result;
        }

        /// <summary>Commits a drop to the plan on screen the way DesktopGroupDragService.MoveGroup does.</summary>
        private static void DropOnScreen(DesktopLayoutPlan plan, DesktopLayoutGroup group, int column, int row)
        {
            int deltaX = (column - group.RailColumn) * plan.GridSpacingX;
            int deltaY = (row - group.RailRow) * plan.GridSpacingY;
            foreach (DesktopLayoutItem member in plan.Items.Where(item => item.LayoutGroupId == group.GroupId.ToString() &&
                         item.IsStableIdentity && !item.IsShellNamespace))
            {
                member.TargetX += deltaX;
                member.TargetY += deltaY;
            }
            group.RailColumn = column;
            group.RailRow = row;
            group.IconBoundingLeft += deltaX;
            group.IconBoundingRight += deltaX;
            group.IconBoundingTop += deltaY;
            group.IconBoundingBottom += deltaY;
            group.HeaderLeft = group.IconBoundingLeft;
            group.HeaderTop = Math.Max(0, group.IconBoundingTop
                - DesktopLayoutSpacing.HeaderToFirstIconAt(plan.Dpi) - DesktopLayoutSpacing.HeaderHeightAt(plan.Dpi));
            group.HasManualAnchor = true;
        }

        private static DesktopLayoutPlan? TryPlan(Scenario scenario,
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones)
        {
            try
            {
                return scenario.PlanWith(zones);
            }
            catch (Exception)
            {
                return null; // Resize refuses such a size as LAYOUT_INVALID.
            }
        }

        private static string? ValidationFailure(DesktopLayoutPlan plan, NativeDesktopSafeGrid grid)
        {
            try
            {
                NativeDesktopLayoutApplicationService.ValidateDualRailPlan(plan, grid);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message;
            }
        }

        private static DesktopLayoutPlan Plan(Display display,
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones,
            (NativeDesktopLayoutGroupId Group, int Count)[] members)
        {
            NativeDesktopSafeGrid grid = display.Grid;
            var items = new List<DesktopLayoutItem>();
            foreach ((NativeDesktopLayoutGroupId group, int count) in members)
            {
                for (int index = 0; index < count; index++)
                {
                    items.Add(new DesktopLayoutItem
                    {
                        LayoutGroupId = group.ToString(),
                        OriginalPath = $@"C:\DesktopHubHarness\{group}\{index:D2}.lnk",
                        ExplorerLabel = $"{group} {index:D2}",
                        DisplayName = $"{group} {index:D2}",
                        Type = "LinkShortcut",
                        ClassificationConfidence = "HIGH",
                        IsStableIdentity = true,
                        ApplyReason = "NOT_PLANNED"
                    });
                }
            }
            // The two shell icons on the real desktop, which the planner never moves.
            items.Add(Shell("此电脑", 0, 0, grid));
            items.Add(Shell("回收站", 0, 1, grid));

            var listState = new NativeDesktopListViewState
            {
                AlignToGridEnabled = true,
                GridSpacingX = display.CellX,
                GridSpacingY = display.CellY
            };
            var planner = new NativeDesktopLayoutPlanner(new NativeDesktopIconLayoutService(), groupZones: zones);
            return planner.PlanDualRailLayout(listState, grid, display.WorkArea, display.Dpi, items);
        }

        private static DesktopLayoutItem Shell(string name, int column, int row, NativeDesktopSafeGrid grid) => new()
        {
            LayoutGroupId = NativeDesktopLayoutGroupId.UNASSIGNED.ToString(),
            ExplorerLabel = name,
            DisplayName = name,
            Type = NativeIconIdentityStatus.ShellNamespace.ToString(),
            CurrentX = column * grid.IconSpacingX,
            CurrentY = row * grid.IconSpacingY,
            TargetX = column * grid.IconSpacingX,
            TargetY = row * grid.IconSpacingY,
            ClassificationConfidence = "HIGH",
            IsShellNamespace = true,
            ApplyReason = "NOT_PLANNED"
        };

        private static DesktopLayoutGroup GroupOf(DesktopLayoutPlan plan, NativeDesktopLayoutGroupId id) =>
            plan.Groups.Single(group => group.GroupId == id);

        private static bool IsPinnedAt(DesktopLayoutGroup group, int column, int row) =>
            group.HasManualAnchor && group.RailColumn == column && group.RailRow == row;

        private static string Describe(DesktopLayoutGroup group) =>
            $"{(group.HasManualAnchor ? "pinned" : "automatic")} {group.Columns}x{group.Rows} " +
            $"at ({group.RailColumn},{group.RailRow})";

        private static string Names(IEnumerable<DesktopLayoutGroup> groups)
        {
            string names = string.Join(", ", groups.Select(group => group.GroupId));
            return names.Length == 0 ? "none" : names;
        }

        private static string Verdict(DesktopGroupResizeResult result) =>
            (result.Accepted ? "accepted" : "refused") + $", {result.Reason}" +
            (string.IsNullOrEmpty(result.Detail) ? string.Empty : $": {result.Detail}");

        private static void Check(StringBuilder body, List<string> failures, bool passed, string description)
        {
            body.AppendLine($"- {(passed ? "PASS" : "FAIL")} {description}");
            if (!passed) failures.Add(description);
        }
    }
}
