namespace ContextEngine.Api
{
    /// <summary>
    /// Container for shared enum types used across the API.
    /// </summary>
    public class Enums
    {
        /// <summary>
        /// Structural role a <see cref="Models.Chunk.Chunk"/> plays within its source document.
        /// </summary>
        public enum ChunkType
        {
            Document,

            Section,
            Heading,

            Paragraph,
            List,
            ListItem,
            Table,
            TableRow,
            TableCell,

            Definition,
            Quote,
            Note,
            Warning,

            Footnote,
            Reference,

            Code,
            Unknown
        }
    }
}
