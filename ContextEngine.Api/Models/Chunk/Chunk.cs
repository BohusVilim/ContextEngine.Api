using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Models.Chunk
{
    /// <summary>
    /// A single unit of a parsed source document (e.g. a heading, paragraph or table cell),
    /// stored as a node in a tree that mirrors the structure of the original document.
    /// </summary>
    public class Chunk
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public Chunk? Parent { get; set; }
        public List<Chunk> Children { get; set; } = new();
        public ChunkType Type { get; set; }
        public int Order { get; set; }
        public List<string> Topics { get; set; } = new();
        public string? Content { get; set; }
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Numeric vector representation of <see cref="Content"/>, produced by
        /// <see cref="Services.Interfaces.IEmbeddingService"/> at ingestion time and compared against a
        /// search query's own embedding (via <see cref="Services.Interfaces.IEmbeddingService.CosineSimilarity"/>)
        /// to rank chunks by relevance in <see cref="Services.SearchService"/>. Empty (not null) when
        /// <see cref="Content"/> was blank at the time it was computed.
        /// </summary>
        public float[] Embedding { get; set; } = Array.Empty<float>();

        public Guid SourceId { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
