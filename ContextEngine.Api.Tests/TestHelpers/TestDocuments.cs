using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace ContextEngine.Api.Tests.TestHelpers
{
    /// <summary>
    /// Generates minimal .docx/.pdf files on disk for parser tests, so tests don't depend on
    /// checked-in binary fixtures. Callers are responsible for deleting the returned path.
    /// </summary>
    public static class TestDocuments
    {
        /// <summary>
        /// Creates a .docx with a heading, two body paragraphs, a second heading, another
        /// paragraph and a two-cell table — enough to exercise every branch of <see cref="Api.Parsers.DocxParser"/>.
        /// </summary>
        /// <returns>Path to the generated file, in a new temp directory.</returns>
        public static string CreateDocxWithHeadingsAndTable()
        {
            var path = NewTempFilePath(".docx");

            using var wordDocument = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(
                    new StyleName { Val = "heading 1" },
                    new BasedOn { Val = "Normal" }
                )
                { Type = StyleValues.Paragraph, StyleId = "Heading1" }
            );
            stylesPart.Styles.Save();

            AppendParagraph(body, "Introduction", "Heading1");
            AppendParagraph(body, "First body paragraph.");
            AppendParagraph(body, "Second body paragraph.");
            AppendParagraph(body, "Details", "Heading1");
            AppendParagraph(body, "Paragraph under the second heading.");

            var table = new Table();
            var row = new TableRow();
            row.Append(new TableCell(new Paragraph(new Run(new Text("Cell A")))));
            row.Append(new TableCell(new Paragraph(new Run(new Text("Cell B")))));
            table.Append(row);
            body.AppendChild(table);

            mainPart.Document.Save();

            return path;
        }

        /// <summary>
        /// Creates a .docx with two "Heading1"s, each containing two "Heading2"s, each followed by a
        /// body paragraph — enough to exercise multi-level nesting (Heading2 under Heading1, a second
        /// Heading2 as a sibling rather than a child, a second Heading1 closing out everything above
        /// it) in <see cref="Api.Parsers.DocxParser"/>.
        /// </summary>
        /// <returns>Path to the generated file, in a new temp directory.</returns>
        public static string CreateDocxWithMultiLevelHeadings()
        {
            var path = NewTempFilePath(".docx");

            using var wordDocument = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(
                    new StyleName { Val = "heading 1" },
                    new BasedOn { Val = "Normal" }
                )
                { Type = StyleValues.Paragraph, StyleId = "Heading1" },
                new Style(
                    new StyleName { Val = "heading 2" },
                    new BasedOn { Val = "Normal" }
                )
                { Type = StyleValues.Paragraph, StyleId = "Heading2" }
            );
            stylesPart.Styles.Save();

            AppendParagraph(body, "Chapter 1", "Heading1");
            AppendParagraph(body, "Chapter intro.");
            AppendParagraph(body, "Section 1.1", "Heading2");
            AppendParagraph(body, "Section content.");
            AppendParagraph(body, "Section 1.2", "Heading2");
            AppendParagraph(body, "More content.");
            AppendParagraph(body, "Chapter 2", "Heading1");
            AppendParagraph(body, "Chapter 2 intro.");

            mainPart.Document.Save();

            return path;
        }

        /// <summary>Creates a .docx with a single one-row, two-cell table whose second cell is blank.</summary>
        public static string CreateDocxWithTableContainingBlankCell()
        {
            var path = NewTempFilePath(".docx");

            using var wordDocument = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            var table = new Table();
            var row = new TableRow();
            row.Append(new TableCell(new Paragraph(new Run(new Text("Only content")))));
            row.Append(new TableCell(new Paragraph(new Run(new Text("   ")))));
            table.Append(row);
            body.AppendChild(table);

            mainPart.Document.Save();

            return path;
        }

        /// <summary>Creates a .docx containing only whitespace-only paragraphs (no extractable content).</summary>
        public static string CreateDocxWithOnlyEmptyParagraphs()
        {
            var path = NewTempFilePath(".docx");

            using var wordDocument = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            AppendParagraph(body, "   ");
            AppendParagraph(body, string.Empty);

            mainPart.Document.Save();

            return path;
        }

        /// <summary>
        /// Creates a PDF with a large/bold heading, a body paragraph wrapped over three lines at the
        /// document's typical (smallest, most common) line spacing, and a second, single-line
        /// paragraph separated from the first by a distinctly larger gap. Exercises both the
        /// font-size heading heuristic and the line-gap paragraph-merging heuristic in
        /// <see cref="Api.Parsers.PdfParser"/>.
        /// </summary>
        /// <returns>Path to the generated file, in a new temp directory.</returns>
        public static string CreatePdfWithHeadingAndParagraphs()
        {
            var path = NewTempFilePath(".pdf");

            using var pdfDocument = new PdfDocument();
            var page = pdfDocument.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var headingFont = new XFont("Arial", 20, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", 11, XFontStyle.Regular);

            double y = 40;
            gfx.DrawString("PDF Test Heading", headingFont, XBrushes.Black, new XPoint(40, y));
            y += 40;
            gfx.DrawString("First paragraph line one,", bodyFont, XBrushes.Black, new XPoint(40, y));
            y += 16;
            gfx.DrawString("line two,", bodyFont, XBrushes.Black, new XPoint(40, y));
            y += 16;
            gfx.DrawString("and line three wrapped.", bodyFont, XBrushes.Black, new XPoint(40, y));
            y += 32;
            gfx.DrawString("Second paragraph.", bodyFont, XBrushes.Black, new XPoint(40, y));

            pdfDocument.Save(path);

            return path;
        }

        /// <summary>
        /// Creates a PDF with two large (24pt) headings, each followed by a smaller (18pt) heading
        /// and a body (11pt) paragraph — enough to exercise <see cref="Api.Parsers.PdfParser"/>'s
        /// font-size-derived heading levels (24pt as level 1, 18pt as level 2) and the resulting
        /// nesting, the PDF equivalent of <see cref="CreateDocxWithMultiLevelHeadings"/>. Every
        /// paragraph is bounded by headings on both sides, so line-gap continuation logic never
        /// comes into play here — only the heading levels matter for this fixture.
        /// </summary>
        /// <returns>Path to the generated file, in a new temp directory.</returns>
        public static string CreatePdfWithMultiLevelHeadings()
        {
            var path = NewTempFilePath(".pdf");

            using var pdfDocument = new PdfDocument();
            var page = pdfDocument.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var level1Font = new XFont("Arial", 24, XFontStyle.Bold);
            var level2Font = new XFont("Arial", 18, XFontStyle.Bold);
            var bodyFont = new XFont("Arial", 11, XFontStyle.Regular);

            double y = 40;
            void DrawLine(string text, XFont font)
            {
                gfx.DrawString(text, font, XBrushes.Black, new XPoint(40, y));
                y += font.Size + 12;
            }

            DrawLine("Chapter 1", level1Font);
            DrawLine("Chapter intro.", bodyFont);
            DrawLine("Section 1.1", level2Font);
            DrawLine("Section content.", bodyFont);
            DrawLine("Section 1.2", level2Font);
            DrawLine("More content.", bodyFont);
            DrawLine("Chapter 2", level1Font);
            DrawLine("Chapter 2 intro.", bodyFont);

            pdfDocument.Save(path);

            return path;
        }

        /// <summary>Creates a PDF with a single blank page and no text content.</summary>
        public static string CreateBlankPdf()
        {
            var path = NewTempFilePath(".pdf");

            using var pdfDocument = new PdfDocument();
            pdfDocument.AddPage();
            pdfDocument.Save(path);

            return path;
        }

        private static void AppendParagraph(Body body, string text, string? styleId = null)
        {
            var paragraph = new Paragraph();
            if (styleId != null)
            {
                paragraph.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = styleId });
            }

            paragraph.AppendChild(new Run(new Text(text)));
            body.AppendChild(paragraph);
        }

        private static string NewTempFilePath(string extension)
        {
            var directory = Path.Combine(Path.GetTempPath(), "ContextEngineApiTests_" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "test" + extension);
        }
    }
}
