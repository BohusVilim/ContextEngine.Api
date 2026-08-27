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
        [HttpGet("{chunkId}")]
        public async Task<IActionResult> GetChunkById(Guid chunkId)
        {
            var chunk = await _chunkService.GetChunkByIdAsync(chunkId);

            if (chunk == null)
            {
                return NotFound();
            }

            return Ok(chunk);
        }

        /// <summary>Gets all chunks belonging to a document, in document order.</summary>
        /// <param name="documentId">Id of the source document.</param>
        [HttpGet("by-document/{documentId}")]
        public async Task<IActionResult> GetChunksByDocumentId(Guid documentId)
        {
            var chunks = await _chunkService.GetChunksByDocumentIdAsync(documentId);

            if (chunks == null)
            {
                return NotFound();
            }

            return Ok(chunks);
        }

        /// <summary>Gets chunks tagged with the given topic.</summary>
        /// <param name="topic">Topic to filter by.</param>
        [HttpGet("by-topic/{topic}")]
        public async Task<IActionResult> GetChunksByTopic(string topic)
        {
            var chunks = await _chunkService.GetChunksByTopicAsync(topic);
            return Ok(chunks);
        }

        /// <summary>Gets chunks tagged with the given tag.</summary>
        /// <param name="tag">Tag to filter by.</param>
        [HttpGet("by-tag/{tag}")]
        public async Task<IActionResult> GetChunksByTag(string tag)
        {
            var chunks = await _chunkService.GetChunksByTagAsync(tag);
            return Ok(chunks);
        }

        /// <summary>Gets chunks created within the given date range.</summary>
        /// <param name="startDate">Start of the date range (inclusive).</param>
        /// <param name="endDate">End of the date range (inclusive).</param>
        [HttpGet("by-date-range")]
        public async Task<IActionResult> GetChunksByDateRange(DateTime startDate, DateTime endDate)
        {
            var chunks = await _chunkService.GetChunksByDateRangeAsync(startDate, endDate);
            return Ok(chunks);
        }

        /// <summary>Updates an existing chunk.</summary>
        /// <param name="chunkId">Id of the chunk to update.</param>
        /// <param name="chunkDto">New values for the chunk.</param>
        [HttpPut("{chunkId}")]
        public async Task<IActionResult> UpdateChunk(Guid chunkId, ChunkDto chunkDto)
        {
            var chunk = await _chunkService.UpdateChunkAsync(chunkId, chunkDto);

            if (chunk == null)
            {
                return NotFound();
            }

            return Ok(chunk);
        }

        /// <summary>Deletes a chunk (and, per the cascade rule on the entity, its child chunks).</summary>
        /// <param name="chunkId">Id of the chunk to delete.</param>
        [HttpDelete("{chunkId}")]
        public async Task<IActionResult> DeleteChunk(Guid chunkId)
        {
            var deleted = await _chunkService.DeleteChunkAsync(chunkId);

            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }

    }
}
