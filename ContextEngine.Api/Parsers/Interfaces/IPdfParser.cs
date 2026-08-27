using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Parsers.Interfaces
{
    /// <summary>
    /// Extracts chunks from a PDF document.
    /// </summary>
    public interface IPdfParser
    {
        /// <summary>
        /// Parses a PDF file into a flat, ordered list of chunks.
        /// </summary>
        /// <param name="filePath">Path to the .pdf file to parse.</param>
        /// <param name="cancellationToken">Propagated to the AI calls this parser makes internally (see <see cref="Services.Interfaces.IAiHelper"/>).</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        Task<List<CreateChunkDto>> ParseAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
