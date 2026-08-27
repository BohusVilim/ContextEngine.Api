using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Controllers
{
    /// <summary>
    /// Manages individual chunks: retrieval, filtering, update and deletion.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/chunks")]
    public class ChunksController : ControllerBase
    {
        private readonly ILogger<ChunksController> _logger;
        private readonly IChunkService _chunkService;
        public ChunksController(ILogger<ChunksController> logger, IChunkService chunkService)
        {
            _logger = logger;
            _chunkService = chunkService;
        }

        /// <summary>Gets a single chunk by id.</summary>
        /// <param name="chunkId">Id of the chunk to retrieve.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("{chunkId}")]
        [ProducesResponseType<ChunkDto>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetChunkById(Guid chunkId, CancellationToken cancellationToken)
        {
            var chunk = await _chunkService.GetChunkByIdAsync(chunkId, cancellationToken);

            if (chunk == null)
            {
                return NotFound();
            }

            return Ok(chunk);
        }

        /// <summary>Gets all chunks belonging to a document, in document order.</summary>
        /// <param name="documentId">Id of the source document.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-document/{documentId}")]
        [ProducesResponseType<List<ChunkDto>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetChunksByDocumentId(Guid documentId, CancellationToken cancellationToken)
        {
            var chunks = await _chunkService.GetChunksByDocumentIdAsync(documentId, cancellationToken);

            if (chunks == null)
            {
                return NotFound();
            }

            return Ok(chunks);
        }

        /// <summary>Gets chunks tagged with the given topic.</summary>
        /// <param name="topic">Topic to filter by.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-topic/{topic}")]
        [ProducesResponseType<List<ChunkDto>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChunksByTopic(string topic, CancellationToken cancellationToken)
        {
            var chunks = await _chunkService.GetChunksByTopicAsync(topic, cancellationToken);
            return Ok(chunks);
        }

        /// <summary>Gets chunks tagged with the given tag.</summary>
        /// <param name="tag">Tag to filter by.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-tag/{tag}")]
        [ProducesResponseType<List<ChunkDto>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChunksByTag(string tag, CancellationToken cancellationToken)
        {
            var chunks = await _chunkService.GetChunksByTagAsync(tag, cancellationToken);
            return Ok(chunks);
        }

        /// <summary>Gets chunks created within the given date range.</summary>
        /// <param name="startDate">Start of the date range (inclusive).</param>
        /// <param name="endDate">End of the date range (inclusive).</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-date-range")]
        [ProducesResponseType<List<ChunkDto>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetChunksByDateRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            var chunks = await _chunkService.GetChunksByDateRangeAsync(startDate, endDate, cancellationToken);
            return Ok(chunks);
        }

        /// <summary>Updates an existing chunk.</summary>
        /// <param name="chunkId">Id of the chunk to update.</param>
        /// <param name="chunkDto">New values for the chunk.</param>
        /// <param name="cancellationToken">Cancels the update if the caller disconnects.</param>
        [HttpPut("{chunkId}")]
        [ProducesResponseType<ChunkDto>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateChunk(Guid chunkId, ChunkDto chunkDto, CancellationToken cancellationToken)
        {
            var chunk = await _chunkService.UpdateChunkAsync(chunkId, chunkDto, cancellationToken);

            if (chunk == null)
            {
                return NotFound();
            }

            return Ok(chunk);
        }

        /// <summary>Deletes a chunk (and, per the cascade rule on the entity, its child chunks).</summary>
        /// <param name="chunkId">Id of the chunk to delete.</param>
        /// <param name="cancellationToken">Cancels the deletion if the caller disconnects.</param>
        [HttpDelete("{chunkId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteChunk(Guid chunkId, CancellationToken cancellationToken)
        {
            var deleted = await _chunkService.DeleteChunkAsync(chunkId, cancellationToken);

            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }

    }
}
