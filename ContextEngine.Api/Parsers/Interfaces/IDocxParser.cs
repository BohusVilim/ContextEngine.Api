using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Parsers.Interfaces
{
    /// <summary>
    /// Extracts chunks from a Word (.docx) document.
    /// </summary>
    public interface IDocxParser
    {
        /// <summary>
        /// Parses a .docx file into a flat, ordered list of chunks.
        /// </summary>
        /// <param name="filePath">Path to the .docx file to parse.</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        Task<List<CreateChunkDto>> ParseAsync(string filePath);
    }
}
