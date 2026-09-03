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
        /// Walks the document body and produces one chunk per non-empty paragraph, plus a structural
        /// sub-tree per table. Paragraphs styled as "HeadingX" become <see cref="ChunkType.Heading"/>,
        /// other non-empty paragraphs become <see cref="ChunkType.Paragraph"/>. A table becomes a
        /// content-less <see cref="ChunkType.Table"/> chunk with one content-less
        /// <see cref="ChunkType.TableRow"/> child per row, each holding one <see cref="ChunkType.TableCell"/>
        /// child per non-empty cell (see <see cref="AddTable"/>) — the table's own text lives only on
        /// its cells, not duplicated onto the row/table containers, the same way a heading's own text
        /// doesn't duplicate onto the chunks nested under it.
        /// Every non-heading top-level chunk is nested (via <see cref="CreateChunkDto.ParentId"/>)
        /// under the most recent heading at the time it was encountered, and a heading is nested under
        /// the most recent heading of a strictly lower level (so "Heading2" nests under the preceding
        /// "Heading1", and a new "Heading1" closes out any open "Heading2"/etc. and becomes a
        /// top-level sibling) - see <see cref="HeadingAncestry.BuildParentId"/>. Topics (document-wide) and tags
        /// (per chunk) are then filled in by <see cref="IAiHelper"/>.
        /// </summary>
        /// <param name="filePath">Path to the .docx file to parse.</param>
        /// <param name="cancellationToken">Propagated to the AI calls this parser makes internally (see <see cref="IAiHelper"/>).</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        public async Task<List<CreateChunkDto>> ParseAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var chunks = new List<CreateChunkDto>();
            var order = 0;

            // Headings currently open above the chunk being processed, outermost first - e.g. while
            // inside "1.1", this holds ["1" (level 1), "1.1" (level 2)]. See HeadingAncestry.BuildParentId.
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
                        var parentId = HeadingAncestry.BuildParentId(ancestors, level);
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
                    Guid? tableParentId = ancestors.Count > 0 ? ancestors.Peek().Id : null;
                    AddTable(chunks, table, tableParentId, ref order);
                }
            }

            // Topics apply to the whole document, so the same list is copied onto every chunk.
            var topicsAndTags = await _aiHelper.CreateTopicsAndTagsAsync(chunks, cancellationToken);
            foreach (var chunk in chunks)
            {
                chunk.Topics = topicsAndTags.Topics;
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                chunks[i].Tags = topicsAndTags.Tags[i];
            }

            return chunks;
        }

        /// <summary>
        /// Appends a table's structural sub-tree to <paramref name="chunks"/>: a content-less
        /// <see cref="ChunkType.Table"/> chunk, one content-less <see cref="ChunkType.TableRow"/>
        /// child per row (kept even if every cell in it is blank, since a row is structure, not
        /// content), and one <see cref="ChunkType.TableCell"/> child per non-empty cell in that row
        /// (blank cells are skipped, same as a blank paragraph). A cell's left-to-right position
        /// within its row - and a row's position within the table - is recoverable from
        /// <see cref="CreateChunkDto.Order"/> alone, since both are walked and numbered in document
        /// order; no separate column-index field is needed.
        /// </summary>
        /// <param name="chunks">List every produced chunk is appended to.</param>
        /// <param name="table">The table element being parsed.</param>
        /// <param name="tableParentId">Id of the heading the table itself nests under, if any.</param>
        /// <param name="order">Running document-order counter, advanced by one per chunk added.</param>
        private static void AddTable(List<CreateChunkDto> chunks, Table table, Guid? tableParentId, ref int order)
        {
            var tableId = Guid.NewGuid();
            chunks.Add(new CreateChunkDto { Id = tableId, ParentId = tableParentId, Type = ChunkType.Table, Order = order++ });

            foreach (var row in table.Elements<TableRow>())
            {
                var rowId = Guid.NewGuid();
                chunks.Add(new CreateChunkDto { Id = rowId, ParentId = tableId, Type = ChunkType.TableRow, Order = order++ });

                foreach (var cell in row.Elements<TableCell>())
                {
                    var cellText = cell.InnerText;
                    if (string.IsNullOrWhiteSpace(cellText))
                    {
                        continue;
                    }

                    chunks.Add(new CreateChunkDto { ParentId = rowId, Type = ChunkType.TableCell, Order = order++, Content = cellText });
                }
            }
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
    }
}
