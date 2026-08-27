using Moq;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Parsers;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Unit.Parsers
{
    public class DocxParserTests : IDisposable
    {
        private readonly List<string> _generatedFiles = new();

        [Fact]
        public async Task ParseAsync_DocumentWithHeadingsAndTable_ProducesExpectedChunksInOrder()
        {
            var path = TrackFile(TestDocuments.CreateDocxWithHeadingsAndTable());
            var parser = new DocxParser(CreateAiHelperMock());

            var chunks = await parser.ParseAsync(path);

            Assert.Equal(6, chunks.Count);

            Assert.Equal(ChunkType.Heading, chunks[0].Type);
            Assert.Equal("Introduction", chunks[0].Content);
            Assert.Equal(0, chunks[0].Order);

            Assert.Equal(ChunkType.Paragraph, chunks[1].Type);
            Assert.Equal("First body paragraph.", chunks[1].Content);

            Assert.Equal(ChunkType.Paragraph, chunks[2].Type);
            Assert.Equal("Second body paragraph.", chunks[2].Content);

            Assert.Equal(ChunkType.Heading, chunks[3].Type);
            Assert.Equal("Details", chunks[3].Content);

            Assert.Equal(ChunkType.Paragraph, chunks[4].Type);
            Assert.Equal("Paragraph under the second heading.", chunks[4].Content);

            Assert.Equal(ChunkType.Table, chunks[5].Type);
            Assert.Equal("Cell ACell B", chunks[5].Content);
            Assert.Equal(5, chunks[5].Order);
        }

        [Fact]
        public async Task ParseAsync_DocumentWithOnlyEmptyParagraphs_ReturnsEmptyList()
        {
            var path = TrackFile(TestDocuments.CreateDocxWithOnlyEmptyParagraphs());
            var parser = new DocxParser(CreateAiHelperMock());

            var chunks = await parser.ParseAsync(path);

            Assert.Empty(chunks);
        }

        [Fact]
        public async Task ParseAsync_FileDoesNotExist_ThrowsFileNotFoundException()
        {
            var parser = new DocxParser(CreateAiHelperMock());
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");

            await Assert.ThrowsAsync<FileNotFoundException>(() => parser.ParseAsync(missingPath));
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
