using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ContextEngine.Api.Data;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Models.Chunk;
using ContextEngine.Api.Tests.TestHelpers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using static ContextEngine.Api.Enums;

namespace ContextEngine.Api.Tests.Api
{
    public class DocumentsControllerApiTests : IClassFixture<ContextEngineApiFactory>, IDisposable
    {
        // HttpContent.ReadFromJsonAsync doesn't pick up the AddJsonOptions converters configured in
        // Program.cs (those only apply to MVC's own output formatter), so ChunkType-as-string needs
        // to be declared again here to deserialize it.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ContextEngineApiFactory _factory;
        private readonly HttpClient _client;
        private readonly List<string> _generatedFiles = new();

        public DocumentsControllerApiTests(ContextEngineApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task UploadDocument_ValidDocx_ReturnsOkWithSourceId()
        {
            var path = TrackFile(TestDocuments.CreateDocxWithHeadingsAndTable());

            var response = await _client.PostAsync(
                $"/api/documents?documentPath={Uri.EscapeDataString(path)}", content: null);

            response.EnsureSuccessStatusCode();
            var sourceId = await response.Content.ReadFromJsonAsync<Guid>();
            Assert.NotEqual(Guid.Empty, sourceId);
        }

        [Fact]
        public async Task UploadDocument_UnsupportedExtension_ReturnsBadRequestProblemDetails()
        {
            var response = await _client.PostAsync(
                "/api/documents?documentPath=document.txt", content: null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
        }

        [Fact]
        public async Task UploadDocument_FileDoesNotExist_ReturnsNotFoundProblemDetails()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");

            var response = await _client.PostAsync(
                $"/api/documents?documentPath={Uri.EscapeDataString(missingPath)}", content: null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal(StatusCodes.Status404NotFound, problem!.Status);
        }

        [Fact]
        public async Task GetDocumentById_ExistingDocument_ReturnsItsChunks()
        {
            var path = TrackFile(TestDocuments.CreateDocxWithHeadingsAndTable());
            var uploadResponse = await _client.PostAsync(
                $"/api/documents?documentPath={Uri.EscapeDataString(path)}", content: null);
            var sourceId = await uploadResponse.Content.ReadFromJsonAsync<Guid>();

            var response = await _client.GetAsync($"/api/documents/{sourceId}");

            response.EnsureSuccessStatusCode();
            var chunks = await response.Content.ReadFromJsonAsync<List<ChunkDto>>(JsonOptions);
            Assert.NotEmpty(chunks!);
            Assert.All(chunks!, chunk => Assert.Equal(sourceId, chunk.SourceId));
        }

        [Fact]
        public async Task GetDocumentById_UnknownDocument_ReturnsNotFound()
        {
            var response = await _client.GetAsync($"/api/documents/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task DeleteDocument_ExistingDocument_ReturnsNoContentAndRemovesIt()
        {
            var path = TrackFile(TestDocuments.CreateDocxWithHeadingsAndTable());
            var uploadResponse = await _client.PostAsync(
                $"/api/documents?documentPath={Uri.EscapeDataString(path)}", content: null);
            var sourceId = await uploadResponse.Content.ReadFromJsonAsync<Guid>();

            var deleteResponse = await _client.DeleteAsync($"/api/documents/{sourceId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            var getResponse = await _client.GetAsync($"/api/documents/{sourceId}");
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }

        [Fact]
        public async Task DeleteDocument_UnknownDocument_ReturnsNotFound()
        {
            var response = await _client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetDocumentsByTopic_MatchingTopic_ReturnsDocumentId()
        {
            var sourceId = Guid.NewGuid();
            var topic = $"topic-{Guid.NewGuid()}";
            await SeedChunkAsync(sourceId, topics: new List<string> { topic });

            var response = await _client.GetAsync($"/api/documents/by-topic/{topic}");

            response.EnsureSuccessStatusCode();
            var documentIds = await response.Content.ReadFromJsonAsync<List<Guid>>();
            Assert.Equal(new List<Guid> { sourceId }, documentIds);
        }

        [Fact]
        public async Task GetDocumentsByTopic_NoMatch_ReturnsEmptyList()
        {
            var response = await _client.GetAsync($"/api/documents/by-topic/{Guid.NewGuid()}");

            response.EnsureSuccessStatusCode();
            var documentIds = await response.Content.ReadFromJsonAsync<List<Guid>>();
            Assert.Empty(documentIds!);
        }

        [Fact]
        public async Task GetDocumentsByTag_MatchingTag_ReturnsDocumentId()
        {
            var sourceId = Guid.NewGuid();
            var tag = $"tag-{Guid.NewGuid()}";
            await SeedChunkAsync(sourceId, tags: new List<string> { tag });

            var response = await _client.GetAsync($"/api/documents/by-tag/{tag}");

            response.EnsureSuccessStatusCode();
            var documentIds = await response.Content.ReadFromJsonAsync<List<Guid>>();
            Assert.Equal(new List<Guid> { sourceId }, documentIds);
        }

        [Fact]
        public async Task GetDocumentsByTag_NoMatch_ReturnsEmptyList()
        {
            var response = await _client.GetAsync($"/api/documents/by-tag/{Guid.NewGuid()}");

            response.EnsureSuccessStatusCode();
            var documentIds = await response.Content.ReadFromJsonAsync<List<Guid>>();
            Assert.Empty(documentIds!);
        }

        [Fact]
        public async Task GetDocumentsByDateRange_MatchingRange_ReturnsDocumentId()
        {
            var sourceId = Guid.NewGuid();
            var createdAt = new DateTimeOffset(2020, 6, 15, 0, 0, 0, TimeSpan.Zero);
            await SeedChunkAsync(sourceId, createdAt: createdAt);

            var response = await _client.GetAsync(
                "/api/documents/by-date-range?startDate=2020-06-01&endDate=2020-06-30");

            response.EnsureSuccessStatusCode();
            var documentIds = await response.Content.ReadFromJsonAsync<List<Guid>>();
            Assert.Equal(new List<Guid> { sourceId }, documentIds);
        }

        [Fact]
        public async Task GetDocumentsByDateRange_NoMatch_ReturnsEmptyList()
        {
            // 1999 predates any chunk this test class ever seeds or uploads.
            var response = await _client.GetAsync(
                "/api/documents/by-date-range?startDate=1999-01-01&endDate=1999-01-02");

            response.EnsureSuccessStatusCode();
            var documentIds = await response.Content.ReadFromJsonAsync<List<Guid>>();
            Assert.Empty(documentIds!);
        }

        /// <summary>Inserts a chunk directly through the DbContext, bypassing the (unimplemented) write API, so GET endpoints have data to filter.</summary>
        private async Task SeedChunkAsync(
            Guid sourceId, List<string>? topics = null, List<string>? tags = null, DateTimeOffset? createdAt = null)
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ContextEngineDbContext>();

            var timestamp = createdAt ?? DateTimeOffset.UtcNow;
            context.Chunks.Add(new Chunk
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                Type = ChunkType.Paragraph,
                Topics = topics ?? new List<string>(),
                Tags = tags ?? new List<string>(),
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });

            await context.SaveChangesAsync();
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
