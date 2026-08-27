using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Newtonsoft.Json;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Owns the on-desktop group chrome: one header and one zone outline per
    /// group. Everything is created on demand and then sits idle - no timer, no
    /// polling, no animation, no per-frame rendering.
    /// </summary>
    internal static class DesktopGroupHeaderManager
    {
        private static readonly List<DesktopGroupHeaderWindow> Windows = new();
        private static readonly Dictionary<NativeDesktopLayoutGroupId, DesktopGroupZoneWindow> Zones = new();
        private static readonly object SyncRoot = new();
        private static DesktopLayoutPlan? _plan;
        private static string _planPath = string.Empty;
        private static bool _editMode;

        public static int WindowCount
        {
            get { lock (SyncRoot) return Windows.Count(window => window.IsLoaded); }
        }

        /// <summary>Directory the live plan was loaded from, used by header commands.</summary>
        public static string AuditDirectory =>
            string.IsNullOrWhiteSpace(_planPath) ? string.Empty : Path.GetDirectoryName(_planPath) ?? string.Empty;

        /// <summary>
        /// Edit mode turns the zones from a hint into a tool: bounds become
        /// clearly visible, each one states its capacity, and resize handles
        /// appear. It is off by default so normal use stays uncluttered.
        /// </summary>
        public static bool EditMode
        {
            get => _editMode;
            set
            {
                if (_editMode == value) return;
                _editMode = value;
                lock (SyncRoot)
                {
                    foreach (DesktopGroupZoneWindow zone in Zones.Values)
                        zone.EditMode = value;
                }
            }
        }

        public static void ToggleEditMode() => EditMode = !EditMode;

        public static void Show(DesktopLayoutPlan plan) => Show(plan, _planPath);

        public static void Show(DesktopLayoutPlan plan, string planPath)
        {
            CloseAll();
            _plan = plan;
            if (!string.IsNullOrWhiteSpace(planPath)) _planPath = planPath;
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupZone> zones = DesktopGroupZoneStore.Load();
            int spacingX = Math.Max(1, plan.GridSpacingX);
            int spacingY = Math.Max(1, plan.GridSpacingY);

            foreach (DesktopLayoutGroup group in plan.Groups.Where(candidate => candidate.Columns > 0))
            {
                zones.TryGetValue(group.GroupId, out DesktopGroupZone? stored);
                bool locked = stored?.Locked ?? false;
                int managed = DesktopGroupZoneService.ManagedItemCount(plan, group.GroupId);

                // The zone outline exists for every laid-out group, including the
                // ones that draw no header, because the boundary is the thing the
                // user was missing.
                var zoneWindow = new DesktopGroupZoneWindow(group.GroupId) { Locked = locked };
                zoneWindow.SetGeometry(group.IconBoundingLeft, group.IconBoundingTop,
                    group.Columns, group.Rows, spacingX, spacingY, managed);
                zoneWindow.ZoneResized += OnZoneResized;
                lock (SyncRoot) Zones[group.GroupId] = zoneWindow;
                zoneWindow.Show();
                zoneWindow.EditMode = _editMode;

                if (!group.ShowHeader || group.HeaderWidth <= 0 || group.PlannedItemCount <= 0)
                    continue;

                var window = new DesktopGroupHeaderWindow(
                    group.GroupId,
                    group.GroupDisplayName,
                    group.HeaderLeft,
                    group.HeaderTop,
                    group.HeaderWidth,
                    group.HeaderHeight)
                {
                    HasManualAnchor = stored?.IsManualPosition ?? false,
                    HasManualSize = stored?.IsManualSize ?? false,
                    Locked = locked
                };
                window.SetZoneSummary(managed, group.Columns, group.Rows);
                window.GroupDropped += OnGroupDropped;
                window.CommandInvoked += OnCommandInvoked;
                window.HoverChanged += OnHeaderHoverChanged;
                lock (SyncRoot) Windows.Add(window);
                window.Show();
            }
        }

        public static void CloseAll()
        {
            lock (SyncRoot)
            {
                foreach (DesktopGroupHeaderWindow window in Windows.Where(w => w.IsLoaded).ToList())
                    window.Close();
                Windows.Clear();
                foreach (DesktopGroupZoneWindow zone in Zones.Values.Where(z => z.IsLoaded).ToList())
                    zone.Close();
                Zones.Clear();
            }
        }

        /// <summary>Hovering a header reveals that group's whole boundary.</summary>
        private static void OnHeaderHoverChanged(NativeDesktopLayoutGroupId groupId, bool hovered)
        {
            lock (SyncRoot)
            {
                if (Zones.TryGetValue(groupId, out DesktopGroupZoneWindow? zone))
                    zone.SetHeaderHovered(hovered);
            }
        }

        /// <summary>
        /// The user dropped a header. The whole native icon group moves once,
        /// the manual position is stored, and the chrome is rebuilt at its new
        /// home. A rejected move snaps the header back.
        /// </summary>
        private static void OnGroupDropped(NativeDesktopLayoutGroupId groupId, int deltaColumns, int deltaRows)
        {
            DesktopGroupHeaderWindow? source;
            lock (SyncRoot) source = Windows.FirstOrDefault(window => window.GroupId == groupId);
            if (_plan == null)
            {
                source?.SnapHome();
                return;
            }

            DesktopGroupMoveResult result;
            try
            {
                result = DesktopGroupDragService.MoveGroup(_plan, groupId, deltaColumns, deltaRows);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Group drag failed for {groupId}: {ex.Message}");
                source?.SnapHome();
                return;
            }

            if (!result.Moved)
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Group drag rejected for {groupId}: {result.Reason}");
                source?.SnapHome();
                // The snap-back alone does not say why, and "nothing happened"
                // is the most confusing possible answer to a deliberate drag.
                if (!string.Equals(result.Reason, "NO_MOVEMENT", StringComparison.Ordinal))
                    SmartToast.Show(Desktop_Frames.Localization.Strings.AppName, RejectionMessage(result.Reason));
                return;
            }

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Group {groupId} moved by ({deltaColumns},{deltaRows}); icons={result.IconsMoved}; " +
                $"filesystemUnchanged={result.FilesystemUnchanged}");
            PersistPlan();
            // The rebuild closes the very window whose mouse-up we are handling,
            // so it has to wait until this input event has finished unwinding.
            RunAfterCurrentInput(() => { if (_plan != null) Show(_plan); });
        }

        /// <summary>
        /// The user dropped a resize handle. The new size is validated and
        /// stored, then the layout is re-run once so the icons fill the zone the
        /// user actually asked for.
        /// </summary>
        private static void OnZoneResized(NativeDesktopLayoutGroupId groupId, int columns, int rows)
        {
            DesktopGroupZoneWindow? zone;
            lock (SyncRoot) Zones.TryGetValue(groupId, out zone);
            if (_plan == null)
            {
                zone?.SnapBack();
                return;
            }

            DesktopGroupResizeResult result;
            try
            {
                result = DesktopGroupZoneService.Resize(_plan, groupId, columns, rows);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Zone resize failed for {groupId}: {ex.Message}");
                zone?.SnapBack();
                return;
            }

            if (!result.Accepted)
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Zone resize rejected for {groupId}: {result.Reason} " +
                    $"(requested {columns}x{rows}, needs {result.RequiredSlots} slots)");
                zone?.SnapBack();
                SmartToast.Show(Desktop_Frames.Localization.Strings.AppName,
                    ResizeRejectionMessage(result));
                return;
            }

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Zone {groupId} resized to {columns}x{rows}");
            RunAfterCurrentInput(() => RefreshLayout());
        }

        /// <summary>Re-plans and reapplies, then rebuilds the chrome. Manual choices survive.</summary>
        private static void RefreshLayout()
        {
            try
            {
                NativeDesktopLayoutApplicationService.RefreshDualRailLayoutForHeaders(AuditDirectory);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Desktop layout refresh failed: {ex.Message}");
                MessageBox.Show(ex.Message, Desktop_Frames.Localization.Strings.AppName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Queues work for after the current input event has unwound.</summary>
        private static void RunAfterCurrentInput(Action action)
        {
            Application? application = Application.Current;
            if (application == null) return;
            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                        $"Desktop group chrome update failed: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Turns an internal rejection code into something the user can act on.</summary>
        private static string RejectionMessage(string reason) => reason switch
        {
            "GROUP_LOCKED" => Desktop_Frames.Localization.Strings.GroupDragLocked,
            "OUTSIDE_SAFE_GRID" => Desktop_Frames.Localization.Strings.GroupDragOutsideGrid,
            "AUTO_ARRANGE_ENABLED" => Desktop_Frames.Localization.Strings.GroupDragAutoArrange,
            _ when reason.StartsWith("GROUP_COLLISION", StringComparison.Ordinal) ||
                   reason.StartsWith("HEADER_ROW_COLLISION", StringComparison.Ordinal) ||
                   reason.StartsWith("DISPLACED_OTHER_ICON", StringComparison.Ordinal)
                => Desktop_Frames.Localization.Strings.GroupDragBlocked,
            _ => Desktop_Frames.Localization.Strings.GroupDragFailed
        };

        private static string ResizeRejectionMessage(DesktopGroupResizeResult result) => result.Reason switch
        {
            "CAPACITY_TOO_SMALL" or "BELOW_MINIMUM" =>
                Desktop_Frames.Localization.Strings.Get("GroupResizeTooSmall", result.RequiredSlots),
            "OUTSIDE_SAFE_GRID" => Desktop_Frames.Localization.Strings.GroupDragOutsideGrid,
            "GROUP_LOCKED" => Desktop_Frames.Localization.Strings.GroupDragLocked,
            _ => Desktop_Frames.Localization.Strings.GroupResizeFailed
        };

        private static void OnCommandInvoked(NativeDesktopLayoutGroupId groupId, DesktopGroupHeaderCommand command)
        {
            try
            {
                switch (command)
                {
                    case DesktopGroupHeaderCommand.ToggleEditMode:
                        ToggleEditMode();
                        break;
                    case DesktopGroupHeaderCommand.ResetAutoPosition:
                        DesktopGroupZoneStore.ClearPosition(groupId);
                        RunAfterCurrentInput(() => RefreshLayout());
                        break;
                    case DesktopGroupHeaderCommand.ResetAutoSize:
                        DesktopGroupZoneStore.ClearSize(groupId);
                        RunAfterCurrentInput(() => RefreshLayout());
                        break;
                    case DesktopGroupHeaderCommand.ToggleLock:
                        DesktopGroupZoneStore.SetLocked(groupId, !DesktopGroupZoneStore.IsLocked(groupId));
                        bool locked = DesktopGroupZoneStore.IsLocked(groupId);
                        lock (SyncRoot)
                        {
                            foreach (DesktopGroupHeaderWindow window in Windows.Where(w => w.GroupId == groupId))
                                window.Locked = locked;
                            if (Zones.TryGetValue(groupId, out DesktopGroupZoneWindow? zone))
                                zone.Locked = locked;
                        }
                        break;
                    case DesktopGroupHeaderCommand.RefreshLayout:
                        RunAfterCurrentInput(() => DesktopLayoutRefreshCommand.Run(AuditDirectory));
                        break;
                    case DesktopGroupHeaderCommand.TriageUnassigned:
                        ManualClassificationWindow.ShowDialogFor(_plan, AuditDirectory);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Group header command {command} failed for {groupId}: {ex.Message}");
                MessageBox.Show(ex.Message, Desktop_Frames.Localization.Strings.AppName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void PersistPlan()
        {
            if (_plan == null || string.IsNullOrWhiteSpace(_planPath)) return;
            try
            {
                Save(_plan, _planPath);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Desktop layout plan was not saved: {ex.Message}");
            }
        }

        public static DesktopLayoutPlan? CurrentPlan => _plan;

        public static void Save(DesktopLayoutPlan plan, string path)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(plan, Formatting.Indented), new UTF8Encoding(false));
            _plan = plan;
            _planPath = fullPath;
        }

        public static bool RestoreOnStartup(string path)
        {
            if (!File.Exists(path)) return false;
            DesktopLayoutPlan? plan = JsonConvert.DeserializeObject<DesktopLayoutPlan>(File.ReadAllText(path, Encoding.UTF8));
            if (plan == null || plan.LayoutMode != NativeDesktopLayoutMode.DualRail.ToString()) return false;
            Show(plan, path);
            return true;
        }
    }
}
