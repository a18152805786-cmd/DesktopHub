using Desktop_Frames.Search.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames.Search
{
    internal sealed class SearchService
    {
        public const int MaxVisibleResults = 20;
        private readonly IReadOnlyList<ISearchProvider> _providers;
        private List<SearchResult> _items = new List<SearchResult>();

        public SearchService()
            : this(new ISearchProvider[]
            {
                new FrameSearchProvider(),
                new DesktopRootSearchProvider(),
                new ManagedFolderSearchProvider()
            })
        {
        }

        internal SearchService(IReadOnlyList<ISearchProvider> providers)
        {
            _providers = providers;
        }

        public IReadOnlyList<SearchResult> Items => _items;

        public void Refresh()
        {
            var all = new List<(SearchResult Result, int ProviderPriority)>();
            foreach (ISearchProvider provider in _providers)
            {
                try
                {
                    all.AddRange(provider.GetResults().Select(result => (result, provider.Priority)));
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"SpotSearch provider '{provider.Name}' failed: {ex.Message}");
                }
            }

            _items = all
                .OrderByDescending(item => item.ProviderPriority)
                .GroupBy(item => CanonicalKey(item.Result), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First().Result)
                .ToList();
        }

        public IReadOnlyList<SearchResult> Search(string query, int maximum = MaxVisibleResults)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();
            string term = query.Trim();

            return _items
                .Select(result => new { Result = result, Score = Score(result, term) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => SourcePriority(item.Result.SourceProvider))
                .ThenBy(item => item.Result.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Max(1, maximum))
                .Select(item => item.Result)
                .ToList();
        }

        internal static int Score(SearchResult result, string query)
        {
            int displayScore = ScoreText(result.DisplayName, query);
            int secondaryScore = new[]
            {
                result.TargetPath,
                Path.GetFileNameWithoutExtension(result.TargetPath),
                result.Subtitle,
                result.FrameTitle
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Math.Max(0, ScoreText(value, query) - 10))
            .DefaultIfEmpty(0)
            .Max();

            return Math.Max(displayScore, secondaryScore);
        }

        internal static string CanonicalKey(SearchResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Uri))
                return "uri:" + result.Uri.Trim().TrimEnd('/');

            if ((result.ResultType == SearchResultType.Shortcut ||
                 (result.ResultType == SearchResultType.FrameItem &&
                  result.OriginalPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))) &&
                !string.IsNullOrWhiteSpace(result.TargetPath))
            {
                return "shortcut:" + SearchProviderUtilities.NormalizePath(result.TargetPath)
                    + "|" + result.LaunchArguments.Trim();
            }

            string path = !string.IsNullOrWhiteSpace(result.OriginalPath)
                ? result.OriginalPath
                : result.FullPathOrUri;
            return "path:" + SearchProviderUtilities.NormalizePath(path);
        }

        private static int ScoreText(string? value, string query)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (value.Equals(query, StringComparison.CurrentCultureIgnoreCase)) return 100;
            if (value.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 80;
            return value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ? 50 : 0;
        }

        private static int SourcePriority(string provider) => provider switch
        {
            "Frame" => 3,
            "DesktopRoot" => 2,
            _ => 1
        };
    }
}
