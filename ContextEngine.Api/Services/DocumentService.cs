using Microsoft.EntityFrameworkCore;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Parsers.Interfaces;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Services
{
    /// <inheritdoc cref="IDocumentService"/>
    public class DocumentService : IDocumentService
    {
        private readonly ContextEngineDbContext _context;
        private readonly IDocxParser _docxParser;
        private readonly IPdfParser _pdfParser;
        private readonly ChunkMappings _chunkMappings;
        private readonly IEmbeddingService _embeddingService;

        public DocumentService(
            ContextEngineDbContext context,
            IDocxParser docxParser,
            IPdfParser pdfParser,
            ChunkMappings chunkMappings,
            IEmbeddingService embeddingService)
        {
            _context = context;
            _docxParser = docxParser;
            _pdfParser = pdfParser;
            _chunkMappings = chunkMappings;
            _embeddingService = embeddingService;
        }

        /// <inheritdoc/>
        public async Task<Guid> UploadDocumentAsync(string documentPath)
        {
            var extension = Path.GetExtension(documentPath);

            // Parser is selected purely by file extension; add a case here (and a new
            // IDocumentParser-style interface/implementation) when supporting a new file type.
            List<CreateChunkDto> createChunkDtos;
            switch (extension.ToLowerInvariant())
            {
                case ".docx":
                    createChunkDtos = await _docxParser.ParseAsync(documentPath);
                    break;
                case ".pdf":
                    createChunkDtos = await _pdfParser.ParseAsync(documentPath);
                    break;
                default:
                    throw new NotSupportedException($"No parser registered for file type: {extension}");
            }

            // Embeddings are computed here, once, rather than inside each parser: unlike topics/tags
            // (which need whole-document context and an AI call - see IAiHelper), an embedding only
            // needs a single chunk's own text, so there's no benefit to duplicating this loop into
            // every IDocxParser/IPdfParser implementation. Doing it right before mapping keeps parsers
            // focused purely on structural extraction.
            foreach (var dto in createChunkDtos)
            {
                dto.Embedding = await _embeddingService.CreateEmbeddingAsync(dto.Content);
            }

            var sourceId = Guid.NewGuid();
            var chunks = _chunkMappings.MapDtosToChunks(createChunkDtos, sourceId);

            await _context.Chunks.AddRangeAsync(chunks);
            await _context.SaveChangesAsync();

            return sourceId;
        }

        /// <inheritdoc/>
        public async Task<List<ChunkDto>?> GetDocumentByIdAsync(Guid documentId)
        {
            // Parent is included so MapChunksToDtos (which reads chunk.Parent?.Id) can
            // populate ChunkDto.ParentId instead of leaving it null.
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
        public async Task<List<Guid>> GetDocumentIdsByTopicAsync(string topic)
        {
            // Topics is stored as a single JSON text column (see ContextEngineDbContext), so it can't be
            // filtered at the SQL level; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.ToListAsync();

            var documentIds = new List<Guid>();

            foreach (var chunk in chunks)
            {
                if (chunk.Topics.Contains(topic) && !documentIds.Contains(chunk.SourceId))
                {
                    documentIds.Add(chunk.SourceId);
                }
            }

            return documentIds;
        }

        /// <inheritdoc/>
        public async Task<List<Guid>> GetDocumentIdsByTagAsync(string tag)
        {
            // Tags is stored as a single JSON text column (see ContextEngineDbContext), so it can't be
            // filtered at the SQL level; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.ToListAsync();

            var documentIds = new List<Guid>();

            foreach (var chunk in chunks)
            {
                if (chunk.Tags.Contains(tag) && !documentIds.Contains(chunk.SourceId))
                {
                    documentIds.Add(chunk.SourceId);
                }
            }

            return documentIds;
        }

        /// <inheritdoc/>
        public async Task<List<Guid>> GetDocumentIdsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var start = new DateTimeOffset(DateTime.SpecifyKind(startDate, DateTimeKind.Utc));
            var end = new DateTimeOffset(DateTime.SpecifyKind(endDate, DateTimeKind.Utc));

            // SQLite has no native DateTimeOffset type, so EF Core's Sqlite provider can't translate
            // a DateTimeOffset comparison into SQL; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.ToListAsync();

            var documentIds = new List<Guid>();

            foreach (var chunk in chunks)
            {
                if (chunk.CreatedAt >= start && chunk.CreatedAt <= end && !documentIds.Contains(chunk.SourceId))
                {
                    documentIds.Add(chunk.SourceId);
                }
            }

            return documentIds;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDocumentAsync(Guid documentId)
        {
            var chunks = await _context.Chunks.Where(c => c.SourceId == documentId).ToListAsync();

            if (chunks.Count == 0)
            {
                return false;
            }

            _context.Chunks.RemoveRange(chunks);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}
