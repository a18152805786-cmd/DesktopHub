using Newtonsoft.Json.Linq;
using Desktop_Frames.ReferenceGrouping;
using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames.Search.Providers
{
    internal sealed class FrameSearchProvider : ISearchProvider
    {
        public string Name => "Frame";
        public int Priority => 300;

        public IEnumerable<SearchResult> GetResults()
        {
            var results = new List<SearchResult>();
            var frames = Framemanager.GetFrameData();
            if (frames == null) return results;

            foreach (dynamic frame in frames)
            {
                if (!string.Equals(frame.ItemsType?.ToString(), "Data", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool tabsEnabled = string.Equals(frame.TabsEnabled?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                if (tabsEnabled && frame.Tabs is JArray tabs)
                {
                    foreach (JToken tab in tabs)
                        AddItems(results, tab["Items"] as JArray, frame);
                }
                else
                {
                    AddItems(results, frame.Items as JArray, frame);
                }
            }

            return results;
        }

        private static void AddItems(List<SearchResult> results, JArray? items, dynamic frame)
        {
            if (items == null) return;

            foreach (JToken item in items)
            {
                string storedPath = ReferenceFrameItem.IsReference(item)
                    ? ReferenceFrameItem.GetOriginalPath(item)
                    : item["Filename"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(storedPath)) continue;

                string path = ResolveFramePath(storedPath);
                SearchResult result = SearchProviderUtilities.FromPath(path, "Frame", frameItem: true);
                result.DisplayName = item["DisplayName"]?.ToString()
                    ?? Path.GetFileNameWithoutExtension(storedPath);
                result.FrameId = frame.Id?.ToString() ?? string.Empty;
                result.FrameTitle = frame.Title?.ToString() ?? string.Empty;
                result.IsFolder = item["IsFolder"]?.ToObject<bool>() ?? Directory.Exists(path);
                if (ReferenceFrameItem.IsReference(item))
                {
                    result.OriginalPath = storedPath;
                    result.TargetPath = item["TargetPath"]?.ToString() ?? result.TargetPath;
                    result.LaunchArguments = item["Arguments"]?.ToString() ?? result.LaunchArguments;
                    result.Uri = item["Uri"]?.ToString() ?? result.Uri;
                    result.LaunchTarget = storedPath;
                    result.IconSource = item["IconSource"]?.ToString() ?? storedPath;
                }
                result.Subtitle = string.IsNullOrWhiteSpace(result.FrameTitle)
                    ? SearchProviderUtilities.FriendlyPath(path)
                    : result.FrameTitle + " · " + SearchProviderUtilities.FriendlyPath(path);
                results.Add(result);
            }
        }

        private static string ResolveFramePath(string storedPath)
        {
            if (Path.IsPathRooted(storedPath)) return SearchProviderUtilities.NormalizePath(storedPath);

            string profileShortcuts = ProfileManager.GetProfileFilePath("Shortcuts");
            string candidate = Path.Combine(profileShortcuts, storedPath);
            return File.Exists(candidate) || Directory.Exists(candidate)
                ? SearchProviderUtilities.NormalizePath(candidate)
                : storedPath;
        }
    }
}
