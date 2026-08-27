using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Services.Interfaces
{
    /// <summary>
    /// Uses an LLM to enrich parsed chunks with document-level topics and chunk-level tags.
    /// </summary>
    public interface IAiHelper
    {
        /// <summary>
        /// Generates a small set of topics that summarize the whole document, from its full content.
        /// Reuses topics already present on other stored chunks wherever one of them is a good fit,
        /// and only introduces a new topic when none of the existing ones are truly relevant, so the
        /// set of topics in use across the system stays small instead of growing one-off variants of
        /// the same idea per document.
        /// </summary>
        /// <param name="chunks">Every chunk parsed from the document, in document order.</param>
        /// <returns>Document-level topics, or an empty list if the document has no content.</returns>
        Task<List<string>> CreateTopicsAsync(List<CreateChunkDto> chunks);

        /// <summary>
        /// Generates tags for each chunk, with the full document given as context so tags can
        /// reflect a chunk's role in the document, not just its own text in isolation. Reuses tags
        /// already present on other stored chunks wherever one of them is a good fit, and only
        /// introduces a new tag when none of the existing ones are truly relevant, so the set of tags
        /// in use across the system stays small instead of growing one-off variants of the same idea
        /// per document.
        /// </summary>
        /// <param name="chunks">Every chunk parsed from the document, in document order.</param>
        /// <returns>
        /// Tags per chunk, in the same order and count as <paramref name="chunks"/>
        /// (<c>result[i]</c> are the tags for <c>chunks[i]</c>).
        /// </returns>
        Task<List<List<string>>> CreateTagsAsync(List<CreateChunkDto> chunks);
    }
}
