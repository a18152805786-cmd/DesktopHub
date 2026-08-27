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
    /// Owns the floating group headers. Headers are created on demand and then
    /// sit idle: there is no timer, no polling, and no animation behind them.
    /// </summary>
    internal static class DesktopGroupHeaderManager
    {
        private static readonly List<DesktopGroupHeaderWindow> Windows = new();
        private static readonly object SyncRoot = new();
        private static DesktopLayoutPlan? _plan;
        private static string _planPath = string.Empty;

        public static int WindowCount
        {
            get { lock (SyncRoot) return Windows.Count(window => window.IsLoaded); }
        }

        /// <summary>Directory the live plan was loaded from, used by header commands.</summary>
        public static string AuditDirectory =>
            string.IsNullOrWhiteSpace(_planPath) ? string.Empty : Path.GetDirectoryName(_planPath) ?? string.Empty;

        public static void Show(DesktopLayoutPlan plan) => Show(plan, _planPath);

        public static void Show(DesktopLayoutPlan plan, string planPath)
        {
            CloseAll();
            _plan = plan;
            if (!string.IsNullOrWhiteSpace(planPath)) _planPath = planPath;
            IReadOnlyDictionary<NativeDesktopLayoutGroupId, DesktopGroupAnchor> anchors = DesktopGroupAnchorStore.Load();

            foreach (DesktopLayoutGroup group in plan.Groups.Where(candidate =>
                         candidate.PlannedItemCount > 0 && candidate.ShowHeader && candidate.HeaderWidth > 0))
            {
                var window = new DesktopGroupHeaderWindow(
                    group.GroupId,
                    group.GroupDisplayName,
                    group.HeaderLeft,
                    group.HeaderTop,
                    group.HeaderWidth,
                    group.HeaderHeight)
                {
                    HasManualAnchor = anchors.ContainsKey(group.GroupId),
                    Locked = anchors.TryGetValue(group.GroupId, out DesktopGroupAnchor? anchor) && anchor.Locked
                };
                window.GroupDropped += OnGroupDropped;
                window.CommandInvoked += OnCommandInvoked;
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
            }
        }

        /// <summary>
        /// The user dropped a header. The whole native icon group moves once,
        /// the manual anchor is stored, and the headers are rebuilt at their
        /// new home positions. A rejected move snaps the header back.
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
            RebuildAfterCurrentInput();
        }

        /// <summary>Queues a header rebuild for after the current input event completes.</summary>
        private static void RebuildAfterCurrentInput()
        {
            Application? application = Application.Current;
            if (application == null || _plan == null) return;
            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_plan != null) Show(_plan);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                        $"Desktop group headers were not rebuilt: {ex.Message}");
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

        private static void OnCommandInvoked(NativeDesktopLayoutGroupId groupId, DesktopGroupHeaderCommand command)
        {
            try
            {
                switch (command)
                {
                    case DesktopGroupHeaderCommand.ResetAutoPosition:
                        DesktopGroupAnchorStore.Clear(groupId);
                        NativeDesktopLayoutApplicationService.RefreshDualRailLayoutForHeaders(AuditDirectory);
                        break;
                    case DesktopGroupHeaderCommand.ToggleLock:
                        DesktopGroupAnchorStore.SetLocked(groupId, !DesktopGroupAnchorStore.IsLocked(groupId));
                        lock (SyncRoot)
                        {
                            foreach (DesktopGroupHeaderWindow window in Windows.Where(w => w.GroupId == groupId))
                                window.Locked = DesktopGroupAnchorStore.IsLocked(groupId);
                        }
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
