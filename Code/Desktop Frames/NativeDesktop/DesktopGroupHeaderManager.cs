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
        private static bool _resizeInProgress;

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
        /// Finish mouse-up before rebuilding windows. Validation, saving and
        /// application then run as one UI operation, so another gesture cannot
        /// overwrite rollback state between saving and applying.
        /// </summary>
        private static void OnZoneResized(NativeDesktopLayoutGroupId groupId, int columns, int rows)
        {
            if (_resizeInProgress || Application.Current == null)
            {
                lock (SyncRoot)
                    if (Zones.TryGetValue(groupId, out DesktopGroupZoneWindow? zone)) zone.SnapBack();
                return;
            }
            _resizeInProgress = true;
            RunAfterCurrentInput(() =>
            {
                try { ResizeAndRefresh(groupId, columns, rows); }
                finally { _resizeInProgress = false; }
            });
        }

        private static void ResizeAndRefresh(NativeDesktopLayoutGroupId groupId, int columns, int rows)
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
                SmartToast.Show(Desktop_Frames.Localization.Strings.AppName,
                    Desktop_Frames.Localization.Strings.Get("GroupResizeLayoutRejected", ex.Message));
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

            try
            {
                DesktopLayoutPlan previousPlan = _plan;
                string previousPlanPath = _planPath;
                string auditDirectory = AuditDirectory;
                if (string.IsNullOrWhiteSpace(auditDirectory))
                    auditDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audit");
                string planFile = NativeDesktopLayoutApplicationService.GetDualRailPlanPath(auditDirectory);
                byte[]? previousPlanFile = File.Exists(planFile) ? File.ReadAllBytes(planFile) : null;
                var layout = new NativeDesktopIconLayoutService();
                NativeDesktopLayoutSnapshot before = layout.CaptureSnapshot();

                result = DesktopGroupZoneService.CommitResize(groupId, result,
                    () => NativeDesktopLayoutApplicationService.RefreshDualRailLayoutForHeaders(auditDirectory),
                    () => RestoreResizeLayout(previousPlan, previousPlanPath, planFile, previousPlanFile, layout, before));
            }
            catch (Exception ex)
            {
                // Preparation/capture failed before CommitResize could write.
                result.Accepted = false;
                result.Reason = "LAYOUT_INVALID";
                result.Detail = ex.Message;
            }

            if (!result.Accepted)
            {
                lock (SyncRoot)
                    if (Zones.TryGetValue(groupId, out DesktopGroupZoneWindow? currentZone)) currentZone.SnapBack();
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"Zone resize rejected: {result.Reason}; {result.Detail}");
                SmartToast.Show(Desktop_Frames.Localization.Strings.AppName, ResizeRejectionMessage(result));
                return;
            }
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Zone {groupId} resized to {columns}x{rows}");
        }

        private static void RestoreResizeLayout(DesktopLayoutPlan previousPlan, string previousPlanPath,
            string planFile, byte[]? previousPlanFile, NativeDesktopIconLayoutService layout,
            NativeDesktopLayoutSnapshot before)
        {
            var failures = new List<string>();
            // Apply normally restores its failed moves. Keep an outer snapshot
            // too: saving the plan or rebuilding chrome can fail after moving.
            // Reuse the existing Explorer restore API without changing it.
            try
            {
                // A pre-apply rejection, or Apply's own successful rollback,
                // needs no coordinate writes at all.
                if (!NativeCoordinatesMatch(before, layout.CaptureSnapshot()))
                {
                    NativeDesktopRestoreResult restored = layout.Restore(before);
                    if (restored.Failed != 0)
                        failures.Add("native positions: " + string.Join("; ", restored.Errors));
                }
            }
            catch (Exception ex) { failures.Add("native positions: " + ex.Message); }
            try
            {
                if (previousPlanFile == null) File.Delete(planFile);
                else File.WriteAllBytes(planFile, previousPlanFile);
            }
            catch (Exception ex) { failures.Add("plan file: " + ex.Message); }

            _plan = previousPlan;
            _planPath = previousPlanPath;
            try { Show(previousPlan, previousPlanPath); }
            catch (Exception ex) { failures.Add("windows: " + ex.Message); }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
        }

        private static bool NativeCoordinatesMatch(NativeDesktopLayoutSnapshot before, NativeDesktopLayoutSnapshot after)
        {
            static IEnumerable<(string Identity, string Path, string Label, NativeIconIdentityStatus Status,
                int Ordinal, int X, int Y)> Ordered(NativeDesktopLayoutSnapshot snapshot) =>
                snapshot.Icons.OrderBy(icon => icon.Identity, StringComparer.Ordinal)
                    .ThenBy(icon => icon.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(icon => icon.DisplayName, StringComparer.Ordinal)
                    .ThenBy(icon => icon.LabelOrdinal)
                    .Select(icon => (icon.Identity, icon.OriginalPath, icon.DisplayName,
                        icon.IdentityStatus, icon.LabelOrdinal, icon.X, icon.Y));
            return Ordered(before).SequenceEqual(Ordered(after));
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
            "LAYOUT_INVALID" => Desktop_Frames.Localization.Strings.Get("GroupResizeLayoutRejected", result.Detail),
            "APPLY_FAILED_ROLLED_BACK" => Desktop_Frames.Localization.Strings.Get("GroupResizeRolledBack", result.Detail),
            "ROLLBACK_FAILED" => Desktop_Frames.Localization.Strings.Get("GroupResizeRollbackFailed", result.Detail),
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
