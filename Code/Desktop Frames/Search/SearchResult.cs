using System;
using System.Collections.Generic;

namespace Desktop_Frames.Search
{
    internal enum SearchResultType
    {
        File,
        Folder,
        Shortcut,
        Url,
        FrameItem
    }

    /// <summary>
    /// A read-only reference to an item in Desktop Hub's managed search scope.
    /// Search never copies, moves, renames, or deletes the referenced object.
    /// </summary>
    internal sealed class SearchResult
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public string FullPathOrUri { get; set; } = string.Empty;
        public SearchResultType ResultType { get; set; }
        public string CategoryKey { get; set; } = "Search_CategoryDesktop";
        public string SourceProvider { get; set; } = string.Empty;
        public string IconSource { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string LaunchTarget { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string LaunchArguments { get; set; } = string.Empty;
        public string Uri { get; set; } = string.Empty;
        public string FrameId { get; set; } = string.Empty;
        public string FrameTitle { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public List<SearchAlias> SearchAliases { get; } = new List<SearchAlias>();
    }

    internal sealed record SearchAlias(string Field, string Value);
}
