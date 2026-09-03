using ContextEngine.Api.DTOs;
using ContextEngine.Api.Parsers.Interfaces;
using ContextEngine.Api.Services.Interfaces;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Parsers
{
    /// <summary>
    /// Extracts chunks from a PDF document using PdfPig.
    /// </summary>
    /// <remarks>
    /// PDF has no native semantic structure (no equivalent of Word's "Heading1" style or paragraph
    /// elements) — text is just glyphs with coordinates. Headings are detected with a font-size
    /// heuristic: a line is treated as a heading when its average font size is at least
    /// <see cref="HeadingFontSizeMultiplier"/> times the document's most common (body) font size.
    /// Paragraphs are reconstructed from wrapped lines with a line-gap heuristic: consecutive
    /// non-heading lines are merged into one chunk as long as the vertical gap between them is close
    /// to the document's typical single-line spacing; a noticeably larger gap (a blank line, extra
    /// paragraph spacing) starts a new chunk. See <see cref="GetTypicalLineGap"/>.
    /// </remarks>
    public class PdfParser : IPdfParser
    {
        /// <summary>Minimum ratio of a line's font size to the body font size for it to be classified as a heading.</summary>
        private const double HeadingFontSizeMultiplier = 1.2;

        /// <summary>Minimum ratio of a line-to-line gap to the typical line gap for it to be treated as a paragraph break rather than a wrapped line.</summary>
        private const double ParagraphBreakGapMultiplier = 1.5;

        private readonly IAiHelper _aiHelper;

        public PdfParser(IAiHelper aiHelper)
        {
            _aiHelper = aiHelper;
        }

        /// <summary>
        /// Reads every page, reconstructs text lines from individual glyphs, classifies each
        /// non-empty line as <see cref="ChunkType.Heading"/> or <see cref="ChunkType.Paragraph"/>
        /// based on font size, and merges consecutive paragraph lines that belong to the same
        /// paragraph into a single chunk. Every non-heading chunk is nested (via
        /// <see cref="CreateChunkDto.ParentId"/>) under the most recent heading at the time it was
        /// encountered, and a heading is nested under the most recent heading with a strictly larger
        /// font size — headings have no explicit outline level in a PDF the way "Heading1"/"Heading2"
        /// do in Word, so distinct heading font sizes double as levels (bigger font = higher/outer
        /// level), ranked document-wide by <see cref="GetHeadingLevelsBySize"/> before the main walk.
        /// This nesting persists across page breaks (unlike paragraph merging, see below) — a
        /// section started on one page still parents chunks at the top of the next. Topics
        /// (document-wide) and tags (per chunk) are then filled in by <see cref="IAiHelper"/>.
        /// </summary>
        /// <param name="filePath">Path to the .pdf file to parse.</param>
        /// <param name="cancellationToken">Propagated to the AI calls this parser makes internally (see <see cref="IAiHelper"/>).</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        public async Task<List<CreateChunkDto>> ParseAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var chunks = new List<CreateChunkDto>();
            var order = 0;

            using var document = PdfDocument.Open(filePath);
            var bodyFontSize = GetMostCommonFontSize(document);

            var pagesLines = new List<List<PdfLine>>();
            foreach (var page in document.GetPages())
            {
                pagesLines.Add(GroupLettersIntoLines(page));
            }

            var typicalLineGap = GetTypicalLineGap(pagesLines);
            var paragraphBreakGapThreshold = typicalLineGap * ParagraphBreakGapMultiplier;
            var headingLevelsBySize = GetHeadingLevelsBySize(pagesLines, bodyFontSize);

            // Headings currently open above the chunk being processed, outermost (largest font) first.
            // Kept at document scope, not per-page, so heading ancestry survives a page break.
            var ancestors = new Stack<(int Level, Guid Id)>();

            foreach (var pageLines in pagesLines)
            {
                // Paragraphs are not merged across a page break: a new page resets the pending
                // paragraph and the vertical-gap tracking, since Y coordinates start over per page.
                CreateChunkDto? pendingParagraph = null;
                double? previousLineY = null;

                foreach (var line in pageLines)
                {
                    var text = BuildLineText(line.Letters);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var averageFontSize = GetAverageFontSize(line.Letters);
                    var isHeading = averageFontSize >= bodyFontSize * HeadingFontSizeMultiplier;

                    if (isHeading)
                    {
                        if (pendingParagraph != null)
                        {
                            chunks.Add(pendingParagraph);
                            pendingParagraph = null;
                        }

                        var level = headingLevelsBySize.TryGetValue(Math.Round(averageFontSize, 1), out var matchedLevel)
                            ? matchedLevel
                            : 1;
                        var parentId = HeadingAncestry.BuildParentId(ancestors, level);
                        var headingId = Guid.NewGuid();

                        chunks.Add(new CreateChunkDto { Id = headingId, ParentId = parentId, Type = ChunkType.Heading, Order = order++, Content = text });
                        ancestors.Push((level, headingId));

                        previousLineY = null;
                        continue;
                    }

                    var isContinuation = false;
                    if (pendingParagraph != null && previousLineY.HasValue)
                    {
                        var gap = previousLineY.Value - line.Y;
                        if (gap <= paragraphBreakGapThreshold)
                        {
                            isContinuation = true;
                        }
                    }

                    if (isContinuation)
                    {
                        pendingParagraph!.Content += " " + text;
                    }
                    else
                    {
                        if (pendingParagraph != null)
                        {
                            chunks.Add(pendingParagraph);
                        }

                        pendingParagraph = new CreateChunkDto
                        {
                            ParentId = ancestors.Count > 0 ? ancestors.Peek().Id : null,
                            Type = ChunkType.Paragraph,
                            Order = order++,
                            Content = text
                        };
                    }

                    previousLineY = line.Y;
                }

                if (pendingParagraph != null)
                {
                    chunks.Add(pendingParagraph);
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

        /// <summary>Concatenates a line's glyphs into trimmed text.</summary>
        private static string BuildLineText(List<Letter> line)
        {
            var builder = new StringBuilder();
            foreach (var letter in line)
            {
                builder.Append(letter.Value);
            }

            return builder.ToString().Trim();
        }

        /// <summary>Computes the mean font size of the glyphs in a line.</summary>
        private static double GetAverageFontSize(List<Letter> line)
        {
            double sum = 0;
            foreach (var letter in line)
            {
                sum += letter.FontSize;
            }

            return sum / line.Count;
        }

        /// <summary>
        /// Ranks every distinct font size used by a heading line document-wide, largest first, into
        /// outline levels 1, 2, 3, ... — the PDF equivalent of Word's "Heading1"/"Heading2" styles,
        /// derived from font size since PDF glyphs carry no explicit outline level. Sizes are rounded
        /// to one decimal (matching <see cref="GetMostCommonFontSize"/>'s bucketing) so trivially
        /// different measurements of what is visually the same heading size land in one level.
        /// </summary>
        private static Dictionary<double, int> GetHeadingLevelsBySize(List<List<PdfLine>> pagesLines, double bodyFontSize)
        {
            var headingSizes = new HashSet<double>();

            foreach (var pageLines in pagesLines)
            {
                foreach (var line in pageLines)
                {
                    if (string.IsNullOrWhiteSpace(BuildLineText(line.Letters)))
                    {
                        continue;
                    }

                    var averageFontSize = GetAverageFontSize(line.Letters);
                    if (averageFontSize >= bodyFontSize * HeadingFontSizeMultiplier)
                    {
                        headingSizes.Add(Math.Round(averageFontSize, 1));
                    }
                }
            }

            return headingSizes
                .OrderByDescending(size => size)
                .Select((size, index) => (size, level: index + 1))
                .ToDictionary(x => x.size, x => x.level);
        }

        /// <summary>
        /// Finds the most frequently occurring font size across the whole document, used as the
        /// baseline "body text" size that heading detection compares against.
        /// </summary>
        private static double GetMostCommonFontSize(PdfDocument document)
        {
            var roundedFontSizes = document.GetPages()
                .SelectMany(page => page.Letters)
                .Select(letter => Math.Round(letter.FontSize, 1));

            return GetMostFrequentValue(roundedFontSizes);
        }

        /// <summary>
        /// Groups a page's glyphs into text lines. PdfPig exposes individual letters with their own
        /// coordinates rather than pre-assembled lines, so glyphs are sorted into reading order
        /// (top to bottom, left to right) and then bucketed by baseline Y-coordinate, allowing a
        /// small tolerance for glyphs that sit on the same visual line but not exactly the same Y.
        /// </summary>
        private static List<PdfLine> GroupLettersIntoLines(Page page)
        {
            var sortedLetters = page.Letters
                .OrderByDescending(letter => Math.Round(letter.StartBaseLine.Y))
                .ThenBy(letter => letter.StartBaseLine.X)
                .ToList();

            var lines = new List<PdfLine>();
            PdfLine? currentLine = null;

            foreach (var letter in sortedLetters)
            {
                var y = Math.Round(letter.StartBaseLine.Y);

                if (currentLine == null || Math.Abs(y - currentLine.Y) > 2)
                {
                    currentLine = new PdfLine { Y = y };
                    lines.Add(currentLine);
                }

                currentLine.Letters.Add(letter);
            }

            return lines;
        }

        /// <summary>
        /// Finds the most frequently occurring gap between consecutive lines' baselines, used as the
        /// typical single-line spacing that <see cref="ParagraphBreakGapMultiplier"/> compares against
        /// to tell a wrapped line (same paragraph) apart from a paragraph break (larger gap).
        /// </summary>
        private static double GetTypicalLineGap(List<List<PdfLine>> pagesLines)
        {
            var gaps = pagesLines
                .SelectMany(pageLines => pageLines.Zip(pageLines.Skip(1), (line, nextLine) => Math.Round(line.Y - nextLine.Y, 1)))
                .Where(gap => gap > 0);

            return GetMostFrequentValue(gaps);
        }

        /// <summary>
        /// Returns the value that occurs most often in <paramref name="values"/> (or 0 if it's empty).
        /// Ties are broken by whichever value occurs first in the sequence, matching what a simple
        /// left-to-right scan that only replaces the current leader on a strictly higher count would
        /// do - both <see cref="GetMostCommonFontSize"/> and <see cref="GetTypicalLineGap"/> rely on
        /// this same tie-breaking behavior.
        /// </summary>
        private static double GetMostFrequentValue(IEnumerable<double> values)
        {
            var countsByFirstAppearance = values.GroupBy(value => value).ToList();
            if (countsByFirstAppearance.Count == 0)
            {
                return 0;
            }

            return countsByFirstAppearance.OrderByDescending(group => group.Count()).First().Key;
        }

        /// <summary>A reconstructed text line: its glyphs plus the line's baseline Y, used to detect paragraph breaks by vertical gap.</summary>
        private class PdfLine
        {
            public double Y { get; set; }
            public List<Letter> Letters { get; set; } = new();
        }
    }
}
