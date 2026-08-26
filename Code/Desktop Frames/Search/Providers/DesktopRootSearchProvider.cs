using System;
using System.Collections.Generic;
using System.IO;

namespace Desktop_Frames.Search.Providers
{
    internal sealed class DesktopRootSearchProvider : ISearchProvider
    {
        public string Name => "DesktopRoot";
        public int Priority => 200;

        public IEnumerable<SearchResult> GetResults()
        {
            var results = new List<SearchResult>();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop)) return results;

            foreach (string path in Directory.EnumerateFileSystemEntries(desktop, "*", SearchOption.TopDirectoryOnly))
            {
                if (SearchProviderUtilities.ShouldSkip(path)) continue;
                results.Add(SearchProviderUtilities.FromPath(path, Name));
            }

            return results;
        }
    }
}
