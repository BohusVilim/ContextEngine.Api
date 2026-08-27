using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Services.Interfaces
{
    /// <summary>
    /// Handles retrieval, update and deletion of individual chunks.
    /// </summary>
    public interface IChunkService
    {
        /// <summary>Gets a single chunk by id.</summary>
        /// <param name="chunkId">Id of the chunk to retrieve.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>The chunk, or <see langword="null"/> if no chunk has that id.</returns>
        Task<ChunkDto?> GetChunkByIdAsync(Guid chunkId, CancellationToken cancellationToken = default);

        /// <summary>Gets every chunk belonging to a document, in document order.</summary>
        /// <param name="documentId">Id of the source document.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>The document's chunks, or <see langword="null"/> if no chunk has that source id.</returns>
        Task<List<ChunkDto>?> GetChunksByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

        /// <summary>Gets chunks tagged with the given topic.</summary>
        /// <param name="topic">Topic to filter by.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>Matching chunks, or an empty list if none match.</returns>
        Task<List<ChunkDto>> GetChunksByTopicAsync(string topic, CancellationToken cancellationToken = default);

        /// <summary>Gets chunks tagged with the given tag.</summary>
        /// <param name="tag">Tag to filter by.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>Matching chunks, or an empty list if none match.</returns>
        Task<List<ChunkDto>> GetChunksByTagAsync(string tag, CancellationToken cancellationToken = default);

        /// <summary>Gets chunks created within the given date range.</summary>
        /// <param name="startDate">Start of the date range (inclusive).</param>
        /// <param name="endDate">End of the date range (inclusive).</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>Matching chunks, or an empty list if none match.</returns>
        Task<List<ChunkDto>> GetChunksByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates a chunk's content, type, order, topics, tags and metadata.
        /// Structural fields (<c>Id</c>, <c>SourceId</c>, <c>ParentId</c>) are not affected by this call.
        /// </summary>
        /// <param name="chunkId">Id of the chunk to update.</param>
        /// <param name="chunkDto">New values for the chunk's mutable fields.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns>The updated chunk, or <see langword="null"/> if no chunk has that id.</returns>
        Task<ChunkDto?> UpdateChunkAsync(Guid chunkId, ChunkDto chunkDto, CancellationToken cancellationToken = default);

        /// <summary>Deletes a chunk (and, per the cascade rule on the entity, its child chunks).</summary>
        /// <param name="chunkId">Id of the chunk to delete.</param>
        /// <param name="cancellationToken">Propagated to the underlying database query.</param>
        /// <returns><see langword="true"/> if a chunk with that id existed and was deleted; otherwise <see langword="false"/>.</returns>
        Task<bool> DeleteChunkAsync(Guid chunkId, CancellationToken cancellationToken = default);
    }
}
