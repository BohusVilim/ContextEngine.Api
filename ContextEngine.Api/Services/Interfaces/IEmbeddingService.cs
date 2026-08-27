namespace ContextEngine.Api.Services.Interfaces
{
    /// <summary>
    /// Turns free-form text into a fixed-length numeric vector ("embedding") that captures its
    /// meaning, and measures how similar two such vectors are — the mechanism
    /// <see cref="SearchService"/> uses to rank chunks by semantic relevance to a search query.
    /// </summary>
    /// <remarks>
    /// Implemented by <see cref="OnnxEmbeddingService"/>, which runs a small, open-source, pre-trained
    /// sentence-embedding model (all-MiniLM-L6-v2 — see EmbeddingModel/NOTICE.txt for its source and
    /// license) entirely locally via Microsoft Semantic Kernel's ONNX Runtime connector
    /// (the <c>Microsoft.SemanticKernel.Connectors.Onnx</c> NuGet package). There is no cloud call, no
    /// API key and no per-request cost involved in embedding text.
    /// <para>
    /// This does NOT live on <see cref="IAiHelper"/> alongside the topic/tag generation, because it
    /// isn't an Anthropic API call at all: Anthropic's API has no embedding endpoint (Claude is a
    /// chat/completion model), so embeddings necessarily come from a separate, purpose-built model
    /// running through a separate pipeline.
    /// </para>
    /// </remarks>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Computes an embedding vector for the given text by running it through the local embedding
        /// model, so it can later be compared against other embeddings with <see cref="CosineSimilarity"/>.
        /// Called once per chunk at ingestion time (see <see cref="DocumentService"/>) and once per
        /// query at search time (see <see cref="SearchService"/>).
        /// </summary>
        /// <param name="text">Text to embed, e.g. a chunk's content or a search query. May be null or blank.</param>
        /// <returns>
        /// A task producing an embedding vector of length <see cref="OnnxEmbeddingService.Dimensions"/>,
        /// or an all-zero vector of that same length if <paramref name="text"/> is null or blank —
        /// there is nothing to embed in that case, and a zero vector naturally scores 0 similarity
        /// against everything via <see cref="CosineSimilarity"/> rather than needing special-case
        /// handling by callers.
        /// </returns>
        Task<float[]> CreateEmbeddingAsync(string? text);

        /// <summary>
        /// Measures how similar two embedding vectors are, as the cosine of the angle between them —
        /// the standard way to compare embedding vectors regardless of their magnitude.
        /// </summary>
        /// <param name="a">First embedding vector.</param>
        /// <param name="b">Second embedding vector.</param>
        /// <returns>
        /// A value from -1 (opposite direction) to 1 (identical direction); higher means more
        /// semantically similar. Also returns exactly 0 — rather than throwing — if either vector has
        /// zero magnitude, or the two have different lengths (e.g. a chunk stored before embeddings
        /// existed, or seeded directly into the database without one): in both cases the vectors
        /// aren't meaningfully comparable, so "no similarity" is a more useful result than a crash.
        /// </returns>
        double CosineSimilarity(float[] a, float[] b);
    }
}
