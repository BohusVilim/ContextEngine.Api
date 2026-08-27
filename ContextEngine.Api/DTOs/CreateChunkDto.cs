using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.DTOs
{
    /// <summary>
    /// Write model produced by a document parser (e.g. <see cref="Parsers.DocxParser"/>, <see cref="Parsers.PdfParser"/>)
    /// and mapped into a persisted <see cref="Models.Chunk.Chunk"/> by <see cref="Mappings.ChunkMappings"/>.
    /// </summary>
    public class CreateChunkDto
    {
        public Guid SourceId { get; set; }
        public Guid? ParentId { get; set; }
        public ChunkType Type { get; set; }
        public int Order { get; set; }
        public string? Content { get; set; }
        public List<string> Topics { get; set; } = new();
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Search embedding for <see cref="Content"/>. Left empty by parsers; filled in by
        /// <see cref="Services.DocumentService"/> right before persistence (see its remarks for why
        /// it lives there rather than in each parser).
        /// </summary>
        public float[] Embedding { get; set; } = Array.Empty<float>();

        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
