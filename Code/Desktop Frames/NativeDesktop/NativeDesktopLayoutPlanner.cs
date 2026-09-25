using Desktop_Frames.ReferenceGrouping;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Builds a native Desktop coordinate plan without changing Explorer or the
    /// filesystem. Only HIGH-confidence, stable-path items are eligible for
    /// later application; every other icon remains an occupied grid slot.
    /// </summary>
    public sealed class NativeDesktopLayoutPlanner
    {
        private readonly NativeDesktopIconLayoutService _layout;
        private readonly IReadOnlyDictionary<string, NativeDesktopLayoutGroupId> _manualLayoutGroups;
        private readonly ISet<string> _manualButUnmappedPaths;
        private readonly IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> _groupZones;

        /// <summary>Smallest gap, in grid columns, kept between the two rails.</summary>
        private const int MinimumCentralColumns = 4;

        /// <summary>Most extra columns a rail may take on to fit the screen height.</summary>
        private const int MaximumRailWidening = 6;

        public NativeDesktopLayoutPlanner(
            NativeDesktopIconLayoutService layout,
            IReadOnlyDictionary<string, NativeDesktopLayoutGroupId>? manualLayoutGroups = null,
            IEnumerable<string>? manualButUnmappedPaths = null,
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>? groupZones = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _manualLayoutGroups = manualLayoutGroups ?? new Dictionary<string, NativeDesktopLayoutGroupId>(StringComparer.OrdinalIgnoreCase);
            _manualButUnmappedPaths = new HashSet<string>(manualButUnmappedPaths ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _groupZones = groupZones ?? new Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>();
        }

        public DesktopLayoutPlan CreatePlan()
        {
            NativeDesktopListViewState listState = _layout.GetListViewState();
            Rectangle workArea = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens.First().WorkingArea;
            int gridX = Math.Max(listState.GridSpacingX, 1);
            int gridY = Math.Max(listState.GridSpacingY, 1);
            int dpi = TryGetDpi(listState.Handle);
            IReadOnlyList<NativeDesktopIconState> nativeIcons = _layout.EnumerateDesktopIcons();

            var plan = new DesktopLayoutPlan
            {
                WorkAreaLeft = workArea.Left,
                WorkAreaTop = workArea.Top,
                WorkAreaWidth = workArea.Width,
                WorkAreaHeight = workArea.Height,
                Dpi = dpi,
                GridSpacingX = gridX,
                GridSpacingY = gridY,
                AutoArrangeEnabled = listState.AutoArrangeEnabled,
                AlignToGridEnabled = listState.AlignToGridEnabled,
                CentralClearZoneLeft = workArea.Left + (int)(workArea.Width * 0.35),
                CentralClearZoneTop = workArea.Top + (int)(workArea.Height * 0.25),
                CentralClearZoneWidth = (int)(workArea.Width * 0.30),
                CentralClearZoneHeight = (int)(workArea.Height * 0.50),
                Groups = CreateGroups(workArea, gridX, gridY)
            };

            foreach (NativeDesktopIconState icon in nativeIcons)
                plan.Items.Add(ToLayoutItem(icon));

            var occupied = new HashSet<string>(nativeIcons.Select(icon => SlotKey(icon.X, icon.Y, workArea, gridX, gridY)), StringComparer.Ordinal);
            foreach (DesktopLayoutGroup group in plan.Groups)
            {
                NativeDesktopLayoutGroupId groupId = group.GroupId;
                List<DesktopLayoutItem> groupItems = plan.Items
                    .Where(item => ParseGroup(item.LayoutGroupId) == groupId)
                    .OrderBy(item => SortBucket(groupId, item))
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                group.PlannedItemCount = groupItems.Count;

                if (groupId == NativeDesktopLayoutGroupId.UNASSIGNED)
                    continue;

                // A group's own movable icons do not obstruct its next plan.
                // Other groups and every unmanaged/ambiguous icon remain
                // occupied so an individual batch can still be applied safely.
                foreach (DesktopLayoutItem item in groupItems.Where(IsMovableCandidate))
                    occupied.Remove(SlotKey(item.CurrentX, item.CurrentY, workArea, gridX, gridY));

                List<DesktopLayoutItem> movableItems = groupItems.Where(IsMovableCandidate).ToList();
                if (movableItems.Count == 0)
                    continue;

                RectangleSlots region = RegionFor(groupId, workArea, gridX, gridY);
                CompactBlock? block = FindCompactBlock(groupId, movableItems.Count, region, occupied, workArea, gridX, gridY);
                if (block == null)
                    block = FindCompactBlock(groupId, movableItems.Count, FullFallbackRegion(groupId, workArea, gridX, gridY),
                        occupied, workArea, gridX, gridY, usedFallback: true);

                if (block == null)
                {
                    foreach (DesktopLayoutItem item in movableItems)
                        item.ApplyReason = "SKIP_NO_CONTIGUOUS_COMPACT_BLOCK";
                    continue;
                }

                group.Columns = block.Value.Columns;
                group.Rows = block.Value.Rows;
                group.BoundingWidthGridSlots = block.Value.Columns;
                group.BoundingHeightGridSlots = block.Value.Rows;
                group.AspectRatio = Math.Round(block.Value.Columns / (double)block.Value.Rows, 3);
                group.FragmentationCount = 1;
                group.UsedFallbackRegion = block.Value.UsedFallback;

                ReserveBlock(block.Value, occupied, workArea, gridX, gridY);
                for (int index = 0; index < movableItems.Count; index++)
                {
                    DesktopLayoutItem item = movableItems[index];
                    int x = workArea.Left + ((block.Value.Left + (index % block.Value.Columns)) * gridX);
                    int y = workArea.Top + ((block.Value.Top + (index / block.Value.Columns)) * gridY);

                    item.TargetX = x;
                    item.TargetY = y;
                    string currentSlot = SlotKey(item.CurrentX, item.CurrentY, workArea, gridX, gridY);
                    string targetSlot = SlotKey(x, y, workArea, gridX, gridY);
                    item.IsEligibleForApply = !plan.AutoArrangeEnabled &&
                        !string.Equals(currentSlot, targetSlot, StringComparison.Ordinal);
                    item.ApplyReason = item.IsEligibleForApply ? "HIGH_CONFIDENCE_STABLE_PATH" : "ALREADY_AT_TARGET";
                    if (item.IsEligibleForApply) group.EligibleItemCount++;
                }

                foreach (DesktopLayoutItem item in groupItems.Except(movableItems))
                    item.ApplyReason = "SKIP_NOT_HIGH_CONFIDENCE_STABLE";
            }

            plan.DuplicateTargetCoordinates = plan.Items.GroupBy(item => $"{item.TargetX},{item.TargetY}").Count(group => group.Count() > 1);
            plan.TargetOutsideDesktopWorkArea = plan.Items.Count(item => !Contains(workArea, item.TargetX, item.TargetY));
            Rectangle clearZone = new(plan.CentralClearZoneLeft, plan.CentralClearZoneTop,
                plan.CentralClearZoneWidth, plan.CentralClearZoneHeight);
            plan.CentralClearZonePreserved = !plan.Items.Any(item => IsPrimaryReflowGroup(ParseGroup(item.LayoutGroupId)) &&
                IsMovableCandidate(item) && Contains(clearZone, item.TargetX, item.TargetY));
            return plan;
        }

        public DesktopLayoutPlan PlanFullDesktopLayout()
        {
            NativeDesktopListViewState listState = _layout.GetListViewState();
            Rectangle workArea = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens.First().WorkingArea;
            int gridX = Math.Max(listState.GridSpacingX, 1);
            int gridY = Math.Max(listState.GridSpacingY, 1);
            int dpi = TryGetDpi(listState.Handle);
            IReadOnlyList<NativeDesktopIconState> nativeIcons = _layout.EnumerateDesktopIcons();

            var plan = new DesktopLayoutPlan
            {
                Version = 3,
                LayoutMode = "FullDesktopConsolidation",
                UnassignedLayoutMode = "COMPACT_VISUALLY",
                WorkAreaLeft = workArea.Left,
                WorkAreaTop = workArea.Top,
                WorkAreaWidth = workArea.Width,
                WorkAreaHeight = workArea.Height,
                Dpi = dpi,
                GridSpacingX = gridX,
                GridSpacingY = gridY,
                AutoArrangeEnabled = listState.AutoArrangeEnabled,
                AlignToGridEnabled = listState.AlignToGridEnabled,
                CentralClearZoneLeft = workArea.Left + (int)(workArea.Width * 0.35),
                CentralClearZoneTop = workArea.Top + (int)(workArea.Height * 0.20),
                CentralClearZoneWidth = (int)(workArea.Width * 0.32),
                CentralClearZoneHeight = (int)(workArea.Height * 0.62),
                Groups = CreateFullGroups(workArea, gridX, gridY)
            };

            foreach (NativeDesktopIconState icon in nativeIcons)
                plan.Items.Add(ToLayoutItem(icon));

            var occupied = new HashSet<string>(nativeIcons.Select(icon => SlotKey(icon.X, icon.Y, workArea, gridX, gridY)), StringComparer.Ordinal);
            foreach (DesktopLayoutItem item in plan.Items.Where(IsFullMovableCandidate))
                occupied.Remove(SlotKey(item.CurrentX, item.CurrentY, workArea, gridX, gridY));

            Rectangle centralClearZone = new(plan.CentralClearZoneLeft, plan.CentralClearZoneTop, plan.CentralClearZoneWidth, plan.CentralClearZoneHeight);
            var reservedBlocks = new List<CompactBlock>();

            foreach (DesktopLayoutGroup group in plan.Groups)
            {
                NativeDesktopLayoutGroupId groupId = group.GroupId;
                List<DesktopLayoutItem> groupItems = plan.Items
                    .Where(item => ParseGroup(item.LayoutGroupId) == groupId)
                    .OrderBy(item => SortBucket(groupId, item))
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                group.PlannedItemCount = groupItems.Count;

                List<DesktopLayoutItem> movableItems = groupItems.Where(IsFullMovableCandidate).ToList();
                if (movableItems.Count == 0)
                    continue;

                RectangleSlots region = FullRegionFor(groupId, workArea, gridX, gridY);
                CompactBlock? block = FindFullCompactBlock(groupId, movableItems.Count, region, occupied, workArea, gridX, gridY, centralClearZone, reservedBlocks);
                if (block == null)
                {
                    foreach (DesktopLayoutItem item in movableItems)
                        item.ApplyReason = "SKIP_NO_CLEAN_FULL_DESKTOP_BLOCK";
                    continue;
                }

                group.Columns = block.Value.Columns;
                group.Rows = block.Value.Rows;
                group.BoundingWidthGridSlots = block.Value.Columns;
                group.BoundingHeightGridSlots = block.Value.Rows;
                group.AspectRatio = Math.Round(block.Value.Columns / (double)block.Value.Rows, 3);
                group.FragmentationCount = 1;
                group.UsedFallbackRegion = block.Value.UsedFallback;

                ReserveBlock(block.Value, occupied, workArea, gridX, gridY);
                reservedBlocks.Add(block.Value);

                string layoutArea = LayoutAreaForGroup(groupId);

                for (int index = 0; index < movableItems.Count; index++)
                {
                    DesktopLayoutItem item = movableItems[index];
                    int x = workArea.Left + ((block.Value.Left + (index % block.Value.Columns)) * gridX);
                    int y = workArea.Top + ((block.Value.Top + (index / block.Value.Columns)) * gridY);

                    item.TargetX = x;
                    item.TargetY = y;
                    item.LayoutArea = layoutArea;
                    string currentSlot = SlotKey(item.CurrentX, item.CurrentY, workArea, gridX, gridY);
                    string targetSlot = SlotKey(x, y, workArea, gridX, gridY);
                    item.IsEligibleForApply = !plan.AutoArrangeEnabled && !string.Equals(currentSlot, targetSlot, StringComparison.Ordinal);
                    item.ApplyReason = item.IsEligibleForApply ? "FULL_DESKTOP_STABLE_IDENTITY" : "ALREADY_AT_TARGET";
                    if (item.IsEligibleForApply)
                        group.EligibleItemCount++;
                }

                foreach (DesktopLayoutItem item in groupItems.Except(movableItems))
                    item.ApplyReason = "PRESERVED_OBSTACLE";
            }

            plan.DuplicateTargetCoordinates = plan.Items.GroupBy(item => $"{item.TargetX},{item.TargetY}").Count(group => group.Count() > 1);
            plan.TargetOutsideDesktopWorkArea = plan.Items.Count(item => !Contains(workArea, item.TargetX, item.TargetY));
            plan.CentralClearZonePreserved = !plan.Items.Any(item => IsFullMovableCandidate(item) && Contains(centralClearZone, item.TargetX, item.TargetY));
            return plan;
        }

        public DesktopLayoutPlan PlanDualRailLayout()
        {
            NativeDesktopListViewState listState = _layout.GetListViewState();
            int dpi = TryGetDpi(listState.Handle);
            // Grid, work area and icon positions are all physical pixels, so a
            // process that outlived a scaling change plans exactly as a new one would.
            NativeDesktopSafeGrid safeGrid = _layout.GetSafeDesktopGrid();
            Rectangle workArea = DesktopChromeDpi.PrimaryWorkArea();
            IReadOnlyList<NativeDesktopIconState> nativeIcons = ReadDesktopIcons();
            return PlanDualRailLayout(listState, safeGrid, workArea, dpi, nativeIcons.Select(ToLayoutItem).ToList());
        }

        /// <summary>
        /// Explorer answers a label request with nothing while it is busy
        /// rebuilding the desktop - right after a display change, for one. A plan
        /// made from that read treats every unnamed icon as an unknown obstacle and
        /// lays out a fraction of the desktop, so the read is retried, and a desktop
        /// that stays unreadable is refused rather than planned.
        /// </summary>
        private IReadOnlyList<NativeDesktopIconState> ReadDesktopIcons()
        {
            for (int attempt = 1; ; attempt++)
            {
                IReadOnlyList<NativeDesktopIconState> icons = _layout.EnumerateDesktopIcons();
                int unreadable = icons.Count(icon => string.IsNullOrEmpty(icon.DisplayName));
                if (unreadable == 0) return icons;
                if (attempt == 3)
                    throw new InvalidOperationException(
                        $"DESKTOP_LABELS_UNREADABLE Unreadable={unreadable}; Icons={icons.Count}");
                System.Threading.Thread.Sleep(300);
            }
        }

        /// <summary>
        /// Plans today's desktop items for a display that is not connected, so its
        /// scheme can be checked in advance. Each icon keeps its grid slot, clamped
        /// onto the other grid; only the icons DesktopHub never moves care where
        /// they start. Nothing is applied.
        /// </summary>
        internal DesktopLayoutPlan SimulateDualRailLayout(NativeDesktopSafeGrid safeGrid, Rectangle workArea, int dpi)
        {
            NativeDesktopListViewState listState = _layout.GetListViewState();
            NativeDesktopSafeGrid live = _layout.GetSafeDesktopGrid();
            var items = new List<DesktopLayoutItem>();
            foreach (NativeDesktopIconState icon in ReadDesktopIcons())
            {
                int column = Math.Clamp((int)Math.Round(icon.X / (double)live.IconSpacingX), 0, safeGrid.SafeColumns - 1);
                int row = Math.Clamp((int)Math.Round(icon.Y / (double)live.IconSpacingY), 0, safeGrid.SafeRows - 1);
                items.Add(ToLayoutItem(icon, workArea.Left + (column * safeGrid.IconSpacingX),
                    workArea.Top + (row * safeGrid.IconSpacingY)));
            }
            return PlanDualRailLayout(listState, safeGrid, workArea, dpi, items);
        }

        /// <summary>
        /// Plans a captured desktop. Nothing from here down reads Explorer, so the
        /// inputs of a recorded plan can be replayed through it.
        ///
        /// The classic layout - one column of groups per rail - is always tried
        /// first and used whenever it fits, so a desktop it suits gets exactly the
        /// plan it always got. Only when the screen is too short for it, as at
        /// 150% scaling, are groups stood side by side within a rail.
        /// </summary>
        internal DesktopLayoutPlan PlanDualRailLayout(NativeDesktopListViewState listState, NativeDesktopSafeGrid safeGrid,
            Rectangle workArea, int dpi, IReadOnlyList<DesktopLayoutItem> items)
        {
            try
            {
                return PlanDualRail(listState, safeGrid, workArea, dpi, items, stacked: false);
            }
            catch (InvalidOperationException ex) when (IsCapacityFailure(ex))
            {
                // The failed attempt may already have placed part of the desktop.
                foreach (DesktopLayoutItem item in items)
                    ResetPlacement(item);
                return PlanDualRail(listState, safeGrid, workArea, dpi, items, stacked: true);
            }
        }

        private DesktopLayoutPlan PlanDualRail(NativeDesktopListViewState listState, NativeDesktopSafeGrid safeGrid,
            Rectangle workArea, int dpi, IReadOnlyList<DesktopLayoutItem> items, bool stacked)
        {
            var plan = new DesktopLayoutPlan
            {
                Version = 4,
                LayoutMode = NativeDesktopLayoutMode.DualRail.ToString(),
                UnassignedLayoutMode = "COMPACT_VISUALLY",
                WorkAreaLeft = workArea.Left,
                WorkAreaTop = workArea.Top,
                WorkAreaWidth = workArea.Width,
                WorkAreaHeight = workArea.Height,
                Dpi = dpi,
                GridSpacingX = safeGrid.IconSpacingX,
                GridSpacingY = safeGrid.IconSpacingY,
                ListViewClientWidth = safeGrid.ClientWidth,
                ListViewClientHeight = safeGrid.ClientHeight,
                SafeColumns = safeGrid.SafeColumns,
                SafeRows = safeGrid.SafeRows,
                SafeRightMargin = safeGrid.RightSafetyMargin,
                SafeBottomMargin = safeGrid.BottomSafetyMargin,
                AutoArrangeEnabled = listState.AutoArrangeEnabled,
                AlignToGridEnabled = listState.AlignToGridEnabled,
                PrimaryMonitorOnly = true,
                Groups = CreateDualRailGroups(workArea, safeGrid)
            };

            plan.Items.AddRange(items);

            // An obstacle is an icon this plan will not place - the shell items and
            // anything whose identity stayed unresolved. It is deliberately NOT
            // "anything without a stable path": an icon whose label is shared but
            // whose group is settled does get placed, so treating it as an
            // obstacle would make a group block its own members.
            var obstacles = plan.Items
                .Where(item => !IsFullMovableCandidate(item))
                .Select(item => SlotKey(item.CurrentX, item.CurrentY, workArea,
                    safeGrid.IconSpacingX, safeGrid.IconSpacingY))
                .ToHashSet(StringComparer.Ordinal);

            // A group the user dragged is pinned first, and its slots become
            // obstacles so the automatic rail flows around it instead of
            // silently reclaiming the position.
            PlaceAnchoredGroups(plan, obstacles, workArea, safeGrid);

            (int leftColumn, int leftWidth, int rightColumn) = stacked
                ? PlanStackedRails(plan, obstacles, workArea, safeGrid)
                : PlanSingleColumnRails(plan, obstacles, workArea, safeGrid);

            int centralLeft = (leftColumn + leftWidth) * safeGrid.IconSpacingX;
            int centralRight = rightColumn * safeGrid.IconSpacingX;
            plan.CentralClearZoneLeft = workArea.Left + centralLeft;
            plan.CentralClearZoneTop = workArea.Top;
            plan.CentralClearZoneWidth = Math.Max(0, centralRight - centralLeft);
            plan.CentralClearZoneHeight = safeGrid.SafeRows * safeGrid.IconSpacingY;
            ValidateRailAlignment(plan, safeGrid);
            plan.DuplicateTargetCoordinates = plan.Items.GroupBy(item => $"{item.TargetX},{item.TargetY}")
                .Count(group => group.Count() > 1);
            plan.TargetOutsideDesktopWorkArea = plan.Items.Count(item => !Contains(workArea, item.TargetX, item.TargetY));
            int outOfSafeGrid = plan.Items.Count(item => item.IsStableIdentity && !item.IsShellNamespace &&
                (!IsSafeGridTarget(item.TargetX, item.TargetY, workArea, safeGrid)));
            if (outOfSafeGrid != 0)
                throw new InvalidOperationException($"DUAL_RAIL_OUT_OF_SAFE_GRID Count={outOfSafeGrid}");

            Rectangle centralClearZone = new(plan.CentralClearZoneLeft, plan.CentralClearZoneTop,
                plan.CentralClearZoneWidth, plan.CentralClearZoneHeight);

            // A group the user pinned by hand is an explicit instruction about
            // where it belongs, so it is exempt from the clear-zone check.
            //
            // The zone is not a fixed region here: it is whatever gap is left
            // between the two automatic rails, and the rails are resolved AFTER
            // PlaceAnchoredGroups has already pinned the anchored groups. Move a
            // group toward the middle and the rails close in behind it, so the
            // gap grows and swallows the very group the user just placed. Every
            // later apply is then refused, and moving the group further out only
            // moves the wall with it - the user cannot win by hand.
            //
            // The zone exists to stop the automatic planner from filling the
            // wallpaper. It has no business overruling a deliberate placement.
            HashSet<string> anchoredGroups = plan.Groups
                .Where(group => group.HasManualAnchor)
                .Select(group => group.GroupId.ToString())
                .ToHashSet(StringComparer.Ordinal);

            plan.CentralClearZonePreserved = !plan.Items.Any(item => IsFullMovableCandidate(item) &&
                !anchoredGroups.Contains(item.LayoutGroupId) &&
                Contains(centralClearZone, item.TargetX, item.TargetY));
            return plan;
        }

        private static List<DesktopLayoutGroup> CreateDualRailGroups(Rectangle workArea, NativeDesktopSafeGrid safeGrid) => new()
        {
            DualRailGroup(NativeDesktopLayoutGroupId.SYSTEM_TEMP, "系统 · 临时", DesktopRail.Left, safeGrid),
            DualRailGroup(NativeDesktopLayoutGroupId.RESEARCH_STUDY, "科研 · 学习", DesktopRail.Left, safeGrid),
            DualRailGroup(NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, "办公 · 文档", DesktopRail.Left, safeGrid),
            DualRailGroup(NativeDesktopLayoutGroupId.UNASSIGNED, "待整理", DesktopRail.Left, safeGrid),
            DualRailGroup(NativeDesktopLayoutGroupId.AI_DEV, "AI · 开发", DesktopRail.Right, safeGrid),
            DualRailGroup(NativeDesktopLayoutGroupId.DAILY_TOOLS, "日常工具", DesktopRail.Right, safeGrid),
            DualRailGroup(NativeDesktopLayoutGroupId.GAMES, "游戏", DesktopRail.Right, safeGrid)
        };

        private static DesktopLayoutGroup DualRailGroup(
            NativeDesktopLayoutGroupId id,
            string title,
            DesktopRail rail,
            NativeDesktopSafeGrid safeGrid) => new()
        {
            GroupId = id,
            GroupDisplayName = title,
            Region = rail == DesktopRail.Left ? "Left rail" : "Right rail",
            Anchor = rail.ToString(),
            Rail = rail,
            HomeRegionLeft = 0,
            HomeRegionTop = 0,
            HomeRegionRight = safeGrid.ClientWidth,
            HomeRegionBottom = safeGrid.ClientHeight
        };

        /// <summary>Widest group in the rail, in grid columns. This is the rail's band width.</summary>
        private int RailWidth(DesktopLayoutPlan plan, DesktopRail rail, int extraColumns)
        {
            int width = 0;
            foreach (DesktopLayoutGroup group in plan.Groups.Where(group => group.Rail == rail && !group.HasManualAnchor))
            {
                int movableCount = MovableCount(plan, group.GroupId);
                if (movableCount == 0)
                    continue;
                (int columns, _) = ResolveShape(group.GroupId, movableCount, extraColumns);
                width = Math.Max(width, columns);
            }
            return width;
        }

        private int RailRequiredRows(DesktopLayoutPlan plan, DesktopRail rail, int extraColumns)
        {
            int rows = 0;
            foreach (DesktopLayoutGroup group in plan.Groups.Where(group => group.Rail == rail && !group.HasManualAnchor))
            {
                int movableCount = MovableCount(plan, group.GroupId);
                if (movableCount == 0)
                    continue;
                (_, int groupRows) = ResolveShape(group.GroupId, movableCount, extraColumns);
                // Every group reserves the same leading row whether or not it
                // draws a header, so the vertical rhythm is identical.
                rows += groupRows + DesktopLayoutSpacing.GroupLeadingRows;
            }
            return rows;
        }

        private static int MovableCount(DesktopLayoutPlan plan, NativeDesktopLayoutGroupId groupId) =>
            plan.Items.Count(item => ParseGroup(item.LayoutGroupId) == groupId && IsFullMovableCandidate(item));

        /// <summary>The left rail hugs the left edge: the first column whose whole band is clear.</summary>
        private static int ResolveLeftRailColumn(int width, int requiredRows, int startRow,
            ISet<string> obstacles, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            if (width <= 0) return 0;
            int rows = Math.Min(safeGrid.SafeRows - startRow, Math.Max(1, requiredRows));
            for (int column = 0; column + width <= safeGrid.SafeColumns; column++)
            {
                if (RailBandIsClear(column, width, startRow, rows, obstacles, workArea, safeGrid))
                    return column;
            }
            throw new InvalidOperationException($"DUAL_RAIL_NO_ALIGNED_COLUMN Rail=Left; Width={width}; Rows={rows}");
        }

        /// <summary>
        /// The right rail is mirrored: its preferred inset from the right edge
        /// equals the left rail's inset from the left edge, so both rails read
        /// as one deliberate frame around the central clear zone.
        /// </summary>
        private static int ResolveRightRailColumn(int leftColumn, int leftWidth, int width, int requiredRows,
            int startRow, ISet<string> obstacles, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            if (width <= 0) return Math.Max(0, safeGrid.SafeColumns - 1);
            int rows = Math.Min(safeGrid.SafeRows - startRow, Math.Max(1, requiredRows));
            int minimum = leftColumn + leftWidth + MinimumCentralColumns;
            int maximum = safeGrid.SafeColumns - width;
            if (maximum < minimum)
                throw new InvalidOperationException($"DUAL_RAIL_TOO_NARROW Rail=Right; Width={width}");

            int mirrored = Math.Clamp(safeGrid.SafeColumns - leftColumn - width, minimum, maximum);
            foreach (int column in CandidateColumnsAround(mirrored, minimum, maximum))
            {
                if (RailBandIsClear(column, width, startRow, rows, obstacles, workArea, safeGrid))
                    return column;
            }
            throw new InvalidOperationException($"DUAL_RAIL_NO_ALIGNED_COLUMN Rail=Right; Width={width}; Rows={rows}");
        }

        /// <summary>Preferred column first, then the nearest alternatives on either side.</summary>
        private static IEnumerable<int> CandidateColumnsAround(int preferred, int minimum, int maximum)
        {
            yield return preferred;
            for (int offset = 1; offset <= maximum - minimum; offset++)
            {
                if (preferred + offset <= maximum) yield return preferred + offset;
                if (preferred - offset >= minimum) yield return preferred - offset;
            }
        }

        private static bool RailBandIsClear(int column, int width, int startRow, int rows,
            ISet<string> obstacles, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            for (int row = startRow; row < startRow + rows; row++)
            {
                for (int col = column; col < column + width; col++)
                {
                    int x = workArea.Left + (col * safeGrid.IconSpacingX);
                    int y = workArea.Top + (row * safeGrid.IconSpacingY);
                    if (obstacles.Contains(SlotKey(x, y, workArea, safeGrid.IconSpacingX, safeGrid.IconSpacingY)))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The slots a laid-out group holds, in grid columns and rows: its icon
        /// rows and the leading row above them that its header is drawn in. An
        /// anchored group reserves exactly this block, so a later anchor whose
        /// icons reach into it is refused, and a header drag checks its
        /// destination against the blocks of every other group.
        /// </summary>
        internal static Rectangle GroupBlock(int column, int iconRow, int columns, int rows) =>
            new(column, iconRow - DesktopLayoutSpacing.GroupLeadingRows,
                columns, rows + DesktopLayoutSpacing.GroupLeadingRows);

        /// <summary>
        /// Places every group the user positioned by hand at its stored grid
        /// anchor and reserves those slots, and records lock and manual-size
        /// state on every group that has any. A manual position always wins over
        /// the planner; a manual size alone still lets the group flow normally,
        /// it just flows at the shape the user chose.
        /// </summary>
        private void PlaceAnchoredGroups(DesktopLayoutPlan plan, ISet<string> obstacles,
            Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            foreach (DesktopLayoutGroup group in plan.Groups)
            {
                if (!_groupZones.TryGetValue(group.GroupId, out DesktopGroupZone? zone) || zone == null)
                    continue;
                group.Locked = zone.Locked;
                group.HasManualSize = zone.IsManualSize;
                if (!zone.IsManualPosition)
                    continue;
                // A pinned position is a grid slot, and a slot only means
                // something on the grid it was chosen on. After a resolution or
                // scaling change it can name a column that no longer exists;
                // clamping it would drop the group where the user never put it
                // and wedge both rails. The group flows automatically instead,
                // and the pin is kept for when that grid comes back.
                if (!AnchorBelongsToGrid(zone, safeGrid))
                    continue;
                List<DesktopLayoutItem> movableItems = OrderedGroupItems(plan, group).Where(IsFullMovableCandidate).ToList();
                group.PlannedItemCount = plan.Items.Count(item => ParseGroup(item.LayoutGroupId) == group.GroupId);
                if (movableItems.Count == 0)
                    continue;

                (int columns, int rows) = ResolveShape(group.GroupId, movableItems.Count, zone.ExtraColumns);
                int column = Math.Clamp(zone.Column, 0, Math.Max(0, safeGrid.SafeColumns - columns));
                int iconRow = Math.Clamp(zone.Row, DesktopLayoutSpacing.GroupLeadingRows,
                    Math.Max(DesktopLayoutSpacing.GroupLeadingRows, safeGrid.SafeRows - rows));
                // The anchor's own header row is deliberately not checked. A
                // refused anchor flows automatically, so every extra reason to
                // refuse one moves a group the user placed. A header row that
                // lands on an earlier group's last icon row - that group gained
                // icons or was resized - keeps the anchor, header over that row.
                // A drag cannot create the overlap: DesktopGroupDragService
                // refuses any destination that reaches into another group's block.
                if (!RailBandIsClear(column, columns, iconRow, rows, obstacles, workArea, safeGrid))
                    continue; // A stale anchor never overlaps an icon we cannot move.

                group.HasManualAnchor = true;
                ApplyGroupGeometry(plan, group, movableItems, column, iconRow, columns, rows, workArea, safeGrid,
                    zone.ExtraColumns);
                Rectangle block = GroupBlock(column, iconRow, columns, rows);
                for (int row = block.Top; row < block.Bottom; row++)
                    for (int col = block.Left; col < block.Right; col++)
                        obstacles.Add(SlotKey(workArea.Left + (col * safeGrid.IconSpacingX),
                            workArea.Top + (row * safeGrid.IconSpacingY), workArea,
                            safeGrid.IconSpacingX, safeGrid.IconSpacingY));
            }
        }

        private static List<DesktopLayoutItem> OrderedGroupItems(DesktopLayoutPlan plan, DesktopLayoutGroup group) =>
            plan.Items
                .Where(item => ParseGroup(item.LayoutGroupId) == group.GroupId)
                .OrderBy(item => SortBucket(group.GroupId, item))
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Writes bounding rect, header geometry, and item targets for one placed group.</summary>
        private static void ApplyGroupGeometry(DesktopLayoutPlan plan, DesktopLayoutGroup group,
            List<DesktopLayoutItem> movableItems, int column, int iconRow, int columns, int rows,
            Rectangle workArea, NativeDesktopSafeGrid safeGrid, int extraColumns)
        {
            group.ExtraColumns = extraColumns;
            group.Columns = columns;
            group.Rows = rows;
            group.RailColumn = column;
            group.RailRow = iconRow;
            group.BoundingWidthGridSlots = columns;
            group.BoundingHeightGridSlots = rows;
            group.AspectRatio = Math.Round(columns / (double)rows, 3);
            group.FragmentationCount = 1;
            group.UsedFallbackRegion = false;
            group.IconBoundingLeft = workArea.Left + (column * safeGrid.IconSpacingX);
            group.IconBoundingTop = workArea.Top + (iconRow * safeGrid.IconSpacingY);
            group.IconBoundingRight = group.IconBoundingLeft + (columns * safeGrid.IconSpacingX);
            group.IconBoundingBottom = group.IconBoundingTop + (rows * safeGrid.IconSpacingY);

            // SYSTEM_TEMP used to be drawn without a header, on the grounds that
            // a pair of shell shortcuts did not deserve a real group's visual
            // weight. But the header is not decoration - it carries the entire
            // context menu. Hiding it left that group as the only one the user
            // could not resize, lock, or reset, and gave them no way to tell what
            // the empty rectangle on their wallpaper even was.
            group.ShowHeader = true;
            // The header is drawn in DIPs, but the plan is pixels.
            group.HeaderHeight = DesktopLayoutSpacing.HeaderHeightAt(plan.Dpi);
            group.HeaderLeft = group.IconBoundingLeft;
            group.HeaderTop = Math.Max(0, group.IconBoundingTop
                - DesktopLayoutSpacing.HeaderToFirstIconAt(plan.Dpi) - group.HeaderHeight);
            group.HeaderWidth = columns * safeGrid.IconSpacingX;

            for (int index = 0; index < movableItems.Count; index++)
            {
                DesktopLayoutItem item = movableItems[index];
                int x = workArea.Left + ((column + (index % columns)) * safeGrid.IconSpacingX);
                int y = workArea.Top + ((iconRow + (index / columns)) * safeGrid.IconSpacingY);
                item.TargetX = x;
                item.TargetY = y;
                item.LayoutArea = group.Rail.ToString();
                string currentSlot = SlotKey(item.CurrentX, item.CurrentY, workArea, safeGrid.IconSpacingX, safeGrid.IconSpacingY);
                string targetSlot = SlotKey(x, y, workArea, safeGrid.IconSpacingX, safeGrid.IconSpacingY);
                item.IsEligibleForApply = !plan.AutoArrangeEnabled &&
                    !string.Equals(currentSlot, targetSlot, StringComparison.Ordinal);
                item.ApplyReason = item.IsEligibleForApply ? "DUAL_RAIL_STABLE_IDENTITY" : "ALREADY_AT_TARGET";
                if (item.IsEligibleForApply)
                    group.EligibleItemCount++;
            }
        }

        private void PlanRail(
            DesktopLayoutPlan plan,
            DesktopRail rail,
            int railColumn,
            int startRow,
            ISet<string> obstacles,
            Rectangle workArea,
            NativeDesktopSafeGrid safeGrid,
            int extraColumns)
        {
            int cursor = startRow;
            foreach (DesktopLayoutGroup group in plan.Groups.Where(group => group.Rail == rail))
            {
                if (group.HasManualAnchor)
                    continue;
                List<DesktopLayoutItem> groupItems = OrderedGroupItems(plan, group);
                group.PlannedItemCount = groupItems.Count;
                List<DesktopLayoutItem> movableItems = groupItems.Where(IsFullMovableCandidate).ToList();
                if (movableItems.Count == 0)
                    continue;

                (int columns, int rows) = ResolveShape(group.GroupId, movableItems.Count, extraColumns);
                int block = rows + DesktopLayoutSpacing.GroupLeadingRows;
                // Only the row is searched. The column is the rail baseline, so
                // groups can never drift out of alignment with one another.
                int? placedLeadingRow = null;
                for (int leadingRow = cursor; leadingRow + block <= safeGrid.SafeRows; leadingRow++)
                {
                    if (!RailBandIsClear(railColumn, columns, leadingRow, block, obstacles, workArea, safeGrid))
                        continue;
                    placedLeadingRow = leadingRow;
                    break;
                }

                if (!placedLeadingRow.HasValue)
                    throw new InvalidOperationException($"DUAL_RAIL_GROUP_NOT_PLACED {group.GroupId}");

                int iconRow = placedLeadingRow.Value + DesktopLayoutSpacing.GroupLeadingRows;
                ApplyGroupGeometry(plan, group, movableItems, railColumn, iconRow, columns, rows, workArea, safeGrid,
                    extraColumns);
                cursor = iconRow + rows;
            }
        }

        /// <summary>Records whether every column of groups really did resolve to one X baseline.</summary>
        private static void ValidateRailAlignment(DesktopLayoutPlan plan, NativeDesktopSafeGrid safeGrid)
        {
            plan.LeftRailAligned = RailIsAligned(plan, DesktopRail.Left, plan.LeftRailX, plan.LeftRailStackCount);
            plan.RightRailAligned = RailIsAligned(plan, DesktopRail.Right, plan.RightRailX, plan.RightRailStackCount);
            int headerToFirstIcon = DesktopLayoutSpacing.HeaderToFirstIconAt(plan.Dpi);
            plan.GroupSeparationGap = safeGrid.IconSpacingY - headerToFirstIcon
                - DesktopLayoutSpacing.HeaderHeightAt(plan.Dpi);
            plan.HeaderToFirstIconGap = headerToFirstIcon;
            // Uniform by construction: every group derives its header offset and
            // leading row from DesktopLayoutSpacing, never from local arithmetic.
            plan.GroupSpacingConsistent = plan.Groups
                .Where(group => group.ShowHeader && group.HeaderWidth > 0)
                .All(group => group.IconBoundingTop - group.HeaderTop - group.HeaderHeight == headerToFirstIcon);
        }

        /// <summary>
        /// A single-column rail has one baseline, the rail's own. A stacked rail
        /// has one per stack, and every group in a stack must hold to it.
        /// </summary>
        private static bool RailIsAligned(DesktopLayoutPlan plan, DesktopRail rail, int railX, int stackCount)
        {
            List<DesktopLayoutGroup> groups = plan.Groups
                .Where(group => group.Rail == rail && group.Columns > 0 && !group.HasManualAnchor)
                .ToList();
            if (stackCount <= 1)
                return groups.All(group => group.IconBoundingLeft == railX &&
                    (!group.ShowHeader || group.HeaderLeft == railX));
            return groups.GroupBy(group => group.RailStack).All(stack =>
            {
                int baseline = stack.First().IconBoundingLeft;
                return stack.All(group => group.IconBoundingLeft == baseline &&
                    (!group.ShowHeader || group.HeaderLeft == baseline));
            });
        }

        private static bool IsSafeGridTarget(int x, int y, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            int relativeX = x - workArea.Left;
            int relativeY = y - workArea.Top;
            return relativeX >= 0 && relativeY >= 0 &&
                relativeX + safeGrid.IconSpacingX + safeGrid.RightSafetyMargin <= safeGrid.ClientWidth &&
                relativeY + safeGrid.IconSpacingY + safeGrid.BottomSafetyMargin <= safeGrid.ClientHeight;
        }

        /// <summary>
        /// Preferred shape of a group, widened by the rail's fitting term. The
        /// preferred column counts are the proven ones; extraColumns is what
        /// lets a rail absorb new icons by growing sideways instead of running
        /// off the bottom of the screen.
        /// </summary>
        /// <summary>
        /// Shape of a group, with the user's choice taking precedence.
        ///
        /// A manual size is a decision, not a hint: it is kept even as the group
        /// gains members. The one thing that cannot be honoured is a size that
        /// no longer holds the icons, so the manual COLUMN count is preserved -
        /// that is the part the user actually sees - and rows grow only as far
        /// as capacity demands.
        /// </summary>
        private (int Columns, int Rows) ResolveShape(NativeDesktopLayoutGroupId group, int itemCount, int extraColumns)
        {
            if (_groupZones.TryGetValue(group, out DesktopGroupZone? zone) && zone is { IsManualSize: true } &&
                zone.ManualColumns > 0 && zone.ManualRows > 0)
            {
                int columns = Math.Max(1, zone.ManualColumns);
                int rows = Math.Max(zone.ManualRows, (int)Math.Ceiling(Math.Max(1, itemCount) / (double)columns));
                return (columns, rows);
            }
            return DualRailShape(group, itemCount, extraColumns);
        }

        private static (int Columns, int Rows) DualRailShape(NativeDesktopLayoutGroupId group, int itemCount,
            int extraColumns = 0)
        {
            // A group never grows wider than it has icons to fill. A negative
            // extra narrows it, which only a stacked rail ever asks for.
            int columns = Math.Max(1, Math.Min(BaseColumns(group, itemCount) + extraColumns, Math.Max(1, itemCount)));
            return (columns, Math.Max(1, (int)Math.Ceiling(itemCount / (double)columns)));
        }

        /// <summary>The proven column count of each group, before any fitting.</summary>
        private static int BaseColumns(NativeDesktopLayoutGroupId group, int itemCount) => group switch
        {
            NativeDesktopLayoutGroupId.SYSTEM_TEMP => 2,
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => 4,
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => 4,
            NativeDesktopLayoutGroupId.UNASSIGNED => 5,
            NativeDesktopLayoutGroupId.AI_DEV => 4,
            NativeDesktopLayoutGroupId.DAILY_TOOLS => 4,
            NativeDesktopLayoutGroupId.GAMES => 5,
            _ => Math.Min(4, Math.Max(1, itemCount))
        };

        /// <summary>
        /// Smallest sideways growth that makes the whole rail fit the screen
        /// height. Widening the rail is always preferable to dropping a group
        /// off the bottom edge, and it keeps every group in the rail on the
        /// same X baseline.
        /// </summary>
        private int FitRailColumns(DesktopLayoutPlan plan, DesktopRail rail, NativeDesktopSafeGrid safeGrid)
        {
            for (int extra = 0; extra <= MaximumRailWidening; extra++)
            {
                if (RailWidth(plan, rail, extra) + MinimumCentralColumns > safeGrid.SafeColumns)
                    break;
                if (RailRequiredRows(plan, rail, extra) <= safeGrid.SafeRows)
                    return extra;
            }
            throw new InvalidOperationException(
                $"DUAL_RAIL_TOO_TALL Rail={rail}; SafeRows={safeGrid.SafeRows}; " +
                $"Required={RailRequiredRows(plan, rail, MaximumRailWidening)}");
        }

        /// <summary>The classic layout: each rail one column of groups, widened just enough to fit the height.</summary>
        private (int LeftColumn, int LeftWidth, int RightColumn) PlanSingleColumnRails(DesktopLayoutPlan plan,
            ISet<string> obstacles, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            // Each rail grows sideways just enough to fit the screen height.
            int leftExtraColumns = FitRailColumns(plan, DesktopRail.Left, safeGrid);
            int rightExtraColumns = FitRailColumns(plan, DesktopRail.Right, safeGrid);
            int leftRequiredRows = RailRequiredRows(plan, DesktopRail.Left, leftExtraColumns);
            int rightRequiredRows = RailRequiredRows(plan, DesktopRail.Right, rightExtraColumns);

            int leftWidth = RailWidth(plan, DesktopRail.Left, leftExtraColumns);
            int rightWidth = RailWidth(plan, DesktopRail.Right, rightExtraColumns);
            if (leftWidth + rightWidth + MinimumCentralColumns > safeGrid.SafeColumns)
                throw new InvalidOperationException(
                    $"DUAL_RAIL_TOO_NARROW SafeColumns={safeGrid.SafeColumns}; LeftWidth={leftWidth}; RightWidth={rightWidth}");

            int railStartRow = Math.Max(0, (safeGrid.SafeRows - Math.Max(leftRequiredRows, rightRequiredRows)) / 2);

            // Every group in a rail shares one X baseline. The baseline is the
            // outermost column whose whole rail band is free of icons we are
            // not allowed to move, so alignment never degrades into per-group
            // column drift.
            int leftColumn = ResolveLeftRailColumn(leftWidth, leftRequiredRows, railStartRow, obstacles, workArea, safeGrid);
            int rightColumn = ResolveRightRailColumn(leftColumn, leftWidth, rightWidth, rightRequiredRows,
                railStartRow, obstacles, workArea, safeGrid);

            plan.LeftRailColumn = leftColumn;
            plan.RightRailColumn = rightColumn;
            plan.LeftRailX = workArea.Left + (leftColumn * safeGrid.IconSpacingX);
            plan.RightRailX = workArea.Left + (rightColumn * safeGrid.IconSpacingX);

            PlanRail(plan, DesktopRail.Left, leftColumn, railStartRow, obstacles, workArea, safeGrid, leftExtraColumns);
            PlanRail(plan, DesktopRail.Right, rightColumn, railStartRow, obstacles, workArea, safeGrid, rightExtraColumns);
            plan.LeftRailColumnSlack = safeGrid.SafeRows - railStartRow - leftRequiredRows;
            plan.RightRailColumnSlack = safeGrid.SafeRows - railStartRow - rightRequiredRows;
            return (leftColumn, leftWidth, rightColumn);
        }

        /// <summary>Columns kept empty between two stacks of one rail, so neighbouring groups never read as one.</summary>
        private const int StackGapColumns = 1;

        /// <summary>How many of the best stacked layouts are tried against the icons that cannot move.</summary>
        private const int MaximumStackedAttempts = 64;

        private static bool IsCapacityFailure(InvalidOperationException ex) =>
            ex.Message.StartsWith("DUAL_RAIL_TOO_TALL", StringComparison.Ordinal) ||
            ex.Message.StartsWith("DUAL_RAIL_TOO_NARROW", StringComparison.Ordinal) ||
            ex.Message.StartsWith("DUAL_RAIL_NO_ALIGNED_COLUMN", StringComparison.Ordinal) ||
            ex.Message.StartsWith("DUAL_RAIL_GROUP_NOT_PLACED", StringComparison.Ordinal);

        private static void ResetPlacement(DesktopLayoutItem item)
        {
            item.TargetX = item.CurrentX;
            item.TargetY = item.CurrentY;
            item.LayoutArea = string.Empty;
            item.IsEligibleForApply = false;
            item.ApplyReason = "NOT_PLANNED";
        }

        private static bool AnchorBelongsToGrid(DesktopGroupZone zone, NativeDesktopSafeGrid safeGrid) =>
            zone.GridColumns > 0 && zone.GridRows > 0
                ? zone.GridColumns == safeGrid.SafeColumns && zone.GridRows == safeGrid.SafeRows
                // Older pins did not record their grid; trust one only while its
                // origin is still on the screen.
                : zone.Column < safeGrid.SafeColumns && zone.Row < safeGrid.SafeRows;

        /// <summary>
        /// Lays both rails out as stacks of groups standing side by side.
        ///
        /// Candidates are ranked by how far their groups stray from their
        /// preferred shapes, then by how few stacks they need, then by how much
        /// clear space they leave in the middle. The first whose groups can all
        /// be placed around the icons DesktopHub may not move is used.
        /// </summary>
        private (int LeftColumn, int LeftWidth, int RightColumn) PlanStackedRails(DesktopLayoutPlan plan,
            ISet<string> obstacles, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            foreach (DesktopLayoutGroup group in plan.Groups.Where(group => !group.HasManualAnchor))
                group.PlannedItemCount = plan.Items.Count(item => ParseGroup(item.LayoutGroupId) == group.GroupId);

            Dictionary<int, StackedRail> left = StackedRailOptions(plan, DesktopRail.Left, safeGrid);
            Dictionary<int, StackedRail> right = StackedRailOptions(plan, DesktopRail.Right, safeGrid);
            if (left.Count == 0 || right.Count == 0)
                throw new InvalidOperationException(
                    $"DUAL_RAIL_TOO_TALL Rail={(left.Count == 0 ? DesktopRail.Left : DesktopRail.Right)}; " +
                    $"SafeRows={safeGrid.SafeRows}; Stacked=True");

            List<(StackedRail Left, StackedRail Right)> candidates = left.Values
                .SelectMany(_ => right.Values, (leftRail, rightRail) => (Left: leftRail, Right: rightRail))
                .Where(pair => pair.Left.Width + pair.Right.Width + MinimumCentralColumns <= safeGrid.SafeColumns)
                .OrderBy(pair => pair.Left.Cost + pair.Right.Cost)
                .ThenBy(pair => pair.Left.Stacks.Count + pair.Right.Stacks.Count)
                .ThenBy(pair => pair.Left.Width + pair.Right.Width)
                .ThenByDescending(pair => pair.Left.OuterRows + pair.Right.OuterRows)
                .Take(MaximumStackedAttempts)
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    $"DUAL_RAIL_TOO_NARROW SafeColumns={safeGrid.SafeColumns}; LeftWidth={left.Keys.Min()}; " +
                    $"RightWidth={right.Keys.Min()}; Stacked=True");

            foreach ((StackedRail leftRail, StackedRail rightRail) in candidates)
            {
                int railStartRow = Math.Max(0, (safeGrid.SafeRows - Math.Max(leftRail.Rows, rightRail.Rows)) / 2);

                int leftColumn = -1;
                List<StackPlacement>? leftPlacements = null;
                for (int column = 0;
                     column + leftRail.Width + MinimumCentralColumns + rightRail.Width <= safeGrid.SafeColumns;
                     column++)
                {
                    leftPlacements = TryPlaceStackedRail(leftRail, DesktopRail.Left, column, railStartRow,
                        obstacles, workArea, safeGrid);
                    if (leftPlacements == null) continue;
                    leftColumn = column;
                    break;
                }
                if (leftPlacements == null) continue;

                // Mirrored, as with single-column rails: the right rail's inset
                // from the right edge matches the left rail's from the left.
                int minimum = leftColumn + leftRail.Width + MinimumCentralColumns;
                int maximum = safeGrid.SafeColumns - rightRail.Width;
                int mirrored = Math.Clamp(safeGrid.SafeColumns - leftColumn - rightRail.Width, minimum, maximum);
                int rightColumn = -1;
                List<StackPlacement>? rightPlacements = null;
                foreach (int column in CandidateColumnsAround(mirrored, minimum, maximum))
                {
                    rightPlacements = TryPlaceStackedRail(rightRail, DesktopRail.Right, column, railStartRow,
                        obstacles, workArea, safeGrid);
                    if (rightPlacements == null) continue;
                    rightColumn = column;
                    break;
                }
                if (rightPlacements == null) continue;

                foreach (StackPlacement placement in leftPlacements.Concat(rightPlacements))
                {
                    DesktopLayoutGroup group = placement.Shape.Group;
                    List<DesktopLayoutItem> movableItems = OrderedGroupItems(plan, group).Where(IsFullMovableCandidate).ToList();
                    group.RailStack = placement.Stack;
                    ApplyGroupGeometry(plan, group, movableItems, placement.Column, placement.IconRow,
                        placement.Shape.Columns, placement.Shape.Rows, workArea, safeGrid, placement.Shape.Extra);
                }

                plan.LeftRailStackCount = leftRail.Stacks.Count;
                plan.RightRailStackCount = rightRail.Stacks.Count;
                plan.LeftRailColumn = leftColumn;
                plan.RightRailColumn = rightColumn;
                plan.LeftRailX = workArea.Left + (leftColumn * safeGrid.IconSpacingX);
                plan.RightRailX = workArea.Left + (rightColumn * safeGrid.IconSpacingX);
                plan.LeftRailColumnSlack = safeGrid.SafeRows - railStartRow - leftRail.Rows;
                plan.RightRailColumnSlack = safeGrid.SafeRows - railStartRow - rightRail.Rows;
                return (leftColumn, leftRail.Width, rightColumn);
            }

            throw new InvalidOperationException(
                $"DUAL_RAIL_NO_ALIGNED_COLUMN Stacked=True; CandidatesTried={candidates.Count}");
        }

        /// <summary>
        /// The best stacked shape of one rail at every width it can take.
        ///
        /// Groups keep their rail order and are cut into consecutive stacks. Each
        /// group may be narrowed or widened from its preferred column count; a
        /// shape costs the square of that distortion, so a small compromise is
        /// spread over several groups instead of crushing one.
        /// </summary>
        private Dictionary<int, StackedRail> StackedRailOptions(DesktopLayoutPlan plan, DesktopRail rail,
            NativeDesktopSafeGrid safeGrid)
        {
            List<DesktopLayoutGroup> groups = plan.Groups
                .Where(group => group.Rail == rail && !group.HasManualAnchor && MovableCount(plan, group.GroupId) > 0)
                .ToList();
            var best = new Dictionary<int, StackedRail>();
            if (groups.Count == 0)
            {
                best[0] = new StackedRail(Array.Empty<RailStackPlan>());
                return best;
            }

            List<List<GroupShape>> shapes = groups.Select(group => ShapesFor(plan, group)).ToList();
            int maximumWidth = safeGrid.SafeColumns - MinimumCentralColumns;
            for (int cuts = 0; cuts < 1 << (groups.Count - 1); cuts++)
            {
                var stacks = new List<Dictionary<int, RailStackPlan>>();
                int start = 0;
                bool fits = true;
                for (int end = 1; end <= groups.Count && fits; end++)
                {
                    if (end < groups.Count && (cuts & (1 << (end - 1))) == 0) continue;
                    Dictionary<int, RailStackPlan> options = StackOptions(shapes.GetRange(start, end - start), safeGrid.SafeRows);
                    fits = options.Count > 0;
                    stacks.Add(options);
                    start = end;
                }
                if (!fits) continue;

                foreach (StackedRail candidate in CombineStacks(stacks, maximumWidth))
                {
                    if (!best.TryGetValue(candidate.Width, out StackedRail? incumbent) || IsBetterRail(candidate, incumbent))
                        best[candidate.Width] = candidate;
                }
            }
            return best;
        }

        /// <summary>Every column count a group may take in a stacked rail. A size the user chose is not negotiable.</summary>
        private List<GroupShape> ShapesFor(DesktopLayoutPlan plan, DesktopLayoutGroup group)
        {
            int count = MovableCount(plan, group.GroupId);
            if (_groupZones.TryGetValue(group.GroupId, out DesktopGroupZone? zone) && zone is { IsManualSize: true } &&
                zone.ManualColumns > 0 && zone.ManualRows > 0)
            {
                (int manualColumns, int manualRows) = ResolveShape(group.GroupId, count, 0);
                return new List<GroupShape> { new(group, 0, manualColumns, manualRows, 0) };
            }

            int baseColumns = BaseColumns(group.GroupId, count);
            int preferred = Math.Max(1, Math.Min(baseColumns, count));
            var shapes = new List<GroupShape>();
            for (int columns = 1; columns <= Math.Min(count, preferred + MaximumRailWidening); columns++)
            {
                int distortion = columns - preferred;
                shapes.Add(new GroupShape(group, columns - baseColumns, columns,
                    (int)Math.Ceiling(count / (double)columns), distortion * distortion));
            }
            return shapes;
        }

        /// <summary>The cheapest shapes of one stack that fit the screen height, by the stack's width.</summary>
        private static Dictionary<int, RailStackPlan> StackOptions(IReadOnlyList<List<GroupShape>> shapes, int safeRows)
        {
            const int MinimumGroupRows = 1 + DesktopLayoutSpacing.GroupLeadingRows;
            var best = new Dictionary<int, RailStackPlan>();
            var chosen = new GroupShape[shapes.Count];

            void Visit(int index, int rows, int width, int cost)
            {
                if (index == shapes.Count)
                {
                    if (!best.TryGetValue(width, out RailStackPlan? incumbent) || cost < incumbent.Cost)
                        best[width] = new RailStackPlan(chosen.ToArray());
                    return;
                }
                int stillNeeded = (shapes.Count - index - 1) * MinimumGroupRows;
                foreach (GroupShape shape in shapes[index])
                {
                    int used = rows + shape.Rows + DesktopLayoutSpacing.GroupLeadingRows;
                    if (used + stillNeeded > safeRows) continue;
                    chosen[index] = shape;
                    Visit(index + 1, used, Math.Max(width, shape.Columns), cost + shape.Cost);
                }
            }

            Visit(0, 0, 0, 0);
            return best;
        }

        /// <summary>Every way to pick one shape per stack that keeps the rail within its width.</summary>
        private static List<StackedRail> CombineStacks(IReadOnlyList<Dictionary<int, RailStackPlan>> stacks, int maximumWidth)
        {
            var results = new List<StackedRail>();
            var chosen = new RailStackPlan[stacks.Count];

            void Visit(int index, int width)
            {
                if (index == stacks.Count)
                {
                    results.Add(new StackedRail(chosen.ToArray()));
                    return;
                }
                int gap = index == 0 ? 0 : StackGapColumns;
                foreach (KeyValuePair<int, RailStackPlan> option in stacks[index].OrderBy(pair => pair.Key))
                {
                    int total = width + gap + option.Key;
                    if (total > maximumWidth) break; // ordered by width, so nothing wider fits either
                    chosen[index] = option.Value;
                    Visit(index + 1, total);
                }
            }

            Visit(0, 0);
            return results;
        }

        private static bool IsBetterRail(StackedRail candidate, StackedRail incumbent)
        {
            if (candidate.Cost != incumbent.Cost) return candidate.Cost < incumbent.Cost;
            if (candidate.Stacks.Count != incumbent.Stacks.Count) return candidate.Stacks.Count < incumbent.Stacks.Count;
            // A taller outer stack frames the screen edge; a short one leaves a ragged gap there.
            return candidate.OuterRows > incumbent.OuterRows;
        }

        /// <summary>
        /// Where every group of a stacked rail goes with the rail's band starting at
        /// <paramref name="railColumn"/>, or null if some group has no room. Nothing
        /// is changed. Stacks fill from the screen edge inward, and each group
        /// searches downward past the icons DesktopHub may not move, exactly as a
        /// group in a single-column rail does.
        /// </summary>
        private static List<StackPlacement>? TryPlaceStackedRail(StackedRail rail, DesktopRail side, int railColumn,
            int startRow, ISet<string> obstacles, Rectangle workArea, NativeDesktopSafeGrid safeGrid)
        {
            var placements = new List<StackPlacement>();
            int fromEdge = 0;
            for (int stackIndex = 0; stackIndex < rail.Stacks.Count; stackIndex++)
            {
                RailStackPlan stack = rail.Stacks[stackIndex];
                int column = side == DesktopRail.Left
                    ? railColumn + fromEdge
                    : railColumn + rail.Width - fromEdge - stack.Width;
                fromEdge += stack.Width + StackGapColumns;

                int cursor = startRow;
                foreach (GroupShape shape in stack.Shapes)
                {
                    int block = shape.Rows + DesktopLayoutSpacing.GroupLeadingRows;
                    int leadingRow = -1;
                    for (int row = cursor; row + block <= safeGrid.SafeRows; row++)
                    {
                        if (!RailBandIsClear(column, shape.Columns, row, block, obstacles, workArea, safeGrid)) continue;
                        leadingRow = row;
                        break;
                    }
                    if (leadingRow < 0) return null;
                    int iconRow = leadingRow + DesktopLayoutSpacing.GroupLeadingRows;
                    placements.Add(new StackPlacement(shape, stackIndex, column, iconRow));
                    cursor = iconRow + shape.Rows;
                }
            }
            return placements;
        }

        /// <summary>One way to shape a group: its column count and the rows that follow from it.</summary>
        private readonly record struct GroupShape(DesktopLayoutGroup Group, int Extra, int Columns, int Rows, int Cost);

        /// <summary>A group's place within a stacked rail.</summary>
        private readonly record struct StackPlacement(GroupShape Shape, int Stack, int Column, int IconRow);

        /// <summary>Groups standing one above another on a shared X baseline.</summary>
        private sealed record RailStackPlan(IReadOnlyList<GroupShape> Shapes)
        {
            public int Width => Shapes.Count == 0 ? 0 : Shapes.Max(shape => shape.Columns);
            public int Rows => Shapes.Sum(shape => shape.Rows + DesktopLayoutSpacing.GroupLeadingRows);
            public int Cost => Shapes.Sum(shape => shape.Cost);
        }

        /// <summary>A rail made of stacks, listed from the screen edge inward.</summary>
        private sealed record StackedRail(IReadOnlyList<RailStackPlan> Stacks)
        {
            public int Width => Stacks.Sum(stack => stack.Width) + (Math.Max(0, Stacks.Count - 1) * StackGapColumns);
            public int Rows => Stacks.Count == 0 ? 0 : Stacks.Max(stack => stack.Rows);
            public int Cost => Stacks.Sum(stack => stack.Cost);
            public int OuterRows => Stacks.Count == 0 ? 0 : Stacks[0].Rows;
        }

        private static bool IsPrimaryReflowGroup(NativeDesktopLayoutGroupId group) =>
            group is NativeDesktopLayoutGroupId.RESEARCH_STUDY or NativeDesktopLayoutGroupId.AI_DEV or
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS;

        private static bool IsMovableCandidate(DesktopLayoutItem item) =>
            item.IsStableIdentity && !item.IsShellNamespace &&
            string.Equals(item.ClassificationConfidence, "HIGH", StringComparison.Ordinal) &&
            (!item.IsManualOverride || ParseGroup(item.LayoutGroupId) != NativeDesktopLayoutGroupId.UNASSIGNED);

        private DesktopLayoutItem ToLayoutItem(NativeDesktopIconState icon) => ToLayoutItem(icon, icon.X, icon.Y);

        /// <summary>Classifies an icon as if it stood at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        private DesktopLayoutItem ToLayoutItem(NativeDesktopIconState icon, int x, int y)
        {
            ClassifiedDesktopItem classified = NativeDesktopItemClassificationService.Classify(
                icon, _manualLayoutGroups, _manualButUnmappedPaths);
            return NewItem(icon, classified.FinalGroup, classified.GroupDisplayName,
                classified.Confidence, classified.IsManualOverride, classified.Reason,
                classified.Reference?.ReferenceType.ToString() ?? icon.CandidateType?.ToString(),
                classified.Reference, classified.ClassifierGroup, x, y);
        }

        private static DesktopLayoutItem NewItem(NativeDesktopIconState icon, NativeDesktopLayoutGroupId group,
            string displayGroup, string confidence, bool isManual, string reason, string? type = null,
            ReferenceFrameItem? reference = null, string? classifierGroup = null, int? x = null, int? y = null) => new()
            {
                LayoutGroupId = group.ToString(),
                GroupDisplayName = displayGroup,
                OriginalPath = icon.OriginalPath,
                ExplorerLabel = icon.DisplayName,
                LabelOrdinal = icon.LabelOrdinal,
                ExplorerLabelCount = icon.ExplorerLabelCount,
                ResolutionStatus = icon.IdentityStatus.ToString(),
                CandidateType = icon.CandidateType?.ToString() ?? string.Empty,
                DisplayName = icon.DisplayName,
                Type = type ?? icon.IdentityStatus.ToString(),
                TargetPath = reference?.TargetPath ?? string.Empty,
                Arguments = reference?.Arguments ?? string.Empty,
                Uri = reference?.Uri ?? string.Empty,
                ClassifierGroup = classifierGroup ?? group.ToString(),
                CurrentX = x ?? icon.X,
                CurrentY = y ?? icon.Y,
                TargetX = x ?? icon.X,
                TargetY = y ?? icon.Y,
                ClassificationConfidence = confidence,
                ClassificationReason = reason,
                IsStableIdentity = icon.IdentityStatus.IsResolved(),
                IsShellNamespace = icon.IdentityStatus == NativeIconIdentityStatus.ShellNamespace,
                IsManualOverride = isManual,
                ApplyReason = "NOT_PLANNED"
            };

        private static List<DesktopLayoutGroup> CreateGroups(Rectangle workArea, int gridX, int gridY) => new()
        {
            Group(NativeDesktopLayoutGroupId.RESEARCH_STUDY, "科研 · 学习", "Top-left clean zone", "TopLeft",
                RegionFor(NativeDesktopLayoutGroupId.RESEARCH_STUDY, workArea, gridX, gridY)),
            Group(NativeDesktopLayoutGroupId.AI_DEV, "AI · 开发", "Top-right compact zone", "TopRight",
                RegionFor(NativeDesktopLayoutGroupId.AI_DEV, workArea, gridX, gridY)),
            Group(NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, "办公 · 文档", "Left-center compact zone", "MiddleLeft",
                RegionFor(NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, workArea, gridX, gridY)),
            Group(NativeDesktopLayoutGroupId.DAILY_TOOLS, "日常工具", "Reserved right-middle zone", "MiddleRight",
                RegionFor(NativeDesktopLayoutGroupId.DAILY_TOOLS, workArea, gridX, gridY)),
            Group(NativeDesktopLayoutGroupId.GAMES, "游戏", "Reserved bottom zone", "BottomLeft",
                RegionFor(NativeDesktopLayoutGroupId.GAMES, workArea, gridX, gridY)),
            Group(NativeDesktopLayoutGroupId.SYSTEM_TEMP, "系统 · 临时", "Bottom-right utility zone", "BottomRight",
                RegionFor(NativeDesktopLayoutGroupId.SYSTEM_TEMP, workArea, gridX, gridY)),
            Group(NativeDesktopLayoutGroupId.UNASSIGNED, "待整理", "Bottom-left reserved block", "COMPACT_VISUALLY",
                RegionFor(NativeDesktopLayoutGroupId.UNASSIGNED, workArea, gridX, gridY))
        };

        private static List<DesktopLayoutGroup> CreateFullGroups(Rectangle workArea, int gridX, int gridY) => new()
        {
            FullGroup(NativeDesktopLayoutGroupId.RESEARCH_STUDY, "科研 · 学习", "Left upper clean block", "TopLeft",
                FullRegionFor(NativeDesktopLayoutGroupId.RESEARCH_STUDY, workArea, gridX, gridY), workArea, gridX, gridY),
            FullGroup(NativeDesktopLayoutGroupId.AI_DEV, "AI · 开发", "Right upper clean block", "TopRight",
                FullRegionFor(NativeDesktopLayoutGroupId.AI_DEV, workArea, gridX, gridY), workArea, gridX, gridY),
            FullGroup(NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, "办公 · 文档", "Left middle clean block", "MiddleLeft",
                FullRegionFor(NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, workArea, gridX, gridY), workArea, gridX, gridY),
            FullGroup(NativeDesktopLayoutGroupId.DAILY_TOOLS, "日常工具", "Right middle clean block", "MiddleRight",
                FullRegionFor(NativeDesktopLayoutGroupId.DAILY_TOOLS, workArea, gridX, gridY), workArea, gridX, gridY),
            FullGroup(NativeDesktopLayoutGroupId.GAMES, "游戏", "Right lower clean block", "BottomRight",
                FullRegionFor(NativeDesktopLayoutGroupId.GAMES, workArea, gridX, gridY), workArea, gridX, gridY),
            FullGroup(NativeDesktopLayoutGroupId.SYSTEM_TEMP, "系统 · 临时", "Right utility slot", "BottomRightUtility",
                FullRegionFor(NativeDesktopLayoutGroupId.SYSTEM_TEMP, workArea, gridX, gridY), workArea, gridX, gridY),
            FullGroup(NativeDesktopLayoutGroupId.UNASSIGNED, "待整理", "Bottom-left reserved block", "BottomLeft",
                FullRegionFor(NativeDesktopLayoutGroupId.UNASSIGNED, workArea, gridX, gridY), workArea, gridX, gridY)
        };

        private static DesktopLayoutGroup FullGroup(
            NativeDesktopLayoutGroupId id,
            string title,
            string region,
            string anchor,
            RectangleSlots home,
            Rectangle workArea,
            int gridX,
            int gridY) => new()
        {
            GroupId = id,
            GroupDisplayName = title,
            Region = region,
            Anchor = anchor,
            HomeRegionLeft = workArea.Left + (home.Left * gridX),
            HomeRegionTop = workArea.Top + (home.Top * gridY),
            HomeRegionRight = workArea.Left + (home.Right * gridX),
            HomeRegionBottom = workArea.Top + (home.Bottom * gridY)
        };

        private static RectangleSlots FullRegionFor(NativeDesktopLayoutGroupId group, Rectangle work, int gridX, int gridY)
        {
            int columns = Math.Max(1, (work.Width - 1) / gridX) + 1;
            int rows = Math.Max(1, (work.Height - 1) / gridY) + 1;
            RectangleSlots Ratio(double left, double right, double top, double bottom) => new(
                Math.Clamp((int)Math.Round(columns * left), 0, columns - 1),
                Math.Clamp((int)Math.Floor(columns * right) - 1, 0, columns - 1),
                Math.Clamp((int)Math.Round(rows * top), 0, rows - 1),
                Math.Clamp((int)Math.Floor(rows * bottom) - 1, 0, rows - 1));

            return group switch
            {
                NativeDesktopLayoutGroupId.RESEARCH_STUDY => Ratio(0.1143, 0.2400, 0.2000, 0.4200),
                NativeDesktopLayoutGroupId.AI_DEV => Ratio(0.8300, 0.9600, 0.0667, 0.3600),
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => Ratio(0.1143, 0.2400, 0.4667, 0.6200),
                NativeDesktopLayoutGroupId.DAILY_TOOLS => Ratio(0.8300, 0.9600, 0.4000, 0.7000),
                NativeDesktopLayoutGroupId.GAMES => Ratio(0.7700, 0.9300, 0.7333, 0.9500),
                NativeDesktopLayoutGroupId.SYSTEM_TEMP => Ratio(0.9400, 1.0000, 0.9333, 1.0000),
                NativeDesktopLayoutGroupId.UNASSIGNED => Ratio(0.0000, 0.1500, 0.6667, 1.0000),
                _ => new RectangleSlots(0, columns - 1, 0, rows - 1)
            };
        }

        private static CompactBlock? FindFullCompactBlock(
            NativeDesktopLayoutGroupId group,
            int itemCount,
            RectangleSlots region,
            ISet<string> occupied,
            Rectangle work,
            int gridX,
            int gridY,
            Rectangle centralClearZone,
            IReadOnlyList<CompactBlock> reservedBlocks)
        {
            CompactBlock? best = null;
            double bestScore = double.MaxValue;
            int bestTop = int.MaxValue;
            int bestLeft = int.MaxValue;

            foreach ((int columns, int rows) in CandidateDimensions(group, itemCount))
            {
                if (columns > 6 || columns > region.Width || rows > region.Height)
                    continue;

                foreach (int top in Enumerable.Range(region.Top, region.Height - rows + 1))
                foreach (int left in Enumerable.Range(region.Left, region.Width - columns + 1))
                {
                    var candidate = new CompactBlock(left, top, columns, rows, UsedFallback: false);
                    if (BlockIntersectsCentralClearZone(candidate, work, gridX, gridY, centralClearZone))
                        continue;
                    if (!BlockSlotsAreVacant(candidate, occupied, work, gridX, gridY))
                        continue;
                    if (ExpandedBlockIntersectsReserved(candidate, work, gridX, gridY, reservedBlocks))
                        continue;

                    int anchorDistance = FullAnchorDistance(group, left, top, columns, rows, region);
                    double aspect = columns / (double)rows;
                    double aspectTarget = group is NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS or NativeDesktopLayoutGroupId.SYSTEM_TEMP ? 2.0 : 1.0;
                    double score = anchorDistance * 0.5 + Math.Abs(aspect - aspectTarget) * 4.0;
                    if (score < bestScore || (Math.Abs(score - bestScore) < 0.0001 && (top < bestTop || (top == bestTop && left < bestLeft))))
                    {
                        best = candidate;
                        bestScore = score;
                        bestTop = top;
                        bestLeft = left;
                    }
                }
            }

            return best;
        }

        private static int FullAnchorDistance(NativeDesktopLayoutGroupId group, int left, int top, int columns, int rows, RectangleSlots region)
        {
            int targetLeft = group is NativeDesktopLayoutGroupId.AI_DEV or NativeDesktopLayoutGroupId.DAILY_TOOLS or NativeDesktopLayoutGroupId.GAMES or NativeDesktopLayoutGroupId.SYSTEM_TEMP
                ? region.Right - columns + 1
                : region.Left;
            int targetTop = group is NativeDesktopLayoutGroupId.GAMES or NativeDesktopLayoutGroupId.UNASSIGNED ? region.Bottom - rows + 1 : region.Top;
            return Math.Abs(left - targetLeft) + Math.Abs(top - targetTop);
        }

        private static bool BlockSlotsAreVacant(CompactBlock block, ISet<string> occupied, Rectangle work, int gridX, int gridY)
        {
            for (int row = 0; row < block.Rows; row++)
            for (int column = 0; column < block.Columns; column++)
            {
                int x = work.Left + ((block.Left + column) * gridX);
                int y = work.Top + ((block.Top + row) * gridY);
                if (occupied.Contains(SlotKey(x, y, work, gridX, gridY)))
                    return false;
            }
            return true;
        }

        private static bool BlockIntersectsCentralClearZone(CompactBlock block, Rectangle work, int gridX, int gridY, Rectangle centralClearZone)
        {
            Rectangle blockRectangle = new(
                work.Left + (block.Left * gridX),
                work.Top + (block.Top * gridY),
                block.Columns * gridX,
                block.Rows * gridY);
            return blockRectangle.IntersectsWith(centralClearZone);
        }

        private static bool ExpandedBlockIntersectsReserved(CompactBlock block, Rectangle work, int gridX, int gridY, IReadOnlyList<CompactBlock> reservedBlocks)
        {
            if (reservedBlocks.Count == 0)
                return false;

            Rectangle expanded = new(
                work.Left + (block.Left * gridX),
                work.Top + (block.Top * gridY),
                block.Columns * gridX,
                block.Rows * gridY);
            expanded.Inflate(gridX, gridY);

            foreach (CompactBlock other in reservedBlocks)
            {
                Rectangle otherRectangle = new(
                    work.Left + (other.Left * gridX),
                    work.Top + (other.Top * gridY),
                    other.Columns * gridX,
                    other.Rows * gridY);
                if (expanded.IntersectsWith(otherRectangle))
                    return true;
            }
            return false;
        }

        private static string LayoutAreaForGroup(NativeDesktopLayoutGroupId group) => group switch
        {
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => "RESEARCH_AREA",
            NativeDesktopLayoutGroupId.AI_DEV => "AI_DEV_AREA",
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => "OFFICE_AREA",
            NativeDesktopLayoutGroupId.DAILY_TOOLS => "DAILY_TOOLS_AREA",
            NativeDesktopLayoutGroupId.GAMES => "GAMES_AREA",
            NativeDesktopLayoutGroupId.SYSTEM_TEMP => "SYSTEM_TEMP_AREA",
            _ => "UNASSIGNED_AREA"
        };

        private static bool IsFullMovableCandidate(DesktopLayoutItem item) => item.IsStableIdentity && !item.IsShellNamespace;

        private static DesktopLayoutGroup Group(NativeDesktopLayoutGroupId id, string title, string region,
            string anchor, RectangleSlots home) => new()
        {
            GroupId = id,
            GroupDisplayName = title,
            Region = region,
            Anchor = anchor,
            HomeRegionLeft = home.Left,
            HomeRegionTop = home.Top,
            HomeRegionRight = home.Right,
            HomeRegionBottom = home.Bottom
        };

        private static RectangleSlots RegionFor(NativeDesktopLayoutGroupId group, Rectangle work, int gridX, int gridY)
        {
            int columns = Math.Max(1, (work.Width - 1) / gridX) + 1;
            int rows = Math.Max(1, (work.Height - 1) / gridY) + 1;
            RectangleSlots Ratio(double left, double right, double top, double bottom) => new(
                Math.Clamp((int)Math.Round(columns * left), 0, columns - 1),
                Math.Clamp((int)Math.Floor(columns * right) - 1, 0, columns - 1),
                Math.Clamp((int)Math.Round(rows * top), 0, rows - 1),
                Math.Clamp((int)Math.Floor(rows * bottom) - 1, 0, rows - 1));

            return group switch
            {
                NativeDesktopLayoutGroupId.RESEARCH_STUDY => Ratio(0.17, 0.34, 0.07, 0.40),
                NativeDesktopLayoutGroupId.AI_DEV => Ratio(0.70, 0.97, 0.03, 0.40),
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => Ratio(0.17, 0.35, 0.43, 0.72),
                NativeDesktopLayoutGroupId.DAILY_TOOLS => Ratio(0.70, 0.97, 0.43, 0.78),
                NativeDesktopLayoutGroupId.GAMES => Ratio(0.25, 0.65, 0.76, 0.98),
                NativeDesktopLayoutGroupId.SYSTEM_TEMP => Ratio(0.82, 0.97, 0.78, 0.98),
                _ => new RectangleSlots(0, columns - 1, 0, rows - 1)
            };
        }

        private static RectangleSlots FullFallbackRegion(NativeDesktopLayoutGroupId group, Rectangle work, int gridX, int gridY)
        {
            int columns = Math.Max(1, (work.Width - 1) / gridX) + 1;
            int rows = Math.Max(1, (work.Height - 1) / gridY) + 1;
            return group switch
            {
                NativeDesktopLayoutGroupId.RESEARCH_STUDY or NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS =>
                    new RectangleSlots(1, Math.Max(1, columns / 2 - 2), 0, rows - 1),
                NativeDesktopLayoutGroupId.AI_DEV or NativeDesktopLayoutGroupId.DAILY_TOOLS or NativeDesktopLayoutGroupId.SYSTEM_TEMP =>
                    new RectangleSlots(Math.Min(columns - 1, columns / 2 + 2), columns - 1, 0, rows - 1),
                _ => new RectangleSlots(0, columns - 1, Math.Min(rows - 1, rows / 2), rows - 1)
            };
        }

        private static CompactBlock? FindCompactBlock(NativeDesktopLayoutGroupId group, int itemCount,
            RectangleSlots region, ISet<string> occupied, Rectangle work, int gridX, int gridY, bool usedFallback = false)
        {
            foreach ((int columns, int rows) in CandidateDimensions(group, itemCount))
            {
                if (columns > region.Width || rows > region.Height) continue;
                var origins = Enumerable.Range(region.Top, region.Height - rows + 1)
                    .SelectMany(top => Enumerable.Range(region.Left, region.Width - columns + 1)
                        .Select(left => new
                        {
                            Left = left,
                            Top = top,
                            Distance = AnchorDistance(group, left, top, columns, rows, region)
                        }))
                    .OrderBy(origin => origin.Distance)
                    .ThenBy(origin => origin.Top)
                    .ThenBy(origin => origin.Left);

                foreach (var origin in origins)
                {
                    int left = origin.Left;
                    int top = origin.Top;
                    bool vacant = true;
                    for (int row = 0; row < rows && vacant; row++)
                    for (int column = 0; column < columns; column++)
                    {
                        int x = work.Left + ((left + column) * gridX);
                        int y = work.Top + ((top + row) * gridY);
                        if (occupied.Contains(SlotKey(x, y, work, gridX, gridY)))
                        {
                            vacant = false;
                            break;
                        }
                    }
                    if (vacant) return new CompactBlock(left, top, columns, rows, usedFallback);
                }
            }
            return null;
        }

        private static IEnumerable<(int Columns, int Rows)> CandidateDimensions(NativeDesktopLayoutGroupId group, int itemCount)
        {
            int preferredColumns = group switch
            {
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => 4,
                NativeDesktopLayoutGroupId.RESEARCH_STUDY => 4,
                NativeDesktopLayoutGroupId.AI_DEV => 4,
                NativeDesktopLayoutGroupId.DAILY_TOOLS => 4,
                NativeDesktopLayoutGroupId.GAMES => 5,
                NativeDesktopLayoutGroupId.UNASSIGNED => 5,
                _ => Math.Min(4, Math.Max(1, itemCount))
            };
            int maxColumns = group switch
            {
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => 4,
                NativeDesktopLayoutGroupId.RESEARCH_STUDY => 5,
                NativeDesktopLayoutGroupId.AI_DEV => 5,
                NativeDesktopLayoutGroupId.DAILY_TOOLS => 5,
                NativeDesktopLayoutGroupId.GAMES => 6,
                _ => Math.Min(5, Math.Max(1, itemCount))
            };

            return Enumerable.Range(1, Math.Min(itemCount, maxColumns))
                .Select(columns => (Columns: columns, Rows: (int)Math.Ceiling(itemCount / (double)columns)))
                .OrderBy(size => Math.Abs(size.Columns - preferredColumns) * 3.0 +
                    Math.Abs((size.Columns / (double)size.Rows) - 1.35) * 2.0 +
                    ((size.Columns * size.Rows) - itemCount) * 0.25)
                .ThenBy(size => size.Columns * size.Rows);
        }

        private static int AnchorDistance(NativeDesktopLayoutGroupId group, int left, int top, int columns, int rows,
            RectangleSlots region)
        {
            int targetLeft = group is NativeDesktopLayoutGroupId.AI_DEV or NativeDesktopLayoutGroupId.DAILY_TOOLS or
                NativeDesktopLayoutGroupId.SYSTEM_TEMP ? region.Right - columns + 1 : region.Left;
            int targetTop = group == NativeDesktopLayoutGroupId.GAMES ? region.Bottom - rows + 1 : region.Top;
            return Math.Abs(left - targetLeft) + Math.Abs(top - targetTop);
        }

        private static void ReserveBlock(CompactBlock block, ISet<string> occupied, Rectangle work, int gridX, int gridY)
        {
            for (int row = 0; row < block.Rows; row++)
            for (int column = 0; column < block.Columns; column++)
                occupied.Add(SlotKey(work.Left + ((block.Left + column) * gridX),
                    work.Top + ((block.Top + row) * gridY), work, gridX, gridY));
        }

        private static int SortBucket(NativeDesktopLayoutGroupId group, DesktopLayoutItem item)
        {
            string name = item.DisplayName.ToLowerInvariant();
            if (group == NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS)
            {
                string[] order = { "文档", "ppt", "pdf", "数据表格", "图片", "压缩包", "安装包", "libreoffice" };
                int index = Array.FindIndex(order, fixedName => string.Equals(fixedName, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0 && name.StartsWith("libreoffice", StringComparison.OrdinalIgnoreCase)) index = 7;
                return index >= 0 ? index : 100;
            }
            if (group == NativeDesktopLayoutGroupId.AI_DEV)
            {
                if (name.Contains("codex")) return 0;
                if (name.Contains("claude")) return 1;
                if (name.Contains("visual studio code") || name.Contains("vscode")) return 2;
                if (name.Contains("zcode")) return 3;
                if (name.Contains("workbuddy")) return 4;
                if (name.Contains("wsl")) return 5;
                return 10;
            }
            if (group == NativeDesktopLayoutGroupId.RESEARCH_STUDY)
            {
                if (name.Contains("hunnu") || name.Contains("research")) return 0;
                if (name.Contains("stata")) return 1;
                if (name.Contains("zotero")) return 2;
                if (name.Contains("obsidian")) return 3;
                if (name.Contains("论文") || name.Contains("资料")) return 4;
                return 10;
            }
            if (item.Type.Equals("Folder", StringComparison.OrdinalIgnoreCase)) return 0;
            if (group == NativeDesktopLayoutGroupId.GAMES && (name.Contains("steam") || name.Contains("battle.net"))) return 1;
            if (group == NativeDesktopLayoutGroupId.RESEARCH_STUDY &&
                (name.Contains("hunnu") || name.Contains("research"))) return 1;
            if (group == NativeDesktopLayoutGroupId.AI_DEV &&
                (name.Contains("codex") || name.Contains("claude") || name.Contains("workbuddy"))) return 1;
            return 10;
        }

        private static NativeDesktopLayoutGroupId ParseGroup(string value) =>
            Enum.TryParse(value, true, out NativeDesktopLayoutGroupId group) ? group : NativeDesktopLayoutGroupId.UNASSIGNED;

        private static bool Contains(Rectangle rectangle, int x, int y) => x >= rectangle.Left && y >= rectangle.Top &&
            x < rectangle.Right && y < rectangle.Bottom;

        internal static string SlotKey(int x, int y, Rectangle work, int gridX, int gridY) =>
            $"{(int)Math.Round((x - work.Left) / (double)gridX)},{(int)Math.Round((y - work.Top) / (double)gridY)}";

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }

        private static int TryGetDpi(IntPtr hwnd)
        {
            try { return (int)GetDpiForWindow(hwnd); }
            catch { return 96; }
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private readonly record struct RectangleSlots(int Left, int Right, int Top, int Bottom)
        {
            public int Width => Right - Left + 1;
            public int Height => Bottom - Top + 1;
        }

        private readonly record struct CompactBlock(int Left, int Top, int Columns, int Rows, bool UsedFallback);
    }
}




