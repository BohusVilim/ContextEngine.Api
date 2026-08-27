using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Models.Responses
{
    /// <summary>
    /// The set of filter values currently available for search (used to populate search UI filters),
    /// returned by <see cref="Controllers.SearchController.GetSearchableOptions"/>.
    /// </summary>
    public class SearchableOptionsResponse
    {
        public List<ChunkType> Types { get; set; } = new();
        public List<string> Topics { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }
}
