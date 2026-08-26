using System.Collections.Generic;

namespace Desktop_Frames.Search
{
    internal interface ISearchProvider
    {
        string Name { get; }
        int Priority { get; }
        IEnumerable<SearchResult> GetResults();
    }
}
