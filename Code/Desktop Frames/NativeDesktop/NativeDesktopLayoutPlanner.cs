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

        public NativeDesktopLayoutPlanner(
            NativeDesktopIconLayoutService layout,
            IReadOnlyDictionary<string, NativeDesktopLayoutGroupId>? manualLayoutGroups = null,
            IEnumerable<string>? manualButUnmappedPaths = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _manualLayoutGroups = manualLayoutGroups ?? new Dictionary<string, NativeDesktopLayoutGroupId>(StringComparer.OrdinalIgnoreCase);
            _manualButUnmappedPaths = new HashSet<string>(manualButUnmappedPaths ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
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
                    item.IsEligibleForApply = !listState.AutoArrangeEnabled &&
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

        private static bool IsPrimaryReflowGroup(NativeDesktopLayoutGroupId group) =>
            group is NativeDesktopLayoutGroupId.RESEARCH_STUDY or NativeDesktopLayoutGroupId.AI_DEV or
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS;

        private static bool IsMovableCandidate(DesktopLayoutItem item) =>
            item.IsStableIdentity && !item.IsShellNamespace &&
            string.Equals(item.ClassificationConfidence, "HIGH", StringComparison.Ordinal) &&
            (!item.IsManualOverride || ParseGroup(item.LayoutGroupId) != NativeDesktopLayoutGroupId.UNASSIGNED);

        private DesktopLayoutItem ToLayoutItem(NativeDesktopIconState icon)
        {
            if (icon.IdentityStatus != NativeIconIdentityStatus.StablePath)
            {
                return NewItem(icon, NativeDesktopLayoutGroupId.UNASSIGNED, "未整理", icon.IdentityStatus.ToString(),
                    isManual: false, "Non-stable identity is an occupied obstacle and is never moved.");
            }

            string path = NormalizePath(icon.OriginalPath);
            if (_manualLayoutGroups.TryGetValue(path, out NativeDesktopLayoutGroupId manualGroup))
            {
                return NewItem(icon, manualGroup, DisplayName(manualGroup), "HIGH", true,
                    "ManualLayoutGroupId is authoritative; automatic classification is skipped.");
            }
            if (_manualButUnmappedPaths.Contains(path))
            {
                return NewItem(icon, NativeDesktopLayoutGroupId.UNASSIGNED, "未整理", "HIGH", true,
                    "ManualFrameId is authoritative but has no mapped LayoutGroupId; icon remains in place.");
            }

            if (!ReferenceFrameItem.IsSupportedPath(path))
            {
                return NewItem(icon, NativeDesktopLayoutGroupId.UNASSIGNED, "未整理", "UNASSIGNED", false,
                    "Desktop item is not a supported shortcut, URL, or folder.");
            }

            ReferenceFrameItem reference = ReferenceFrameItem.Create(path);
            ReferenceGroupingDecision decision = ReferenceGroupingClassifier.Classify(reference);
            NativeDesktopLayoutGroupId group = Map(decision.Group);
            return NewItem(icon, group, DisplayName(group), decision.Confidence.ToString().ToUpperInvariant(),
                decision.IsManualOverride, decision.Reason, reference.ReferenceType.ToString());
        }

        private static DesktopLayoutItem NewItem(NativeDesktopIconState icon, NativeDesktopLayoutGroupId group,
            string displayGroup, string confidence, bool isManual, string reason, string? type = null) => new()
            {
                LayoutGroupId = group.ToString(),
                GroupDisplayName = displayGroup,
                OriginalPath = icon.OriginalPath,
                DisplayName = icon.DisplayName,
                Type = type ?? icon.IdentityStatus.ToString(),
                CurrentX = icon.X,
                CurrentY = icon.Y,
                TargetX = icon.X,
                TargetY = icon.Y,
                ClassificationConfidence = confidence,
                ClassificationReason = reason,
                IsStableIdentity = icon.IdentityStatus == NativeIconIdentityStatus.StablePath,
                IsShellNamespace = icon.IdentityStatus == NativeIconIdentityStatus.UnmanagedShellItem,
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
            Group(NativeDesktopLayoutGroupId.UNASSIGNED, "未整理", "Original positions / occupied obstacles", "Preserve",
                RegionFor(NativeDesktopLayoutGroupId.UNASSIGNED, workArea, gridX, gridY))
        };

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

        private static NativeDesktopLayoutGroupId Map(ReferenceGroup group) => group switch
        {
            ReferenceGroup.ResearchStudy => NativeDesktopLayoutGroupId.RESEARCH_STUDY,
            ReferenceGroup.AiDev => NativeDesktopLayoutGroupId.AI_DEV,
            ReferenceGroup.OfficeDocuments or ReferenceGroup.WorkTools => NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS,
            ReferenceGroup.DailyTools => NativeDesktopLayoutGroupId.DAILY_TOOLS,
            ReferenceGroup.Games => NativeDesktopLayoutGroupId.GAMES,
            ReferenceGroup.SystemTemp => NativeDesktopLayoutGroupId.SYSTEM_TEMP,
            _ => NativeDesktopLayoutGroupId.UNASSIGNED
        };

        private static string DisplayName(NativeDesktopLayoutGroupId group) => group switch
        {
            NativeDesktopLayoutGroupId.RESEARCH_STUDY => "科研 · 学习",
            NativeDesktopLayoutGroupId.AI_DEV => "AI · 开发",
            NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => "办公 · 文档",
            NativeDesktopLayoutGroupId.DAILY_TOOLS => "日常工具",
            NativeDesktopLayoutGroupId.GAMES => "游戏",
            NativeDesktopLayoutGroupId.SYSTEM_TEMP => "系统 · 临时",
            _ => "未整理"
        };

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
