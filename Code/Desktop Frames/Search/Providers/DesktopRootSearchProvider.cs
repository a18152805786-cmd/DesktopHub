using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Desktop_Frames.Search.Providers
{
    internal sealed class DesktopRootSearchProvider : ISearchProvider
    {
        public string Name => "DesktopRoot";
        public int Priority => 200;

        private readonly IReadOnlyList<string> _desktopRoots;

        public DesktopRootSearchProvider()
            : this(GetVisibleDesktopRoots())
        {
        }

        internal DesktopRootSearchProvider(IEnumerable<string> desktopRoots)
        {
            _desktopRoots = desktopRoots
                .Select(SearchProviderUtilities.NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IEnumerable<SearchResult> GetResults()
        {
            var results = new List<SearchResult>();
            foreach (string desktop in _desktopRoots.Where(Directory.Exists))
            {
                string[] entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(desktop, "*", SearchOption.TopDirectoryOnly)
                        .ToArray();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string path in entries)
                {
                    if (SearchProviderUtilities.ShouldSkip(path)) continue;
                    results.Add(SearchProviderUtilities.FromPath(path, Name));
                }
            }

            return results;
        }

        internal static IReadOnlyList<string> GetVisibleDesktopRoots()
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            return new[] { userDesktop, publicDesktop }
                .Select(SearchProviderUtilities.NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
