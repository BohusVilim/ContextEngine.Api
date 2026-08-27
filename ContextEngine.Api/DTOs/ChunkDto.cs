using Microsoft.Extensions.Diagnostics.HealthChecks;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.DTOs
{
    /// <summary>
    /// Read model for a <see cref="Models.Chunk.Chunk"/>, returned to API clients.
    /// </summary>
    public class ChunkDto
    {
        public Guid Id { get; set; }
        public Guid SourceId { get; set; }
        public Guid? ParentId { get; set; }
        public ChunkType Type { get; set; }
        public int Order { get; set; }
        public string? Content { get; set; }
        public List<string> Topics { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
