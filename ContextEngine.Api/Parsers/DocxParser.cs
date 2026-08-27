using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Parsers.Interfaces;
using ContextEngine.Api.Services.Interfaces;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Parsers
{
    /// <summary>
    /// Extracts chunks from a Word (.docx) document using the Open XML SDK.
    /// </summary>
    public class DocxParser : IDocxParser
    {
        private readonly IAiHelper _aiHelper;

        public DocxParser(IAiHelper aiHelper)
        {
            _aiHelper = aiHelper;
        }

        /// <summary>
        /// Walks the document body and produces one chunk per non-empty paragraph or table.
        /// Paragraphs styled as "HeadingX" become <see cref="ChunkType.Heading"/>, other
        /// non-empty paragraphs become <see cref="ChunkType.Paragraph"/>, and tables become
        /// <see cref="ChunkType.Table"/> (with their full text as content, not yet split into rows/cells).
        /// Every non-heading chunk is nested (via <see cref="CreateChunkDto.ParentId"/>) under the
        /// most recent heading at the time it was encountered, and a heading is nested under the most
        /// recent heading of a strictly lower level (so "Heading2" nests under the preceding
        /// "Heading1", and a new "Heading1" closes out any open "Heading2"/etc. and becomes a
        /// top-level sibling) - see <see cref="BuildAncestry"/>. Topics (document-wide) and tags
        /// (per chunk) are then filled in by <see cref="IAiHelper"/>.
        /// </summary>
        /// <param name="filePath">Path to the .docx file to parse.</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        public async Task<List<CreateChunkDto>> ParseAsync(string filePath)
        {
            var chunks = new List<CreateChunkDto>();
            var order = 0;

            // Headings currently open above the chunk being processed, outermost first - e.g. while
            // inside "1.1", this holds ["1" (level 1), "1.1" (level 2)]. See BuildAncestry.
            var ancestors = new Stack<(int Level, Guid Id)>();

            using var document = WordprocessingDocument.Open(filePath, false);
            var body = document.MainDocumentPart?.Document?.Body;

            if (body == null)
            {
                return chunks;
            }

            foreach (var element in body.Elements())
            {
                if (element is Paragraph paragraph)
                {
                    var text = paragraph.InnerText;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

                    if (IsHeadingStyle(styleId))
                    {
                        var level = GetHeadingLevel(styleId);
                        var parentId = BuildAncestry(ancestors, level);
                        var headingId = Guid.NewGuid();

                        chunks.Add(new CreateChunkDto
                        {
                            Id = headingId,
                            ParentId = parentId,
                            Type = ChunkType.Heading,
                            Order = order++,
                            Content = text
                        });

                        ancestors.Push((level, headingId));
                    }
                    else
                    {
                        chunks.Add(new CreateChunkDto
                        {
                            ParentId = ancestors.Count > 0 ? ancestors.Peek().Id : null,
                            Type = ChunkType.Paragraph,
                            Order = order++,
                            Content = text
                        });
                    }
                }
                else if (element is Table table)
                {
                    chunks.Add(new CreateChunkDto
                    {
                        ParentId = ancestors.Count > 0 ? ancestors.Peek().Id : null,
                        Type = ChunkType.Table,
                        Order = order++,
                        Content = table.InnerText
                    });
                }
            }

            // Topics apply to the whole document, so the same list is copied onto every chunk.
            var topics = await _aiHelper.CreateTopicsAsync(chunks);
            foreach (var chunk in chunks)
            {
                chunk.Topics = topics;
            }

            var tags = await _aiHelper.CreateTagsAsync(chunks);
            for (var i = 0; i < chunks.Count; i++)
            {
                chunks[i].Tags = tags[i];
            }

            return chunks;
        }

        /// <summary>Determines whether a paragraph style id represents a Word heading style (e.g. "Heading1").</summary>
        private static bool IsHeadingStyle(string? styleId)
        {
            return !string.IsNullOrEmpty(styleId) && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the outline level from a heading style id (e.g. "Heading2" -> 2). Defaults to 1 for a
        /// heading style with no trailing number, so an unusual/custom heading style still nests
        /// sensibly as a top-level heading rather than being rejected.
        /// </summary>
        private static int GetHeadingLevel(string? styleId)
        {
            var digits = new string((styleId ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length > 0 && int.TryParse(digits, out var level) ? level : 1;
        }

        /// <summary>
        /// Pops any open heading whose level is not strictly less than <paramref name="level"/> - e.g.
        /// hitting a new "Heading2" closes out the previous "Heading2" (same level: it's a sibling, not
        /// a child) but leaves an enclosing "Heading1" open (lower level: still an ancestor) - then
        /// returns what remains on top as the new heading's parent.
        /// </summary>
        /// <param name="ancestors">Open headings, outermost first; mutated in place.</param>
        /// <param name="level">Outline level of the heading about to be added.</param>
        /// <returns>Id of the heading <paramref name="level"/> should nest under, or null if it belongs at the top.</returns>
        private static Guid? BuildAncestry(Stack<(int Level, Guid Id)> ancestors, int level)
        {
            while (ancestors.Count > 0 && ancestors.Peek().Level >= level)
            {
                ancestors.Pop();
            }

            return ancestors.Count > 0 ? ancestors.Peek().Id : null;
        }
    }
}
