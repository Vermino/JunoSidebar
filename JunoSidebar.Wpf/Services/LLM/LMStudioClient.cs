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

namespace JunoSidebar.Wpf.Services.LLM
{
    public class LMStudioClient : ILLMClient
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _baseUrl;
        public string ProviderName => "LM Studio";

        public LMStudioClient(string baseUrl = "http://localhost:1234/v1")
        {
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public async Task<IEnumerable<string>> GetAvailableModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<ModelsResponse>($"{_baseUrl}/models", _jsonOptions);
                return response?.Data?.Select(m => m.Id) ?? Enumerable.Empty<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving models: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public async Task<LLMResponse> GetChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            float temperature = 0.7f,
            int? maxTokens = null,
            CancellationToken cancellationToken = default)
        {
            var request = new ChatCompletionRequest
            {
                Model = model,
                Messages = messages.Select(m => new Message
                {
                    Role = m.Role,
                    Content = m.Content,
                    Name = m.Name
                }).ToList(),
                Temperature = temperature,
                MaxTokens = maxTokens
            };
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
            try
            {
                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", requestContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                var completionResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseString, _jsonOptions);
                if (completionResponse == null || completionResponse.Choices == null || completionResponse.Choices.Count == 0)
                {
                    throw new Exception("Invalid response from LM Studio");
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
                        ["created"] = completionResponse.Created,
                        ["object"] = completionResponse.Object
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting chat completion: {ex.Message}");
                throw;
            }
        }

        public async IAsyncEnumerable<LLMResponseChunk> GetStreamingChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            float temperature = 0.7f,
            int? maxTokens = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = new ChatCompletionRequest
            {
                Model = model,
                Messages = messages.Select(m => new Message
                {
                    Role = m.Role,
                    Content = m.Content,
                    Name = m.Name
                }).ToList(),
                Temperature = temperature,
                MaxTokens = maxTokens,
                Stream = true
            };
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
            
            HttpResponseMessage? response = null;
            System.IO.Stream? stream = null;
            System.IO.StreamReader? reader = null;
            
            try
            {
                response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", requestContent, cancellationToken);
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
                        
                    ChatCompletionChunkResponse? chunkResponse = null;
                    
                    try
                    {
                        chunkResponse = JsonSerializer.Deserialize<ChatCompletionChunkResponse>(line, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        Console.WriteLine($"Failed to parse streaming response: {line}");
                        continue;
                    }
                    
                    if (chunkResponse == null || chunkResponse.Choices == null || chunkResponse.Choices.Count == 0)
                    {
                        continue;
                    }
                    
                    var choice = chunkResponse.Choices[0];
                    var chunkContent = choice.Delta?.Content ?? string.Empty;
                    
                    yield return new LLMResponseChunk
                    {
                        Content = chunkContent,
                        IsFinal = choice.FinishReason != null,
                        ChoiceIndex = 0,
                        Metadata = new Dictionary<string, object>
                        {
                            ["id"] = chunkResponse.Id,
                            ["created"] = chunkResponse.Created,
                            ["object"] = chunkResponse.Object,
                            ["finish_reason"] = choice.FinishReason ?? string.Empty
                        }
                    };
                }
            }
            finally
            {
                if (reader != null) 
                {
                    reader.Dispose();
                }
                if (stream != null) 
                {
                    stream.Dispose();
                }
                if (response != null) 
                {
                    response.Dispose();
                }
            }
        }

        private class ModelsResponse
        {
            [JsonPropertyName("data")]
            public List<Model>? Data { get; set; }
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
            public List<Message> Messages { get; set; } = new List<Message>();
            [JsonPropertyName("temperature")]
            public float Temperature { get; set; } = 0.7f;
            [JsonPropertyName("max_tokens")]
            public int? MaxTokens { get; set; }
            [JsonPropertyName("stream")]
            public bool Stream { get; set; }
        }

        private class Message
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;
            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private class ChatCompletionResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
            [JsonPropertyName("object")]
            public string Object { get; set; } = string.Empty;
            [JsonPropertyName("created")]
            public long Created { get; set; }
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;
            [JsonPropertyName("choices")]
            public List<Choice>? Choices { get; set; }
            [JsonPropertyName("usage")]
            public Usage? Usage { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }
            [JsonPropertyName("message")]
            public Message Message { get; set; } = new Message();
            [JsonPropertyName("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class ChatCompletionChunkResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
            [JsonPropertyName("object")]
            public string Object { get; set; } = string.Empty;
            [JsonPropertyName("created")]
            public long Created { get; set; }
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;
            [JsonPropertyName("choices")]
            public List<ChunkChoice>? Choices { get; set; }
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
            [JsonPropertyName("role")]
            public string? Role { get; set; }
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
    }
}