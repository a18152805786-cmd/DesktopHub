using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
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
        private static bool _applyingScheme;
        private static DispatcherTimer? _displayCheck;
        // A display whose automatic apply failed is not retried on every later
        // notification; the tray refresh is the way to try again.
        private static DesktopDisplayKey? _autoApplyFailedFor;

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
            EnsureDisplayWatch();
            // A layout that made it onto the screen clears an earlier failed attempt.
            _autoApplyFailedFor = null;
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
                    group.Columns, group.Rows, spacingX, spacingY, managed, plan.Dpi);
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
                    group.HeaderHeight,
                    plan.Dpi)
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

        /// <summary>
        /// Whether a plan still describes the desktop Explorer is showing. A plan
        /// is pixels on one particular grid; after a resolution or scaling change
        /// its headers and zones point at cells that no longer exist.
        /// </summary>
        internal static bool PlanMatchesLiveDesktop(DesktopLayoutPlan plan, out string detail)
        {
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            NativeDesktopSafeGrid grid = layout.GetSafeDesktopGrid();
            int dpi = DesktopChromeDpi.WindowDpi(state.Handle);
            detail = $"plan {plan.ListViewClientWidth}x{plan.ListViewClientHeight} at {plan.Dpi} DPI, " +
                     $"cell {plan.GridSpacingX}x{plan.GridSpacingY}; desktop {grid.ClientWidth}x{grid.ClientHeight} " +
                     $"at {dpi} DPI, cell {grid.IconSpacingX}x{grid.IconSpacingY}; process {DesktopChromeDpi.ProcessDpi} DPI";
            return plan.Dpi == dpi &&
                   plan.ListViewClientWidth == grid.ClientWidth && plan.ListViewClientHeight == grid.ClientHeight &&
                   plan.GridSpacingX == grid.IconSpacingX && plan.GridSpacingY == grid.IconSpacingY;
        }

        /// <summary>Like <see cref="PlanMatchesLiveDesktop"/>, but a desktop that cannot be read counts as a mismatch.</summary>
        internal static bool PlanStillFitsDesktop(DesktopLayoutPlan plan)
        {
            try
            {
                return PlanMatchesLiveDesktop(plan, out _);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Follows the display. Headers drawn for the old grid sit over unrelated
        /// icons and swallow their clicks, so they are hidden as soon as the display
        /// stops matching the plan. The new display's own scheme is then applied,
        /// unless the user turned that off - then a toast says how to do it by hand.
        /// </summary>
        private static void EnsureDisplayWatch()
        {
            Application? application = Application.Current;
            if (_displayCheck != null || application == null) return;
            // Explorer re-spaces its grid a moment after the display changes, so
            // the check waits out the burst of notifications one change produces.
            var timer = new DispatcherTimer(DispatcherPriority.Background, application.Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                OnDisplaySettled();
            };
            _displayCheck = timer;
            SystemEvents.DisplaySettingsChanged += (_, _) => RestartDisplayCheck();
            SystemEvents.UserPreferenceChanged += (_, args) =>
            {
                if (args.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.Window or
                    UserPreferenceCategory.General)
                    RestartDisplayCheck();
            };
        }

        private static void RestartDisplayCheck() =>
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _displayCheck?.Stop();
                _displayCheck?.Start();
            }));

        private static void OnDisplaySettled()
        {
            if (_resizeInProgress || _applyingScheme)
            {
                // Look again once the gesture or the apply has finished.
                RestartDisplayCheck();
                return;
            }
            DesktopLayoutPlan? plan = _plan;
            // Nothing to follow until the desktop has been laid out at least once.
            if (plan == null && string.IsNullOrWhiteSpace(_planPath)) return;

            bool current;
            string detail = "no plan shown";
            try
            {
                current = plan != null && PlanMatchesLiveDesktop(plan, out detail);
            }
            catch
            {
                return; // Explorer is restarting; the next notification looks again.
            }
            bool chromeShown;
            lock (SyncRoot) chromeShown = Windows.Count > 0 || Zones.Count > 0;
            if (current && chromeShown) return;

            if (!DesktopDisplaySchemes.AutoApplyOnDisplayChange)
            {
                // Hide the stale chrome and say so once; the user refreshes by hand.
                if (!chromeShown) return;
                CloseAll();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Display changed; desktop group chrome hidden until the layout is refreshed ({detail}).");
                SmartToast.Show(Desktop_Frames.Localization.Strings.AppName,
                    Desktop_Frames.Localization.Strings.DesktopLayoutDisplayChanged);
                return;
            }
            CloseAll();
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"Display changed; applying the layout scheme of the new display ({detail}).");
            ApplySchemeForCurrentDisplay(attempt: 1);
        }

        /// <summary>
        /// Lays the desktop out from the scheme of the display Explorer is showing
        /// now - exactly what "refresh desktop layout" does, minus the question
        /// about dropped icons: after a display change every icon sits wherever
        /// Explorer reflowed it, which says nothing about where the user wants it.
        /// </summary>
        private static void ApplySchemeForCurrentDisplay(int attempt)
        {
            // A delayed start-up apply and a display notification can both ask;
            // whichever comes second finds the work done.
            DesktopLayoutPlan? shown = _plan;
            bool chromeShown;
            lock (SyncRoot) chromeShown = Windows.Count > 0 || Zones.Count > 0;
            if (shown != null && chromeShown && PlanStillFitsDesktop(shown)) return;

            DesktopDisplayKey key;
            try
            {
                key = DesktopDisplaySchemes.Refresh();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Desktop layout scheme not applied, the desktop is not readable: {ex.Message}");
                return;
            }
            // A failure only holds for the display it happened on.
            if (_autoApplyFailedFor.HasValue && _autoApplyFailedFor != key) _autoApplyFailedFor = null;
            if (_autoApplyFailedFor == key) return;

            _applyingScheme = true;
            try
            {
                DesktopLayoutRefreshCommand.Run(AuditDirectory, askAboutDroppedIcons: false);
                _autoApplyFailedFor = null;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Desktop layout scheme {key} applied after a display change.");
                SmartToast.Show(Desktop_Frames.Localization.Strings.AppName,
                    Desktop_Frames.Localization.Strings.Get("DesktopLayoutSchemeApplied", DesktopDisplaySchemes.Describe(key)));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"Desktop layout scheme {key} not applied (attempt {attempt}): {ex.Message}");
                if (attempt == 1)
                {
                    // Explorer can still be reflowing icons a few seconds after the
                    // change, and a move into a slot it is about to fill is rolled back.
                    RunLater(TimeSpan.FromSeconds(5), () => ApplySchemeForCurrentDisplay(attempt: 2));
                    return;
                }
                _autoApplyFailedFor = key;
                SmartToast.Show(Desktop_Frames.Localization.Strings.AppName,
                    Desktop_Frames.Localization.Strings.Get("DesktopLayoutSchemeApplyFailed",
                        DesktopDisplaySchemes.Describe(key), FirstLine(ex.Message)));
            }
            finally
            {
                _applyingScheme = false;
            }
        }

        private static void RunLater(TimeSpan delay, Action action)
        {
            Application? application = Application.Current;
            if (application == null) return;
            var timer = new DispatcherTimer(DispatcherPriority.Background, application.Dispatcher) { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        private static string FirstLine(string message)
        {
            string line = message.Split('\n')[0].Trim();
            return line.Length <= 120 ? line : line.Substring(0, 117) + "...";
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
            // Remember where the plan lives, so a refresh replaces it in place,
            // and follow the display from now on even if nothing is drawn yet.
            _planPath = Path.GetFullPath(path);
            EnsureDisplayWatch();

            bool current = true;
            string detail = string.Empty;
            try
            {
                current = PlanMatchesLiveDesktop(plan, out detail);
            }
            catch
            {
                // Explorer is not up yet; the saved plan is the best information
                // there is. Compare again once it is, in case the display changed.
                RunLater(TimeSpan.FromSeconds(5), OnDisplaySettled);
            }
            if (!current)
            {
                // The display changed while DesktopHub was not running - typically
                // the laptop went home or came back. Drawing this plan would put
                // headers over icons it no longer describes.
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Desktop group chrome not restored: the display changed since the layout was planned ({detail}).");
                if (DesktopDisplaySchemes.AutoApplyOnDisplayChange)
                    // At sign-in Explorer is still placing icons; let it finish first.
                    RunLater(TimeSpan.FromSeconds(4), () => ApplySchemeForCurrentDisplay(attempt: 1));
                else
                    SmartToast.Show(Desktop_Frames.Localization.Strings.AppName,
                        Desktop_Frames.Localization.Strings.DesktopLayoutDisplayChanged);
                return false;
            }
            Show(plan, path);
            return true;
        }
    }
}
