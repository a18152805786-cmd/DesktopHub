using Desktop_Frames.ReferenceGrouping;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Desktop_Frames.NativeDesktop
{
    /// <summary>
    /// Narrow, opt-in operations used for the Phase 5.2 certification and the
    /// bounded first production batch. Nothing here runs during normal startup.
    /// </summary>
    internal static class NativeDesktopFinalCertificationService
    {
        private const string TestFrameTitle = "NATIVE FINAL TEST";
        private const string ProductionFrameTitle = "AI · 开发";

        public static void PrepareTestFrame(string testShortcutPath, string auditDirectory)
        {
            if (!ReferenceFrameItem.IsSupportedPath(testShortcutPath))
                throw new InvalidOperationException("The native final test shortcut does not exist or is unsupported.");

            var layout = new NativeDesktopIconLayoutService();
            Directory.CreateDirectory(auditDirectory);
            NativeDesktopIconLayoutService.SaveSnapshot(layout.CaptureSnapshot(),
                Path.Combine(auditDirectory, "ui-phase5-2-final-layout-before.json"));

            RemoveFrame(TestFrameTitle);
            JObject frame = CreateNativeFrame(TestFrameTitle, new[] { testShortcutPath });
            FrameDataManager.FrameData.Add(frame);
            FrameDataManager.SaveFrameData();
            Framemanager.CreateFrame(frame, new TargetChecker(1000));
        }

        public static void CleanupTestFrame(string auditDirectory)
        {
            string snapshotPath = Path.Combine(auditDirectory, "ui-phase5-2-final-layout-before.json");
            if (File.Exists(snapshotPath))
            {
                var layout = new NativeDesktopIconLayoutService();
                NativeDesktopRestoreResult restore = layout.Restore(NativeDesktopIconLayoutService.LoadSnapshot(snapshotPath));
                if (restore.Failed != 0)
                    throw new InvalidOperationException("TEST_LAYOUT_RESTORE_FAILED: " + string.Join("; ", restore.Errors));
            }
            RemoveFrame(TestFrameTitle);
            FrameDataManager.SaveFrameData();
        }

        public static NativeProductionBatchResult ApplyProductionBatchOne(string auditDirectory)
        {
            Directory.CreateDirectory(auditDirectory);
            var layout = new NativeDesktopIconLayoutService();
            NativeDesktopListViewState state = layout.GetListViewState();
            if (state.AutoArrangeEnabled)
                return new NativeProductionBatchResult { Reason = "AUTO_ARRANGE_ENABLED" };

            IReadOnlyList<ReferenceGroupingPreviewItem> preview = ReferenceGroupingService.BuildDesktopPreview();
            List<ReferenceGroupingPreviewItem> candidates = SelectPrimaryHighConfidenceShortcuts(preview, layout).Take(5).ToList();
            if (candidates.Count == 0)
                return new NativeProductionBatchResult { Reason = "NO_ELIGIBLE_HIGH_CONFIDENCE_SHORTCUTS" };

            string snapshotPath = Path.Combine(auditDirectory, "ui-phase5-final-production-before.json");
            NativeDesktopIconLayoutService.SaveSnapshot(layout.CaptureSnapshot(), snapshotPath);

            dynamic? target = FrameDataManager.FrameData.FirstOrDefault(frame =>
                string.Equals(frame.Title?.ToString(), ProductionFrameTitle, StringComparison.CurrentCulture));
            if (target != null && HasFrameRenderedItems(target))
                return new NativeProductionBatchResult { Reason = "EXISTING_FRAME_HAS_FRAME_RENDERED_ITEMS" };

            if (target == null)
            {
                target = CreateNativeFrame(ProductionFrameTitle, candidates.Select(candidate => candidate.Item.OriginalPath));
                FrameDataManager.FrameData.Add(target);
            }
            else
            {
                AddNativeReferences(target, candidates.Select(candidate => candidate.Item.OriginalPath));
            }

            FrameDataManager.SaveFrameData();
            string frameId = target.Id?.ToString() ?? string.Empty;
            NonActivatingWindow? existing = Application.Current.Windows.OfType<NonActivatingWindow>()
                .FirstOrDefault(window => window.Tag?.ToString() == frameId);
            existing?.Close();
            Framemanager.CreateFrame(target, new TargetChecker(1000));

            return new NativeProductionBatchResult
            {
                Applied = true,
                Reason = "APPLIED",
                FrameTitle = ProductionFrameTitle,
                SnapshotPath = snapshotPath,
                Items = candidates.Select(candidate => candidate.Item.OriginalPath).ToList()
            };
        }

        public static int WriteProductionPreview(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("A production preview output path is required.", nameof(outputPath));

            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            IReadOnlyList<ReferenceGroupingPreviewItem> preview = ReferenceGroupingService.BuildDesktopPreview();
            int highConfidence = preview.Count(item => item.Decision.Confidence == GroupingConfidence.High);
            int highShortcuts = preview.Count(item => item.Decision.Confidence == GroupingConfidence.High &&
                                                       item.Item.ReferenceType == ReferenceType.Shortcut);
            int highAiDev = preview.Count(item => item.Decision.Confidence == GroupingConfidence.High &&
                                                  item.Decision.Group == ReferenceGroup.AiDev);

            var markdown = new StringBuilder();
            markdown.AppendLine("# Phase 5 final production preview");
            markdown.AppendLine();
            markdown.AppendLine($"- GeneratedLocalTime: `{DateTime.Now:O}`");
            markdown.AppendLine($"- DesktopRoot: `{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}`");
            markdown.AppendLine($"- CandidateCount: `{preview.Count}`");
            markdown.AppendLine($"- HighConfidenceCount: `{highConfidence}`");
            markdown.AppendLine($"- HighConfidenceShortcutCount: `{highShortcuts}`");
            markdown.AppendLine($"- HighConfidenceAiDevCount: `{highAiDev}`");
            markdown.AppendLine("- ProductionApply: `NOT_RUN` (native real-GUI gate was not certified)");
            markdown.AppendLine();
            markdown.AppendLine("| DisplayName | Type | Group | Confidence | TargetPath | OriginalPath | Action | Reason |");
            markdown.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (ReferenceGroupingPreviewItem entry in preview)
            {
                markdown.Append('|').Append(MarkdownCell(entry.Item.DisplayName)).Append('|')
                    .Append(MarkdownCell(entry.Item.ReferenceType.ToString())).Append('|')
                    .Append(MarkdownCell(entry.Decision.Group.ToString())).Append('|')
                    .Append(MarkdownCell(entry.Decision.Confidence.ToString())).Append('|')
                    .Append(MarkdownCell(entry.Item.TargetPath)).Append('|')
                    .Append(MarkdownCell(entry.Item.OriginalPath)).Append('|')
                    .Append(MarkdownCell(entry.Action)).Append('|')
                    .Append(MarkdownCell(entry.Decision.Reason)).AppendLine("|");
            }

            File.WriteAllText(outputPath, markdown.ToString(), new UTF8Encoding(false));
            return highConfidence;
        }

        public static NativeDesktopRestoreResult RestoreProductionLayout(string auditDirectory)
        {
            string snapshotPath = Path.Combine(auditDirectory, "ui-phase5-final-production-before.json");
            if (!File.Exists(snapshotPath)) throw new FileNotFoundException("Production layout snapshot was not found.", snapshotPath);
            var layout = new NativeDesktopIconLayoutService();
            return layout.Restore(NativeDesktopIconLayoutService.LoadSnapshot(snapshotPath));
        }

        private static List<ReferenceGroupingPreviewItem> SelectPrimaryHighConfidenceShortcuts(
            IReadOnlyList<ReferenceGroupingPreviewItem> preview, NativeDesktopIconLayoutService layout)
        {
            Drawing.Rectangle primary = Forms.Screen.PrimaryScreen?.Bounds ?? Forms.Screen.AllScreens.First().Bounds;
            var native = layout.EnumerateDesktopIcons()
                .Where(icon => icon.IdentityStatus.IsResolved() && primary.Contains(icon.X, icon.Y))
                .Select(icon => NormalizePath(icon.OriginalPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return preview
                .Where(entry => entry.Decision.Confidence == GroupingConfidence.High &&
                                entry.Decision.Group == ReferenceGroup.AiDev &&
                                entry.Item.ReferenceType == ReferenceType.Shortcut &&
                                !string.IsNullOrWhiteSpace(entry.Item.TargetPath) &&
                                native.Contains(NormalizePath(entry.Item.OriginalPath)))
                .OrderBy(entry => entry.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static JObject CreateNativeFrame(string title, IEnumerable<string> paths)
        {
            Drawing.Rectangle workArea = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.Screen.AllScreens.First().WorkingArea;
            string frameId = Guid.NewGuid().ToString();
            var items = new JArray();
            int order = 0;
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                items.Add(ReferenceFrameItem.Create(path, frameId).ToJson(order++, VisualOwnershipModes.NativeDesktopIcon));

            return new JObject
            {
                ["Id"] = frameId,
                ["Title"] = title,
                ["X"] = Math.Max(workArea.Left + 140, workArea.Left),
                ["Y"] = Math.Max(workArea.Top + 120, workArea.Top),
                ["Width"] = 640,
                ["Height"] = 440,
                ["ItemsType"] = "Data",
                ["Items"] = items,
                ["IsLocked"] = "false",
                ["IsHidden"] = "false",
                ["IsRolled"] = "false",
                ["AutoRoll"] = "false",
                ["NativeModeRollUpSupported"] = false,
                ["AlwaysOnTop"] = "false",
                ["UnrolledHeight"] = 440,
                ["TabsEnabled"] = "false",
                ["CurrentTab"] = 0,
                ["Tabs"] = new JArray(),
                ["IconSize"] = "Medium",
                ["IconSpacing"] = 5,
                ["NativeVisualGroupingEnabled"] = true
            };
        }

        private static void AddNativeReferences(dynamic frame, IEnumerable<string> paths)
        {
            JArray items = frame.Items as JArray ?? new JArray();
            frame.Items = items;
            string frameId = frame.Id?.ToString() ?? string.Empty;
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string normalized = NormalizePath(path);
                JObject? existing = items.OfType<JObject>().FirstOrDefault(item =>
                    string.Equals(NormalizePath(ReferenceFrameItem.GetOriginalPath(item)), normalized, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing["VisualOwnershipMode"] = VisualOwnershipModes.NativeDesktopIcon;
                    continue;
                }
                items.Add(ReferenceFrameItem.Create(normalized, frameId).ToJson(items.Count, VisualOwnershipModes.NativeDesktopIcon));
            }
        }

        private static bool HasFrameRenderedItems(dynamic frame)
        {
            try
            {
                return (frame.Items as JArray)?.OfType<JObject>().Any(item =>
                    !ReferenceFrameItem.IsNativeDesktopIcon(item)) == true;
            }
            catch { return true; }
        }

        private static void RemoveFrame(string title)
        {
            List<dynamic> frames = FrameDataManager.FrameData;
            foreach (dynamic frame in frames.Where(frame => string.Equals(frame.Title?.ToString(), title, StringComparison.CurrentCulture)).ToList())
            {
                string id = frame.Id?.ToString() ?? string.Empty;
                Application.Current.Windows.OfType<NonActivatingWindow>()
                    .FirstOrDefault(window => window.Tag?.ToString() == id)?.Close();
                frames.Remove(frame);
            }
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }

        private static string MarkdownCell(string? value) =>
            (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    internal sealed class NativeProductionBatchResult
    {
        public bool Applied { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string FrameTitle { get; set; } = string.Empty;
        public string SnapshotPath { get; set; } = string.Empty;
        public List<string> Items { get; set; } = new();
    }
}
