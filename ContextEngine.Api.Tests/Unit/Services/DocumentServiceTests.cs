using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Mappings;
using ContextEngine.Api.Options;
using ContextEngine.Api.Parsers.Interfaces;
using ContextEngine.Api.Services;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Unit.Services
{
    [Collection(EmbeddingModelCollection.Name)]
    public class DocumentServiceTests
    {
        // Matches DocumentUploadOptions' own default (no root configured), so every existing test
        // below - written before upload-root restriction existed - keeps behaving exactly as before.
        // OptionsWrapper<T> (rather than Options.Create) sidesteps a name clash between the
        // Microsoft.Extensions.Options.Options class and the ContextEngine.Api.Options namespace.
        private static readonly IOptions<DocumentUploadOptions> NoUploadRestriction =
            new OptionsWrapper<DocumentUploadOptions>(new DocumentUploadOptions());

        private readonly IEmbeddingService _embeddingService;

        public DocumentServiceTests(EmbeddingServiceFixture embeddingServiceFixture)
        {
            _embeddingService = embeddingServiceFixture.EmbeddingService;
        }

        private static ContextEngineDbContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<ContextEngineDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ContextEngineDbContext(options);
        }

        [Fact]
        public async Task UploadDocumentAsync_DocxExtension_UsesDocxParserAndPersistsChunks()
        {
            using var context = CreateInMemoryContext();

            var docxParserMock = new Mock<IDocxParser>();
            docxParserMock
                .Setup(p => p.ParseAsync("document.docx", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CreateChunkDto>
                {
                    new CreateChunkDto { Type = ChunkType.Heading, Order = 0, Content = "Title" }
                });

            var pdfParserMock = new Mock<IPdfParser>();

            var service = new DocumentService(context, docxParserMock.Object, pdfParserMock.Object, new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var sourceId = await service.UploadDocumentAsync("document.docx");

            docxParserMock.Verify(p => p.ParseAsync("document.docx", It.IsAny<CancellationToken>()), Times.Once);
            pdfParserMock.Verify(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

            var savedChunks = await context.Chunks.ToListAsync();
            var savedChunk = Assert.Single(savedChunks);
            Assert.Equal(sourceId, savedChunk.SourceId);
            Assert.Equal("Title", savedChunk.Content);
        }

        [Fact]
        public async Task UploadDocumentAsync_PdfExtension_UsesPdfParser()
        {
            using var context = CreateInMemoryContext();

            var docxParserMock = new Mock<IDocxParser>();
            var pdfParserMock = new Mock<IPdfParser>();
            pdfParserMock
                .Setup(p => p.ParseAsync("document.pdf", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CreateChunkDto>());

            var service = new DocumentService(context, docxParserMock.Object, pdfParserMock.Object, new ChunkMappings(), _embeddingService, NoUploadRestriction);

            await service.UploadDocumentAsync("document.pdf");

            pdfParserMock.Verify(p => p.ParseAsync("document.pdf", It.IsAny<CancellationToken>()), Times.Once);
            docxParserMock.Verify(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("document.PDF")]
        [InlineData("document.DOCX")]
        public async Task UploadDocumentAsync_ExtensionIsCaseInsensitive(string documentPath)
        {
            using var context = CreateInMemoryContext();

            var docxParserMock = new Mock<IDocxParser>();
            docxParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CreateChunkDto>());
            var pdfParserMock = new Mock<IPdfParser>();
            pdfParserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CreateChunkDto>());

            var service = new DocumentService(context, docxParserMock.Object, pdfParserMock.Object, new ChunkMappings(), _embeddingService, NoUploadRestriction);

            await service.UploadDocumentAsync(documentPath);
        }

        [Fact]
        public async Task UploadDocumentAsync_UnsupportedExtension_ThrowsNotSupportedException()
        {
            using var context = CreateInMemoryContext();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                () => service.UploadDocumentAsync("document.txt"));

            Assert.Contains(".txt", exception.Message);
        }

        [Fact]
        public async Task UploadDocumentAsync_PathOutsideConfiguredAllowedRoot_ThrowsUnauthorizedAccessException()
        {
            using var context = CreateInMemoryContext();
            var allowedRoot = Path.Combine(Path.GetTempPath(), "allowed-" + Guid.NewGuid());
            var restrictedUploadOptions = new OptionsWrapper<DocumentUploadOptions>(new DocumentUploadOptions { AllowedRootPath = allowedRoot });

            var service = new DocumentService(
                context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, restrictedUploadOptions);

            var pathOutsideRoot = Path.Combine(Path.GetTempPath(), "elsewhere", "document.docx");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UploadDocumentAsync(pathOutsideRoot));
        }

        [Fact]
        public async Task UploadDocumentAsync_PathInsideConfiguredAllowedRoot_ParsesNormally()
        {
            using var context = CreateInMemoryContext();
            var allowedRoot = Path.Combine(Path.GetTempPath(), "allowed-" + Guid.NewGuid());
            var restrictedUploadOptions = new OptionsWrapper<DocumentUploadOptions>(new DocumentUploadOptions { AllowedRootPath = allowedRoot });
            var pathInsideRoot = Path.Combine(allowedRoot, "document.docx");

            var docxParserMock = new Mock<IDocxParser>();
            docxParserMock.Setup(p => p.ParseAsync(pathInsideRoot, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CreateChunkDto>());

            var service = new DocumentService(
                context, docxParserMock.Object, Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, restrictedUploadOptions);

            await service.UploadDocumentAsync(pathInsideRoot);

            docxParserMock.Verify(p => p.ParseAsync(pathInsideRoot, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetDocumentByIdAsync_DocumentExists_ReturnsChunksInOrder()
        {
            using var context = CreateInMemoryContext();
            var sourceId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = sourceId, Type = ChunkType.Paragraph, Order = 1, Content = "Second" },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = sourceId, Type = ChunkType.Heading, Order = 0, Content = "First" },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Heading, Order = 0, Content = "OtherDocument" });
            await context.SaveChangesAsync();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var chunks = await service.GetDocumentByIdAsync(sourceId);

            Assert.NotNull(chunks);
            Assert.Equal(2, chunks!.Count);
            Assert.Equal("First", chunks[0].Content);
            Assert.Equal("Second", chunks[1].Content);
            Assert.All(chunks, dto => Assert.Equal(sourceId, dto.SourceId));
        }

        [Fact]
        public async Task GetDocumentByIdAsync_DocumentDoesNotExist_ReturnsNull()
        {
            using var context = CreateInMemoryContext();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var chunks = await service.GetDocumentByIdAsync(Guid.NewGuid());

            Assert.Null(chunks);
        }

        [Fact]
        public async Task GetDocumentIdsByTopicAsync_ReturnsDistinctMatchingSourceIds()
        {
            using var context = CreateInMemoryContext();
            var matchingSourceId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = matchingSourceId, Type = ChunkType.Heading, Topics = new List<string> { "billing" } },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = matchingSourceId, Type = ChunkType.Paragraph, Topics = new List<string> { "billing" } },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Topics = new List<string> { "other" } });
            await context.SaveChangesAsync();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var documentIds = await service.GetDocumentIdsByTopicAsync("billing");

            Assert.Equal(new List<Guid> { matchingSourceId }, documentIds);
        }

        [Fact]
        public async Task GetDocumentIdsByTagAsync_ReturnsDistinctMatchingSourceIds()
        {
            using var context = CreateInMemoryContext();
            var matchingSourceId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = matchingSourceId, Type = ChunkType.Heading, Tags = new List<string> { "urgent" } },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Paragraph, Tags = new List<string> { "other" } });
            await context.SaveChangesAsync();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var documentIds = await service.GetDocumentIdsByTagAsync("urgent");

            Assert.Equal(new List<Guid> { matchingSourceId }, documentIds);
        }

        [Fact]
        public async Task GetDocumentIdsByDateRangeAsync_ReturnsOnlySourceIdsWithinRange()
        {
            using var context = CreateInMemoryContext();
            var inRangeSourceId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = inRangeSourceId, Type = ChunkType.Heading, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), Type = ChunkType.Heading, CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) });
            await context.SaveChangesAsync();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var documentIds = await service.GetDocumentIdsByDateRangeAsync(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

            Assert.Equal(new List<Guid> { inRangeSourceId }, documentIds);
        }

        [Fact]
        public async Task DeleteDocumentAsync_DocumentExists_RemovesItsChunksAndReturnsTrue()
        {
            using var context = CreateInMemoryContext();
            var sourceId = Guid.NewGuid();
            var otherSourceId = Guid.NewGuid();

            context.Chunks.AddRange(
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = sourceId, Type = ChunkType.Heading },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = sourceId, Type = ChunkType.Paragraph },
                new Models.Chunk.Chunk { Id = Guid.NewGuid(), SourceId = otherSourceId, Type = ChunkType.Heading });
            await context.SaveChangesAsync();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var deleted = await service.DeleteDocumentAsync(sourceId);

            Assert.True(deleted);
            var remaining = await context.Chunks.ToListAsync();
            var remainingChunk = Assert.Single(remaining);
            Assert.Equal(otherSourceId, remainingChunk.SourceId);
        }

        [Fact]
        public async Task DeleteDocumentAsync_DocumentDoesNotExist_ReturnsFalse()
        {
            using var context = CreateInMemoryContext();

            var service = new DocumentService(context, Mock.Of<IDocxParser>(), Mock.Of<IPdfParser>(), new ChunkMappings(), _embeddingService, NoUploadRestriction);

            var deleted = await service.DeleteDocumentAsync(Guid.NewGuid());

            Assert.False(deleted);
        }
    }
}
