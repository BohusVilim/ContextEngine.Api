using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.DTOs
{
    /// <summary>
    /// Write model produced by a document parser (e.g. <see cref="Parsers.DocxParser"/>, <see cref="Parsers.PdfParser"/>)
    /// and mapped into a persisted <see cref="Models.Chunk.Chunk"/> by <see cref="Mappings.ChunkMappings"/>.
    /// </summary>
    public class CreateChunkDto
    {
        /// <summary>
        /// Id this chunk will be persisted under. Assigned by the parser (not by
        /// <see cref="Mappings.ChunkMappings"/>) so a parser can set another chunk's
        /// <see cref="ParentId"/> to it before either one has actually been saved - see
        /// <see cref="Parsers.DocxParser"/>/<see cref="Parsers.PdfParser"/> for how the heading tree
        /// is built this way.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

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
