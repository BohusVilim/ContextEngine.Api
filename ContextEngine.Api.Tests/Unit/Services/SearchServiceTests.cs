using Microsoft.EntityFrameworkCore;
using ContextEngine.Api.Data;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Models.Chunk;
using ContextEngine.Api.Models.Requests;
using ContextEngine.Api.Services;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Unit.Services
{
    [Collection(EmbeddingModelCollection.Name)]
    public class SearchServiceTests
    {
        private readonly IEmbeddingService _embeddingService;

        public SearchServiceTests(EmbeddingServiceFixture embeddingServiceFixture)
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
        public async Task GetSearchableOptionsAsync_NoChunks_ReturnsEmptyLists()
        {
            using var context = CreateInMemoryContext();
            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var options = await service.GetSearchableOptionsAsync();

            Assert.Empty(options.Types);
            Assert.Empty(options.Topics);
            Assert.Empty(options.Tags);
        }

        [Fact]
        public async Task GetSearchableOptionsAsync_ReturnsDistinctTypesTopicsAndTagsAcrossAllChunks()
        {
            using var context = CreateInMemoryContext();

            context.Chunks.AddRange(
                new Chunk
                {
                    Id = Guid.NewGuid(),
                    SourceId = Guid.NewGuid(),
                    Type = ChunkType.Heading,
                    Topics = new List<string> { "billing" },
                    Tags = new List<string> { "urgent" }
                },
                new Chunk
                {
                    Id = Guid.NewGuid(),
                    SourceId = Guid.NewGuid(),
                    Type = ChunkType.Paragraph,
                    Topics = new List<string> { "billing", "onboarding" },
                    Tags = new List<string> { "urgent", "draft" }
                });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var options = await service.GetSearchableOptionsAsync();

            Assert.Equal(new List<ChunkType> { ChunkType.Heading, ChunkType.Paragraph }, options.Types);
            Assert.Equal(new List<string> { "billing", "onboarding" }, options.Topics);
            Assert.Equal(new List<string> { "draft", "urgent" }, options.Tags);
        }

        [Fact]
        public async Task GetSearchableOptionsAsync_Types_OrderedByEnumDeclarationOrder()
        {
            using var context = CreateInMemoryContext();

            // Added out of declaration order (Paragraph, Table, Heading declare after one another
            // in Enums.ChunkType as Heading < Paragraph < Table), to prove the result is sorted
            // rather than returned in insertion order.
            context.Chunks.AddRange(
                new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Table },
                new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Heading },
                new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var options = await service.GetSearchableOptionsAsync();

            Assert.Equal(new List<ChunkType> { ChunkType.Heading, ChunkType.Paragraph, ChunkType.Table }, options.Types);
        }

        [Fact]
        public async Task GetSearchableOptionsAsync_TopicsAndTags_OrderedCaseInsensitively()
        {
            using var context = CreateInMemoryContext();

            context.Chunks.Add(new Chunk
            {
                Id = Guid.NewGuid(),
                SourceId = Guid.NewGuid(),
                Type = ChunkType.Paragraph,
                Topics = new List<string> { "zeta", "Alpha", "beta" },
                Tags = new List<string> { "Zulu", "alpha" }
            });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var options = await service.GetSearchableOptionsAsync();

            Assert.Equal(new List<string> { "Alpha", "beta", "zeta" }, options.Topics);
            Assert.Equal(new List<string> { "alpha", "Zulu" }, options.Tags);
        }

        [Fact]
        public async Task GetSearchableOptionsAsync_ChunksWithNoTopicsOrTags_AreIgnoredWithoutError()
        {
            using var context = CreateInMemoryContext();

            context.Chunks.Add(new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Document });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var options = await service.GetSearchableOptionsAsync();

            Assert.Equal(new List<ChunkType> { ChunkType.Document }, options.Types);
            Assert.Empty(options.Topics);
            Assert.Empty(options.Tags);
        }

        [Fact]
        public async Task SearchAsync_QueryGiven_RanksMoreRelevantChunkFirst()
        {
            using var context = CreateInMemoryContext();

            var relevantId = Guid.NewGuid();
            var unrelatedId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Chunk
                {
                    Id = relevantId,
                    SourceId = Guid.NewGuid(),
                    Type = ChunkType.Paragraph,
                    Content = "Your invoice payment is due within 14 days of receipt.",
                    Embedding = await _embeddingService.CreateEmbeddingAsync("Your invoice payment is due within 14 days of receipt.")
                },
                new Chunk
                {
                    Id = unrelatedId,
                    SourceId = Guid.NewGuid(),
                    Type = ChunkType.Paragraph,
                    Content = "The office kitchen coffee machine is out of order.",
                    Embedding = await _embeddingService.CreateEmbeddingAsync("The office kitchen coffee machine is out of order.")
                });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var response = await service.SearchAsync(new SearchRequest { Query = "invoice payment due date" });

            Assert.Equal(2, response.Chunks.Count);
            Assert.Equal(relevantId, response.Chunks[0].Id);
            Assert.Equal(unrelatedId, response.Chunks[1].Id);
        }

        [Fact]
        public async Task SearchAsync_BlankQuery_ReturnsFilteredChunksWithoutRanking()
        {
            using var context = CreateInMemoryContext();
            var chunkId = Guid.NewGuid();

            context.Chunks.Add(new Chunk { Id = chunkId, SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Content = "Some content" });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var response = await service.SearchAsync(new SearchRequest { Query = "" });

            var chunk = Assert.Single(response.Chunks);
            Assert.Equal(chunkId, chunk.Id);
        }

        [Fact]
        public async Task SearchAsync_TypeFilter_ExcludesNonMatchingTypes()
        {
            using var context = CreateInMemoryContext();
            var headingId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Chunk { Id = headingId, SourceId = Guid.NewGuid(), Type = ChunkType.Heading, Content = "Section title" },
                new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Content = "Body text" });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var response = await service.SearchAsync(new SearchRequest
            {
                Query = "",
                Types = new List<ChunkType> { ChunkType.Heading }
            });

            var chunk = Assert.Single(response.Chunks);
            Assert.Equal(headingId, chunk.Id);
        }

        [Fact]
        public async Task SearchAsync_TopicFilter_MatchesAnyRequestedTopic()
        {
            using var context = CreateInMemoryContext();
            var billingId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Chunk { Id = billingId, SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Topics = new List<string> { "billing" } },
                new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Topics = new List<string> { "onboarding" } });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var response = await service.SearchAsync(new SearchRequest
            {
                Query = "",
                Topics = new List<string> { "billing", "shipping" }
            });

            var chunk = Assert.Single(response.Chunks);
            Assert.Equal(billingId, chunk.Id);
        }

        [Fact]
        public async Task SearchAsync_TagFilter_ExcludesNonMatchingTags()
        {
            using var context = CreateInMemoryContext();
            var urgentId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Chunk { Id = urgentId, SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Tags = new List<string> { "urgent" } },
                new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Tags = new List<string> { "draft" } });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var response = await service.SearchAsync(new SearchRequest
            {
                Query = "",
                Tags = new List<string> { "urgent" }
            });

            var chunk = Assert.Single(response.Chunks);
            Assert.Equal(urgentId, chunk.Id);
        }

        [Fact]
        public async Task SearchAsync_NoChunksMatchFilters_ReturnsEmptyList()
        {
            using var context = CreateInMemoryContext();

            context.Chunks.Add(new Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Tags = new List<string> { "draft" } });
            await context.SaveChangesAsync();

            var service = new SearchService(context, new ChunkMappings(), _embeddingService);

            var response = await service.SearchAsync(new SearchRequest
            {
                Query = "",
                Tags = new List<string> { "urgent" }
            });

            Assert.Empty(response.Chunks);
        }
    }
}
