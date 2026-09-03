using ContextEngine.Api.DTOs;

namespace ContextEngine.Api.Services.Interfaces
{
    /// <summary>
    /// Uses an LLM to enrich parsed chunks with document-level topics and chunk-level tags.
    /// </summary>
    public interface IAiHelper
    {
        /// <summary>
        /// Generates a small set of document-level topics and, in the same call, a small set of tags
        /// for each chunk - one combined call rather than two, so the document's text is only sent to
        /// the model once instead of once per concern. Reuses topic/tag values already present on
        /// other stored chunks wherever one of them is a good fit, and only introduces a new one when
        /// none of the existing ones are truly relevant, so the sets of topics/tags in use across the
        /// system stay small instead of growing one-off variants of the same idea per document.
        /// </summary>
        /// <param name="chunks">Every chunk parsed from the document, in document order.</param>
        /// <param name="cancellationToken">Propagated to the underlying Anthropic API call.</param>
        /// <returns>
        /// Document-level topics (empty if the document has no content) and per-chunk tags, in the
        /// same order and count as <paramref name="chunks"/> (<c>Tags[i]</c> are the tags for
        /// <c>chunks[i]</c>).
        /// </returns>
        Task<TopicsAndTags> CreateTopicsAndTagsAsync(List<CreateChunkDto> chunks, CancellationToken cancellationToken = default);
    }

    /// <summary>Result of <see cref="IAiHelper.CreateTopicsAndTagsAsync"/>.</summary>
    public class TopicsAndTags
    {
        /// <summary>Document-level topics.</summary>
        public List<string> Topics { get; set; } = new();

        /// <summary>Per-chunk tags, in the same order and count as the chunks that were passed in.</summary>
        public List<List<string>> Tags { get; set; } = new();
    }
}
