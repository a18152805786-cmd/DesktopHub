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

                RectangleSlots region = RegionFor(groupId, workArea, gridX, gridY);
                foreach (DesktopLayoutItem item in groupItems)
                {
                    if (!IsMovableCandidate(item))
                    {
                        item.ApplyReason = "SKIP_NOT_HIGH_CONFIDENCE_STABLE";
                        continue;
                    }

                    if (!TryTakeSlot(region, occupied, workArea, gridX, gridY, out int x, out int y))
                    {
                        item.ApplyReason = "SKIP_NO_SAFE_SLOT";
                        continue;
                    }

                    item.TargetX = x;
                    item.TargetY = y;
                    string currentSlot = SlotKey(item.CurrentX, item.CurrentY, workArea, gridX, gridY);
                    string targetSlot = SlotKey(x, y, workArea, gridX, gridY);
                    item.IsEligibleForApply = !listState.AutoArrangeEnabled &&
                        !string.Equals(currentSlot, targetSlot, StringComparison.Ordinal);
                    item.ApplyReason = item.IsEligibleForApply ? "HIGH_CONFIDENCE_STABLE_PATH" : "ALREADY_AT_TARGET";
                    if (item.IsEligibleForApply) group.EligibleItemCount++;
                }
            }

            plan.DuplicateTargetCoordinates = plan.Items.GroupBy(item => $"{item.TargetX},{item.TargetY}").Count(group => group.Count() > 1);
            plan.TargetOutsideDesktopWorkArea = plan.Items.Count(item => !Contains(workArea, item.TargetX, item.TargetY));
            return plan;
        }

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
            Group(NativeDesktopLayoutGroupId.RESEARCH_STUDY, "科研 · 学习", "Left upper / middle"),
            Group(NativeDesktopLayoutGroupId.AI_DEV, "AI · 开发", "Right upper / middle"),
            Group(NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS, "办公 · 文档", "Left lower / middle"),
            Group(NativeDesktopLayoutGroupId.DAILY_TOOLS, "日常工具", "Right lower / middle"),
            Group(NativeDesktopLayoutGroupId.GAMES, "游戏", "Bottom remaining block"),
            Group(NativeDesktopLayoutGroupId.SYSTEM_TEMP, "系统 · 临时", "Bottom remaining block"),
            Group(NativeDesktopLayoutGroupId.UNASSIGNED, "未整理", "Original positions / occupied obstacles")
        };

        private static DesktopLayoutGroup Group(NativeDesktopLayoutGroupId id, string title, string region) => new()
        {
            GroupId = id,
            GroupDisplayName = title,
            Region = region
        };

        private static RectangleSlots RegionFor(NativeDesktopLayoutGroupId group, Rectangle work, int gridX, int gridY)
        {
            int columns = Math.Max(1, (work.Width - 1) / gridX);
            int rows = Math.Max(1, (work.Height - 1) / gridY);
            int gap = columns >= 8 ? 2 : 1;
            int leftWidth = Math.Max(1, (columns - gap) / 2);
            int rightStart = Math.Min(columns - 1, leftWidth + gap);
            int topEnd = Math.Max(1, (rows * 35 / 100) - 1);
            int middleStart = Math.Min(rows - 1, topEnd + 2);
            int middleEnd = Math.Min(rows - 1, middleStart + Math.Max(1, (rows * 27 / 100) - 1));
            int bottomStart = Math.Min(rows - 1, middleEnd + 2);

            return group switch
            {
                NativeDesktopLayoutGroupId.RESEARCH_STUDY => new RectangleSlots(0, leftWidth - 1, 0, topEnd),
                NativeDesktopLayoutGroupId.AI_DEV => new RectangleSlots(rightStart, columns - 1, 0, topEnd),
                NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS => new RectangleSlots(0, leftWidth - 1, middleStart, middleEnd),
                NativeDesktopLayoutGroupId.DAILY_TOOLS => new RectangleSlots(rightStart, columns - 1, middleStart, middleEnd),
                NativeDesktopLayoutGroupId.GAMES => new RectangleSlots(0, columns - 1, bottomStart, rows - 1),
                NativeDesktopLayoutGroupId.SYSTEM_TEMP => new RectangleSlots(Math.Max(0, columns - 4), columns - 1, bottomStart, rows - 1),
                _ => new RectangleSlots(0, columns - 1, 0, rows - 1)
            };
        }

        private static bool TryTakeSlot(RectangleSlots region, ISet<string> occupied, Rectangle work, int gridX, int gridY,
            out int x, out int y)
        {
            for (int row = region.Top; row <= region.Bottom; row++)
            for (int column = region.Left; column <= region.Right; column++)
            {
                x = work.Left + (column * gridX);
                y = work.Top + (row * gridY);
                string key = SlotKey(x, y, work, gridX, gridY);
                if (occupied.Contains(key)) continue;
                occupied.Add(key);
                return true;
            }
            x = y = 0;
            return false;
        }

        private static int SortBucket(NativeDesktopLayoutGroupId group, DesktopLayoutItem item)
        {
            string name = item.DisplayName.ToLowerInvariant();
            if (group == NativeDesktopLayoutGroupId.OFFICE_DOCUMENTS)
            {
                string[] order = { "文档", "ppt", "pdf", "数据表格", "图片", "压缩包", "安装包" };
                int index = Array.FindIndex(order, fixedName => string.Equals(fixedName, name, StringComparison.OrdinalIgnoreCase));
                return index >= 0 ? index : 100;
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

        private readonly record struct RectangleSlots(int Left, int Right, int Top, int Bottom);
    }
}
