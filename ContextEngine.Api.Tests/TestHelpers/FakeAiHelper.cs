using ContextEngine.Api.DTOs;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Tests.TestHelpers
{
    /// <summary>
    /// No-op <see cref="IAiHelper"/> used by the integration test host, so requests through the
    /// real HTTP pipeline never call out to the actual Anthropic API.
    /// </summary>
    public class FakeAiHelper : IAiHelper
    {
        /// <inheritdoc/>
        public Task<List<string>> CreateTopicsAsync(List<CreateChunkDto> chunks)
        {
            return Task.FromResult(new List<string>());
        }

        /// <inheritdoc/>
        public Task<List<List<string>>> CreateTagsAsync(List<CreateChunkDto> chunks)
        {
            var tags = new List<List<string>>();
            foreach (var chunk in chunks)
            {
                tags.Add(new List<string>());
            }

            return Task.FromResult(tags);
        }
    }
}
