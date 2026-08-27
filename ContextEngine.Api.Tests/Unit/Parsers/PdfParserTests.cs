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

            mock.Setup(a => a.CreateTopicsAsync(It.IsAny<List<CreateChunkDto>>()))
                .ReturnsAsync(new List<string>());

            mock.Setup(a => a.CreateTagsAsync(It.IsAny<List<CreateChunkDto>>()))
                .ReturnsAsync((List<CreateChunkDto> chunks) =>
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
