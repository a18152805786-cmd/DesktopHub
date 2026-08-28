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
                .Select(MergeCanonicalGroup)
                .ToList();
        }

        public IReadOnlyList<SearchResult> Search(string query, int maximum = MaxVisibleResults)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchResult>();
            string term = query.Trim();

            return _items
                .Select(result => new { Result = result, Match = Evaluate(result, term) })
                .Where(item => item.Match.Score > 0)
                .OrderByDescending(item => item.Match.Score)
                .ThenByDescending(item => SourcePriority(item.Result.SourceProvider))
                .ThenBy(item => item.Result.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(Math.Max(1, maximum))
                .Select(item => item.Result)
                .ToList();
        }

        internal static int Score(SearchResult result, string query)
            => Evaluate(result, query).Score;

        internal static SearchMatchEvaluation Evaluate(SearchResult result, string query)
        {
            var fields = new List<(string Field, string Value, int Penalty)>
            {
                ("DisplayName", result.DisplayName, 0),
                ("TargetPath", result.TargetPath, 10),
                ("TargetFileNameWithoutExtension", Path.GetFileNameWithoutExtension(result.TargetPath), 10),
                ("Subtitle", result.Subtitle, 10),
                ("FrameTitle", result.FrameTitle, 10)
            };
            fields.AddRange(result.SearchAliases.Select(alias =>
                (alias.Field, alias.Value, IsDisplayAlias(alias.Field) ? 0 : 10)));

            return fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => new SearchMatchEvaluation(field.Field, field.Value,
                    Math.Max(0, ScoreText(field.Value, query) - field.Penalty)))
                .OrderByDescending(match => match.Score)
                .FirstOrDefault() ?? new SearchMatchEvaluation(string.Empty, string.Empty, 0);
        }

        private static SearchResult MergeCanonicalGroup(
            IGrouping<string, (SearchResult Result, int ProviderPriority)> group)
        {
            SearchResult winner = group.First().Result;
            foreach (SearchResult candidate in group.Select(item => item.Result))
            {
                SearchProviderUtilities.AddAlias(winner, "DisplayName", candidate.DisplayName);
                SearchProviderUtilities.AddAlias(winner, "FileName", Path.GetFileName(candidate.OriginalPath));
                SearchProviderUtilities.AddAlias(winner, "FileNameWithoutExtension",
                    Path.GetFileNameWithoutExtension(candidate.OriginalPath));
                SearchProviderUtilities.AddAlias(winner, "TargetPath", candidate.TargetPath);
                SearchProviderUtilities.AddAlias(winner, "TargetFileName", Path.GetFileName(candidate.TargetPath));
                SearchProviderUtilities.AddAlias(winner, "TargetFileNameWithoutExtension",
                    Path.GetFileNameWithoutExtension(candidate.TargetPath));
                SearchProviderUtilities.AddAlias(winner, "Arguments", candidate.LaunchArguments);
                SearchProviderUtilities.AddAlias(winner, "URI", candidate.Uri);
                foreach (SearchAlias alias in candidate.SearchAliases)
                    SearchProviderUtilities.AddAlias(winner, alias.Field, alias.Value);
            }
            return winner;
        }

        private static bool IsDisplayAlias(string field) =>
            field.Equals("DisplayName", StringComparison.OrdinalIgnoreCase) ||
            field.Equals("FileName", StringComparison.OrdinalIgnoreCase) ||
            field.Equals("FileNameWithoutExtension", StringComparison.OrdinalIgnoreCase);

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

    internal sealed record SearchMatchEvaluation(string Field, string Value, int Score);
}
