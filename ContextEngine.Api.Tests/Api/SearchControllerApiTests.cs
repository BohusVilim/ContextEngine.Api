using Microsoft.Extensions.DependencyInjection;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Models.Chunk;
using ContextEngine.Api.Models.Requests;
using ContextEngine.Api.Models.Responses;
using ContextEngine.Api.Services.Interfaces;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Api
{
    // Tests share one ContextEngineApiFactory (and its database) per class instance, so they use
    // unique, randomly generated topic/tag values and assert with Contains rather than assuming an
    // empty database - the same pattern DocumentsControllerApiTests uses.
    public class SearchControllerApiTests : IClassFixture<ContextEngineApiFactory>
    {
        // HttpContent.ReadFromJsonAsync doesn't pick up the AddJsonOptions converters configured in
        // Program.cs (those only apply to MVC's own output formatter), so ChunkType-as-string needs
        // to be declared again here to deserialize it.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ContextEngineApiFactory _factory;
        private readonly HttpClient _client;

        public SearchControllerApiTests(ContextEngineApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetSearchableOptions_ReturnsSeededTypeTopicAndTag()
        {
            var topic = $"topic-{Guid.NewGuid()}";
            var tag = $"tag-{Guid.NewGuid()}";
            await SeedChunkAsync(ChunkType.Definition, topics: new List<string> { topic }, tags: new List<string> { tag });

            var response = await _client.GetAsync("/api/search");

            response.EnsureSuccessStatusCode();
            var options = await response.Content.ReadFromJsonAsync<SearchableOptionsResponse>(JsonOptions);

            Assert.Contains(ChunkType.Definition, options!.Types);
            Assert.Contains(topic, options.Topics);
            Assert.Contains(tag, options.Tags);
        }

        [Fact]
        public async Task GetSearchableOptions_DuplicateTopicAcrossChunks_AppearsOnlyOnce()
        {
            var topic = $"topic-{Guid.NewGuid()}";
            await SeedChunkAsync(ChunkType.Paragraph, topics: new List<string> { topic });
            await SeedChunkAsync(ChunkType.Heading, topics: new List<string> { topic });

            var response = await _client.GetAsync("/api/search");

            response.EnsureSuccessStatusCode();
            var options = await response.Content.ReadFromJsonAsync<SearchableOptionsResponse>(JsonOptions);

            Assert.Single(options!.Topics, t => t == topic);
        }

        [Fact]
        public async Task GetSearchableOptions_SerializesChunkTypesAsStrings_NotNumbers()
        {
            await SeedChunkAsync(ChunkType.Warning);

            var response = await _client.GetAsync("/api/search");

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var types = document.RootElement.GetProperty("types");

            Assert.True(types.EnumerateArray().Any(), "Expected at least one chunk type in the response.");
            Assert.All(types.EnumerateArray(), type => Assert.Equal(JsonValueKind.String, type.ValueKind));
            Assert.Contains(types.EnumerateArray(), type => type.GetString() == nameof(ChunkType.Warning));
        }

        [Fact]
        public async Task Search_AcceptsChunkTypeNamesAsStrings_ReturnsOk()
        {
            // Confirms the request side accepts the same string enum names GetSearchableOptions
            // returns, so a caller can round-trip a value straight from one endpoint into the other.
            var requestJson = """{ "query": "invoice", "types": ["Paragraph", "Heading"] }""";

            var response = await _client.PostAsync(
                "/api/search", new StringContent(requestJson, Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Search_QueryGiven_RanksMoreRelevantChunkFirst()
        {
            var relevantId = await SeedChunkAsync(ChunkType.Paragraph, content: "Your invoice payment is due within 14 days of receipt.");
            var unrelatedId = await SeedChunkAsync(ChunkType.Paragraph, content: "The office kitchen coffee machine is out of order.");

            var response = await _client.PostAsJsonAsync("/api/search", new SearchRequest { Query = "invoice payment due date" }, JsonOptions);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

            var relevantIndex = result!.Chunks.FindIndex(c => c.Id == relevantId);
            var unrelatedIndex = result.Chunks.FindIndex(c => c.Id == unrelatedId);
            Assert.True(relevantIndex >= 0 && unrelatedIndex >= 0, "Expected both seeded chunks in the result.");
            Assert.True(relevantIndex < unrelatedIndex, "Expected the relevant chunk to rank before the unrelated one.");
        }

        [Fact]
        public async Task Search_TypeFilter_ExcludesNonMatchingTypes()
        {
            var tag = $"tag-{Guid.NewGuid()}";
            var matchingId = await SeedChunkAsync(ChunkType.Note, tags: new List<string> { tag });
            await SeedChunkAsync(ChunkType.Quote, tags: new List<string> { tag });

            var response = await _client.PostAsJsonAsync("/api/search", new SearchRequest
            {
                Query = "",
                Types = new List<ChunkType> { ChunkType.Note },
                Tags = new List<string> { tag }
            }, JsonOptions);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

            var chunk = Assert.Single(result!.Chunks);
            Assert.Equal(matchingId, chunk.Id);
        }

        [Fact]
        public async Task Search_NoMatchingTag_ReturnsEmptyChunkList()
        {
            var response = await _client.PostAsJsonAsync("/api/search", new SearchRequest
            {
                Query = "",
                Tags = new List<string> { $"tag-{Guid.NewGuid()}" }
            }, JsonOptions);

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SearchResponse>(JsonOptions);

            Assert.Empty(result!.Chunks);
        }

        /// <summary>Inserts a chunk directly through the DbContext, with its embedding precomputed the same way DocumentService would at ingestion time, bypassing the parse/upload pipeline so tests can control content precisely.</summary>
        private async Task<Guid> SeedChunkAsync(ChunkType type, string? content = null, List<string>? topics = null, List<string>? tags = null)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ContextEngineDbContext>();
            // Resolves the same singleton IEmbeddingService instance the running app uses (see
            // Program.cs), rather than constructing a new OnnxEmbeddingService here, so this doesn't
            // pay to reload the ONNX model a second time.
            var embeddingService = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

            var chunkId = Guid.NewGuid();
            context.Chunks.Add(new Chunk
            {
                Id = chunkId,
                SourceId = Guid.NewGuid(),
                Type = type,
                Content = content,
                Embedding = await embeddingService.CreateEmbeddingAsync(content),
                Topics = topics ?? new List<string>(),
                Tags = tags ?? new List<string>()
            });

            await context.SaveChangesAsync();

            return chunkId;
        }
    }
}
