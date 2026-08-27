using ContextEngine.Api.Models.Requests;
using ContextEngine.Api.Models.Responses;

namespace ContextEngine.Api.Services.Interfaces
{
    /// <summary>
    /// Handles searching and filtering of stored chunks.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Gets the distinct set of chunk types, topics and tags currently present across all
        /// stored chunks, i.e. every value that can meaningfully be used to filter a search.
        /// </summary>
        /// <returns>The currently available filter values. Lists are empty, never <see langword="null"/>, when nothing is stored yet.</returns>
        Task<SearchableOptionsResponse> GetSearchableOptionsAsync();

        /// <summary>
        /// Filters stored chunks by <see cref="SearchRequest.Types"/>/<see cref="SearchRequest.Topics"/>/
        /// <see cref="SearchRequest.Tags"/>, then ranks the surviving chunks by how relevant their
        /// content is to <see cref="SearchRequest.Query"/> using embedding similarity
        /// (see <see cref="IEmbeddingService"/>).
        /// </summary>
        /// <param name="searchRequest">Query text and filter criteria.</param>
        /// <returns>The best-matching chunks, most relevant first, capped at <see cref="SearchService.MaxResults"/>.</returns>
        Task<SearchResponse> SearchAsync(SearchRequest searchRequest);
    }
}
