using Microsoft.EntityFrameworkCore;
using ContextEngine.Api.Data;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Models.Chunk;
using ContextEngine.Api.Models.Requests;
using ContextEngine.Api.Models.Responses;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Services
{
    /// <inheritdoc cref="ISearchService"/>
    public class SearchService : ISearchService
    {
        /// <summary>
        /// Maximum number of chunks <see cref="SearchAsync"/> returns. Ranking already has to score
        /// every filtered candidate in memory (see <see cref="SearchAsync"/>), so this cap exists to
        /// keep the response payload itself small and useful, not to bound the ranking work.
        /// </summary>
        public const int MaxResults = 20;

        private readonly ContextEngineDbContext _context;
        private readonly ChunkMappings _chunkMappings;
        private readonly IEmbeddingService _embeddingService;

        public SearchService(ContextEngineDbContext context, ChunkMappings chunkMappings, IEmbeddingService embeddingService)
        {
            _context = context;
            _chunkMappings = chunkMappings;
            _embeddingService = embeddingService;
        }

        /// <inheritdoc/>
        public async Task<SearchableOptionsResponse> GetSearchableOptionsAsync(CancellationToken cancellationToken = default)
        {
            // Type is a plain scalar column, so distinct values can be resolved in SQL.
            var types = await _context.Chunks
                .Select(c => c.Type)
                .Distinct()
                .ToListAsync(cancellationToken);

            // Topics/Tags are stored as JSON text columns (see ContextEngineDbContext), so they
            // can't be flattened/deduplicated at the SQL level; every chunk has to be loaded and
            // flattened in memory.
            var topicsAndTags = await _context.Chunks
                .Select(c => new { c.Topics, c.Tags })
                .ToListAsync(cancellationToken);

            return new SearchableOptionsResponse
            {
                Types = types.OrderBy(t => t).ToList(),
                Topics = topicsAndTags.SelectMany(c => c.Topics).Distinct().OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                Tags = topicsAndTags.SelectMany(c => c.Tags).Distinct().OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        /// <inheritdoc/>
        public async Task<SearchResponse> SearchAsync(SearchRequest searchRequest, CancellationToken cancellationToken = default)
        {
            // Type is a plain scalar column, so it can be filtered at the SQL level. Topics/Tags
            // (JSON text columns - see ContextEngineDbContext) can't be, so every chunk surviving the
            // Types filter has to be loaded and checked against them in memory below.
            var typeFilteredQuery = _context.Chunks.AsQueryable();
            if (searchRequest.Types != null && searchRequest.Types.Count > 0)
            {
                typeFilteredQuery = typeFilteredQuery.Where(c => searchRequest.Types.Contains(c.Type));
            }

            var candidates = await typeFilteredQuery.Include(c => c.Parent).ToListAsync(cancellationToken);

            // A chunk matches if it has at least one of the requested topics/tags (OR, not AND) -
            // requiring every one of them would make adding an extra filter value only ever narrow
            // results, which isn't how filter checkboxes are normally expected to behave.
            if (searchRequest.Topics.Count > 0)
            {
                candidates = candidates.Where(c => searchRequest.Topics.Any(topic => c.Topics.Contains(topic))).ToList();
            }

            if (searchRequest.Tags.Count > 0)
            {
                candidates = candidates.Where(c => searchRequest.Tags.Any(tag => c.Tags.Contains(tag))).ToList();
            }

            var rankedChunks = await RankByRelevanceAsync(candidates, searchRequest.Query, cancellationToken);

            return new SearchResponse { Chunks = _chunkMappings.MapChunksToDtos(rankedChunks) };
        }

        /// <summary>
        /// Orders <paramref name="candidates"/> by embedding similarity to <paramref name="query"/>
        /// (most relevant first) and caps the result at <see cref="MaxResults"/>. A blank query has no
        /// meaningful embedding to rank against, so in that case the candidates are simply truncated
        /// in their existing (storage) order instead of being scored.
        /// </summary>
        private async Task<List<Chunk>> RankByRelevanceAsync(List<Chunk> candidates, string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return candidates.Take(MaxResults).ToList();
            }

            var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(query, cancellationToken);

            return candidates
                .Select(chunk => (Chunk: chunk, Relevance: _embeddingService.CosineSimilarity(queryEmbedding, chunk.Embedding)))
                .OrderByDescending(scored => scored.Relevance)
                .Take(MaxResults)
                .Select(scored => scored.Chunk)
                .ToList();
        }
    }
}
