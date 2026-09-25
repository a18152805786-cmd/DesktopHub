using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Desktop_Frames.NativeDesktop
{
    public sealed class DesktopGroupResizeResult
    {
        public bool Accepted { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
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
        /// Validates the entire candidate layout without storing the size, down
        /// to every group pinned on screen keeping its slot.
        /// CommitResize owns persistence and rolls it back if application fails.
        /// </summary>
        public static DesktopGroupResizeResult Resize(
            DesktopLayoutPlan plan,
            NativeDesktopLayoutGroupId groupId,
            int columns,
            int rows)
            => Resize(plan, groupId, columns, rows,
                () => DesktopGroupZoneStore.Load(),
                () => new NativeDesktopIconLayoutService().GetSafeDesktopGrid(),
                zones => NativeDesktopLayoutApplicationService.CreateDualRailPlan(zones));

        // Separating the read-only environment from the decision allows the
        // exact production gates to run against an isolated layout snapshot.
        internal static DesktopGroupResizeResult Resize(
            DesktopLayoutPlan plan,
            NativeDesktopLayoutGroupId groupId,
            int columns,
            int rows,
            Func<IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>> loadZones,
            Func<NativeDesktopSafeGrid> getSafeGrid,
            Func<IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone>, DesktopLayoutPlan> createPlan)
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
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = loadZones();
            zones.TryGetValue(groupId, out DesktopGroupZone? previous);
            if (previous?.Locked == true)
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
            if ((long)columns * rows < required)
            {
                result.Reason = "CAPACITY_TOO_SMALL";
                return result;
            }

            NativeDesktopSafeGrid safeGrid = getSafeGrid();
            if ((long)group.RailColumn + columns > safeGrid.SafeColumns ||
                (long)group.RailRow + rows > safeGrid.SafeRows)
            {
                result.Reason = "OUTSIDE_SAFE_GRID";
                return result;
            }

            var proposedZones = zones.ToDictionary(pair => pair.Key, pair => pair.Value);
            proposedZones[groupId] = new DesktopGroupZone
            {
                Column = previous?.Column ?? 0,
                Row = previous?.Row ?? 0,
                GridColumns = previous?.GridColumns ?? 0,
                GridRows = previous?.GridRows ?? 0,
                IsManualPosition = previous?.IsManualPosition ?? false,
                ExtraColumns = previous?.ExtraColumns ?? 0,
                PinnedMembers = previous?.PinnedMembers ?? 0,
                Locked = previous?.Locked ?? false,
                IsManualSize = true,
                ManualColumns = columns,
                ManualRows = rows
            };
            try
            {
                // This is the full, unchanged Dual-Rail planner, not just the
                // resized rectangle: neighbours, anchors and clear space count.
                DesktopLayoutPlan candidate = createPlan(proposedZones);
                NativeDesktopLayoutApplicationService.ValidateDualRailPlan(candidate, safeGrid);
                string? anchorLost = FirstAnchorLost(plan, candidate, () => createPlan(zones));
                if (anchorLost != null)
                {
                    result.Reason = "LAYOUT_INVALID";
                    result.Detail = anchorLost;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Reason = "LAYOUT_INVALID";
                result.Detail = ex.Message;
                return result;
            }

            result.Accepted = true;
            result.Reason = "VALIDATED";
            return result;
        }

        /// <summary>
        /// The first group the plan on screen shows pinned that the candidate plan
        /// no longer keeps at the same slot, described for the rejection, or null.
        ///
        /// A valid layout can still have let a pin go: PlaceAnchoredGroups skips,
        /// without a word, a pinned group whose icons would reach into the block
        /// of a group pinned before it, and the skipped group flows automatically,
        /// often into the middle of the screen. Which pins survive is the
        /// planner's decision, so each one is looked up in the plan it made
        /// rather than predicted here.
        ///
        /// The plan on screen can be stale: a drop the planner does not honour, or
        /// icons added since the last refresh, can leave a pin on screen that the
        /// next refresh drops whatever the size. A missing pin therefore counts
        /// against the resize only if the layout without it keeps that pin, and
        /// that layout is planned only once a pin has gone missing.
        /// </summary>
        private static string? FirstAnchorLost(DesktopLayoutPlan current, DesktopLayoutPlan candidate,
            Func<DesktopLayoutPlan> planWithoutResize)
        {
            List<DesktopLayoutGroup> missing = current.Groups
                .Where(group => group.HasManualAnchor && group.Columns > 0 &&
                    !IsAnchoredAt(candidate, group.GroupId, group.RailColumn, group.RailRow))
                .ToList();
            if (missing.Count == 0) return null;

            DesktopLayoutPlan? unchanged;
            try
            {
                unchanged = planWithoutResize();
            }
            catch
            {
                // Nothing then shows that a missing pin was lost already, so each one counts.
                unchanged = null;
            }

            var lost = new List<(DesktopLayoutGroup Group, int Column, int Row)>();
            foreach (DesktopLayoutGroup group in missing)
            {
                if (unchanged == null)
                {
                    lost.Add((group, group.RailColumn, group.RailRow));
                    continue;
                }
                // Only a slot the next refresh keeps is the resize's to lose; that
                // refresh may already drop the pin, or move it because the group grew.
                DesktopLayoutGroup? kept = unchanged.Groups.FirstOrDefault(other => other.GroupId == group.GroupId);
                if (kept is { HasManualAnchor: true } &&
                    !IsAnchoredAt(candidate, kept.GroupId, kept.RailColumn, kept.RailRow))
                    lost.Add((group, kept.RailColumn, kept.RailRow));
            }
            if (lost.Count == 0) return null;

            (DesktopLayoutGroup first, int column, int row) = lost[0];
            DesktopLayoutGroup? after = candidate.Groups.FirstOrDefault(other => other.GroupId == first.GroupId);
            string outcome = after == null || after.Columns <= 0 ? "would no longer be laid out"
                : after.HasManualAnchor ? $"would move to ({after.RailColumn},{after.RailRow})"
                : $"would flow automatically to ({after.RailColumn},{after.RailRow})";
            string others = lost.Count > 1 ? $"; {lost.Count - 1} more pinned group(s) would move too" : string.Empty;
            return $"ANCHOR_LOST '{first.GroupDisplayName}' ({first.GroupId}) is pinned at ({column},{row}) " +
                $"but {outcome}{others}";
        }

        private static bool IsAnchoredAt(DesktopLayoutPlan plan, NativeDesktopLayoutGroupId groupId, int column, int row) =>
            plan.Groups.Any(group => group.GroupId == groupId && group.HasManualAnchor &&
                group.RailColumn == column && group.RailRow == row);

        /// <summary>
        /// One synchronous commit after the mouse event has unwound. The
        /// caller's rollback restores its plan, window geometry and native
        /// snapshot; the store restores exact bytes even if saving itself fails.
        /// </summary>
        internal static DesktopGroupResizeResult CommitResize(
            NativeDesktopLayoutGroupId groupId, DesktopGroupResizeResult result,
            Action applyLayout, Action rollbackLayout)
        {
            if (!result.Accepted) return result;
            Action restoreZones = DesktopGroupZoneStore.CaptureRollback();
            bool applyStarted = false;
            try
            {
                DesktopGroupZoneStore.SaveSize(groupId, result.Columns, result.Rows);
                applyStarted = true;
                applyLayout();
                result.Reason = "RESIZED";
            }
            catch (Exception ex)
            {
                result.Accepted = false;
                result.Reason = "APPLY_FAILED_ROLLED_BACK";
                result.Detail = ex.Message;
                var failures = new List<string>();
                try { restoreZones(); }
                catch (Exception rollbackError) { failures.Add("metadata: " + rollbackError.Message); }
                if (applyStarted)
                {
                    try { rollbackLayout(); }
                    catch (Exception rollbackError) { failures.Add("layout: " + rollbackError.Message); }
                }
                if (failures.Count > 0)
                {
                    result.Reason = "ROLLBACK_FAILED";
                    result.Detail += "; rollback: " + string.Join("; ", failures);
                }
            }
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

        public static Rectangle GetWorkArea() => DesktopChromeDpi.PrimaryWorkArea();

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
