using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Models.Responses
{
    /// <summary>
    /// Result of a chunk search, returned by <see cref="Controllers.SearchController.Search"/>.
    /// </summary>
    public class SearchResponse
    {
        /// <summary>
        /// Chunks matching the search's <c>Types</c>/<c>Topics</c>/<c>Tags</c> filters, ordered by
        /// relevance to the search's <c>Query</c> (most relevant first) when a query was given, or in
        /// their default storage order when it was blank. Capped at
        /// <see cref="Services.SearchService.MaxResults"/> entries.
        /// </summary>
        public List<ChunkDto> Chunks { get; set; } = new();
    }
}
