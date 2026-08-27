using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Forms = System.Windows.Forms;

namespace Desktop_Frames.NativeDesktop
{
    public sealed class DesktopGroupMoveResult
    {
        public bool Moved { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int DeltaColumns { get; set; }
        public int DeltaRows { get; set; }
        public int IconsMoved { get; set; }
        public int NewColumn { get; set; }
        public int NewRow { get; set; }
        public bool InternalLayoutPreserved { get; set; }
        public bool FilesystemUnchanged { get; set; } = true;
    }

    /// <summary>
    /// Moves one whole native Desktop icon group by a grid delta after the user
    /// drops its header. There is no mouse polling and no per-pixel Explorer
    /// call: the icons are repositioned exactly once, on drop.
    /// </summary>
    internal static class DesktopGroupDragService
    {
        /// <summary>
        /// Validates a proposed group move and, if it is safe, applies it to
        /// every managed icon in the group at once. A failed validation leaves
        /// the Desktop completely untouched.
        /// </summary>
        public static DesktopGroupMoveResult MoveGroup(
            DesktopLayoutPlan plan,
            NativeDesktopLayoutGroupId groupId,
            int deltaColumns,
            int deltaRows)
        {
            var result = new DesktopGroupMoveResult { DeltaColumns = deltaColumns, DeltaRows = deltaRows };
            DesktopLayoutGroup? group = plan.Groups.FirstOrDefault(candidate => candidate.GroupId == groupId);
            if (group == null || group.Columns <= 0 || group.Rows <= 0)
            {
                result.Reason = "GROUP_NOT_PLANNED";
                return result;
            }
            if (group.Locked)
            {
                result.Reason = "GROUP_LOCKED";
                return result;
            }
            if (deltaColumns == 0 && deltaRows == 0)
            {
                result.Reason = "NO_MOVEMENT";
                result.NewColumn = group.RailColumn;
                result.NewRow = group.RailRow;
                return result;
            }

            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            if (state.AutoArrangeEnabled)
            {
                result.Reason = "AUTO_ARRANGE_ENABLED";
                return result;
            }

            NativeDesktopSafeGrid safeGrid = layout.GetSafeDesktopGrid();
            Rectangle workArea = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens.First().WorkingArea;
            int newColumn = group.RailColumn + deltaColumns;
            int newRow = group.RailRow + deltaRows;
            result.NewColumn = newColumn;
            result.NewRow = newRow;

            // 1. The whole block, header row included, must stay inside the safe grid.
            if (newColumn < 0 || newRow < DesktopLayoutSpacing.GroupLeadingRows ||
                newColumn + group.Columns > safeGrid.SafeColumns ||
                newRow + group.Rows > safeGrid.SafeRows)
            {
                result.Reason = "OUTSIDE_SAFE_GRID";
                return result;
            }

            List<DesktopLayoutItem> members = plan.Items
                .Where(item => ParseGroup(item.LayoutGroupId) == groupId &&
                    item.IsStableIdentity && !item.IsShellNamespace)
                .ToList();
            if (members.Count == 0)
            {
                result.Reason = "GROUP_HAS_NO_MOVABLE_ICONS";
                return result;
            }

            var memberPaths = members
                .Select(item => NormalizePath(item.OriginalPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<NativeDesktopIconState> icons = layout.EnumerateDesktopIcons();

            // 2. Nothing outside the group may already occupy a destination slot,
            //    whether that is a shell icon, another group, or a loose icon.
            var occupied = icons
                .Where(icon => !(icon.IdentityStatus == NativeIconIdentityStatus.StablePath &&
                    memberPaths.Contains(NormalizePath(icon.OriginalPath))))
                .Select(icon => NativeDesktopLayoutPlanner.SlotKey(icon.X, icon.Y, workArea,
                    safeGrid.IconSpacingX, safeGrid.IconSpacingY))
                .ToHashSet(StringComparer.Ordinal);

            for (int row = newRow - DesktopLayoutSpacing.GroupLeadingRows; row < newRow + group.Rows; row++)
            {
                for (int column = newColumn; column < newColumn + group.Columns; column++)
                {
                    string slot = NativeDesktopLayoutPlanner.SlotKey(
                        workArea.Left + (column * safeGrid.IconSpacingX),
                        workArea.Top + (row * safeGrid.IconSpacingY),
                        workArea, safeGrid.IconSpacingX, safeGrid.IconSpacingY);
                    if (!occupied.Contains(slot)) continue;
                    result.Reason = row < newRow ? "HEADER_ROW_COLLISION" : "GROUP_COLLISION";
                    return result;
                }
            }

            // 3. Apply. The whole group shifts by one delta, so the internal
            //    arrangement is preserved by construction.
            NativeDesktopLayoutSnapshot before = layout.CaptureSnapshot();
            DesktopFilesystemFingerprint filesystemBefore = DesktopFilesystemFingerprint.CaptureNames();
            int deltaX = deltaColumns * safeGrid.IconSpacingX;
            int deltaY = deltaRows * safeGrid.IconSpacingY;
            try
            {
                foreach (DesktopLayoutItem member in InVacatingOrder(members, deltaColumns, deltaRows))
                {
                    if (!layout.TrySetPosition(member.OriginalPath, member.TargetX + deltaX, member.TargetY + deltaY,
                            out _, out string reason))
                        throw new InvalidOperationException($"{member.DisplayName}: {reason}");
                    result.IconsMoved++;
                }
            }
            catch (Exception ex)
            {
                layout.Restore(before);
                result.IconsMoved = 0;
                result.Reason = "MOVE_FAILED_ROLLED_BACK: " + ex.Message;
                return result;
            }

            result.FilesystemUnchanged = filesystemBefore.Equals(DesktopFilesystemFingerprint.CaptureNames());
            if (!result.FilesystemUnchanged)
            {
                layout.Restore(before);
                result.IconsMoved = 0;
                result.Reason = "FILESYSTEM_CHANGED_ROLLED_BACK";
                return result;
            }

            // Moving a group must never disturb an icon outside it. Explorer can
            // displace a neighbour when it snaps an item into an occupied cell,
            // and an icon whose identity we cannot resolve would otherwise be
            // pushed around silently, so the whole move is verified and undone
            // if anything else shifted.
            NativeDesktopLayoutSnapshot after = layout.CaptureSnapshot();
            string? displaced = FirstDisplacedOutsider(before, after, memberPaths);
            if (displaced != null)
            {
                layout.Restore(before);
                result.IconsMoved = 0;
                result.Reason = "DISPLACED_OTHER_ICON_ROLLED_BACK: " + displaced;
                return result;
            }

            string? offTarget = FirstMemberOffTarget(after, members, deltaX, deltaY, workArea, safeGrid);
            if (offTarget != null)
            {
                layout.Restore(before);
                result.IconsMoved = 0;
                result.Reason = "MEMBER_OFF_TARGET_ROLLED_BACK: " + offTarget;
                return result;
            }

            // 4. Commit the new geometry into the in-memory plan so a second
            //    drag starts from the position the user can actually see.
            foreach (DesktopLayoutItem member in members)
            {
                member.TargetX += deltaX;
                member.TargetY += deltaY;
            }
            group.RailColumn = newColumn;
            group.RailRow = newRow;
            group.IconBoundingLeft += deltaX;
            group.IconBoundingRight += deltaX;
            group.IconBoundingTop += deltaY;
            group.IconBoundingBottom += deltaY;
            group.HeaderLeft = group.IconBoundingLeft;
            group.HeaderTop = Math.Max(0, group.IconBoundingTop
                - DesktopLayoutSpacing.HeaderToFirstIcon - DesktopLayoutSpacing.HeaderHeight);
            group.HasManualAnchor = true;

            DesktopGroupAnchorStore.Save(groupId, newColumn, newRow, group.Locked, group.ExtraColumns);
            result.InternalLayoutPreserved = true;
            result.Moved = true;
            result.Reason = "MOVED";
            return result;
        }

        /// <summary>
        /// Order that guarantees a destination cell is always vacated before it
        /// is written to.
        ///
        /// The group is a rigid block, so the cell an icon moves into is either
        /// outside the block - already proven empty - or still held by another
        /// member. Moving in the direction of travel first (rightmost first when
        /// shifting right, bottom-most first when shifting down) means that other
        /// member has already left. Writing into a cell Explorer still considers
        /// occupied makes it shove the occupant somewhere arbitrary, which is how
        /// an unrelated icon ends up on the other side of the screen.
        /// </summary>
        private static IEnumerable<DesktopLayoutItem> InVacatingOrder(
            List<DesktopLayoutItem> members, int deltaColumns, int deltaRows)
        {
            IOrderedEnumerable<DesktopLayoutItem> ordered = deltaColumns >= 0
                ? members.OrderByDescending(member => member.TargetX)
                : members.OrderBy(member => member.TargetX);
            return deltaRows >= 0
                ? ordered.ThenByDescending(member => member.TargetY)
                : ordered.ThenBy(member => member.TargetY);
        }

        /// <summary>Name of the first group member that did not land in its intended grid slot.</summary>
        private static string? FirstMemberOffTarget(
            NativeDesktopLayoutSnapshot after,
            List<DesktopLayoutItem> members,
            int deltaX,
            int deltaY,
            Rectangle workArea,
            NativeDesktopSafeGrid safeGrid)
        {
            var actual = after.Icons
                .Where(icon => icon.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .GroupBy(icon => NormalizePath(icon.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (DesktopLayoutItem member in members)
            {
                if (!actual.TryGetValue(NormalizePath(member.OriginalPath), out NativeDesktopIconSnapshotItem? icon))
                    return member.DisplayName;
                string intended = NativeDesktopLayoutPlanner.SlotKey(member.TargetX + deltaX, member.TargetY + deltaY,
                    workArea, safeGrid.IconSpacingX, safeGrid.IconSpacingY);
                string landed = NativeDesktopLayoutPlanner.SlotKey(icon.X, icon.Y, workArea,
                    safeGrid.IconSpacingX, safeGrid.IconSpacingY);
                if (!string.Equals(intended, landed, StringComparison.Ordinal))
                    return member.DisplayName;
            }
            return null;
        }

        /// <summary>
        /// Name of the first icon outside the moved group that changed position,
        /// or null when every outsider stayed put. Icons with an unresolvable
        /// identity are compared as a multiset of positions, which is the
        /// strongest statement that can honestly be made about them.
        /// </summary>
        private static string? FirstDisplacedOutsider(
            NativeDesktopLayoutSnapshot before,
            NativeDesktopLayoutSnapshot after,
            ISet<string> memberPaths)
        {
            bool IsOutsider(NativeDesktopIconSnapshotItem icon) =>
                !(icon.IdentityStatus == NativeIconIdentityStatus.StablePath &&
                  memberPaths.Contains(NormalizePath(icon.OriginalPath)));

            Dictionary<string, (int X, int Y)> beforeStable = before.Icons
                .Where(icon => IsOutsider(icon) && icon.IdentityStatus == NativeIconIdentityStatus.StablePath)
                .GroupBy(icon => NormalizePath(icon.OriginalPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (group.First().X, group.First().Y),
                    StringComparer.OrdinalIgnoreCase);

            foreach (NativeDesktopIconSnapshotItem icon in after.Icons
                         .Where(icon => IsOutsider(icon) && icon.IdentityStatus == NativeIconIdentityStatus.StablePath))
            {
                if (!beforeStable.TryGetValue(NormalizePath(icon.OriginalPath), out (int X, int Y) origin))
                    return icon.DisplayName;
                if (origin.X != icon.X || origin.Y != icon.Y)
                    return icon.DisplayName;
            }

            List<string> Unresolvable(NativeDesktopLayoutSnapshot snapshot) => snapshot.Icons
                .Where(icon => IsOutsider(icon) && icon.IdentityStatus != NativeIconIdentityStatus.StablePath)
                .Select(icon => $"{icon.DisplayName}|{icon.X},{icon.Y}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

            List<string> unresolvableBefore = Unresolvable(before);
            List<string> unresolvableAfter = Unresolvable(after);
            if (!unresolvableBefore.SequenceEqual(unresolvableAfter, StringComparer.Ordinal))
            {
                return unresolvableAfter.Except(unresolvableBefore, StringComparer.Ordinal).FirstOrDefault()
                    ?? "unresolvable icon";
            }
            return null;
        }

        /// <summary>Grid geometry the header window needs to translate pixels into slots.</summary>
        public static (int SpacingX, int SpacingY, int WorkAreaLeft, int WorkAreaTop) GetGridMetrics()
        {
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopSafeGrid safeGrid = layout.GetSafeDesktopGrid();
            Rectangle workArea = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens.First().WorkingArea;
            return (safeGrid.IconSpacingX, safeGrid.IconSpacingY, workArea.Left, workArea.Top);
        }

        private static NativeDesktopLayoutGroupId ParseGroup(string value) =>
            Enum.TryParse(value, true, out NativeDesktopLayoutGroupId group)
                ? group
                : NativeDesktopLayoutGroupId.UNASSIGNED;

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }
    }
}
