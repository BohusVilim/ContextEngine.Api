using Moq;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Parsers;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Unit.Parsers
{
    public class PdfParserTests : IDisposable
    {
        private readonly List<string> _generatedFiles = new();

        [Fact]
        public async Task ParseAsync_DocumentWithHeadingAndParagraphs_ClassifiesByFontSize()
        {
            var path = TrackFile(TestDocuments.CreatePdfWithHeadingAndParagraphs());
            var parser = new PdfParser(CreateAiHelperMock());

            var chunks = await parser.ParseAsync(path);

            Assert.Equal(3, chunks.Count);

            Assert.Equal(ChunkType.Heading, chunks[0].Type);
            Assert.Equal("PDF Test Heading", chunks[0].Content);
            Assert.Equal(0, chunks[0].Order);

            Assert.Equal(ChunkType.Paragraph, chunks[1].Type);
            Assert.Equal("First paragraph line one, line two, and line three wrapped.", chunks[1].Content);

            Assert.Equal(ChunkType.Paragraph, chunks[2].Type);
            Assert.Equal("Second paragraph.", chunks[2].Content);

            Assert.Null(chunks[0].ParentId);
            Assert.Equal(chunks[0].Id, chunks[1].ParentId);
            Assert.Equal(chunks[0].Id, chunks[2].ParentId);
        }

        [Fact]
        public async Task ParseAsync_DocumentWithMultiLevelHeadings_NestsByFontSize()
        {
            var path = TrackFile(TestDocuments.CreatePdfWithMultiLevelHeadings());
            var parser = new PdfParser(CreateAiHelperMock());

            var chunks = await parser.ParseAsync(path);

            Assert.Equal(8, chunks.Count);

            var chapter1 = chunks[0];
            var chapter1Intro = chunks[1];
            var section11 = chunks[2];
            var section11Body = chunks[3];
            var section12 = chunks[4];
            var section12Body = chunks[5];
            var chapter2 = chunks[6];
            var chapter2Intro = chunks[7];

            Assert.Equal("Chapter 1", chapter1.Content);
            Assert.Equal("Section 1.1", section11.Content);
            Assert.Equal("Section 1.2", section12.Content);
            Assert.Equal("Chapter 2", chapter2.Content);

            // The 24pt heading has no parent; an 18pt heading nests under the preceding 24pt one; a
            // paragraph nests under whichever heading most recently opened.
            Assert.Null(chapter1.ParentId);
            Assert.Equal(chapter1.Id, chapter1Intro.ParentId);
            Assert.Equal(chapter1.Id, section11.ParentId);
            Assert.Equal(section11.Id, section11Body.ParentId);

            // A second 18pt heading closes out the first one (sibling, not child) but stays nested
            // under the same still-open 24pt heading.
            Assert.Equal(chapter1.Id, section12.ParentId);
            Assert.Equal(section12.Id, section12Body.ParentId);

            // A second 24pt heading closes out every open 18pt heading *and* the first 24pt heading,
            // becoming a new top-level sibling with no parent of its own.
            Assert.Null(chapter2.ParentId);
            Assert.Equal(chapter2.Id, chapter2Intro.ParentId);
        }

        [Fact]
        public async Task ParseAsync_BlankDocument_ReturnsEmptyList()
        {
            var path = TrackFile(TestDocuments.CreateBlankPdf());
            var parser = new PdfParser(CreateAiHelperMock());

            var chunks = await parser.ParseAsync(path);

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task ParseAsync_FileDoesNotExist_Throws()
        {
            var parser = new PdfParser(CreateAiHelperMock());
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");

            await Assert.ThrowsAnyAsync<Exception>(() => parser.ParseAsync(missingPath));
        }

        /// <summary>Builds an <see cref="IAiHelper"/> mock that returns empty topics/tags, sized to match the input.</summary>
        private static IAiHelper CreateAiHelperMock()
        {
            var mock = new Mock<IAiHelper>();

            mock.Setup(a => a.CreateTopicsAsync(It.IsAny<List<CreateChunkDto>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string>());

            mock.Setup(a => a.CreateTagsAsync(It.IsAny<List<CreateChunkDto>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<CreateChunkDto> chunks, CancellationToken _) =>
                {
                    var tags = new List<List<string>>();
                    foreach (var chunk in chunks)
                    {
                        tags.Add(new List<string>());
                    }

                    return tags;
                });

            return mock.Object;
        }

        private string TrackFile(string path)
        {
            _generatedFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var file in _generatedFiles)
            {
                var directory = Path.GetDirectoryName(file);
                if (directory != null && Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }
}
