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

        private const int TopicsMaxTokens = 1024;
        private const int TagsMaxTokens = 4096;

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
        public async Task<List<string>> CreateTopicsAsync(List<CreateChunkDto> chunks, CancellationToken cancellationToken = default)
        {
            var documentText = BuildDocumentText(chunks);
            if (string.IsNullOrWhiteSpace(documentText))
            {
                return new List<string>();
            }

            var existingTopics = (await _searchService.GetSearchableOptionsAsync(cancellationToken)).Topics;

            var parameters = new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = TopicsMaxTokens,
                OutputConfig = new OutputConfig { Effort = Effort.Low, Format = BuildTopicsFormat() },
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = "Identify the 1 to 5 main topics covered by the following document. " +
                            "Topics should be short (one to three words) and describe subject matter, not document structure.\n\n" +
                            BuildReuseInstruction("topics", existingTopics) + "\n\n" +
                            "Document:\n" + documentText
                    }
                ]
            };

            var response = await _client.Messages.Create(parameters, cancellationToken);
            var json = GetResponseText(response);

            var result = JsonSerializer.Deserialize<TopicsResult>(json, DeserializeOptions);
            if (result == null || result.Topics == null)
            {
                return new List<string>();
            }

            return result.Topics;
        }

        /// <inheritdoc/>
        public async Task<List<List<string>>> CreateTagsAsync(List<CreateChunkDto> chunks, CancellationToken cancellationToken = default)
        {
            var emptyTags = BuildEmptyTags(chunks.Count);

            var documentText = BuildDocumentText(chunks);
            if (string.IsNullOrWhiteSpace(documentText))
            {
                return emptyTags;
            }

            var existingTags = (await _searchService.GetSearchableOptionsAsync(cancellationToken)).Tags;

            var parameters = new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = TagsMaxTokens,
                OutputConfig = new OutputConfig { Effort = Effort.Low, Format = BuildTagsFormat() },
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = "Tag each numbered chunk below with 1 to 5 short, specific tags. " +
                            "Use the full set of chunks as context so a chunk's tags can reflect its role " +
                            "in the document, not just its isolated text.\n\n" +
                            BuildReuseInstruction("tags", existingTags) + "\n\n" +
                            BuildIndexedChunkText(chunks)
                    }
                ]
            };

            var response = await _client.Messages.Create(parameters, cancellationToken);
            var json = GetResponseText(response);

            var result = JsonSerializer.Deserialize<TagsResult>(json, DeserializeOptions);
            if (result == null || result.ChunkTags == null)
            {
                return emptyTags;
            }

            foreach (var entry in result.ChunkTags)
            {
                if (entry.Index >= 0 && entry.Index < emptyTags.Count && entry.Tags != null)
                {
                    emptyTags[entry.Index] = entry.Tags;
                }
            }

            return emptyTags;
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

        /// <summary>Concatenates every chunk's content into one document-level text block.</summary>
        private static string BuildDocumentText(List<CreateChunkDto> chunks)
        {
            var builder = new StringBuilder();

            foreach (var chunk in chunks)
            {
                if (!string.IsNullOrWhiteSpace(chunk.Content))
                {
                    builder.AppendLine(chunk.Content);
                }
            }

            return builder.ToString().Trim();
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

        /// <summary>Builds the JSON schema that constrains <see cref="CreateTopicsAsync"/>'s response.</summary>
        private static JsonOutputFormat BuildTopicsFormat()
        {
            return new JsonOutputFormat
            {
                Schema = new Dictionary<string, JsonElement>
                {
                    ["type"] = JsonSerializer.SerializeToElement("object"),
                    ["properties"] = JsonSerializer.SerializeToElement(new
                    {
                        topics = new { type = "array", items = new { type = "string" } }
                    }),
                    ["required"] = JsonSerializer.SerializeToElement(new[] { "topics" }),
                    ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
                }
            };
        }

        /// <summary>Builds the JSON schema that constrains <see cref="CreateTagsAsync"/>'s response.</summary>
        private static JsonOutputFormat BuildTagsFormat()
        {
            return new JsonOutputFormat
            {
                Schema = new Dictionary<string, JsonElement>
                {
                    ["type"] = JsonSerializer.SerializeToElement("object"),
                    ["properties"] = JsonSerializer.SerializeToElement(new
                    {
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
                    ["required"] = JsonSerializer.SerializeToElement(new[] { "chunkTags" }),
                    ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
                }
            };
        }

        /// <summary>Deserialization target for <see cref="CreateTopicsAsync"/>'s structured response.</summary>
        private class TopicsResult
        {
            public List<string>? Topics { get; set; }
        }

        /// <summary>Deserialization target for <see cref="CreateTagsAsync"/>'s structured response.</summary>
        private class TagsResult
        {
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
