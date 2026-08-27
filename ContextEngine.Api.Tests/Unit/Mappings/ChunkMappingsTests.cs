using ContextEngine.Api.DTOs;
using ContextEngine.Api.Mappings;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Unit.Mappings
{
    public class ChunkMappingsTests
    {
        [Fact]
        public void MapDtosToChunks_CopiesFieldsAndAssignsIdAndTimestamps()
        {
            var mappings = new ChunkMappings();
            var sourceId = Guid.NewGuid();
            var parentId = Guid.NewGuid();

            var dtos = new List<CreateChunkDto>
            {
                new CreateChunkDto
                {
                    ParentId = parentId,
                    Type = ChunkType.Heading,
                    Order = 0,
                    Content = "Introduction",
                    Topics = new List<string> { "topic-a" },
                    Tags = new List<string> { "tag-a" },
                    Metadata = new Dictionary<string, string> { ["key"] = "value" }
                }
            };

            var chunks = mappings.MapDtosToChunks(dtos, sourceId);

            var chunk = Assert.Single(chunks);
            Assert.NotEqual(Guid.Empty, chunk.Id);
            Assert.Equal(sourceId, chunk.SourceId);
            Assert.Equal(parentId, chunk.ParentId);
            Assert.Equal(ChunkType.Heading, chunk.Type);
            Assert.Equal(0, chunk.Order);
            Assert.Equal("Introduction", chunk.Content);
            Assert.Equal(new List<string> { "topic-a" }, chunk.Topics);
            Assert.Equal(new List<string> { "tag-a" }, chunk.Tags);
            Assert.Equal("value", chunk.Metadata["key"]);
            Assert.True(chunk.CreatedAt <= DateTimeOffset.UtcNow);
            Assert.Equal(chunk.CreatedAt, chunk.UpdatedAt);
        }

        [Fact]
        public void MapDtosToChunks_NullCollections_FallBackToEmpty()
        {
            var mappings = new ChunkMappings();

            var dtos = new List<CreateChunkDto>
            {
                new CreateChunkDto
                {
                    Type = ChunkType.Paragraph,
                    Order = 0,
                    Content = "Body text",
                    Topics = null!,
                    Tags = null!,
                    Metadata = null!
                }
            };

            var chunks = mappings.MapDtosToChunks(dtos, Guid.NewGuid());

            var chunk = Assert.Single(chunks);
            Assert.Empty(chunk.Topics);
            Assert.Empty(chunk.Tags);
            Assert.Empty(chunk.Metadata);
        }

        [Fact]
        public void MapDtosToChunks_EmptyInput_ReturnsEmptyList()
        {
            var mappings = new ChunkMappings();

            var chunks = mappings.MapDtosToChunks(new List<CreateChunkDto>(), Guid.NewGuid());

            Assert.Empty(chunks);
        }

        [Fact]
        public void MapChunkToDto_ParentNotLoaded_ParentIdIsNullEvenIfChunkHasParentId()
        {
            var mappings = new ChunkMappings();

            var chunk = new Models.Chunk.Chunk
            {
                Id = Guid.NewGuid(),
                ParentId = Guid.NewGuid(),
                Parent = null,
                Type = ChunkType.Paragraph,
                Content = "Body text"
            };

            var dto = mappings.MapChunkToDto(chunk);

            // Documents a known gotcha: mapping reads chunk.Parent?.Id, not chunk.ParentId,
            // so ParentId only survives the round trip when the Parent navigation property was loaded.
            Assert.Null(dto.ParentId);
        }

        [Fact]
        public void MapChunkToDto_ParentLoaded_ParentIdIsPopulated()
        {
            var mappings = new ChunkMappings();
            var parentId = Guid.NewGuid();

            var chunk = new Models.Chunk.Chunk
            {
                Id = Guid.NewGuid(),
                ParentId = parentId,
                Parent = new Models.Chunk.Chunk { Id = parentId },
                Type = ChunkType.Paragraph,
                Content = "Body text"
            };

            var dto = mappings.MapChunkToDto(chunk);

            Assert.Equal(parentId, dto.ParentId);
        }

        [Fact]
        public void MapChunksToDtos_PreservesOrderAndCount()
        {
            var mappings = new ChunkMappings();

            var chunks = new List<Models.Chunk.Chunk>
            {
                new() { Id = Guid.NewGuid(), Type = ChunkType.Heading, Content = "First" },
                new() { Id = Guid.NewGuid(), Type = ChunkType.Paragraph, Content = "Second" }
            };

            var dtos = mappings.MapChunksToDtos(chunks);

            Assert.Equal(2, dtos.Count);
            Assert.Equal("First", dtos[0].Content);
            Assert.Equal("Second", dtos[1].Content);
        }
    }
}
