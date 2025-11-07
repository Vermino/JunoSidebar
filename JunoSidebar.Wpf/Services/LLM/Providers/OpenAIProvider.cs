// File: JunoSidebar.Wpf/Services/LLM/Providers/OpenAIProvider.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    public class OpenAIProvider : ILLMProvider
    {
        private OpenAIClient? _client;
        private ProviderConfiguration? _configuration;

        public string ProviderId => "openai";
        public string ProviderName => "OpenAI (ChatGPT)";

        public ModelCapabilities Capabilities => new ModelCapabilities
        {
            SupportsStreaming = true,
            SupportsFunctionCalling = true,
            SupportsVision = true,
            SupportsSystemPrompt = true,
            SupportsTemperature = true,
            SupportsTopP = true,
            SupportsStopSequences = true,
            DefaultMaxTokens = 4096,
            MaxContextLength = 128000
        };

        public Task InitializeAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _configuration = configuration;

            if (string.IsNullOrEmpty(configuration.ApiKey))
            {
                throw new ArgumentException("OpenAI API key is required");
            }

            var auth = new OpenAIAuthentication(configuration.ApiKey, configuration.Organization);
            _client = new OpenAIClient(auth);

            Debug.WriteLine("OpenAI provider initialized successfully");
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            if (_client == null)
            {
                return Enumerable.Empty<ModelInfo>();
            }

            try
            {
                var models = await _client.ModelsEndpoint.GetModelsAsync(cancellationToken);
                return models
                    .Where(m => m.Id.Contains("gpt"))
                    .Select(m => new ModelInfo
                    {
                        Id = m.Id,
                        Name = m.Id,
                        Description = m.OwnedBy,
                        SupportsStreaming = true,
                        Metadata = new Dictionary<string, object>
                        {
                            ["created"] = m.CreatedAt,
                            ["owned_by"] = m.OwnedBy ?? string.Empty
                        }
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching OpenAI models: {ex.Message}");

                // Return known models as fallback
                return GetKnownModels();
            }
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_client == null)
                {
                    return false;
                }

                // Try to list models to test connection
                await _client.ModelsEndpoint.GetModelsAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenAI connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task<LLMResponse> GetChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            LLMRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Provider not initialized");
            }

            options ??= new LLMRequestOptions();

            var chatMessages = ConvertMessages(messages);

            var chatRequest = new ChatRequest(
                messages: chatMessages,
                model: model,
                temperature: options.Temperature,
                topP: options.TopP,
                maxTokens: options.MaxTokens ?? _configuration?.DefaultMaxTokens,
                frequencyPenalty: options.FrequencyPenalty,
                presencePenalty: options.PresencePenalty,
                stopSequences: options.StopSequences
            );

            try
            {
                var response = await _client.ChatEndpoint.GetCompletionAsync(chatRequest, cancellationToken);

                if (response?.FirstChoice == null)
                {
                    throw new Exception("No response from OpenAI");
                }

                return new LLMResponse
                {
                    Content = response.FirstChoice.Message.Content?.ToString() ?? string.Empty,
                    ModelId = response.Model ?? model,
                    PromptTokens = response.Usage?.PromptTokens,
                    CompletionTokens = response.Usage?.CompletionTokens,
                    TotalTokens = response.Usage?.TotalTokens,
                    Metadata = new Dictionary<string, object>
                    {
                        ["id"] = response.Id ?? string.Empty,
                        ["finish_reason"] = response.FirstChoice.FinishReason ?? string.Empty,
                        ["created"] = response.CreatedAt
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenAI completion error: {ex.Message}");
                throw new Exception($"Failed to get completion from OpenAI: {ex.Message}", ex);
            }
        }

        public async IAsyncEnumerable<LLMResponseChunk> GetStreamingChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            LLMRequestOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Provider not initialized");
            }

            options ??= new LLMRequestOptions();

            var chatMessages = ConvertMessages(messages);

            var chatRequest = new ChatRequest(
                messages: chatMessages,
                model: model,
                temperature: options.Temperature,
                topP: options.TopP,
                maxTokens: options.MaxTokens ?? _configuration?.DefaultMaxTokens,
                frequencyPenalty: options.FrequencyPenalty,
                presencePenalty: options.PresencePenalty,
                stopSequences: options.StopSequences
            );

            await foreach (var response in _client.ChatEndpoint.StreamCompletionAsync(chatRequest, cancellationToken))
            {
                if (response?.FirstChoice?.Delta?.Content != null)
                {
                    yield return new LLMResponseChunk
                    {
                        Content = response.FirstChoice.Delta.Content.ToString(),
                        IsFinal = response.FirstChoice.FinishReason != null,
                        ChoiceIndex = response.FirstChoice.Index,
                        Metadata = new Dictionary<string, object>
                        {
                            ["id"] = response.Id ?? string.Empty,
                            ["finish_reason"] = response.FirstChoice.FinishReason ?? string.Empty
                        }
                    };
                }
                else if (response?.FirstChoice?.FinishReason != null)
                {
                    yield return new LLMResponseChunk
                    {
                        Content = string.Empty,
                        IsFinal = true,
                        ChoiceIndex = response.FirstChoice.Index,
                        Metadata = new Dictionary<string, object>
                        {
                            ["id"] = response.Id ?? string.Empty,
                            ["finish_reason"] = response.FirstChoice.FinishReason
                        }
                    };
                }
            }
        }

        private List<Message> ConvertMessages(IEnumerable<LLMMessage> messages)
        {
            return messages.Select(m =>
            {
                var role = m.Role.ToLowerInvariant() switch
                {
                    "system" => Role.System,
                    "user" => Role.User,
                    "assistant" => Role.Assistant,
                    _ => Role.User
                };

                return new Message(role, m.Content);
            }).ToList();
        }

        private IEnumerable<ModelInfo> GetKnownModels()
        {
            return new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "gpt-4-turbo",
                    Name = "GPT-4 Turbo",
                    Description = "Most capable GPT-4 model",
                    ContextLength = 128000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "gpt-4",
                    Name = "GPT-4",
                    Description = "Standard GPT-4",
                    ContextLength = 8192,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true
                },
                new ModelInfo
                {
                    Id = "gpt-3.5-turbo",
                    Name = "GPT-3.5 Turbo",
                    Description = "Fast and efficient",
                    ContextLength = 16385,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true
                },
                new ModelInfo
                {
                    Id = "gpt-4o",
                    Name = "GPT-4o",
                    Description = "Latest multimodal model",
                    ContextLength = 128000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                }
            };
        }
    }
}
