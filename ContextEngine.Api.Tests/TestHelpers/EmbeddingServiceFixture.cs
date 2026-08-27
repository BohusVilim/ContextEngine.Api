using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ContextEngine.Api.Services;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Tests.TestHelpers
{
    /// <summary>
    /// Loads the ONNX embedding model exactly once for every unit test that needs a real
    /// <see cref="IEmbeddingService"/>, instead of each test constructing its own
    /// <see cref="OnnxEmbeddingService"/> and re-reading the (~23MB) model file from disk.
    /// Shared across test classes via <see cref="EmbeddingModelCollection"/> - see its remarks for
    /// how xUnit wires that sharing up.
    /// </summary>
    public class EmbeddingServiceFixture
    {
        public IEmbeddingService EmbeddingService { get; }

        public EmbeddingServiceFixture()
        {
            var services = new ServiceCollection();
            services.AddBertOnnxEmbeddingGenerator(
                Path.Combine(AppContext.BaseDirectory, "EmbeddingModel", "model.onnx"),
                Path.Combine(AppContext.BaseDirectory, "EmbeddingModel", "vocab.txt"));

            var generator = services.BuildServiceProvider()
                .GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            EmbeddingService = new OnnxEmbeddingService(generator);
        }
    }

    /// <summary>
    /// Declares the "EmbeddingModel" xUnit test collection: every test class tagged with
    /// <c>[Collection(EmbeddingModelCollection.Name)]</c> receives the exact same
    /// <see cref="EmbeddingServiceFixture"/> instance (constructed once, before any of those classes'
    /// tests run, and disposed once after the last of them finishes) rather than xUnit's default of
    /// one fresh fixture per test class.
    /// </summary>
    [CollectionDefinition(Name)]
    public class EmbeddingModelCollection : ICollectionFixture<EmbeddingServiceFixture>
    {
        public const string Name = "EmbeddingModel";
    }
}
