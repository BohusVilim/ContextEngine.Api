using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Controllers
{
    /// <summary>
    /// Manages source documents: upload/parsing, retrieval and deletion.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/documents")]
    public class DocumentsController : ControllerBase
    {
        private readonly ILogger<DocumentsController> _logger;
        private readonly IDocumentService _documentService;
        public DocumentsController(ILogger<DocumentsController> logger, IDocumentService documentService)
        {
            _logger = logger;
            _documentService = documentService;
        }

        /// <summary>Parses the document at the given path and stores its chunks.</summary>
        /// <param name="documentPath">Path to the document file to upload.</param>
        /// <param name="cancellationToken">Cancels parsing, AI enrichment, embedding and persistence if the caller disconnects.</param>
        /// <returns>The generated source document id.</returns>
        [HttpPost]
        [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadDocument(string documentPath, CancellationToken cancellationToken)
        {
            var sourceId = await _documentService.UploadDocumentAsync(documentPath, cancellationToken);
            return Ok(sourceId);
        }

        /// <summary>Gets a document's chunks, in document order, by document id.</summary>
        /// <param name="documentId">Id of the document to retrieve.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        /// <returns>The document's chunks, or 404 if no chunk has that source id.</returns>
        [HttpGet("{documentId}")]
        [ProducesResponseType<List<ChunkDto>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDocumentById(Guid documentId, CancellationToken cancellationToken)
        {
            var chunks = await _documentService.GetDocumentByIdAsync(documentId, cancellationToken);

            if (chunks == null)
            {
                return NotFound();
            }

            return Ok(chunks);
        }

        /// <summary>Gets the ids of documents that have chunks tagged with the given topic.</summary>
        /// <param name="topic">Topic to filter by.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-topic/{topic}")]
        [ProducesResponseType<List<Guid>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDocumentsByTopic(string topic, CancellationToken cancellationToken)
        {
            var documentIds = await _documentService.GetDocumentIdsByTopicAsync(topic, cancellationToken);
            return Ok(documentIds);
        }

        /// <summary>Gets the ids of documents that have chunks tagged with the given tag.</summary>
        /// <param name="tag">Tag to filter by.</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-tag/{tag}")]
        [ProducesResponseType<List<Guid>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDocumentsByTag(string tag, CancellationToken cancellationToken)
        {
            var documentIds = await _documentService.GetDocumentIdsByTagAsync(tag, cancellationToken);
            return Ok(documentIds);
        }

        /// <summary>Gets the ids of documents created within the given date range.</summary>
        /// <param name="startDate">Start of the date range (inclusive) - format: yyyy-MM-dd (ISO 8601).</param>
        /// <param name="endDate">End of the date range (inclusive) - format: yyyy-MM-dd (ISO 8601).</param>
        /// <param name="cancellationToken">Cancels the lookup if the caller disconnects.</param>
        [HttpGet("by-date-range")]
        [ProducesResponseType<List<Guid>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDocumentsByDateRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            var documentIds = await _documentService.GetDocumentIdsByDateRangeAsync(startDate, endDate, cancellationToken);
            return Ok(documentIds);
        }

        /// <summary>Deletes a document and all of its chunks.</summary>
        /// <param name="documentId">Id of the document to delete.</param>
        /// <param name="cancellationToken">Cancels the deletion if the caller disconnects.</param>
        [HttpDelete("{documentId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteDocument(Guid documentId, CancellationToken cancellationToken)
        {
            var deleted = await _documentService.DeleteDocumentAsync(documentId, cancellationToken);

            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }
    }
}
