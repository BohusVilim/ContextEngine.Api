using Microsoft.Extensions.DependencyInjection;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Models.Chunk;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Api
{
    public class ChunksControllerApiTests : IClassFixture<ContextEngineApiFactory>
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

        public ChunksControllerApiTests(ContextEngineApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetChunkById_ExistingChunk_ReturnsIt()
        {
            var chunkId = await SeedChunkAsync(content: "Body text");

            var response = await _client.GetAsync($"/api/chunks/{chunkId}");

            response.EnsureSuccessStatusCode();
            var chunk = await response.Content.ReadFromJsonAsync<ChunkDto>(JsonOptions);
            Assert.Equal(chunkId, chunk!.Id);
            Assert.Equal("Body text", chunk.Content);
        }

        [Fact]
        public async Task GetChunkById_UnknownChunk_ReturnsNotFound()
        {
            var response = await _client.GetAsync($"/api/chunks/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetChunksByDocumentId_ExistingDocument_ReturnsItsChunks()
        {
            var sourceId = Guid.NewGuid();
            await SeedChunkAsync(sourceId: sourceId);

            var response = await _client.GetAsync($"/api/chunks/by-document/{sourceId}");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.NotEmpty(chunks!);
            Assert.All(chunks!, chunk => Assert.Equal(sourceId, chunk.SourceId));
        }

        [Fact]
        public async Task GetChunksByDocumentId_UnknownDocument_ReturnsNotFound()
        {
            var response = await _client.GetAsync($"/api/chunks/by-document/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetChunksByTopic_MatchingTopic_ReturnsChunk()
        {
            var topic = $"topic-{Guid.NewGuid()}";
            var chunkId = await SeedChunkAsync(topics: new List<string> { topic });

            var response = await _client.GetAsync($"/api/chunks/by-topic/{topic}");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.Equal(new List<Guid> { chunkId }, chunks!.Select(c => c.Id).ToList());
        }

        [Fact]
        public async Task GetChunksByTopic_NoMatch_ReturnsEmptyList()
        {
            var response = await _client.GetAsync($"/api/chunks/by-topic/{Guid.NewGuid()}");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.Empty(chunks!);
        }

        [Fact]
        public async Task GetChunksByTag_MatchingTag_ReturnsChunk()
        {
            var tag = $"tag-{Guid.NewGuid()}";
            var chunkId = await SeedChunkAsync(tags: new List<string> { tag });

            var response = await _client.GetAsync($"/api/chunks/by-tag/{tag}");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.Equal(new List<Guid> { chunkId }, chunks!.Select(c => c.Id).ToList());
        }

        [Fact]
        public async Task GetChunksByTag_NoMatch_ReturnsEmptyList()
        {
            var response = await _client.GetAsync($"/api/chunks/by-tag/{Guid.NewGuid()}");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.Empty(chunks!);
        }

        [Fact]
        public async Task GetChunksByDateRange_MatchingRange_ReturnsChunk()
        {
            var createdAt = new DateTimeOffset(2020, 6, 15, 0, 0, 0, TimeSpan.Zero);
            var chunkId = await SeedChunkAsync(createdAt: createdAt);

            var response = await _client.GetAsync(
                "/api/chunks/by-date-range?startDate=2020-06-01&endDate=2020-06-30");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.Equal(new List<Guid> { chunkId }, chunks!.Select(c => c.Id).ToList());
        }

        [Fact]
        public async Task GetChunksByDateRange_NoMatch_ReturnsEmptyList()
        {
            // 1999 predates any chunk this test class ever seeds.
            var response = await _client.GetAsync(
                "/api/chunks/by-date-range?startDate=1999-01-01&endDate=1999-01-02");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.Empty(chunks!);
        }

        [Fact]
        public async Task UpdateChunk_ExistingChunk_ReturnsUpdatedChunk()
        {
            var chunkId = await SeedChunkAsync(content: "Old content");

            var response = await _client.PutAsJsonAsync($"/api/chunks/{chunkId}", new ChunkDto
            {
                Content = "New content",
                Type = ChunkType.Heading,
                Order = 3
            }, JsonOptions);

            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<ChunkDto>(JsonOptions);
            Assert.Equal("New content", updated!.Content);
            Assert.Equal(ChunkType.Heading, updated.Type);
            Assert.Equal(3, updated.Order);

            var getResponse = await _client.GetAsync($"/api/chunks/{chunkId}");
            var persisted = await getResponse.Content.ReadFromJsonAsync<ChunkDto>(JsonOptions);
            Assert.Equal("New content", persisted!.Content);
        }

        [Fact]
        public async Task UpdateChunk_UnknownChunk_ReturnsNotFound()
        {
            var response = await _client.PutAsJsonAsync($"/api/chunks/{Guid.NewGuid()}", new ChunkDto
            {
                Content = "New content"
            }, JsonOptions);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task DeleteChunk_ExistingChunk_ReturnsNoContentAndRemovesIt()
        {
            var chunkId = await SeedChunkAsync();

            var deleteResponse = await _client.DeleteAsync($"/api/chunks/{chunkId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            var getResponse = await _client.GetAsync($"/api/chunks/{chunkId}");
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }

        [Fact]
        public async Task DeleteChunk_UnknownChunk_ReturnsNotFound()
        {
            var response = await _client.DeleteAsync($"/api/chunks/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>Inserts a chunk directly through the DbContext, bypassing the (unimplemented) write API, so GET/PUT/DELETE endpoints have data to work with.</summary>
        private async Task<Guid> SeedChunkAsync(
            Guid? sourceId = null,
            string? content = null,
            List<string>? topics = null,
            List<string>? tags = null,
            DateTimeOffset? createdAt = null)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ContextEngineDbContext>();

            var chunkId = Guid.NewGuid();
            var timestamp = createdAt ?? DateTimeOffset.UtcNow;
            context.Chunks.Add(new Chunk
            {
                Id = chunkId,
                SourceId = sourceId ?? Guid.NewGuid(),
                Type = ChunkType.Paragraph,
                Content = content,
                Topics = topics ?? new List<string>(),
                Tags = tags ?? new List<string>(),
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });

            await context.SaveChangesAsync();

            return chunkId;
        }
    }
}
