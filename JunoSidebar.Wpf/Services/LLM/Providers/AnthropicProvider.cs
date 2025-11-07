// File: JunoSidebar.Wpf/Services/LLM/Providers/AnthropicProvider.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    public class AnthropicProvider : ILLMProvider
    {
        private AnthropicClient? _client;
        private ProviderConfiguration? _configuration;

        public string ProviderId => "anthropic";
        public string ProviderName => "Anthropic (Claude)";

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
            MaxContextLength = 200000
        };

        public Task InitializeAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _configuration = configuration;

            if (string.IsNullOrEmpty(configuration.ApiKey))
            {
                throw new ArgumentException("Anthropic API key is required");
            }

            _client = new AnthropicClient(new APIAuthentication(configuration.ApiKey));
            Debug.WriteLine("Anthropic provider initialized successfully");

            return Task.CompletedTask;
        }

        public Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            // Anthropic doesn't provide a models endpoint, so we return known models
            var models = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = AnthropicModels.Claude3_5Sonnet,
                    Name = "Claude 3.5 Sonnet",
                    Description = "Most intelligent model",
                    ContextLength = 200000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "claude-sonnet-4-20250514",
                    Name = "Claude Sonnet 4",
                    Description = "Latest and most capable model",
                    ContextLength = 200000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = AnthropicModels.Claude3Opus,
                    Name = "Claude 3 Opus",
                    Description = "Most powerful model for complex tasks",
                    ContextLength = 200000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = AnthropicModels.Claude3Haiku,
                    Name = "Claude 3 Haiku",
                    Description = "Fastest and most compact model",
                    ContextLength = 200000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                }
            };

            return Task.FromResult<IEnumerable<ModelInfo>>(models);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_client == null)
                {
                    return false;
                }

                // Try a simple message to test the connection
                var messages = new List<Message>
                {
                    new Message(RoleType.User, "Hi")
                };

                var parameters = new MessageParameters
                {
                    Messages = messages,
                    MaxTokens = 10,
                    Model = AnthropicModels.Claude3Haiku, // Use cheapest model for testing
                    Stream = false,
                    Temperature = 1.0m
                };

                var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
                return response?.Content?.Any() == true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Anthropic connection test failed: {ex.Message}");
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

            var anthropicMessages = ConvertMessages(messages, out var systemPrompt);

            var parameters = new MessageParameters
            {
                Messages = anthropicMessages,
                MaxTokens = options.MaxTokens ?? _configuration?.DefaultMaxTokens ?? 4096,
                Model = model,
                Stream = false,
                Temperature = (decimal)options.Temperature,
                TopP = options.TopP.HasValue ? (decimal)options.TopP.Value : null,
                StopSequences = options.StopSequences
            };

            // Add system prompt if provided
            if (!string.IsNullOrEmpty(systemPrompt) || !string.IsNullOrEmpty(options.SystemPrompt))
            {
                parameters.System = new List<SystemMessage>
                {
                    new SystemMessage(options.SystemPrompt ?? systemPrompt ?? string.Empty)
                };
            }

            try
            {
                var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

                if (response?.Content == null || !response.Content.Any())
                {
                    throw new Exception("No content in response");
                }

                var content = string.Join("", response.Content.Select(c => c.Text));

                return new LLMResponse
                {
                    Content = content,
                    ModelId = response.Model ?? model,
                    PromptTokens = response.Usage?.InputTokens,
                    CompletionTokens = response.Usage?.OutputTokens,
                    TotalTokens = (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0),
                    Metadata = new Dictionary<string, object>
                    {
                        ["id"] = response.Id ?? string.Empty,
                        ["stop_reason"] = response.StopReason ?? string.Empty,
                        ["role"] = response.Role ?? string.Empty
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Anthropic completion error: {ex.Message}");
                throw new Exception($"Failed to get completion from Anthropic: {ex.Message}", ex);
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

            var anthropicMessages = ConvertMessages(messages, out var systemPrompt);

            var parameters = new MessageParameters
            {
                Messages = anthropicMessages,
                MaxTokens = options.MaxTokens ?? _configuration?.DefaultMaxTokens ?? 4096,
                Model = model,
                Stream = true,
                Temperature = (decimal)options.Temperature,
                TopP = options.TopP.HasValue ? (decimal)options.TopP.Value : null,
                StopSequences = options.StopSequences
            };

            // Add system prompt if provided
            if (!string.IsNullOrEmpty(systemPrompt) || !string.IsNullOrEmpty(options.SystemPrompt))
            {
                parameters.System = new List<SystemMessage>
                {
                    new SystemMessage(options.SystemPrompt ?? systemPrompt ?? string.Empty)
                };
            }

            await foreach (var response in _client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken))
            {
                if (response.Delta?.Text != null)
                {
                    yield return new LLMResponseChunk
                    {
                        Content = response.Delta.Text,
                        IsFinal = response.Delta.StopReason != null,
                        Metadata = new Dictionary<string, object>
                        {
                            ["type"] = response.Type ?? string.Empty,
                            ["stop_reason"] = response.Delta.StopReason ?? string.Empty
                        }
                    };
                }
                else if (response.Type == "message_stop")
                {
                    yield return new LLMResponseChunk
                    {
                        Content = string.Empty,
                        IsFinal = true,
                        Metadata = new Dictionary<string, object>
                        {
                            ["type"] = "message_stop"
                        }
                    };
                }
            }
        }

        private List<Message> ConvertMessages(IEnumerable<LLMMessage> messages, out string? systemPrompt)
        {
            systemPrompt = null;
            var anthropicMessages = new List<Message>();

            foreach (var msg in messages)
            {
                if (msg.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                {
                    // Anthropic handles system messages separately
                    systemPrompt = msg.Content;
                    continue;
                }

                var role = msg.Role.ToLowerInvariant() switch
                {
                    "user" => RoleType.User,
                    "assistant" => RoleType.Assistant,
                    _ => RoleType.User
                };

                anthropicMessages.Add(new Message(role, msg.Content));
            }

            return anthropicMessages;
        }
    }
}
