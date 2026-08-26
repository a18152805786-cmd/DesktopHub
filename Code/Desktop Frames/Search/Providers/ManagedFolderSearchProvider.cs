using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames.Search.Providers
{
    internal sealed class ManagedFolderSearchProvider : ISearchProvider
    {
        public string Name => "ManagedFolders";
        public int Priority => 100;

        public IEnumerable<SearchResult> GetResults()
        {
            var results = new List<SearchResult>();
            foreach (string folder in GetManagedFolders())
            {
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (SearchProviderUtilities.ShouldSkip(path)) continue;
                        results.Add(SearchProviderUtilities.FromPath(path, Name));
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"SpotSearch skipped unavailable managed folder '{folder}': {ex.Message}");
                }
            }
            return results;
        }

        internal static IReadOnlyList<string> GetManagedFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string desktop = SearchProviderUtilities.NormalizePath(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            string rulesPath = ProfileManager.GetProfileFilePath("auto_organize.json");
            if (!File.Exists(rulesPath)) return new List<string>();

            try
            {
                JArray rules = JArray.Parse(File.ReadAllText(rulesPath));
                foreach (JToken rule in rules)
                {
                    if (rule["IsEnabled"]?.ToObject<bool>() != true) continue;
                    string target = SearchProviderUtilities.NormalizePath(rule["TargetFolderPath"]?.ToString() ?? string.Empty);
                    if (string.IsNullOrEmpty(target) || !Directory.Exists(target)) continue;

                    string? parent = Path.GetDirectoryName(target);
                    if (parent != null && SearchProviderUtilities.NormalizePath(parent)
                        .Equals(desktop, StringComparison.OrdinalIgnoreCase))
                    {
                        folders.Add(target);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                    $"SpotSearch could not read managed folders: {ex.Message}");
            }

            return new List<string>(folders);
        }
    }
}
