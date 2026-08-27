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
        /// <returns>The generated source document id shared by all chunks produced from this document.</returns>
        /// <exception cref="NotSupportedException">Thrown when the file extension has no registered parser.</exception>
        Task<Guid> UploadDocumentAsync(string documentPath);

        /// <summary>
        /// Gets every chunk belonging to a document, in document order.
        /// </summary>
        /// <param name="documentId">Id of the source document.</param>
        /// <returns>The document's chunks, or <see langword="null"/> if no chunk has that source id.</returns>
        Task<List<ChunkDto>?> GetDocumentByIdAsync(Guid documentId);

        /// <summary>
        /// Gets the ids of documents that have at least one chunk tagged with the given topic.
        /// </summary>
        /// <param name="topic">Topic to filter by.</param>
        /// <returns>Distinct source document ids, or an empty list if none match.</returns>
        Task<List<Guid>> GetDocumentIdsByTopicAsync(string topic);

        /// <summary>
        /// Gets the ids of documents that have at least one chunk tagged with the given tag.
        /// </summary>
        /// <param name="tag">Tag to filter by.</param>
        /// <returns>Distinct source document ids, or an empty list if none match.</returns>
        Task<List<Guid>> GetDocumentIdsByTagAsync(string tag);

        /// <summary>
        /// Gets the ids of documents that have at least one chunk created within the given date range.
        /// </summary>
        /// <param name="startDate">Start of the date range (inclusive).</param>
        /// <param name="endDate">End of the date range (inclusive).</param>
        /// <returns>Distinct source document ids, or an empty list if none match.</returns>
        Task<List<Guid>> GetDocumentIdsByDateRangeAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// Deletes a document and all of its chunks.
        /// </summary>
        /// <param name="documentId">Id of the document to delete.</param>
        /// <returns><see langword="true"/> if a document with that id existed and was deleted; otherwise <see langword="false"/>.</returns>
        Task<bool> DeleteDocumentAsync(Guid documentId);
    }
}
