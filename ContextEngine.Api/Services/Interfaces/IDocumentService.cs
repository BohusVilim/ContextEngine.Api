using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Services.Interfaces
{
    /// <summary>
    /// Handles ingestion and lifecycle of source documents and the chunks parsed from them.
    /// </summary>
    public interface IDocumentService
    {
        /// <summary>
        /// Parses the document at <paramref name="documentPath"/> according to its file extension,
        /// maps the result into <see cref="Models.Chunk.Chunk"/> entities, and persists them.
        /// </summary>
        /// <param name="documentPath">Path to the document file to parse and store.</param>
        /// <param name="cancellationToken">
        /// Propagated to parsing, the AI topic/tag calls, embedding generation and the database
        /// write - lets a caller abandon this otherwise long-running, synchronous call (see
        /// <see cref="DocumentService.UploadDocumentAsync"/>) if the request is cancelled.
        /// </param>
        /// <returns>The generated source document id shared by all chunks produced from this document.</returns>
        /// <exception cref="NotSupportedException">Thrown when the file extension has no registered parser.</exception>
        /// <exception cref="UnauthorizedAccessException">
        /// Thrown when a document upload root is configured (see <see cref="Options.DocumentUploadOptions.AllowedRootPath"/>)
        /// and <paramref name="documentPath"/> resolves to somewhere outside it.
        /// </exception>
        Task<Guid> UploadDocumentAsync(string documentPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets every chunk belonging to a document, in document order.
        /// </summary>
        /// <param name="documentId">Id of the source document.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>The document's chunks, or <see langword="null"/> if no chunk has that source id.</returns>
        Task<List<ChunkDto>?> GetDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the ids of documents that have at least one chunk tagged with the given topic.
        /// </summary>
        /// <param name="topic">Topic to filter by.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>Distinct source document ids, or an empty list if none match.</returns>
        Task<List<Guid>> GetDocumentIdsByTopicAsync(string topic, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the ids of documents that have at least one chunk tagged with the given tag.
        /// </summary>
        /// <param name="tag">Tag to filter by.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>Distinct source document ids, or an empty list if none match.</returns>
        Task<List<Guid>> GetDocumentIdsByTagAsync(string tag, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the ids of documents that have at least one chunk created within the given date range.
        /// </summary>
        /// <param name="startDate">Start of the date range (inclusive).</param>
        /// <param name="endDate">End of the date range (inclusive).</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>Distinct source document ids, or an empty list if none match.</returns>
        Task<List<Guid>> GetDocumentIdsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a document and all of its chunks.
        /// </summary>
        /// <param name="documentId">Id of the document to delete.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns><see langword="true"/> if a document with that id existed and was deleted; otherwise <see langword="false"/>.</returns>
        Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    }
}
