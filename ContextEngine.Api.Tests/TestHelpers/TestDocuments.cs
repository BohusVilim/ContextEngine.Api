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
