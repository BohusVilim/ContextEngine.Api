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
        /// The result is a flat list — chunks are not yet nested under their headings. Topics (document-wide)
        /// and tags (per chunk) are then filled in by <see cref="IAiHelper"/>.
        /// </summary>
        /// <param name="filePath">Path to the .docx file to parse.</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        public async Task<List<CreateChunkDto>> ParseAsync(string filePath)
        {
            var chunks = new List<CreateChunkDto>();
            var order = 0;

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

                    ChunkType type;
                    if (IsHeadingStyle(styleId))
                    {
                        type = ChunkType.Heading;
                    }
                    else
                    {
                        type = ChunkType.Paragraph;
                    }

                    chunks.Add(new CreateChunkDto
                    {
                        Type = type,
                        Order = order++,
                        Content = text
                    });
                }
                else if (element is Table table)
                {
                    chunks.Add(new CreateChunkDto
                    {
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
    }
}
