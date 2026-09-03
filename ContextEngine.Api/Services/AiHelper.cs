using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ContextEngine.Api.DTOs;
using ContextEngine.Api.Services.Interfaces;

namespace ContextEngine.Api.Services
{
    /// <inheritdoc cref="IAiHelper"/>
    public class AiHelper : IAiHelper
    {
        /// <summary>Model used for topic/tag generation. Cheap classification task, not worth a larger model.</summary>
        private const string ModelId = "claude-haiku-4-5-20251001";

        /// <summary>
        /// Generous enough for a large document's per-chunk tags (the dominant part of the response);
        /// the handful of extra document-level topics add negligible overhead on top of that.
        /// </summary>
        private const int MaxTokens = 4096;

        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly AnthropicClient _client;
        private readonly ISearchService _searchService;

        public AiHelper(AnthropicClient client, ISearchService searchService)
        {
            _client = client;
            _searchService = searchService;
        }

        /// <inheritdoc/>
        public async Task<TopicsAndTags> CreateTopicsAndTagsAsync(List<CreateChunkDto> chunks, CancellationToken cancellationToken = default)
        {
            var emptyTags = BuildEmptyTags(chunks.Count);

            if (!chunks.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
            {
                return new TopicsAndTags { Topics = new List<string>(), Tags = emptyTags };
            }

            // Fetches both existing topics and existing tags in one call, matching the one-call-covers-
            // both-concerns approach this method itself takes below.
            var existingOptions = await _searchService.GetSearchableOptionsAsync(cancellationToken);

            var parameters = new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = MaxTokens,
                OutputConfig = new OutputConfig { Effort = Effort.Low, Format = BuildTopicsAndTagsFormat() },
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = "First, identify the 1 to 5 main topics covered by the whole document below. " +
                            "Topics should be short (one to three words) and describe subject matter, not document structure.\n" +
                            BuildReuseInstruction("topics", existingOptions.Topics) + "\n\n" +
                            "Second, tag each numbered chunk below with 1 to 5 short, specific tags. " +
                            "Use the full set of chunks as context so a chunk's tags can reflect its role " +
                            "in the document, not just its isolated text.\n" +
                            BuildReuseInstruction("tags", existingOptions.Tags) + "\n\n" +
                            BuildIndexedChunkText(chunks)
                    }
                ]
            };

            var response = await _client.Messages.Create(parameters, cancellationToken);
            var json = GetResponseText(response);

            var result = JsonSerializer.Deserialize<TopicsAndTagsResult>(json, DeserializeOptions);
            if (result == null)
            {
                return new TopicsAndTags { Topics = new List<string>(), Tags = emptyTags };
            }

            if (result.ChunkTags != null)
            {
                foreach (var entry in result.ChunkTags)
                {
                    if (entry.Index >= 0 && entry.Index < emptyTags.Count && entry.Tags != null)
                    {
                        emptyTags[entry.Index] = entry.Tags;
                    }
                }
            }

            return new TopicsAndTags { Topics = result.Topics ?? new List<string>(), Tags = emptyTags };
        }

        /// <summary>
        /// Builds the instruction telling the model to prefer reusing an already-existing topic/tag
        /// value over inventing a new one, so the set of values in use across the system stays small
        /// instead of accumulating near-duplicate variants of the same idea per document.
        /// </summary>
        /// <param name="label">Either "topics" or "tags", to phrase the instruction correctly.</param>
        /// <param name="existingValues">Distinct topic/tag values already present on other stored chunks.</param>
        private static string BuildReuseInstruction(string label, List<string> existingValues)
        {
            if (existingValues.Count == 0)
            {
                return $"No {label} exist yet, so you are free to introduce new ones.";
            }

            return $"Existing {label} already in use elsewhere in the system: {string.Join(", ", existingValues)}.\n" +
                $"Reuse one of these whenever it's a genuinely good fit. Only introduce a new {label.TrimEnd('s')} " +
                $"when none of the existing ones are truly relevant.";
        }

        /// <summary>Renders every chunk as "Chunk {index}: {content}", so the model can key its response by index.</summary>
        private static string BuildIndexedChunkText(List<CreateChunkDto> chunks)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < chunks.Count; i++)
            {
                builder.AppendLine($"Chunk {i}: {chunks[i].Content}");
            }

            return builder.ToString();
        }

        /// <summary>Builds a list of empty tag lists, one per chunk, used as the fallback/default result.</summary>
        private static List<List<string>> BuildEmptyTags(int count)
        {
            var tags = new List<List<string>>();

            for (var i = 0; i < count; i++)
            {
                tags.Add(new List<string>());
            }

            return tags;
        }

        /// <summary>Extracts the text of the first text block in a response.</summary>
        private static string GetResponseText(Message response)
        {
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    return text.Text;
                }
            }

            return string.Empty;
        }

        /// <summary>Builds the JSON schema that constrains <see cref="CreateTopicsAndTagsAsync"/>'s response.</summary>
        private static JsonOutputFormat BuildTopicsAndTagsFormat()
        {
            return new JsonOutputFormat
            {
                Schema = new Dictionary<string, JsonElement>
                {
                    ["type"] = JsonSerializer.SerializeToElement("object"),
                    ["properties"] = JsonSerializer.SerializeToElement(new
                    {
                        topics = new { type = "array", items = new { type = "string" } },
                        chunkTags = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    index = new { type = "integer" },
                                    tags = new { type = "array", items = new { type = "string" } }
                                },
                                required = new[] { "index", "tags" },
                                additionalProperties = false
                            }
                        }
                    }),
                    ["required"] = JsonSerializer.SerializeToElement(new[] { "topics", "chunkTags" }),
                    ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
                }
            };
        }

        /// <summary>Deserialization target for <see cref="CreateTopicsAndTagsAsync"/>'s structured response.</summary>
        private class TopicsAndTagsResult
        {
            public List<string>? Topics { get; set; }
            public List<ChunkTagsEntry>? ChunkTags { get; set; }
        }

        /// <summary>A single chunk's tags, keyed by its index in the request's chunk list.</summary>
        private class ChunkTagsEntry
        {
            public int Index { get; set; }
            public List<string>? Tags { get; set; }
        }
    }
}
