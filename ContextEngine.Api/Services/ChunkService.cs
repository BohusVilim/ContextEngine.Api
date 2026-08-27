using Microsoft.EntityFrameworkCore;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Services
{
    /// <inheritdoc cref="IChunkService"/>
    public class ChunkService : IChunkService
    {
        private readonly ContextEngineDbContext _context;
        private readonly ChunkMappings _chunkMappings;

        public ChunkService(ContextEngineDbContext context, ChunkMappings chunkMappings)
        {
            _context = context;
            _chunkMappings = chunkMappings;
        }

        /// <inheritdoc/>
        public async Task<ChunkDto?> GetChunkByIdAsync(Guid chunkId)
        {
            // Parent is included so ChunkMappings (which reads chunk.Parent?.Id) can
            // populate ChunkDto.ParentId instead of leaving it null.
            var chunk = await _context.Chunks
                .Include(c => c.Parent)
                .FirstOrDefaultAsync(c => c.Id == chunkId);

            if (chunk == null)
            {
                return null;
            }

            return _chunkMappings.MapChunkToDto(chunk);
        }

        /// <inheritdoc/>
        public async Task<List<ChunkDto>?> GetChunksByDocumentIdAsync(Guid documentId)
        {
            var chunks = await _context.Chunks
                .Where(c => c.SourceId == documentId)
                .Include(c => c.Parent)
                .OrderBy(c => c.Order)
                .ToListAsync();

            if (chunks.Count == 0)
            {
                return null;
            }

            return _chunkMappings.MapChunksToDtos(chunks);
        }

        /// <inheritdoc/>
        public async Task<List<ChunkDto>> GetChunksByTopicAsync(string topic)
        {
            // Topics is stored as a single JSON text column (see ContextEngineDbContext), so it can't be
            // filtered at the SQL level; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.Include(c => c.Parent).ToListAsync();

            var matches = chunks.Where(c => c.Topics.Contains(topic)).ToList();

            return _chunkMappings.MapChunksToDtos(matches);
        }

        /// <inheritdoc/>
        public async Task<List<ChunkDto>> GetChunksByTagAsync(string tag)
        {
            // Tags is stored as a single JSON text column (see ContextEngineDbContext), so it can't be
            // filtered at the SQL level; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.Include(c => c.Parent).ToListAsync();

            var matches = chunks.Where(c => c.Tags.Contains(tag)).ToList();

            return _chunkMappings.MapChunksToDtos(matches);
        }

        /// <inheritdoc/>
        public async Task<List<ChunkDto>> GetChunksByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var start = new DateTimeOffset(DateTime.SpecifyKind(startDate, DateTimeKind.Utc));
            var end = new DateTimeOffset(DateTime.SpecifyKind(endDate, DateTimeKind.Utc));

            // SQLite has no native DateTimeOffset type, so EF Core's Sqlite provider can't translate
            // a DateTimeOffset comparison into SQL; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.Include(c => c.Parent).ToListAsync();

            var matches = chunks.Where(c => c.CreatedAt >= start && c.CreatedAt <= end).ToList();

            return _chunkMappings.MapChunksToDtos(matches);
        }

        /// <inheritdoc/>
        public async Task<ChunkDto?> UpdateChunkAsync(Guid chunkId, ChunkDto chunkDto)
        {
            var chunk = await _context.Chunks
                .Include(c => c.Parent)
                .FirstOrDefaultAsync(c => c.Id == chunkId);

            if (chunk == null)
            {
                return null;
            }

            chunk.Content = chunkDto.Content;
            chunk.Type = chunkDto.Type;
            chunk.Order = chunkDto.Order;
            chunk.Topics = chunkDto.Topics ?? new List<string>();
            chunk.Tags = chunkDto.Tags ?? new List<string>();
            chunk.Metadata = chunkDto.Metadata ?? new Dictionary<string, string>();
            chunk.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return _chunkMappings.MapChunkToDto(chunk);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteChunkAsync(Guid chunkId)
        {
            var chunk = await _context.Chunks.FirstOrDefaultAsync(c => c.Id == chunkId);

            if (chunk == null)
            {
                return false;
            }

            _context.Chunks.Remove(chunk);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}
