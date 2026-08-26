using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace Desktop_Frames.ReferenceGrouping
{
    internal static class ReferenceFrameItemService
    {
        public static bool TryAddDroppedReference(dynamic frame, string sourcePath,
            out JObject? addedItem, out string reason)
        {
            addedItem = null;
            reason = string.Empty;

            if (!ReferenceFrameItem.IsSupportedPath(sourcePath))
            {
                reason = "Unsupported reference type.";
                return false;
            }

            string frameId = frame.Id?.ToString() ?? string.Empty;
            dynamic liveFrame = FrameDataManager.FrameData
                .FirstOrDefault(candidate => candidate.Id?.ToString() == frameId) ?? frame;
            JArray items = GetActiveItems(liveFrame);
            string canonicalSource = NormalizePath(sourcePath);

            bool duplicate = items.OfType<JObject>().Any(existing =>
                NormalizePath(ReferenceFrameItem.GetOriginalPath(existing))
                    .Equals(canonicalSource, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                reason = "Reference already exists in this frame.";
                return false;
            }

            ReferenceFrameItem reference = ReferenceFrameItem.Create(canonicalSource, frameId);
            addedItem = reference.ToJson(items.Count);
            items.Add(addedItem);
            FrameDataManager.SaveFrameData();
            return true;
        }

        public static bool RemoveMembership(JArray items, JObject reference)
        {
            if (!ReferenceFrameItem.IsReference(reference)) return false;
            bool removed = items.Remove(reference);
            if (removed) FrameDataManager.SaveFrameData();
            return removed;
        }

        public static void MarkManualOverride(JObject item, string targetFrameId)
        {
            if (!ReferenceFrameItem.IsReference(item)) return;
            item["ManualFrameId"] = targetFrameId ?? string.Empty;
            item["FrameId"] = targetFrameId ?? string.Empty;
        }

        private static JArray GetActiveItems(dynamic frame)
        {
            bool tabsEnabled = frame.TabsEnabled?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            if (tabsEnabled)
            {
                var tabs = frame.Tabs as JArray ?? new JArray();
                int currentTab = Convert.ToInt32(frame.CurrentTab?.ToString() ?? "0");
                if (currentTab >= 0 && currentTab < tabs.Count && tabs[currentTab] is JObject tab)
                {
                    if (tab["Items"] is not JArray tabItems)
                    {
                        tabItems = new JArray();
                        tab["Items"] = tabItems;
                    }
                    return tabItems;
                }
            }

            if (frame.Items is JArray items) return items;
            var created = new JArray();
            frame.Items = created;
            return created;
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path?.Trim() ?? string.Empty; }
        }
    }
}
