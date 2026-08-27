using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Models.Requests
{
    /// <summary>
    /// Filter criteria for a chunk search, submitted to <see cref="Controllers.SearchController.Search"/>.
    /// </summary>
    public class SearchRequest
    {
        public SearchRequest() { }

        public string Query { get; set; } = null!;
        public List<ChunkType>? Types { get; set; }
        public List<string> Topics { get; set; } = new();
        public List<string> Tags { get; set; } = new();

    }
}
