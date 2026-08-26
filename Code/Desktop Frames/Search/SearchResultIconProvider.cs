using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Desktop_Frames.Search
{
    internal sealed class SearchResultIconProvider
    {
        private const int MaximumCachedIcons = 64;
        private readonly Dictionary<string, ImageSource?> _cache =
            new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

        public ImageSource? GetIcon(SearchResult result)
        {
            string key = result.IconSource;
            if (string.IsNullOrWhiteSpace(key)) return null;
            if (_cache.TryGetValue(key, out ImageSource? cached)) return cached;

            ImageSource? icon;
            try
            {
                icon = Utility.GetShellIcon(key, result.IsFolder);
            }
            catch
            {
                icon = null;
            }
            if (_cache.Count < MaximumCachedIcons) _cache[key] = icon;
            return icon;
        }

        public void Clear() => _cache.Clear();
    }
}
