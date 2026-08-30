using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Forms = System.Windows.Forms;

namespace Desktop_Frames.NativeDesktop
{
    public sealed class DesktopGroupResizeResult
    {
        public bool Accepted { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int RequiredSlots { get; set; }
    }

    /// <summary>
    /// Everything about a group's rectangle that is not the drag itself: how big
    /// it may be, whether a proposed size is legal, and which zone a desktop
    /// coordinate falls inside.
    ///
    /// A zone is layout metadata and nothing else. Growing "游戏" from 5x3 to 6x4
    /// changes where DesktopHub puts icons; it never creates, moves, renames or
    /// deletes a single file or folder.
    /// </summary>
    internal static class DesktopGroupZoneService
    {
        /// <summary>Smallest zone the user may drag a group down to, before capacity is considered.</summary>
        public const int MinimumColumns = 1;
        public const int MinimumRows = 1;

        /// <summary>
        /// Validates a proposed size and stores it. Nothing on the desktop moves
        /// here; the caller re-runs the layout once the size is accepted.
        /// </summary>
        public static DesktopGroupResizeResult Resize(
            DesktopLayoutPlan plan,
            NativeDesktopLayoutGroupId groupId,
            int columns,
            int rows)
        {
            var result = new DesktopGroupResizeResult { Columns = columns, Rows = rows };
            DesktopLayoutGroup? group = plan.Groups.FirstOrDefault(candidate => candidate.GroupId == groupId);
            if (group == null)
            {
                result.Reason = "GROUP_NOT_PLANNED";
                return result;
            }
            // A restored plan may predate the last lock/unlock command.
            // The store is the authority for both drag and resize decisions.
            if (DesktopGroupZoneStore.IsLocked(groupId))
            {
                result.Reason = "GROUP_LOCKED";
                return result;
            }

            int required = ManagedItemCount(plan, groupId);
            result.RequiredSlots = required;
            if (columns < MinimumColumns || rows < MinimumRows)
            {
                result.Reason = "BELOW_MINIMUM";
                return result;
            }

            // A zone that cannot hold its own members is not a smaller zone, it
            // is a broken one. Refuse rather than silently dropping icons out.
            if (columns * rows < required)
            {
                result.Reason = "CAPACITY_TOO_SMALL";
                return result;
            }

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopSafeGrid safeGrid = layout.GetSafeDesktopGrid();
            if (group.RailColumn + columns > safeGrid.SafeColumns ||
                group.RailRow + rows > safeGrid.SafeRows)
            {
                result.Reason = "OUTSIDE_SAFE_GRID";
                return result;
            }

            DesktopGroupZoneStore.SaveSize(groupId, columns, rows);
            group.HasManualSize = true;
            result.Accepted = true;
            result.Reason = "RESIZED";
            return result;
        }

        /// <summary>Managed icons the zone has to be able to hold.</summary>
        public static int ManagedItemCount(DesktopLayoutPlan plan, NativeDesktopLayoutGroupId groupId) =>
            plan.Items.Count(item => ParseGroup(item.LayoutGroupId) == groupId &&
                item.IsStableIdentity && !item.IsShellNamespace);

        /// <summary>
        /// The group whose zone contains a desktop coordinate, or null.
        ///
        /// This is what lets DesktopHub answer "is this icon inside 游戏 or not"
        /// instead of leaving the user to guess where an invisible boundary was.
        /// </summary>
        public static NativeDesktopLayoutGroupId? ResolveGroupAtPosition(DesktopLayoutPlan plan, int x, int y)
        {
            foreach (DesktopLayoutGroup group in plan.Groups)
            {
                if (group.Columns <= 0 || group.Rows <= 0) continue;
                if (x >= group.IconBoundingLeft && x < group.IconBoundingRight &&
                    y >= group.IconBoundingTop && y < group.IconBoundingBottom)
                {
                    return group.GroupId;
                }
            }
            return null;
        }

        /// <summary>
        /// Icons sitting inside a zone that does not match their classification.
        ///
        /// Detected by comparing stored coordinates against stored bounds, on an
        /// explicit refresh. There is no mouse hook, no polling and no Explorer
        /// injection: DesktopHub simply looks at where things ended up the next
        /// time it is asked to.
        /// </summary>
        public static IReadOnlyList<DesktopZoneMismatch> FindIconsDroppedIntoOtherZones(DesktopLayoutPlan plan)
        {
            var mismatches = new List<DesktopZoneMismatch>();
            var layout = new NativeDesktopIconLayoutService();
            IReadOnlyList<NativeDesktopIconState> icons;
            try
            {
                icons = layout.EnumerateDesktopIcons();
            }
            catch
            {
                return mismatches;
            }

            var byPath = plan.Items
                .Where(item => item.IsStableIdentity && !item.IsShellNamespace)
                .GroupBy(item => NormalizePath(item.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (NativeDesktopIconState icon in icons)
            {
                if (!icon.IdentityStatus.IsResolved()) continue;
                if (!byPath.TryGetValue(NormalizePath(icon.OriginalPath), out DesktopLayoutItem? item)) continue;

                // The centre of the icon cell, so an icon sitting on a boundary
                // is judged by where it mostly is.
                NativeDesktopLayoutGroupId? zone = ResolveGroupAtPosition(plan, icon.X + 1, icon.Y + 1);
                if (zone == null) continue;

                NativeDesktopLayoutGroupId current = ParseGroup(item.LayoutGroupId);
                if (zone.Value == current) continue;
                if (zone.Value == NativeDesktopLayoutGroupId.UNASSIGNED) continue;

                mismatches.Add(new DesktopZoneMismatch
                {
                    OriginalPath = item.OriginalPath,
                    DisplayName = item.DisplayName,
                    CurrentGroup = current,
                    ZoneGroup = zone.Value
                });
            }
            return mismatches;
        }

        /// <summary>Zone rectangle for a group, recomputed from the live grid.</summary>
        public static Rectangle BoundsOf(DesktopLayoutGroup group) => new(
            group.IconBoundingLeft,
            group.IconBoundingTop,
            Math.Max(0, group.IconBoundingRight - group.IconBoundingLeft),
            Math.Max(0, group.IconBoundingBottom - group.IconBoundingTop));

        public static (int SpacingX, int SpacingY, int SafeColumns, int SafeRows) GetGrid()
        {
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopSafeGrid safeGrid = layout.GetSafeDesktopGrid();
            return (Math.Max(1, safeGrid.IconSpacingX), Math.Max(1, safeGrid.IconSpacingY),
                safeGrid.SafeColumns, safeGrid.SafeRows);
        }

        public static Rectangle GetWorkArea() =>
            Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens.First().WorkingArea;

        private static NativeDesktopLayoutGroupId ParseGroup(string value) =>
            Enum.TryParse(value, true, out NativeDesktopLayoutGroupId group)
                ? group
                : NativeDesktopLayoutGroupId.UNASSIGNED;

        private static string NormalizePath(string path)
        {
            try
            {
                return System.IO.Path.GetFullPath(path)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path?.Trim() ?? string.Empty;
            }
        }
    }

    /// <summary>An icon whose position says one group and whose classification says another.</summary>
    public sealed class DesktopZoneMismatch
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public NativeDesktopLayoutGroupId CurrentGroup { get; set; }
        public NativeDesktopLayoutGroupId ZoneGroup { get; set; }
    }
}
