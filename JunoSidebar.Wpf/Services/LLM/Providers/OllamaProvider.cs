// File: JunoSidebar.Wpf/Services/LLM/Providers/OllamaProvider.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    public class OllamaProvider : ILLMProvider
    {
        private HttpClient? _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private string _baseUrl = "http://localhost:11434";
        private ProviderConfiguration? _configuration;

        public string ProviderId => "ollama";
        public string ProviderName => "Ollama";

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

        public OllamaProvider()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public Task InitializeAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _configuration = configuration;
            _baseUrl = configuration.BaseUrl ?? "http://localhost:11434";

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds)
            };

            Debug.WriteLine($"Ollama provider initialized with base URL: {_baseUrl}");
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            if (_httpClient == null)
            {
                return Enumerable.Empty<ModelInfo>();
            }

            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(responseString, _jsonOptions);

                if (modelsResponse?.Models == null)
                {
                    return Enumerable.Empty<ModelInfo>();
                }

                return modelsResponse.Models.Select(m => new ModelInfo
                {
                    Id = m.Name,
                    Name = m.Name,
                    Description = $"Size: {FormatBytes(m.Size)}",
                    SupportsStreaming = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["size"] = m.Size,
                        ["modified_at"] = m.ModifiedAt ?? string.Empty
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting Ollama models: {ex.Message}");
                return Enumerable.Empty<ModelInfo>();
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

                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ollama connection test failed: {ex.Message}");
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

            var request = new OllamaChatRequest
            {
                Model = model,
                Messages = messages.Select(m => new OllamaMessage
                {
                    Role = m.Role,
                    Content = m.Content
                }).ToList(),
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = options.Temperature,
                    NumPredict = options.MaxTokens,
                    TopP = options.TopP,
                    Stop = options.StopSequences
                }
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseString, _jsonOptions);

                if (chatResponse?.Message == null)
                {
                    throw new Exception("No message in response");
                }

                return new LLMResponse
                {
                    Content = chatResponse.Message.Content,
                    ModelId = chatResponse.Model,
                    PromptTokens = chatResponse.PromptEvalCount,
                    CompletionTokens = chatResponse.EvalCount,
                    TotalTokens = (chatResponse.PromptEvalCount ?? 0) + (chatResponse.EvalCount ?? 0),
                    Metadata = new Dictionary<string, object>
                    {
                        ["total_duration"] = chatResponse.TotalDuration ?? 0,
                        ["load_duration"] = chatResponse.LoadDuration ?? 0,
                        ["eval_duration"] = chatResponse.EvalDuration ?? 0
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ollama completion error: {ex.Message}");
                throw new Exception($"Failed to get completion from Ollama: {ex.Message}", ex);
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

            var request = new OllamaChatRequest
            {
                Model = model,
                Messages = messages.Select(m => new OllamaMessage
                {
                    Role = m.Role,
                    Content = m.Content
                }).ToList(),
                Stream = true,
                Options = new OllamaOptions
                {
                    Temperature = options.Temperature,
                    NumPredict = options.MaxTokens,
                    TopP = options.TopP
                }
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            System.IO.Stream? stream = null;
            System.IO.StreamReader? reader = null;

            try
            {
                response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cancellationToken);
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

                    var chunkResponse = JsonSerializer.Deserialize<OllamaChatResponse>(line, _jsonOptions);
                    if (chunkResponse?.Message != null)
                    {
                        yield return new LLMResponseChunk
                        {
                            Content = chunkResponse.Message.Content,
                            IsFinal = chunkResponse.Done,
                            Metadata = new Dictionary<string, object>
                            {
                                ["done"] = chunkResponse.Done
                            }
                        };
                    }

                    if (chunkResponse?.Done == true)
                    {
                        break;
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

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        #region DTOs

        private class OllamaModelsResponse
        {
            [JsonPropertyName("models")]
            public List<OllamaModel> Models { get; set; } = new();
        }

        private class OllamaModel
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("modified_at")]
            public string? ModifiedAt { get; set; }
        }

        private class OllamaChatRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("messages")]
            public List<OllamaMessage> Messages { get; set; } = new();

            [JsonPropertyName("stream")]
            public bool Stream { get; set; }

            [JsonPropertyName("options")]
            public OllamaOptions? Options { get; set; }
        }

        private class OllamaMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
        }

        private class OllamaOptions
        {
            [JsonPropertyName("temperature")]
            public float Temperature { get; set; }

            [JsonPropertyName("num_predict")]
            public int? NumPredict { get; set; }

            [JsonPropertyName("top_p")]
            public float? TopP { get; set; }

            [JsonPropertyName("stop")]
            public List<string>? Stop { get; set; }
        }

        private class OllamaChatResponse
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("message")]
            public OllamaMessage? Message { get; set; }

            [JsonPropertyName("done")]
            public bool Done { get; set; }

            [JsonPropertyName("total_duration")]
            public long? TotalDuration { get; set; }

            [JsonPropertyName("load_duration")]
            public long? LoadDuration { get; set; }

            [JsonPropertyName("prompt_eval_count")]
            public int? PromptEvalCount { get; set; }

            [JsonPropertyName("eval_count")]
            public int? EvalCount { get; set; }

            [JsonPropertyName("eval_duration")]
            public long? EvalDuration { get; set; }
        }

        #endregion
    }
}
