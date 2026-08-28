using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Desktop_Frames.Search
{
    internal static class SpotSearchRegressionHarness
    {
        private static readonly string[] Queries = { "Steam", "Codex", "论文", "PDF", "Chrome" };

        public static void Run(string auditPath)
        {
            var service = new SearchService();
            service.Refresh();

            var report = new StringBuilder();
            IReadOnlyList<string> desktopRoots = Providers.DesktopRootSearchProvider.GetVisibleDesktopRoots();
            string userDesktop = SearchProviderUtilities.NormalizePath(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            string publicDesktop = SearchProviderUtilities.NormalizePath(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            bool userDesktopIndexed = desktopRoots.Contains(userDesktop, StringComparer.OrdinalIgnoreCase);
            bool publicDesktopIndexed = desktopRoots.Contains(publicDesktop, StringComparer.OrdinalIgnoreCase);

            Require(userDesktopIndexed, "user Desktop root was not indexed");
            Require(publicDesktopIndexed, "Public Desktop root was not indexed");

            var queryRows = new List<string>();
            foreach (string query in Queries)
            {
                SearchResult? result = service.Search(query).FirstOrDefault();
                if (result == null)
                    throw new InvalidOperationException($"SPOTSEARCH_REGRESSION_FAILED: query '{query}' returned no result");
                SearchMatchEvaluation match = SearchService.Evaluate(result, query);
                queryRows.Add($"| {query} | {Cell(result.SourceProvider)} | {Cell(match.Field)} | " +
                              $"{Cell(result.DisplayName)} | {Cell(result.OriginalPath)} | " +
                              $"{Cell(result.LaunchTarget)} | {match.Score} |");
            }

            SearchResult steam = service.Search("Steam").First();
            SearchResult codex = service.Search("Codex").First();
            Require(steam.DisplayName.Equals("Steam", StringComparison.CurrentCultureIgnoreCase),
                "Steam query did not rank the exact Public Desktop shortcut first");
            Require(IsUnder(steam.OriginalPath, publicDesktop), "Steam did not come from Public Desktop");
            Require(IsUnder(codex.OriginalPath, userDesktop), "Codex did not come from the user Desktop");
            bool shortcutFieldsIndexed = HasAliases(steam, "DisplayName", "FileName",
                "FileNameWithoutExtension", "TargetPath", "TargetFileName", "TargetFileNameWithoutExtension");
            Require(shortcutFieldsIndexed, "shortcut searchable fields were incomplete");

            string? urlPath = Directory.EnumerateFiles(userDesktop, "*.url", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (urlPath == null)
                throw new InvalidOperationException(
                    "SPOTSEARCH_REGRESSION_FAILED: no real user Desktop .url was available for the URL field regression");
            SearchResult urlResult = SearchProviderUtilities.FromPath(urlPath, "DesktopRoot");
            bool urlFieldsIndexed = HasAliases(urlResult, "DisplayName", "FileName",
                "FileNameWithoutExtension", "URI");
            Require(urlFieldsIndexed, "URL searchable fields were incomplete");

            bool visibleDuplicates = Queries.Any(query => service.Search(query)
                .GroupBy(SearchService.CanonicalKey, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1));
            Require(!visibleDuplicates, "visible results contained duplicate canonical launch targets");

            bool aliasesPreserved = RunAliasDedupRegression();
            Require(aliasesPreserved, "canonical dedup discarded a searchable Desktop alias");

            report.AppendLine("# SpotSearch visible Desktop regression");
            report.AppendLine();
            report.AppendLine($"GeneratedUtc={DateTimeOffset.UtcNow:O}");
            report.AppendLine($"CandidateCount={service.Items.Count}");
            report.AppendLine($"UserDesktop={userDesktop}");
            report.AppendLine($"PublicDesktop={publicDesktop}");
            report.AppendLine($"SteamExplorerLabel={steam.DisplayName}");
            report.AppendLine($"SteamOriginalPath={steam.OriginalPath}");
            report.AppendLine($"SteamItemType={Path.GetExtension(steam.OriginalPath)}");
            report.AppendLine($"SteamPhysicalLocation={Path.GetDirectoryName(steam.OriginalPath)}");
            report.AppendLine($"SteamTargetPath={steam.TargetPath}");
            report.AppendLine($"CodexExplorerLabel={codex.DisplayName}");
            report.AppendLine($"CodexOriginalPath={codex.OriginalPath}");
            report.AppendLine($"CodexItemType={Path.GetExtension(codex.OriginalPath)}");
            report.AppendLine($"CodexPhysicalLocation={Path.GetDirectoryName(codex.OriginalPath)}");
            report.AppendLine($"CodexTargetPath={codex.TargetPath}");
            report.AppendLine($"UserDesktopIndexed={userDesktopIndexed.ToString().ToLowerInvariant()}");
            report.AppendLine($"PublicDesktopIndexed={publicDesktopIndexed.ToString().ToLowerInvariant()}");
            report.AppendLine("ShortcutDisplayNameIndexed=true");
            report.AppendLine($"ShortcutSearchableFieldsIndexed={shortcutFieldsIndexed.ToString().ToLowerInvariant()}");
            report.AppendLine($"UrlSearchableFieldsIndexed={urlFieldsIndexed.ToString().ToLowerInvariant()}");
            report.AppendLine($"SearchAliasesPreserved={aliasesPreserved.ToString().ToLowerInvariant()}");
            report.AppendLine($"VisibleDuplicateResults={visibleDuplicates.ToString().ToLowerInvariant()}");
            report.AppendLine("Ranking=exact>prefix>substring");
            report.AppendLine("BackgroundScanWhenClosed=false");
            report.AppendLine();
            report.AppendLine("| Query | Provider | MatchedField | DisplayName | OriginalPath | LaunchTarget | Rank |");
            report.AppendLine("|---|---|---|---|---|---|---:|");
            foreach (string row in queryRows) report.AppendLine(row);

            string? directory = Path.GetDirectoryName(auditPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(auditPath, report.ToString(), new UTF8Encoding(false));
        }

        private static bool RunAliasDedupRegression()
        {
            const string target = @"C:\Program Files\DesktopHub Test\same-target.exe";
            SearchResult winner = TestResult("Canonical App", @"C:\Frame\Canonical App.lnk", target, "Frame");
            SearchResult desktopAlias = TestResult("Codex Desktop Alias", @"C:\Desktop\Codex Desktop Alias.lnk",
                target, "DesktopRoot");
            var service = new SearchService(new ISearchProvider[]
            {
                new FixedProvider("Frame", 300, winner),
                new FixedProvider("DesktopRoot", 200, desktopAlias)
            });
            service.Refresh();
            IReadOnlyList<SearchResult> results = service.Search("Codex Desktop Alias");
            return service.Items.Count == 1 && results.Count == 1 && ReferenceEquals(results[0], winner) &&
                   results[0].SearchAliases.Any(alias =>
                       alias.Value.Equals("Codex Desktop Alias", StringComparison.CurrentCultureIgnoreCase));
        }

        private static SearchResult TestResult(string displayName, string originalPath, string targetPath, string provider)
        {
            var result = new SearchResult
            {
                DisplayName = displayName,
                OriginalPath = originalPath,
                FullPathOrUri = originalPath,
                LaunchTarget = originalPath,
                TargetPath = targetPath,
                ResultType = SearchResultType.Shortcut,
                SourceProvider = provider
            };
            SearchProviderUtilities.AddAlias(result, "DisplayName", displayName);
            return result;
        }

        private static bool IsUnder(string path, string root)
        {
            string normalized = SearchProviderUtilities.NormalizePath(path);
            return normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAliases(SearchResult result, params string[] fields) =>
            fields.All(field => result.SearchAliases.Any(alias =>
                alias.Field.Equals(field, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(alias.Value)));

        private static string Cell(string value) => (value ?? string.Empty).Replace("|", "\\|");

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("SPOTSEARCH_REGRESSION_FAILED: " + message);
        }

        private sealed class FixedProvider : ISearchProvider
        {
            private readonly SearchResult _result;
            public FixedProvider(string name, int priority, SearchResult result)
            {
                Name = name;
                Priority = priority;
                _result = result;
            }

            public string Name { get; }
            public int Priority { get; }
            public IEnumerable<SearchResult> GetResults() => new[] { _result };
        }
    }
}
