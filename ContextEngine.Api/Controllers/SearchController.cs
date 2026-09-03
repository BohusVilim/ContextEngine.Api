using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ContextEngine.Api.Models.Requests;
using ContextEngine.Api.Models.Responses;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Controllers
{
    /// <summary>
    /// Searches and filters stored chunks.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/search")]
    public class SearchController : ControllerBase
    {
        private readonly ILogger<SearchController> _logger;
        private readonly ISearchService _searchService;
        public SearchController(ILogger<SearchController> logger, ISearchService searchService)
        {
            _logger = logger;
            _searchService = searchService;
        }

        /// <summary>Searches chunks matching the given query and filters.</summary>
        /// <param name="searchRequest">Search query and filter criteria.</param>
        /// <param name="cancellationToken">Cancels the search if the caller disconnects.</param>
        [HttpPost]
        [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
        public async Task<IActionResult> Search([FromBody] SearchRequest searchRequest, CancellationToken cancellationToken)
        {
            var response = await _searchService.SearchAsync(searchRequest, cancellationToken);

            _logger.LogInformation(
                "Search for {Query} with {TypeCount} types, {TopicCount} topics, {TagCount} tags returned {ResultCount} chunks",
                searchRequest.Query, searchRequest.Types?.Count ?? 0, searchRequest.Topics.Count, searchRequest.Tags.Count, response.Chunks.Count);

            return Ok(response);
        }

        /// <summary>
        /// Gets the set of chunk types, topics and tags currently available to filter on.
        /// Call this before <see cref="Search"/> to discover valid, currently non-empty values
        /// for <see cref="Models.Requests.SearchRequest.Types"/>, <c>Topics</c> and <c>Tags</c> —
        /// filtering by a value not in this list will always return zero results.
        /// </summary>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet]
        [ProducesResponseType<SearchableOptionsResponse>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSearchableOptions(CancellationToken cancellationToken)
        {
            var options = await _searchService.GetSearchableOptionsAsync(cancellationToken);
            return Ok(options);
        }
    }
}
