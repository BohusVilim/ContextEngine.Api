using Microsoft.Extensions.AI;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Services
{
    /// <inheritdoc cref="IEmbeddingService"/>
    /// <remarks>
    /// <para>
    /// This is a thin adapter around Microsoft Semantic Kernel's
    /// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> abstraction (from the
    /// <c>Microsoft.Extensions.AI</c> package that Semantic Kernel builds on), which in turn runs the
    /// all-MiniLM-L6-v2 sentence-embedding model locally through ONNX Runtime. Wiring the actual model
    /// files up happens once, at startup, via <c>IServiceCollection.AddBertOnnxEmbeddingGenerator</c>
    /// in <c>Program.cs</c> — this class only ever sees the already-loaded generator through DI, it
    /// never touches file paths or the ONNX runtime directly.
    /// </para>
    /// <para>
    /// <b>Why a real model instead of something hand-rolled:</b> a trained sentence-embedding model
    /// places semantically similar sentences near each other in vector space even when they share no
    /// words — e.g. "kedy treba zaplatiť" and "splatnosť faktúry je 14 dní" score meaningfully similar
    /// here, which a simpler technique (matching shared words) cannot do. That's the whole point of
    /// semantic search: finding the right chunk by what it means, not by whether it happens to repeat
    /// the query's exact wording.
    /// </para>
    /// </remarks>
    public class OnnxEmbeddingService : IEmbeddingService
    {
        /// <summary>
        /// Output vector length of the all-MiniLM-L6-v2 model (see EmbeddingModel/NOTICE.txt) that
        /// <see cref="CreateEmbeddingAsync"/> runs text through. Every embedding this service produces
        /// — chunk or query — has exactly this many components.
        /// </summary>
        public const int Dimensions = 384;

        private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

        public OnnxEmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator)
        {
            _generator = generator;
        }

        /// <inheritdoc/>
        public async Task<float[]> CreateEmbeddingAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new float[Dimensions];
            }

            var vector = await _generator.GenerateVectorAsync(text);
            return vector.ToArray();
        }

        /// <inheritdoc/>
        public double CosineSimilarity(float[] a, float[] b)
        {
            // A length mismatch means the two vectors aren't really comparable (see the interface
            // doc for when this happens) - treated as "no similarity" rather than a thrown exception,
            // since a single unranked chunk shouldn't be able to fail an entire search.
            if (a.Length != b.Length)
            {
                return 0;
            }

            var magnitudeA = Magnitude(a);
            var magnitudeB = Magnitude(b);
            if (magnitudeA == 0 || magnitudeB == 0)
            {
                return 0;
            }

            double dotProduct = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
            }

            return dotProduct / (magnitudeA * magnitudeB);
        }

        /// <summary>Computes a vector's Euclidean (L2) length: the square root of the sum of its squared components.</summary>
        private static double Magnitude(float[] vector)
        {
            double sumOfSquares = 0;
            foreach (var value in vector)
            {
                sumOfSquares += value * value;
            }

            return Math.Sqrt(sumOfSquares);
        }
    }
}
