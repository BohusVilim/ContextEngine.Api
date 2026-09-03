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
        private readonly ISearchService _searchService;
        public SearchController(ISearchService searchService)
        {
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
