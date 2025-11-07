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

            var clientOptions = new OpenAIClientOptions
            {
                ApiKey = configuration.ApiKey,
                Organization = configuration.Organization
            };

            _client = new OpenAIClient(clientOptions);

            Debug.WriteLine("OpenAI provider initialized successfully");
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            // Return known GPT models since OpenAI API structure has changed
            return GetKnownModels();
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_client == null)
                {
                    return false;
                }

                // Try a simple completion to test
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage("Test"),
                    new UserChatMessage("Hi")
                };

                var chatClient = _client.GetChatClient("gpt-3.5-turbo");
                var completion = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);

                return completion?.Value != null;
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
            var chatClient = _client.GetChatClient(model);

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = options.Temperature,
                TopP = options.TopP,
                MaxOutputTokenCount = options.MaxTokens ?? _configuration?.DefaultMaxTokens,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty
            };

            if (options.StopSequences != null)
            {
                foreach (var stop in options.StopSequences)
                {
                    chatOptions.StopSequences.Add(stop);
                }
            }

            try
            {
                var completion = await chatClient.CompleteChatAsync(chatMessages, chatOptions, cancellationToken);

                if (completion?.Value == null)
                {
                    throw new Exception("No response from OpenAI");
                }

                var result = completion.Value;
                var content = result.Content.FirstOrDefault()?.Text ?? string.Empty;

                return new LLMResponse
                {
                    Content = content,
                    ModelId = result.Model ?? model,
                    PromptTokens = result.Usage?.InputTokenCount,
                    CompletionTokens = result.Usage?.OutputTokenCount,
                    TotalTokens = result.Usage?.TotalTokenCount,
                    Metadata = new Dictionary<string, object>
                    {
                        ["id"] = result.Id ?? string.Empty,
                        ["finish_reason"] = result.FinishReason.ToString() ?? string.Empty,
                        ["created"] = result.CreatedAt
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
            var chatClient = _client.GetChatClient(model);

            var chatOptions = new ChatCompletionOptions
            {
                Temperature = options.Temperature,
                TopP = options.TopP,
                MaxOutputTokenCount = options.MaxTokens ?? _configuration?.DefaultMaxTokens,
                FrequencyPenalty = options.FrequencyPenalty,
                PresencePenalty = options.PresencePenalty
            };

            if (options.StopSequences != null)
            {
                foreach (var stop in options.StopSequences)
                {
                    chatOptions.StopSequences.Add(stop);
                }
            }

            await foreach (var update in chatClient.CompleteChatStreamingAsync(chatMessages, chatOptions, cancellationToken))
            {
                if (update.ContentUpdate != null && update.ContentUpdate.Count > 0)
                {
                    foreach (var contentPart in update.ContentUpdate)
                    {
                        if (!string.IsNullOrEmpty(contentPart.Text))
                        {
                            yield return new LLMResponseChunk
                            {
                                Content = contentPart.Text,
                                IsFinal = update.FinishReason != null,
                                Metadata = new Dictionary<string, object>
                                {
                                    ["finish_reason"] = update.FinishReason?.ToString() ?? string.Empty
                                }
                            };
                        }
                    }
                }
                
                if (update.FinishReason != null)
                {
                    yield return new LLMResponseChunk
                    {
                        Content = string.Empty,
                        IsFinal = true,
                        Metadata = new Dictionary<string, object>
                        {
                            ["finish_reason"] = update.FinishReason.ToString()
                        }
                    };
                }
            }
        }

        private List<ChatMessage> ConvertMessages(IEnumerable<LLMMessage> messages)
        {
            var chatMessages = new List<ChatMessage>();

            foreach (var msg in messages)
            {
                ChatMessage chatMessage = msg.Role.ToLowerInvariant() switch
                {
                    "system" => new SystemChatMessage(msg.Content),
                    "user" => new UserChatMessage(msg.Content),
                    "assistant" => new AssistantChatMessage(msg.Content),
                    _ => new UserChatMessage(msg.Content)
                };

                chatMessages.Add(chatMessage);
            }

            return chatMessages;
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
