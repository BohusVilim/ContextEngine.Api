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

            // Both headings are "Heading1" (same level), so the second heading is a sibling of the
            // first, not its child - it closes out "Introduction" rather than nesting under it.
            Assert.Null(chunks[0].ParentId);
            Assert.Equal(chunks[0].Id, chunks[1].ParentId);
            Assert.Equal(chunks[0].Id, chunks[2].ParentId);
            Assert.Null(chunks[3].ParentId);
            Assert.Equal(chunks[3].Id, chunks[4].ParentId);
            Assert.Equal(chunks[3].Id, chunks[5].ParentId);
        }

        [Fact]
        public async Task ParseAsync_DocumentWithMultiLevelHeadings_NestsByHeadingLevel()
        {
            var path = TrackFile(TestDocuments.CreateDocxWithMultiLevelHeadings());
            var parser = new DocxParser(CreateAiHelperMock());

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

            // A Heading1 has no parent; a Heading2 nests under the preceding Heading1; a paragraph
            // nests under whichever heading most recently opened.
            Assert.Null(chapter1.ParentId);
            Assert.Equal(chapter1.Id, chapter1Intro.ParentId);
            Assert.Equal(chapter1.Id, section11.ParentId);
            Assert.Equal(section11.Id, section11Body.ParentId);

            // A second Heading2 closes out the first one (sibling, not child) but stays nested under
            // the same still-open Heading1.
            Assert.Equal(chapter1.Id, section12.ParentId);
            Assert.Equal(section12.Id, section12Body.ParentId);

            // A second Heading1 closes out every open Heading2 *and* the first Heading1, becoming a
            // new top-level sibling with no parent of its own.
            Assert.Null(chapter2.ParentId);
            Assert.Equal(chapter2.Id, chapter2Intro.ParentId);
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
