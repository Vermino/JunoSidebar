// File: JunoSidebar.Wpf/Services/LLM/LMStudioClient.cs

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

namespace JunoSidebar.Wpf.Services.LLM
{
    public class LMStudioClient : ILLMClient
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _baseUrl;
        private readonly TimeSpan _connectionTestTimeout = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
        private bool _isConnectionValid = false;
        private DateTime _lastConnectionCheck = DateTime.MinValue;
        private const int CACHE_EXPIRY_SECONDS = 60;

        public string ProviderName => "LM Studio";

        public LMStudioClient(string baseUrl = "http://localhost:1234/v1")
        {
            _baseUrl = baseUrl;
            
            // Create HTTP client with appropriate timeout
            _httpClient = new HttpClient();
            _httpClient.Timeout = _defaultTimeout;
            
            // Configure JSON serialization options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };
            
            Debug.WriteLine($"LMStudioClient initialized with base URL: {_baseUrl}");
        }

        public async Task<IEnumerable<string>> GetAvailableModelsAsync()
        {
            try
            {
                Debug.WriteLine("Getting available models from LM Studio...");
                
                // Check connection first to avoid long timeouts
                if (!await EnsureConnectionValidAsync())
                {
                    Debug.WriteLine("Connection to LM Studio is not valid, returning fallback model list");
                    return new[] { "local-model" };
                }
                
                var response = await _httpClient.GetAsync($"{_baseUrl}/models");
                response.EnsureSuccessStatusCode();
                
                var responseString = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Models response: {responseString}");
                
                // Try to parse the models response
                try
                {
                    var modelsResponse = JsonSerializer.Deserialize<ModelsResponse>(responseString, _jsonOptions);
                    if (modelsResponse?.Data == null || modelsResponse.Data.Count == 0)
                    {
                        Debug.WriteLine("No models found in the response");
                        return new[] { "local-model" };
                    }
                    
                    var modelIds = modelsResponse.Data
                        .Where(m => !string.IsNullOrEmpty(m.Id))
                        .Select(m => m.Id)
                        .ToList();
                    
                    if (modelIds.Count == 0)
                    {
                        Debug.WriteLine("Parsed response contained no valid model IDs");
                        return new[] { "local-model" };
                    }
                    
                    Debug.WriteLine($"Found {modelIds.Count} models: {string.Join(", ", modelIds)}");
                    return modelIds;
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"Error parsing models JSON: {ex.Message}");
                    Debug.WriteLine($"Response was: {responseString}");
                    
                    // Try alternative parsing approach
                    try
                    {
                        // Some LM Studio versions might have a different response format
                        if (responseString.Contains("\"id\"") && responseString.Contains("\"object\""))
                        {
                            Debug.WriteLine("Trying alternative parsing approach...");
                            var jsonDoc = JsonDocument.Parse(responseString);
                            
                            if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement) && 
                                dataElement.ValueKind == JsonValueKind.Array)
                            {
                                var models = new List<string>();
                                foreach (var modelElement in dataElement.EnumerateArray())
                                {
                                    if (modelElement.TryGetProperty("id", out var idElement))
                                    {
                                        models.Add(idElement.GetString());
                                    }
                                }
                                
                                if (models.Count > 0)
                                {
                                    Debug.WriteLine($"Alternative parsing found {models.Count} models");
                                    return models;
                                }
                            }
                        }
                    }
                    catch (Exception altEx)
                    {
                        Debug.WriteLine($"Alternative parsing also failed: {altEx.Message}");
                    }
                    
                    return new[] { "local-model" };
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"HTTP error retrieving models: {ex.Message}");
                _isConnectionValid = false;
                return new[] { "local-model" };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception retrieving models: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new[] { "local-model" };
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                Debug.WriteLine("Testing connection to LM Studio...");
                _isConnectionValid = false;
                
                using var cts = new CancellationTokenSource(_connectionTestTimeout);
                
                // Try to get the models endpoint - this is a lightweight test
                var response = await _httpClient.GetAsync($"{_baseUrl}/models", cts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Connection test failed: {response.StatusCode} - {response.ReasonPhrase}");
                    return false;
                }
                
                var responseString = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Connection test response: {responseString.Substring(0, Math.Min(100, responseString.Length))}...");
                
                // Check if the response looks like proper JSON
                try
                {
                    var jsonDoc = JsonDocument.Parse(responseString);
                    _isConnectionValid = true;
                    _lastConnectionCheck = DateTime.Now;
                    return true;
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"Connection test received invalid JSON: {ex.Message}");
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("Connection test timed out");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Connection test exception: {ex.Message}");
                return false;
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
                // Ensure we have a valid connection before proceeding
                if (!await EnsureConnectionValidAsync())
                {
                    throw new Exception("Could not establish connection to LM Studio");
                }
                
                Debug.WriteLine($"Sending chat completion request to {_baseUrl}/chat/completions");
                Debug.WriteLine($"Request payload: {json}");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", requestContent, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    Debug.WriteLine($"Error response: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"LM Studio request failed with status code {response.StatusCode}: {errorContent}");
                }
                
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                Debug.WriteLine($"Response received. Length: {responseString.Length} characters");
                
                var completionResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseString, _jsonOptions);
                if (completionResponse == null || completionResponse.Choices == null || completionResponse.Choices.Count == 0)
                {
                    throw new Exception("Invalid response from LM Studio: No completion choices returned");
                }
                
                Debug.WriteLine("Successfully processed chat completion response");
                
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
            catch (TaskCanceledException)
            {
                throw new TimeoutException("The request to LM Studio API timed out.");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"HTTP error getting chat completion: {ex.Message}");
                _isConnectionValid = false;
                throw new Exception($"Communication error with LM Studio: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting chat completion: {ex.Message}");
                throw new Exception($"Failed to get completion from LM Studio: {ex.Message}", ex);
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
            
            HttpResponseMessage? response = null;
            System.IO.Stream? stream = null;
            System.IO.StreamReader? reader = null;
            
            try
            {
                // Ensure we have a valid connection before proceeding
                if (!await EnsureConnectionValidAsync())
                {
                    throw new Exception("Could not establish connection to LM Studio");
                }
                
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
                
                Debug.WriteLine($"Sending streaming chat completion request to {_baseUrl}/chat/completions");
                
                response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", requestContent, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    Debug.WriteLine($"Streaming error response: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"LM Studio streaming request failed with status code {response.StatusCode}: {errorContent}");
                }
                
                stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                reader = new System.IO.StreamReader(stream);
                
                Debug.WriteLine("Stream connection established, beginning to read response chunks");
            }
            catch (Exception ex)
            {
                reader?.Dispose();
                stream?.Dispose();
                response?.Dispose();
                
                Debug.WriteLine($"Error initializing streaming completion: {ex.Message}");
                throw;
            }
            
            try
            {
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
                        Debug.WriteLine("Streaming completion finished");
                        break;
                    }
                    
                    LLMResponseChunk? chunk = null;
                    try
                    {
                        chunk = ParseChunkResponse(line);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error parsing chunk: {ex.Message}");
                        continue;
                    }
                    
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
                Debug.WriteLine("Streaming resources disposed");
            }
        }

        private async Task<bool> EnsureConnectionValidAsync()
        {
            // If we've validated the connection recently, skip the check
            if (_isConnectionValid && (DateTime.Now - _lastConnectionCheck).TotalSeconds < CACHE_EXPIRY_SECONDS)
            {
                return true;
            }
            
            return await TestConnectionAsync();
        }

        private LLMResponseChunk? ParseChunkResponse(string line)
        {
            try
            {
                var chunkResponse = JsonSerializer.Deserialize<ChatCompletionChunkResponse>(line, _jsonOptions);
                if (chunkResponse == null || chunkResponse.Choices == null || chunkResponse.Choices.Count == 0)
                {
                    return null;
                }
                
                var choice = chunkResponse.Choices[0];
                var chunkContent = choice.Delta?.Content ?? string.Empty;
                
                return new LLMResponseChunk
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
            catch (JsonException ex)
            {
                Debug.WriteLine($"Failed to parse streaming response: {line}, Error: {ex.Message}");
                return null;
            }
        }

        private class ModelsResponse
        {
            [JsonPropertyName("data")]
            public List<Model>? Data { get; set; }
            
            [JsonPropertyName("object")]
            public string Object { get; set; } = string.Empty;
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