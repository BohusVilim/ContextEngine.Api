using ContextEngine.Api.DTOs;
using ContextEngine.Api.Models.Chunk;

namespace ContextEngine.Api.Mappings
{
    /// <summary>
    /// Converts between <see cref="Chunk"/> entities and their DTO representations.
    /// </summary>
    public class ChunkMappings
    {
        /// <summary>
        /// Maps parser output into new <see cref="Chunk"/> entities ready to be persisted.
        /// Carries over each <see cref="CreateChunkDto.Id"/> as-is (see its doc comment for why the
        /// parser, not this method, owns id assignment) and stamps creation/update timestamps.
        /// </summary>
        /// <param name="dtos">Chunks produced by a document parser, in document order.</param>
        /// <param name="sourceId">Id of the source document these chunks belong to.</param>
        /// <returns>New <see cref="Chunk"/> entities, not yet saved to the database.</returns>
        public List<Chunk> MapDtosToChunks(List<CreateChunkDto> dtos, Guid sourceId)
        {
            var now = DateTimeOffset.UtcNow;

            var chunks = new List<Chunk>();

            foreach (var dto in dtos)
            {
                var chunk = new Chunk
                {
                    Id = dto.Id,
                    SourceId = sourceId,
                    ParentId = dto.ParentId,
                    Type = dto.Type,
                    Order = dto.Order,
                    Content = dto.Content,
                    Topics = dto.Topics ?? new List<string>(),
                    Tags = dto.Tags ?? new List<string>(),
                    Embedding = dto.Embedding ?? Array.Empty<float>(),
                    Metadata = dto.Metadata ?? new Dictionary<string, string>(),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                chunks.Add(chunk);
            }

            return chunks;
        }

        /// <summary>Maps a list of persisted chunks to their read DTOs.</summary>
        /// <param name="chunks">Chunks loaded from the database.</param>
        /// <returns>The corresponding <see cref="ChunkDto"/> list, in the same order.</returns>
        public List<ChunkDto> MapChunksToDtos(List<Chunk> chunks)
        {
            var chunkDtos = new List<ChunkDto>();

            foreach (var chunk in chunks)
            {
                var chunkDto = new ChunkDto
                {
                    Id = chunk.Id,
                    SourceId = chunk.SourceId,
                    Type = chunk.Type,
                    Order = chunk.Order,
                    Topics = chunk.Topics,
                    Tags = chunk.Tags,
                    Content = chunk.Content,
                    // Read from the Parent navigation property rather than chunk.ParentId directly,
                    // so this stays null unless the caller has loaded Parent (e.g. via Include).
                    ParentId = chunk.Parent?.Id,
                    // Embedding is deliberately NOT copied onto the DTO: it's a 128-number array that
                    // only has meaning as ranking input to SearchService, so shipping it in every API
                    // response would just be dead weight for callers.
                    Metadata = chunk.Metadata,
                    CreatedAt = chunk.CreatedAt,
                    UpdatedAt = chunk.UpdatedAt
                };

                chunkDtos.Add(chunkDto);
            }

            return chunkDtos;
        }

        /// <summary>Maps a single persisted chunk to its read DTO.</summary>
        /// <param name="chunk">Chunk loaded from the database.</param>
        /// <returns>The corresponding <see cref="ChunkDto"/>.</returns>
        public ChunkDto MapChunkToDto(Chunk chunk)
        {
            return new ChunkDto
            {
                Id = chunk.Id,
                SourceId = chunk.SourceId,
                Type = chunk.Type,
                Order = chunk.Order,
                Topics = chunk.Topics,
                Tags = chunk.Tags,
                Content = chunk.Content,
                // See note in MapChunksToDtos: relies on the Parent navigation property being loaded.
                ParentId = chunk.Parent?.Id,
                Metadata = chunk.Metadata,
                CreatedAt = chunk.CreatedAt,
                UpdatedAt = chunk.UpdatedAt
            };
        }
    }
}
