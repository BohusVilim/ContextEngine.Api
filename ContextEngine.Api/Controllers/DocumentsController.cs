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
        /// <returns>The generated source document id.</returns>
        [HttpPost]
        public async Task<IActionResult> UploadDocument(string documentPath)
        {
            var sourceId = await _documentService.UploadDocumentAsync(documentPath);
            return Ok(sourceId);
        }

        /// <summary>Gets a document's chunks, in document order, by document id.</summary>
        /// <param name="documentId">Id of the document to retrieve.</param>
        /// <returns>The document's chunks, or 404 if no chunk has that source id.</returns>
        [HttpGet("{documentId}")]
        public async Task<IActionResult> GetDocumentById(Guid documentId)
        {
            var chunks = await _documentService.GetDocumentByIdAsync(documentId);

            if (chunks == null)
            {
                return NotFound();
            }

            return Ok(chunks);
        }

        /// <summary>Gets the ids of documents that have chunks tagged with the given topic.</summary>
        /// <param name="topic">Topic to filter by.</param>
        [HttpGet("by-topic/{topic}")]
        public async Task<IActionResult> GetDocumentsByTopic(string topic)
        {
            var documentIds = await _documentService.GetDocumentIdsByTopicAsync(topic);
            return Ok(documentIds);
        }

        /// <summary>Gets the ids of documents that have chunks tagged with the given tag.</summary>
        /// <param name="tag">Tag to filter by.</param>
        [HttpGet("by-tag/{tag}")]
        public async Task<IActionResult> GetDocumentsByTag(string tag)
        {
            var documentIds = await _documentService.GetDocumentIdsByTagAsync(tag);
            return Ok(documentIds);
        }

        /// <summary>Gets the ids of documents created within the given date range.</summary>
        /// <param name="startDate">Start of the date range (inclusive) - format: yyyy-MM-dd (ISO 8601).</param>
        /// <param name="endDate">End of the date range (inclusive) - format: yyyy-MM-dd (ISO 8601).</param>
        [HttpGet("by-date-range")]
        public async Task<IActionResult> GetDocumentsByDateRange(DateTime startDate, DateTime endDate)
        {
            var documentIds = await _documentService.GetDocumentIdsByDateRangeAsync(startDate, endDate);
            return Ok(documentIds);
        }

        /// <summary>Deletes a document and all of its chunks.</summary>
        /// <param name="documentId">Id of the document to delete.</param>
        [HttpDelete("{documentId}")]
        public async Task<IActionResult> DeleteDocument(Guid documentId)
        {
            var deleted = await _documentService.DeleteDocumentAsync(documentId);

            if (!deleted)
            {
                return NotFound();
            }

            return NoContent();
        }
    }
}
