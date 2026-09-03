using Microsoft.EntityFrameworkCore;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Services;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Unit.Services
{
    [Collection(EmbeddingModelCollection.Name)]
    public class ChunkServiceTests
    {
        private readonly IEmbeddingService _embeddingService;

        public ChunkServiceTests(EmbeddingServiceFixture embeddingServiceFixture)
        {
            _embeddingService = embeddingServiceFixture.EmbeddingService;
        }

        private static ContextEngineDbContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<ContextEngineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ContextEngineDbContext(options);
        }

        [Fact]
        public async Task GetChunkByIdAsync_ChunkExists_ReturnsIt()
        {
            using var context = CreateInMemoryContext();
            var chunkId = Guid.NewGuid();

            context.Chunks.Add(new Models.Chunk.Chunk { Id = chunkId, Type = ChunkType.Paragraph, Content = "Body text" });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunk = await service.GetChunkByIdAsync(chunkId);

            Assert.NotNull(chunk);
            Assert.Equal(chunkId, chunk!.Id);
            Assert.Equal("Body text", chunk.Content);
        }

        [Fact]
        public async Task GetChunkByIdAsync_ChunkDoesNotExist_ReturnsNull()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunk = await service.GetChunkByIdAsync(Guid.NewGuid());

            Assert.Null(chunk);
        }

        [Fact]
        public async Task GetChunksByDocumentIdAsync_DocumentExists_ReturnsChunksInOrder()
        {
            using var context = CreateInMemoryContext();
            var sourceId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = sourceId, Type = ChunkType.Paragraph, Order = 1, Content = "Second" },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = sourceId, Type = ChunkType.Heading, Order = 0, Content = "First" },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Heading, Order = 0, Content = "OtherDocument" });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByDocumentIdAsync(sourceId);

            Assert.NotNull(chunks);
            Assert.Equal(2, chunks!.Count);
            Assert.Equal("First", chunks[0].Content);
            Assert.Equal("Second", chunks[1].Content);
            Assert.All(chunks, dto => Assert.Equal(sourceId, dto.SourceId));
        }

        [Fact]
        public async Task GetChunksByDocumentIdAsync_DocumentDoesNotExist_ReturnsNull()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByDocumentIdAsync(Guid.NewGuid());

            Assert.Null(chunks);
        }

        [Fact]
        public async Task GetChunksByTopicAsync_ReturnsMatchingChunks()
        {
            using var context = CreateInMemoryContext();
            var matchingId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = matchingId, Type = ChunkType.Heading, Topics = new List<string> { "billing" } },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), Type = ChunkType.Paragraph, Topics = new List<string> { "other" } });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByTopicAsync("billing");

            var chunk = Assert.Single(chunks);
            Assert.Equal(matchingId, chunk.Id);
        }

        [Fact]
        public async Task GetChunksByTopicAsync_NoMatch_ReturnsEmptyList()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByTopicAsync("billing");

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task GetChunksByTagAsync_ReturnsMatchingChunks()
        {
            using var context = CreateInMemoryContext();
            var matchingId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = matchingId, Type = ChunkType.Heading, Tags = new List<string> { "urgent" } },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), Type = ChunkType.Paragraph, Tags = new List<string> { "other" } });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByTagAsync("urgent");

            var chunk = Assert.Single(chunks);
            Assert.Equal(matchingId, chunk.Id);
        }

        [Fact]
        public async Task GetChunksByTagAsync_NoMatch_ReturnsEmptyList()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByTagAsync("urgent");

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task GetChunksByDateRangeAsync_ReturnsOnlyChunksWithinRange()
        {
            using var context = CreateInMemoryContext();
            var inRangeId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = inRangeId, Type = ChunkType.Heading, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), Type = ChunkType.Heading, CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

            var chunk = Assert.Single(chunks);
            Assert.Equal(inRangeId, chunk.Id);
        }

        [Fact]
        public async Task GetChunksByDateRangeAsync_ChunkCreatedLaterOnEndDate_IsIncluded()
        {
            using var context = CreateInMemoryContext();
            var lateOnEndDateId = Guid.NewGuid();

            context.Chunks.Add(new Models.Chunk.Chunk
            {
                Id = lateOnEndDateId,
                Type = ChunkType.Heading,
                CreatedAt = new DateTimeOffset(2026, 1, 31, 23, 0, 0, TimeSpan.Zero)
            });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByDateRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

            var chunk = Assert.Single(chunks);
            Assert.Equal(lateOnEndDateId, chunk.Id);
        }

        [Fact]
        public async Task GetChunksByDateRangeAsync_NoMatch_ReturnsEmptyList()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var chunks = await service.GetChunksByDateRangeAsync(new DateTime(1999, 1, 1), new DateTime(1999, 1, 2));

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task UpdateChunkAsync_ChunkExists_UpdatesFieldsAndReturnsIt()
        {
            using var context = CreateInMemoryContext();
            var chunkId = Guid.NewGuid();
            var originalCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            context.Chunks.Add(new Models.Chunk.Chunk
            {
                Id = chunkId,
                Type = ChunkType.Paragraph,
                Order = 0,
                Content = "Old content",
                CreatedAt = originalCreatedAt,
                UpdatedAt = originalCreatedAt
            });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var updated = await service.UpdateChunkAsync(chunkId, new ChunkDto
            {
                Content = "New content",
                Type = ChunkType.Heading,
                Order = 5,
                Topics = new List<string> { "topic-a" },
                Tags = new List<string> { "tag-a" },
                Metadata = new Dictionary<string, string> { ["key"] = "value" }
            });

            Assert.NotNull(updated);
            Assert.Equal("New content", updated!.Content);
            Assert.Equal(ChunkType.Heading, updated.Type);
            Assert.Equal(5, updated.Order);
            Assert.Equal(new List<string> { "topic-a" }, updated.Topics);
            Assert.Equal(new List<string> { "tag-a" }, updated.Tags);
            Assert.Equal("value", updated.Metadata["key"]);
            Assert.True(updated.UpdatedAt > originalCreatedAt);

            var persisted = await context.Chunks.SingleAsync(c => c.Id == chunkId);
            Assert.Equal("New content", persisted.Content);
        }

        [Fact]
        public async Task UpdateChunkAsync_ContentChanges_RecomputesEmbedding()
        {
            using var context = CreateInMemoryContext();
            var chunkId = Guid.NewGuid();
            var staleEmbedding = new float[OnnxEmbeddingService.Dimensions];

            context.Chunks.Add(new Models.Chunk.Chunk
            {
                Id = chunkId,
                Type = ChunkType.Paragraph,
                Content = "Old content",
                Embedding = staleEmbedding
            });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            await service.UpdateChunkAsync(chunkId, new ChunkDto { Content = "New content" });

            var persisted = await context.Chunks.SingleAsync(c => c.Id == chunkId);
            Assert.NotEqual(staleEmbedding, persisted.Embedding);
        }

        [Fact]
        public async Task UpdateChunkAsync_ChunkDoesNotExist_ReturnsNull()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var updated = await service.UpdateChunkAsync(Guid.NewGuid(), new ChunkDto { Content = "New content" });

            Assert.Null(updated);
        }

        [Fact]
        public async Task DeleteChunkAsync_ChunkExists_RemovesItAndReturnsTrue()
        {
            using var context = CreateInMemoryContext();
            var chunkId = Guid.NewGuid();
            var otherChunkId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = chunkId, Type = ChunkType.Heading },
                new Models.Chunk.Chunk { Id = otherChunkId, Type = ChunkType.Paragraph });
            await context.SaveChangesAsync();

            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var deleted = await service.DeleteChunkAsync(chunkId);

            Assert.True(deleted);
            var remaining = await context.Chunks.ToListAsync();
            var remainingChunk = Assert.Single(remaining);
            Assert.Equal(otherChunkId, remainingChunk.Id);
        }

        [Fact]
        public async Task DeleteChunkAsync_ChunkDoesNotExist_ReturnsFalse()
        {
            using var context = CreateInMemoryContext();
            var service = new ChunkService(context, new ChunkMappings(), _embeddingService);

            var deleted = await service.DeleteChunkAsync(Guid.NewGuid());

            Assert.False(deleted);
        }
    }
}
