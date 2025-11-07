// File: JunoSidebar.Wpf/Services/LLM/Providers/LMStudioProvider.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    public class LMStudioProvider : ILLMProvider
    {
        private HttpClient? _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private string _baseUrl = "http://localhost:1234/v1";
        private ProviderConfiguration? _configuration;

        public string ProviderId => "lmstudio";
        public string ProviderName => "LM Studio";

        public ModelCapabilities Capabilities => new ModelCapabilities
        {
            SupportsStreaming = true,
            SupportsFunctionCalling = false,
            SupportsVision = false,
            SupportsSystemPrompt = true,
            SupportsTemperature = true,
            SupportsTopP = true,
            SupportsStopSequences = true,
            DefaultMaxTokens = 4096,
            MaxContextLength = 32000
        };

        public LMStudioProvider()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };
        }

        public Task InitializeAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _configuration = configuration;
            _baseUrl = configuration.BaseUrl ?? "http://localhost:1234/v1";

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds)
            };

            Debug.WriteLine($"LM Studio provider initialized with base URL: {_baseUrl}");
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            if (_httpClient == null)
            {
                return new[] { new ModelInfo { Id = "local-model", Name = "Local Model" } };
            }

            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/models", cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var modelsResponse = JsonSerializer.Deserialize<ModelsResponse>(responseString, _jsonOptions);

                if (modelsResponse?.Data == null || modelsResponse.Data.Count == 0)
                {
                    return new[] { new ModelInfo { Id = "local-model", Name = "Local Model" } };
                }

                return modelsResponse.Data.Select(m => new ModelInfo
                {
                    Id = m.Id,
                    Name = m.Id,
                    SupportsStreaming = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["object"] = m.Object ?? string.Empty,
                        ["owned_by"] = m.OwnedBy ?? string.Empty
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting LM Studio models: {ex.Message}");
                return new[] { new ModelInfo { Id = "local-model", Name = "Local Model" } };
            }
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_httpClient == null)
            {
                return false;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                var response = await _httpClient.GetAsync($"{_baseUrl}/models", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LM Studio connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task<LLMResponse> GetChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            LLMRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_httpClient == null)
            {
                throw new InvalidOperationException("Provider not initialized");
            }

            options ??= new LLMRequestOptions();

            var request = new ChatCompletionRequest
            {
                Model = model,
                Messages = messages.Select(m => new Message
                {
                    Role = m.Role,
                    Content = m.Content
                }).ToList(),
                Temperature = options.Temperature,
                MaxTokens = options.MaxTokens,
                TopP = options.TopP,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty,
                Stop = options.StopSequences
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var completionResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseString, _jsonOptions);

                if (completionResponse?.Choices == null || completionResponse.Choices.Count == 0)
                {
                    throw new Exception("No completion choices returned");
                }

                return new LLMResponse
                {
                    Content = completionResponse.Choices[0].Message.Content,
                    ModelId = completionResponse.Model,
                    PromptTokens = completionResponse.Usage?.PromptTokens,
                    CompletionTokens = completionResponse.Usage?.CompletionTokens,
                    TotalTokens = completionResponse.Usage?.TotalTokens,
                    Metadata = new Dictionary<string, object>
                    {
                        ["id"] = completionResponse.Id,
                        ["finish_reason"] = completionResponse.Choices[0].FinishReason ?? string.Empty
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LM Studio completion error: {ex.Message}");
                throw new Exception($"Failed to get completion from LM Studio: {ex.Message}", ex);
            }
        }

        public async IAsyncEnumerable<LLMResponseChunk> GetStreamingChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            LLMRequestOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_httpClient == null)
            {
                throw new InvalidOperationException("Provider not initialized");
            }

            options ??= new LLMRequestOptions();

            var request = new ChatCompletionRequest
            {
                Model = model,
                Messages = messages.Select(m => new Message
                {
                    Role = m.Role,
                    Content = m.Content
                }).ToList(),
                Temperature = options.Temperature,
                MaxTokens = options.MaxTokens,
                TopP = options.TopP,
                Stream = true
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            System.IO.Stream? stream = null;
            System.IO.StreamReader? reader = null;

            try
            {
                response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                reader = new System.IO.StreamReader(stream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("data: "))
                    {
                        line = line.Substring(6);
                    }

                    if (line == "[DONE]")
                    {
                        break;
                    }

                    var chunk = ParseChunk(line);
                    if (chunk != null)
                    {
                        yield return chunk;
                    }
                }
            }
            finally
            {
                reader?.Dispose();
                stream?.Dispose();
                response?.Dispose();
            }
        }

        private LLMResponseChunk? ParseChunk(string line)
        {
            try
            {
                var chunkResponse = JsonSerializer.Deserialize<ChatCompletionChunkResponse>(line, _jsonOptions);
                if (chunkResponse?.Choices == null || chunkResponse.Choices.Count == 0)
                {
                    return null;
                }

                var choice = chunkResponse.Choices[0];
                return new LLMResponseChunk
                {
                    Content = choice.Delta?.Content ?? string.Empty,
                    IsFinal = choice.FinishReason != null,
                    ChoiceIndex = choice.Index,
                    Metadata = new Dictionary<string, object>
                    {
                        ["finish_reason"] = choice.FinishReason ?? string.Empty
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing chunk: {ex.Message}");
                return null;
            }
        }

        #region DTOs

        private class ModelsResponse
        {
            [JsonPropertyName("data")]
            public List<Model> Data { get; set; } = new();
        }

        private class Model
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("object")]
            public string? Object { get; set; }

            [JsonPropertyName("owned_by")]
            public string? OwnedBy { get; set; }
        }

        private class ChatCompletionRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("messages")]
            public List<Message> Messages { get; set; } = new();

            [JsonPropertyName("temperature")]
            public float Temperature { get; set; } = 0.7f;

            [JsonPropertyName("max_tokens")]
            public int? MaxTokens { get; set; }

            [JsonPropertyName("top_p")]
            public float? TopP { get; set; }

            [JsonPropertyName("frequency_penalty")]
            public float? FrequencyPenalty { get; set; }

            [JsonPropertyName("presence_penalty")]
            public float? PresencePenalty { get; set; }

            [JsonPropertyName("stop")]
            public List<string>? Stop { get; set; }

            [JsonPropertyName("stream")]
            public bool Stream { get; set; }
        }

        private class Message
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
        }

        private class ChatCompletionResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("choices")]
            public List<Choice> Choices { get; set; } = new();

            [JsonPropertyName("usage")]
            public Usage? Usage { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("message")]
            public Message Message { get; set; } = new();

            [JsonPropertyName("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class ChatCompletionChunkResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("choices")]
            public List<ChunkChoice> Choices { get; set; } = new();
        }

        private class ChunkChoice
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("delta")]
            public DeltaMessage? Delta { get; set; }

            [JsonPropertyName("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class DeltaMessage
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }

        private class Usage
        {
            [JsonPropertyName("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonPropertyName("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }
        }

        #endregion
    }
}
