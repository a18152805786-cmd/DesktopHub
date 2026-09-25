using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Deterministic check that a group header drag and the Dual-Rail planner
    /// agree on where a group may be pinned. Every desktop here is synthetic and
    /// is planned through the planner's replay entry point, so Explorer is never
    /// read and nothing on the real desktop moves. No test framework dependency.
    /// </summary>
    internal static class NativeDesktopGroupDragTestHarness
    {
        private sealed record Display(string Key, int Width, int Height, int Dpi, int CellX, int CellY)
        {
            public NativeDesktopSafeGrid Grid => NativeDesktopIconLayoutService.SafeGridFor(Width, Height, CellX, CellY);
            public Rectangle WorkArea => new(0, 0, Width, Height);
        }

        /// <summary>What happened to every drop one scenario allows, checked against the planner.</summary>
        private sealed class SweepTally
        {
            public string Scenario { get; init; } = string.Empty;
            public int Destinations { get; set; }
            public int OldAccepted { get; set; }
            public int NewAccepted { get; set; }
            /// <summary>The reported defect: accepted, and the next refresh drops an anchor.</summary>
            public int OldAcceptedAnchorDropped { get; set; }
            public int NewAcceptedAnchorDropped { get; set; }
            /// <summary>Refused now, although the planner would have re-flowed an automatic group around it.</summary>
            public int RefusedOverAutomaticGroup { get; set; }
            /// <summary>Refused now, although the planner would have kept both anchors, one header over the other's icons.</summary>
            public int RefusedOverAnchoredGroup { get; set; }
            public int OldAcceptedUnplannable { get; set; }
            public int NewAcceptedUnplannable { get; set; }
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
            IncidentCase(body, failures);
            ReasonCases(body, failures);
            LiveLaptopCase(body, failures);
            GrowthCase(body, failures);
            GainedIconsCase(body, failures);
            SweepCases(body, failures, new List<SweepTally>
            {
                Sweep("laptop, anchors as stored", Laptop, LaptopZones()),
                Sweep("laptop, 游戏 3x6 anchored at (10,2)", Laptop, IncidentZones()),
                Sweep("monitor, sizes as stored", Monitor, MonitorZones())
            });

            var report = new StringBuilder();
            report.AppendLine("# Group drag and Dual-Rail planner: header rows");
            report.AppendLine();
            report.AppendLine($"GroupDragZoneHarness={(failures.Count == 0 ? "PASS" : "FAIL")}");
            report.AppendLine($"Failures={failures.Count}");
            report.AppendLine("ExplorerRead=false");
            report.AppendLine("DesktopMoved=false");
            report.AppendLine();
            report.Append(body);
            Directory.CreateDirectory(auditDirectory);
            File.WriteAllText(Path.Combine(auditDirectory, "ui-group-drag-zone-tests.md"),
                report.ToString(), new UTF8Encoding(false));
            if (failures.Count > 0)
                throw new InvalidOperationException("GROUP_DRAG_ZONE_HARNESS_FAILED " + string.Join(" | ", failures));
        }

        /// <summary>
        /// 2026-09-24 on the laptop: 游戏, 3x6, was dropped with its first icon at
        /// (9,1). Its last icon row, row 6, is the header row of 待整理, anchored
        /// 5x3 at (5,7). The icon check accepted the drop and the next refresh
        /// refused the anchor. 游戏 is reconstructed standing at (10,2), where
        /// that refresh put it; the other anchors are the ones stored today.
        /// </summary>
        private static void IncidentCase(StringBuilder body, List<string> failures)
        {
            body.AppendLine("## 2026-09-24: 游戏 dropped at (9,1), laptop 22x10");
            body.AppendLine();
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = IncidentZones();
            DesktopLayoutPlan before = Plan(Laptop, zones);
            DesktopLayoutGroup games = GroupOf(before, NativeDesktopLayoutGroupId.GAMES);
            DesktopLayoutGroup unassigned = GroupOf(before, NativeDesktopLayoutGroupId.UNASSIGNED);
            Check(body, failures, Laptop.Grid.SafeColumns == 22 && Laptop.Grid.SafeRows == 10,
                $"safe grid {Laptop.Grid.SafeColumns}x{Laptop.Grid.SafeRows}");
            Check(body, failures, IsAnchoredAt(games, 10, 2, 3, 6), $"游戏 before the drop: {Describe(games)}");
            Check(body, failures, IsAnchoredAt(unassigned, 5, 7, 5, 3), $"待整理: {Describe(unassigned)}");

            (string? iconsOnly, string? drag) = DragVerdict(Laptop, before, games, 9, 1);
            Check(body, failures, iconsOnly == null,
                $"the icon check alone (the drag until now) accepts (9,1): {iconsOnly ?? "accepted"}");
            Check(body, failures,
                drag == "HEADER_ROW_COLLISION at slot (9,6) in the header row of '待整理' (UNASSIGNED)",
                $"the drag now refuses (9,1): {drag ?? "accepted"}");

            DesktopLayoutGroup refused = GroupOf(Plan(Laptop, WithAnchor(before, zones, games, 9, 1, Laptop.Grid)),
                NativeDesktopLayoutGroupId.GAMES);
            Check(body, failures, !refused.HasManualAnchor,
                $"the planner refuses an anchor at (9,1), 游戏 flows instead: {Describe(refused)}");

            (_, drag) = DragVerdict(Laptop, before, games, 10, 1);
            DesktopLayoutGroup kept = GroupOf(Plan(Laptop, WithAnchor(before, zones, games, 10, 1, Laptop.Grid)),
                NativeDesktopLayoutGroupId.GAMES);
            Check(body, failures, drag == null && IsAnchoredAt(kept, 10, 1, 3, 6),
                $"one column to the right both accept (10,1): drag {drag ?? "accepted"}, planner {Describe(kept)}");
            body.AppendLine();
        }

        /// <summary>Every way two blocks can meet, on a hand-made plan: 游戏 3x2 at (2,2), 待整理 4x3 at (10,5).</summary>
        private static void ReasonCases(StringBuilder body, List<string> failures)
        {
            body.AppendLine("## Zone collision reasons");
            body.AppendLine();
            var plan = new DesktopLayoutPlan
            {
                Groups =
                {
                    Placed(NativeDesktopLayoutGroupId.GAMES, "游戏", 2, 2, 3, 2),
                    Placed(NativeDesktopLayoutGroupId.UNASSIGNED, "待整理", 10, 5, 4, 3),
                    Placed(NativeDesktopLayoutGroupId.AI_DEV, "AI · 开发", 0, 0, 0, 0)
                }
            };
            DesktopLayoutGroup games = plan.Groups[0];
            const string headerRow = "in the header row of '待整理' (UNASSIGNED)";
            const string zone = "inside the zone of '待整理' (UNASSIGNED)";
            foreach ((int column, int row, string? expected, string meaning) in new (int, int, string?, string)[]
            {
                (10, 2, null, "last icon row just above the other header row"),
                (10, 3, "HEADER_ROW_COLLISION at slot (10,4) " + headerRow, "icons in the other header row"),
                (10, 5, "HEADER_ROW_COLLISION at slot (10,4) " + headerRow, "both header rows in one row"),
                (10, 6, "HEADER_ROW_COLLISION at slot (10,5) " + zone, "header row over the other group's icons"),
                (10, 8, "HEADER_ROW_COLLISION at slot (10,7) " + zone, "header row over the other's last icon row"),
                (10, 9, null, "header row just below the other's last icon row"),
                (13, 6, "HEADER_ROW_COLLISION at slot (13,5) " + zone, "one column of overlap"),
                (14, 6, null, "side by side, to the right"),
                (7, 6, null, "side by side, to the left"),
                (3, 2, null, "the group's own current block is no obstacle"),
                (0, 1, null, "a group that was not laid out is no obstacle")
            })
            {
                string? actual = DesktopGroupDragService.FirstZoneCollision(plan, games, column, row);
                Check(body, failures, actual == expected, $"({column},{row}) {meaning}: {actual ?? "accepted"}");
            }
            body.AppendLine();
        }

        /// <summary>
        /// The synthetic laptop is only evidence if it plans like the real one:
        /// the live plan of 2026-09-25 00:15 in bin\Release\...\audit.
        /// </summary>
        private static void LiveLaptopCase(StringBuilder body, List<string> failures)
        {
            body.AppendLine("## The synthetic laptop reproduces the live plan");
            body.AppendLine();
            DesktopLayoutPlan plan = Plan(Laptop, LaptopZones());
            foreach ((NativeDesktopLayoutGroupId id, int column, int row, int columns, int rows, bool anchored) in
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
                DesktopLayoutGroup group = GroupOf(plan, id);
                Check(body, failures, group.HasManualAnchor == anchored && group.RailColumn == column &&
                    group.RailRow == row && group.Columns == columns && group.Rows == rows,
                    $"{id}: {Describe(group)}");
            }
            body.AppendLine();
        }

        /// <summary>
        /// Why PlaceAnchoredGroups keeps checking only an anchor's icon rows. With
        /// one more member, 系统 · 临时 (anchored 3x1 at (5,5)) needs a second row,
        /// and that row is the header row of 待整理 (anchored at (5,7)). Checking
        /// the header row too would refuse 待整理's anchor, which is exactly what
        /// ignoring it (IsManualPosition off) produces.
        /// </summary>
        private static void GrowthCase(StringBuilder body, List<string> failures)
        {
            body.AppendLine("## Growth: why the planner's own header-row check stays off");
            body.AppendLine();
            (NativeDesktopLayoutGroupId Group, int Count)[] grown = RealMembers
                .Select(pair => pair.Group == NativeDesktopLayoutGroupId.SYSTEM_TEMP ? (pair.Group, pair.Count + 1) : pair)
                .ToArray();
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = LaptopZones();
            DesktopLayoutPlan plan = Plan(Laptop, zones, grown);
            DesktopLayoutGroup systemTemp = GroupOf(plan, NativeDesktopLayoutGroupId.SYSTEM_TEMP);
            DesktopLayoutGroup unassigned = GroupOf(plan, NativeDesktopLayoutGroupId.UNASSIGNED);
            Rectangle overlap = Rectangle.Intersect(Block(systemTemp), Block(unassigned));
            Check(body, failures, IsAnchoredAt(systemTemp, 5, 5, 3, 2), $"系统 · 临时 with 4 members: {Describe(systemTemp)}");
            Check(body, failures, IsAnchoredAt(unassigned, 5, 7, 5, 3) && overlap.Height == 1 && overlap.Top == 6,
                $"待整理 keeps its anchor, header row over 系统 · 临时's second row: {Describe(unassigned)}; " +
                $"overlap columns {overlap.Left}-{overlap.Right - 1}, row {overlap.Top}");

            zones[NativeDesktopLayoutGroupId.UNASSIGNED] = Copy(zones[NativeDesktopLayoutGroupId.UNASSIGNED], anchored: false);
            try
            {
                DesktopLayoutGroup flowed = GroupOf(Plan(Laptop, zones, grown), NativeDesktopLayoutGroupId.UNASSIGNED);
                body.AppendLine($"- INFO with the header row checked, 待整理 would flow instead: {Describe(flowed)}");
            }
            catch (InvalidOperationException ex)
            {
                body.AppendLine($"- INFO with the header row checked, the laptop could not be planned at all: {ex.Message}");
            }
            body.AppendLine();
        }

        /// <summary>
        /// The refresh of 2026-09-24: 日常工具 had been dropped 8x2 at (13,4)
        /// holding 16 icons, then gained a 17th. At its dropped width that needs a
        /// third row, which is 游戏's header row, and 游戏 was pushed into the
        /// middle. Having gained an icon, 日常工具 widens to 9x2 instead and 游戏
        /// keeps the right edge. A group that has gained nothing since its drop
        /// keeps the shape it was dropped with.
        /// </summary>
        private static void GainedIconsCase(StringBuilder body, List<string> failures)
        {
            body.AppendLine("## A dropped group widens only after gaining icons");
            body.AppendLine();
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = LaptopZones();
            DesktopGroupZone daily = Anchor(13, 4, 4, Laptop.Grid);
            daily.PinnedMembers = 16;
            zones[NativeDesktopLayoutGroupId.DAILY_TOOLS] = daily;
            DesktopLayoutPlan plan = Plan(Laptop, zones);
            DesktopLayoutGroup grown = GroupOf(plan, NativeDesktopLayoutGroupId.DAILY_TOOLS);
            DesktopLayoutGroup games = GroupOf(plan, NativeDesktopLayoutGroupId.GAMES);
            Check(body, failures, IsAnchoredAt(grown, 13, 4, 9, 2) && grown.GrowthColumns == 1,
                $"日常工具, dropped 8x2 with 16 icons, now holding 17: {Describe(grown)}, widened by {grown.GrowthColumns}");
            Check(body, failures, !games.HasManualAnchor && games.RailColumn == 16 && games.RailRow == 7 &&
                games.Columns == 6 && games.Rows == 3, $"游戏 keeps the right edge: {Describe(games)}");

            daily.PinnedMembers = 17;
            try
            {
                DesktopLayoutGroup kept = GroupOf(Plan(Laptop, zones), NativeDesktopLayoutGroupId.DAILY_TOOLS);
                Check(body, failures, IsAnchoredAt(kept, 13, 4, 8, 3) && kept.GrowthColumns == 0,
                    $"dropped while holding 17, it keeps its dropped shape: {Describe(kept)}");
            }
            catch (InvalidOperationException ex)
            {
                Check(body, failures, false, $"dropped while holding 17, the laptop could not be planned: {ex.Message}");
            }
            body.AppendLine();
        }

        private static void SweepCases(StringBuilder body, List<string> failures, List<SweepTally> tallies)
        {
            body.AppendLine("## Every drop, checked against the planner");
            body.AppendLine();
            body.AppendLine("Each laid-out group is dropped at every destination inside the safe grid. A drop");
            body.AppendLine("counts as honoured when the next plan keeps the group anchored there, same shape, and");
            body.AppendLine("keeps every other anchor where it was.");
            body.AppendLine();
            body.AppendLine("| Scenario | Drops | Accepted before | Accepted now | Accepted before, anchor dropped | " +
                "Accepted now, anchor dropped | Refused now over an automatic group | Refused now over an anchored group | " +
                "Accepted before / now, unplannable |");
            body.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (SweepTally tally in tallies)
            {
                body.AppendLine($"| {tally.Scenario} | {tally.Destinations} | {tally.OldAccepted} | {tally.NewAccepted} | " +
                    $"{tally.OldAcceptedAnchorDropped} | {tally.NewAcceptedAnchorDropped} | " +
                    $"{tally.RefusedOverAutomaticGroup} | {tally.RefusedOverAnchoredGroup} | " +
                    $"{tally.OldAcceptedUnplannable} / {tally.NewAcceptedUnplannable} |");
            }
            body.AppendLine();
            foreach (SweepTally tally in tallies)
            {
                Check(body, failures, tally.NewAcceptedAnchorDropped == 0,
                    $"{tally.Scenario}: no accepted drop loses an anchor on the next plan");
            }
            Check(body, failures, tallies.Sum(tally => tally.OldAcceptedAnchorDropped) > 0,
                "the sweep does detect the old defect, so the zero above is not vacuous");
            body.AppendLine();
            foreach (SweepTally tally in tallies.Where(tally => tally.Examples.Count > 0))
            {
                body.AppendLine($"Examples, {tally.Scenario}:");
                body.AppendLine();
                foreach (string example in tally.Examples) body.AppendLine($"- {example}");
                body.AppendLine();
            }
        }

        private static SweepTally Sweep(string scenario, Display display,
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones)
        {
            var tally = new SweepTally { Scenario = scenario };
            NativeDesktopSafeGrid grid = display.Grid;
            DesktopLayoutPlan before = Plan(display, zones);
            foreach (DesktopLayoutGroup group in before.Groups.Where(group => group.Columns > 0 && group.Rows > 0))
            {
                for (int row = DesktopLayoutSpacing.GroupLeadingRows; row + group.Rows <= grid.SafeRows; row++)
                for (int column = 0; column + group.Columns <= grid.SafeColumns; column++)
                {
                    if (column == group.RailColumn && row == group.RailRow) continue;
                    tally.Destinations++;
                    (string? iconsOnly, string? drag) = DragVerdict(display, before, group, column, row);
                    if (iconsOnly != null) continue; // refused before and after alike
                    tally.OldAccepted++;
                    if (drag == null) tally.NewAccepted++;

                    DesktopLayoutPlan after;
                    try
                    {
                        after = Plan(display, WithAnchor(before, zones, group, column, row, grid));
                    }
                    catch (InvalidOperationException ex)
                    {
                        tally.OldAcceptedUnplannable++;
                        if (drag == null)
                        {
                            tally.NewAcceptedUnplannable++;
                            if (tally.Examples.Count < 8)
                                tally.Examples.Add($"accepted, but no plan: {group.GroupId} at ({column},{row}): {ex.Message}");
                        }
                        continue;
                    }

                    string? dropped = FirstDroppedAnchor(before, after, group, column, row);
                    if (dropped != null)
                    {
                        tally.OldAcceptedAnchorDropped++;
                        if (drag == null)
                        {
                            tally.NewAcceptedAnchorDropped++;
                            tally.Examples.Add($"STILL ACCEPTED: {group.GroupId} at ({column},{row}): {dropped}");
                        }
                        else if (tally.Examples.Count < 8)
                        {
                            tally.Examples.Add($"now refused: {group.GroupId} at ({column},{row}) would have ended with " +
                                $"{dropped}; refused with {drag}");
                        }
                    }
                    else if (drag != null)
                    {
                        if (CollidingGroups(before, group, column, row).Any(other => other.HasManualAnchor))
                            tally.RefusedOverAnchoredGroup++;
                        else
                            tally.RefusedOverAutomaticGroup++;
                    }
                }
            }
            return tally;
        }

        /// <summary>
        /// The icon check alone, which is the whole drag validation as it was, and
        /// the drag as it is now. Explorer is taken to hold every icon at its target.
        /// </summary>
        private static (string? IconsOnly, string? Drag) DragVerdict(Display display, DesktopLayoutPlan plan,
            DesktopLayoutGroup group, int column, int row)
        {
            List<NativeDesktopIconState> icons = plan.Items.Select(item => new NativeDesktopIconState
            {
                DisplayName = item.DisplayName,
                OriginalPath = item.OriginalPath,
                X = item.TargetX,
                Y = item.TargetY
            }).ToList();
            HashSet<string> members = plan.Items
                .Where(item => item.LayoutGroupId == group.GroupId.ToString() && item.IsStableIdentity &&
                    !item.IsShellNamespace && !string.IsNullOrEmpty(item.OriginalPath))
                .Select(item => Path.GetFullPath(item.OriginalPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string? iconsOnly = DesktopGroupDragService.FirstIconCollision(icons, members, group, column, row,
                display.WorkArea, display.Grid);
            return (iconsOnly, DesktopGroupDragService.FirstZoneCollision(plan, group, column, row) ?? iconsOnly);
        }

        /// <summary>The moved group, or any group anchored before the drop, that the next plan does not keep.</summary>
        private static string? FirstDroppedAnchor(DesktopLayoutPlan before, DesktopLayoutPlan after,
            DesktopLayoutGroup moved, int column, int row)
        {
            DesktopLayoutGroup movedAfter = GroupOf(after, moved.GroupId);
            if (!IsAnchoredAt(movedAfter, column, row, moved.Columns, moved.Rows))
                return $"{moved.GroupId} {Describe(movedAfter)}";
            foreach (DesktopLayoutGroup anchored in before.Groups.Where(group => group.HasManualAnchor &&
                         group.GroupId != moved.GroupId))
            {
                DesktopLayoutGroup other = GroupOf(after, anchored.GroupId);
                if (!IsAnchoredAt(other, anchored.RailColumn, anchored.RailRow, anchored.Columns, anchored.Rows))
                    return $"{anchored.GroupId} {Describe(other)}";
            }
            return null;
        }

        private static IEnumerable<DesktopLayoutGroup> CollidingGroups(DesktopLayoutPlan plan, DesktopLayoutGroup group,
            int column, int row)
        {
            Rectangle destination = NativeDesktopLayoutPlanner.GroupBlock(column, row, group.Columns, group.Rows);
            return plan.Groups.Where(other => other.GroupId != group.GroupId && other.Columns > 0 && other.Rows > 0 &&
                Block(other).IntersectsWith(destination));
        }

        private static DesktopLayoutPlan Plan(Display display,
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones,
            (NativeDesktopLayoutGroupId Group, int Count)[]? members = null)
        {
            NativeDesktopSafeGrid grid = display.Grid;
            var items = new List<DesktopLayoutItem>();
            foreach ((NativeDesktopLayoutGroupId group, int count) in members ?? RealMembers)
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

        /// <summary>The laptop's scheme as stored on 2026-09-25, native-desktop-group-anchors.2560x1600@144.json.</summary>
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

        /// <summary>The laptop with 游戏 standing 3x6 at (10,2), as on 2026-09-24.</summary>
        private static Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> IncidentZones()
        {
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = LaptopZones();
            zones[NativeDesktopLayoutGroupId.GAMES] = Anchor(10, 2, -2, Laptop.Grid);
            return zones;
        }

        /// <summary>The monitor's scheme as stored on 2026-09-25: two manual sizes, no anchors.</summary>
        private static Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> MonitorZones() => new()
        {
            [NativeDesktopLayoutGroupId.SYSTEM_TEMP] = new DesktopGroupZone { ManualColumns = 3, ManualRows = 1, IsManualSize = true },
            [NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS] = new DesktopGroupZone { ManualColumns = 5, ManualRows = 2, IsManualSize = true }
        };

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

        /// <summary>The zones after a drop, exactly as DesktopGroupDragService saves the position.</summary>
        private static Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> WithAnchor(DesktopLayoutPlan plan,
            Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones, DesktopLayoutGroup group,
            int column, int row, NativeDesktopSafeGrid grid)
        {
            var result = new Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>(zones);
            zones.TryGetValue(group.GroupId, out DesktopGroupZone? previous);
            DesktopGroupZone moved = previous == null ? new DesktopGroupZone() : Copy(previous, anchored: true);
            moved.Column = column;
            moved.Row = row;
            moved.ExtraColumns = group.ExtraColumns;
            moved.GridColumns = grid.SafeColumns;
            moved.GridRows = grid.SafeRows;
            // MoveGroup's members: the group's icons with a stable identity.
            moved.PinnedMembers = plan.Items.Count(item => item.LayoutGroupId == group.GroupId.ToString() &&
                item.IsStableIdentity && !item.IsShellNamespace && !string.IsNullOrEmpty(item.OriginalPath));
            moved.IsManualPosition = true;
            result[group.GroupId] = moved;
            return result;
        }

        private static DesktopGroupZone Copy(DesktopGroupZone zone, bool anchored) => new()
        {
            Column = zone.Column,
            Row = zone.Row,
            GridColumns = zone.GridColumns,
            GridRows = zone.GridRows,
            IsManualPosition = anchored,
            ManualColumns = zone.ManualColumns,
            ManualRows = zone.ManualRows,
            IsManualSize = zone.IsManualSize,
            ExtraColumns = zone.ExtraColumns,
            PinnedMembers = zone.PinnedMembers,
            Locked = zone.Locked
        };

        private static DesktopLayoutGroup Placed(NativeDesktopLayoutGroupId id, string title, int column, int row,
            int columns, int rows) => new()
        {
            GroupId = id,
            GroupDisplayName = title,
            RailColumn = column,
            RailRow = row,
            Columns = columns,
            Rows = rows
        };

        private static Rectangle Block(DesktopLayoutGroup group) =>
            NativeDesktopLayoutPlanner.GroupBlock(group.RailColumn, group.RailRow, group.Columns, group.Rows);

        private static DesktopLayoutGroup GroupOf(DesktopLayoutPlan plan, NativeDesktopLayoutGroupId id) =>
            plan.Groups.Single(group => group.GroupId == id);

        private static bool IsAnchoredAt(DesktopLayoutGroup group, int column, int row, int columns, int rows) =>
            group.HasManualAnchor && group.RailColumn == column && group.RailRow == row &&
            group.Columns == columns && group.Rows == rows;

        private static string Describe(DesktopLayoutGroup group) =>
            $"{(group.HasManualAnchor ? "anchored" : "automatic")} {group.Columns}x{group.Rows} " +
            $"at ({group.RailColumn},{group.RailRow})";

        private static void Check(StringBuilder body, List<string> failures, bool passed, string description)
        {
            body.AppendLine($"- {(passed ? "PASS" : "FAIL")} {description}");
            if (!passed) failures.Add(description);
        }
    }
}
