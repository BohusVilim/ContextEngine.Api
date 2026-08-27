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
        /// paragraph into a single chunk. The result is a flat list — chunks are not yet nested
        /// under their headings. Topics (document-wide) and tags (per chunk) are then filled in by
        /// <see cref="IAiHelper"/>.
        /// </summary>
        /// <param name="filePath">Path to the .pdf file to parse.</param>
        /// <returns>Chunks in document order, ready to be mapped and persisted.</returns>
        public async Task<List<CreateChunkDto>> ParseAsync(string filePath)
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

                        chunks.Add(new CreateChunkDto { Type = ChunkType.Heading, Order = order++, Content = text });
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

                        pendingParagraph = new CreateChunkDto { Type = ChunkType.Paragraph, Order = order++, Content = text };
                    }

                    previousLineY = line.Y;
                }

                if (pendingParagraph != null)
                {
                    chunks.Add(pendingParagraph);
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
        /// Finds the most frequently occurring font size across the whole document, used as the
        /// baseline "body text" size that heading detection compares against.
        /// </summary>
        private static double GetMostCommonFontSize(PdfDocument document)
        {
            var fontSizeCounts = new Dictionary<double, int>();

            foreach (var page in document.GetPages())
            {
                foreach (var letter in page.Letters)
                {
                    var roundedSize = Math.Round(letter.FontSize, 1);
                    if (!fontSizeCounts.ContainsKey(roundedSize))
                    {
                        fontSizeCounts[roundedSize] = 0;
                    }

                    fontSizeCounts[roundedSize]++;
                }
            }

            if (fontSizeCounts.Count == 0)
            {
                return 0;
            }

            var mostCommonSize = 0.0;
            var mostCommonCount = 0;
            foreach (var pair in fontSizeCounts)
            {
                if (pair.Value > mostCommonCount)
                {
                    mostCommonSize = pair.Key;
                    mostCommonCount = pair.Value;
                }
            }

            return mostCommonSize;
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
            var gapCounts = new Dictionary<double, int>();

            foreach (var pageLines in pagesLines)
            {
                for (var i = 1; i < pageLines.Count; i++)
                {
                    var gap = Math.Round(pageLines[i - 1].Y - pageLines[i].Y, 1);
                    if (gap <= 0)
                    {
                        continue;
                    }

                    if (!gapCounts.ContainsKey(gap))
                    {
                        gapCounts[gap] = 0;
                    }

                    gapCounts[gap]++;
                }
            }

            if (gapCounts.Count == 0)
            {
                return 0;
            }

            var mostCommonGap = 0.0;
            var mostCommonCount = 0;
            foreach (var pair in gapCounts)
            {
                if (pair.Value > mostCommonCount)
                {
                    mostCommonGap = pair.Key;
                    mostCommonCount = pair.Value;
                }
            }

            return mostCommonGap;
        }

        /// <summary>A reconstructed text line: its glyphs plus the line's baseline Y, used to detect paragraph breaks by vertical gap.</summary>
        private class PdfLine
        {
            public double Y { get; set; }
            public List<Letter> Letters { get; set; } = new();
        }
    }
}
