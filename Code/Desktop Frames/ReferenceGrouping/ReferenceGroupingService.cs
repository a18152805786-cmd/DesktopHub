using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames.ReferenceGrouping
{
    internal sealed record ReferenceGroupingPreviewItem(
        ReferenceFrameItem Item,
        ReferenceGroupingDecision Decision,
        string Action);

    internal sealed class ReferenceGroupingApplyResult
    {
        public int Assigned { get; set; }
        public int SkippedLowConfidence { get; set; }
        public int SkippedUnassigned { get; set; }
        public int SkippedManual { get; set; }
        public bool VisualOwnershipConfirmed { get; set; }
    }

    /// <summary>
    /// Bounded, manual/on-demand grouping service. It installs no watcher and
    /// never moves, copies, renames, or deletes the referenced Desktop objects.
    /// </summary>
    internal static class ReferenceGroupingService
    {
        public const bool ReferenceAutoGroupingEnabledByDefault = false;

        public static IReadOnlyList<ReferenceGroupingPreviewItem> BuildDesktopPreview(string? desktopPath = null)
        {
            string desktop = desktopPath ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var preview = new List<ReferenceGroupingPreviewItem>();
            if (!Directory.Exists(desktop)) return preview;

            foreach (string path in Directory.EnumerateFileSystemEntries(desktop, "*", SearchOption.TopDirectoryOnly))
            {
                if (ShouldSkip(path) || !ReferenceFrameItem.IsSupportedPath(path)) continue;
                ReferenceFrameItem item = ReferenceFrameItem.Create(path);
                ReferenceGroupingDecision decision = ReferenceGroupingClassifier.Classify(item);
                string action = decision.IsManualOverride
                    ? "SKIP_MANUAL"
                    : decision.Confidence == GroupingConfidence.High
                        ? "ASSIGN_REFERENCE"
                        : decision.Confidence == GroupingConfidence.Unassigned
                            ? "SKIP_UNASSIGNED"
                            : "SKIP_LOW_CONFIDENCE";
                preview.Add(new ReferenceGroupingPreviewItem(item, decision, action));
            }

            return preview
                .OrderBy(item => item.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static ReferenceGroupingApplyResult ApplyHighConfidenceToFrames(
            IList<dynamic> frames,
            IEnumerable<ReferenceGroupingPreviewItem> preview,
            bool visualOwnershipConfirmed)
        {
            var result = new ReferenceGroupingApplyResult
            {
                VisualOwnershipConfirmed = visualOwnershipConfirmed
            };

            if (!visualOwnershipConfirmed)
                throw new InvalidOperationException(
                    "Per-icon Desktop visual ownership is unavailable; applying references would create duplicate visuals.");

            foreach (ReferenceGroupingPreviewItem entry in preview)
            {
                if (entry.Decision.IsManualOverride)
                {
                    result.SkippedManual++;
                    continue;
                }
                if (entry.Decision.Confidence == GroupingConfidence.Unassigned)
                {
                    result.SkippedUnassigned++;
                    continue;
                }
                if (entry.Decision.Confidence != GroupingConfidence.High)
                {
                    result.SkippedLowConfidence++;
                    continue;
                }

                string title = ReferenceGroupingClassifier.GroupTitle(entry.Decision.Group);
                dynamic? frame = frames.FirstOrDefault(candidate =>
                    string.Equals(candidate.Title?.ToString(), title, StringComparison.CurrentCulture));
                if (frame == null)
                {
                    frame = CreateDataFrame(title, frames.Count);
                    frames.Add(frame);
                }

                JArray items = frame.Items as JArray ?? new JArray();
                frame.Items = items;
                string frameId = frame.Id?.ToString() ?? string.Empty;
                string canonical = NormalizePath(entry.Item.OriginalPath);
                bool exists = items.OfType<JObject>().Any(item =>
                    NormalizePath(ReferenceFrameItem.GetOriginalPath(item))
                        .Equals(canonical, StringComparison.OrdinalIgnoreCase));
                if (exists) continue;

                ReferenceFrameItem persisted = ReferenceFrameItem.Create(canonical, frameId);
                items.Add(persisted.ToJson(items.Count));
                result.Assigned++;
            }

            return result;
        }

        private static JObject CreateDataFrame(string title, int cascadeIndex)
        {
            return new JObject
            {
                ["Id"] = Guid.NewGuid().ToString(),
                ["Title"] = title,
                ["X"] = 20 + ((cascadeIndex % 5) * 24),
                ["Y"] = 20 + ((cascadeIndex % 5) * 24),
                ["Width"] = 360,
                ["Height"] = 180,
                ["ItemsType"] = "Data",
                ["Items"] = new JArray(),
                ["IsLocked"] = "false",
                ["IsHidden"] = "false",
                ["IsRolled"] = "false",
                ["AutoRoll"] = "false",
                ["AlwaysOnTop"] = "false",
                ["UnrolledHeight"] = 180,
                ["TabsEnabled"] = "false",
                ["CurrentTab"] = 0,
                ["Tabs"] = new JArray(),
                ["IconSize"] = "Medium",
                ["IconSpacing"] = 5
            };
        }

        private static bool ShouldSkip(string path)
        {
            try
            {
                if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
            }
            catch { return true; }
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }
    }
}
