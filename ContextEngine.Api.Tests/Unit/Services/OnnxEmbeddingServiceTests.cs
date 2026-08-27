using ContextEngine.Api.Services;
using ContextEngine.Api.Services.Interfaces;
using ContextEngine.Api.Tests.TestHelpers;

namespace ContextEngine.Api.Tests.Unit.Services
{
    [Collection(EmbeddingModelCollection.Name)]
    public class OnnxEmbeddingServiceTests
    {
        private readonly IEmbeddingService _embeddingService;

        public OnnxEmbeddingServiceTests(EmbeddingServiceFixture embeddingServiceFixture)
        {
            _embeddingService = embeddingServiceFixture.EmbeddingService;
        }

        [Fact]
        public async Task CreateEmbeddingAsync_BlankText_ReturnsZeroVectorOfModelDimensions()
        {
            var embedding = await _embeddingService.CreateEmbeddingAsync("   ");

            Assert.Equal(OnnxEmbeddingService.Dimensions, embedding.Length);
            Assert.All(embedding, value => Assert.Equal(0f, value));
        }

        [Fact]
        public async Task CreateEmbeddingAsync_NullText_ReturnsZeroVector()
        {
            var embedding = await _embeddingService.CreateEmbeddingAsync(null);

            Assert.All(embedding, value => Assert.Equal(0f, value));
        }

        [Fact]
        public async Task CreateEmbeddingAsync_SameTextTwice_IsDeterministic()
        {
            var first = await _embeddingService.CreateEmbeddingAsync("Invoices are due within 14 days of receipt.");
            var second = await _embeddingService.CreateEmbeddingAsync("Invoices are due within 14 days of receipt.");

            Assert.Equal(first, second);
        }

        [Fact]
        public async Task CosineSimilarity_ParaphrasedSentence_ScoresHigherThanUnrelatedSentence()
        {
            // The whole point of a real (trained) embedding model over a simpler word-matching
            // technique: this query shares almost no words with the "relevant" sentence below, yet a
            // model that understands meaning should still rank it clearly above the unrelated one.
            var query = await _embeddingService.CreateEmbeddingAsync("When do I need to pay my invoice?");
            var paraphrasedMatch = await _embeddingService.CreateEmbeddingAsync("Your invoice payment is due within 14 days of receipt.");
            var unrelated = await _embeddingService.CreateEmbeddingAsync("The office kitchen coffee machine is out of order.");

            var matchScore = _embeddingService.CosineSimilarity(query, paraphrasedMatch);
            var unrelatedScore = _embeddingService.CosineSimilarity(query, unrelated);

            Assert.True(matchScore > unrelatedScore,
                $"Expected the paraphrased match ({matchScore}) to score higher than the unrelated sentence ({unrelatedScore}).");
        }

        [Fact]
        public async Task CosineSimilarity_IdenticalText_IsApproximatelyOne()
        {
            var embedding = await _embeddingService.CreateEmbeddingAsync("Invoices are due within 14 days of receipt.");

            var similarity = _embeddingService.CosineSimilarity(embedding, embedding);

            Assert.Equal(1.0, similarity, precision: 4);
        }

        [Fact]
        public async Task CosineSimilarity_EitherVectorIsZero_ReturnsZero()
        {
            var zeroVector = new float[OnnxEmbeddingService.Dimensions];
            var wordVector = await _embeddingService.CreateEmbeddingAsync("some content");

            Assert.Equal(0, _embeddingService.CosineSimilarity(zeroVector, wordVector));
            Assert.Equal(0, _embeddingService.CosineSimilarity(wordVector, zeroVector));
        }

        [Fact]
        public async Task CosineSimilarity_DifferentLengthVectors_ReturnsZeroInsteadOfThrowing()
        {
            var normal = await _embeddingService.CreateEmbeddingAsync("some content");
            var wrongLength = new float[OnnxEmbeddingService.Dimensions + 1];

            Assert.Equal(0, _embeddingService.CosineSimilarity(normal, wrongLength));
        }
    }
}
