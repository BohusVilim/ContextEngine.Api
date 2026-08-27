using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Options;
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
        private readonly DocumentUploadOptions _uploadOptions;

        public DocumentService(
            ContextEngineDbContext context,
            IDocxParser docxParser,
            IPdfParser pdfParser,
            ChunkMappings chunkMappings,
            IEmbeddingService embeddingService,
            IOptions<DocumentUploadOptions> uploadOptions)
        {
            _context = context;
            _docxParser = docxParser;
            _pdfParser = pdfParser;
            _chunkMappings = chunkMappings;
            _embeddingService = embeddingService;
            _uploadOptions = uploadOptions.Value;
        }

        /// <inheritdoc/>
        public async Task<Guid> UploadDocumentAsync(string documentPath, CancellationToken cancellationToken = default)
        {
            EnsurePathIsAllowed(documentPath);

            var extension = Path.GetExtension(documentPath);

            // Parser is selected purely by file extension; add a case here (and a new
            // IDocumentParser-style interface/implementation) when supporting a new file type.
            List<CreateChunkDto> createChunkDtos;
            switch (extension.ToLowerInvariant())
            {
                case ".docx":
                    createChunkDtos = await _docxParser.ParseAsync(documentPath, cancellationToken);
                    break;
                case ".pdf":
                    createChunkDtos = await _pdfParser.ParseAsync(documentPath, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"No parser registered for file type: {extension}");
            }

            // Embeddings are computed here, once, rather than inside each parser: unlike topics/tags
            // (which need whole-document context and an AI call - see IAiHelper), an embedding only
            // needs a single chunk's own text, so there's no benefit to duplicating this loop into
            // every IDocxParser/IPdfParser implementation. Doing it right before mapping keeps parsers
            // focused purely on structural extraction.
            await ComputeEmbeddingsAsync(createChunkDtos, cancellationToken);

            var sourceId = Guid.NewGuid();
            var chunks = _chunkMappings.MapDtosToChunks(createChunkDtos, sourceId);

            await _context.Chunks.AddRangeAsync(chunks, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return sourceId;
        }

        /// <inheritdoc/>
        public async Task<List<ChunkDto>?> GetDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            // Parent is included so MapChunksToDtos (which reads chunk.Parent?.Id) can
            // populate ChunkDto.ParentId instead of leaving it null.
            var chunks = await _context.Chunks
                .Where(c => c.SourceId == documentId)
                .Include(c => c.Parent)
                .OrderBy(c => c.Order)
                .ToListAsync(cancellationToken);

            if (chunks.Count == 0)
            {
                return null;
            }

            return _chunkMappings.MapChunksToDtos(chunks);
        }

        /// <inheritdoc/>
        public async Task<List<Guid>> GetDocumentIdsByTopicAsync(string topic, CancellationToken cancellationToken = default)
        {
            // Topics is stored as a single JSON text column (see ContextEngineDbContext), so it can't be
            // filtered at the SQL level; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.ToListAsync(cancellationToken);

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
        public async Task<List<Guid>> GetDocumentIdsByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            // Tags is stored as a single JSON text column (see ContextEngineDbContext), so it can't be
            // filtered at the SQL level; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.ToListAsync(cancellationToken);

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
        public async Task<List<Guid>> GetDocumentIdsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            var start = new DateTimeOffset(DateTime.SpecifyKind(startDate, DateTimeKind.Utc));
            var end = new DateTimeOffset(DateTime.SpecifyKind(endDate, DateTimeKind.Utc));

            // SQLite has no native DateTimeOffset type, so EF Core's Sqlite provider can't translate
            // a DateTimeOffset comparison into SQL; every chunk has to be loaded and checked in memory.
            var chunks = await _context.Chunks.ToListAsync(cancellationToken);

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
        public async Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            var chunks = await _context.Chunks.Where(c => c.SourceId == documentId).ToListAsync(cancellationToken);

            if (chunks.Count == 0)
            {
                return false;
            }

            _context.Chunks.RemoveRange(chunks);
            await _context.SaveChangesAsync(cancellationToken);

            return true;
        }

        /// <summary>
        /// Rejects <paramref name="documentPath"/> when a document upload root is configured (see
        /// <see cref="DocumentUploadOptions.AllowedRootPath"/>) and the path resolves to somewhere
        /// outside it - e.g. via a relative segment like <c>..\..\Windows\win.ini</c>. Does nothing
        /// when no root is configured, which is the default (see the option's own remarks for why).
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">The resolved path falls outside the configured root.</exception>
        private void EnsurePathIsAllowed(string documentPath)
        {
            var allowedRootPath = _uploadOptions.AllowedRootPath;
            if (string.IsNullOrWhiteSpace(allowedRootPath))
            {
                return;
            }

            // GetFullPath resolves ".." segments and relative paths against the current directory,
            // so comparing the resolved paths (rather than the raw strings) can't be tricked by
            // something like "<allowedRoot>\..\..\secret.docx".
            var fullDocumentPath = Path.GetFullPath(documentPath);
            var fullAllowedRoot = Path.GetFullPath(allowedRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var isInsideAllowedRoot =
                fullDocumentPath.Equals(fullAllowedRoot, StringComparison.OrdinalIgnoreCase) ||
                fullDocumentPath.StartsWith(fullAllowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            if (!isInsideAllowedRoot)
            {
                throw new UnauthorizedAccessException(
                    $"'{documentPath}' is outside the allowed document upload root ('{allowedRootPath}').");
            }
        }

        /// <summary>
        /// Computes each chunk's embedding concurrently rather than one at a time - a chunk's
        /// embedding only depends on its own text (see <see cref="IEmbeddingService"/>), so there's no
        /// reason a document with hundreds of chunks should pay for hundreds of sequential model
        /// calls. Degree of parallelism is capped at the processor count so a very large document
        /// doesn't fan out an unbounded number of concurrent ONNX inference calls at once.
        /// </summary>
        private async Task ComputeEmbeddingsAsync(List<CreateChunkDto> chunks, CancellationToken cancellationToken)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(chunks, parallelOptions, async (chunk, ct) =>
            {
                chunk.Embedding = await _embeddingService.CreateEmbeddingAsync(chunk.Content, ct);
            });
        }
    }
}
