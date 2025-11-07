// File: JunoSidebar.Wpf/Services/LLM/Providers/AnthropicProvider.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Anthropic.SDK;
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

            _client = new AnthropicClient(configuration.ApiKey);
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
                    Id = "claude-3-5-sonnet-20241022",
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
                    Id = "claude-3-opus-20240229",
                    Name = "Claude 3 Opus",
                    Description = "Most powerful model for complex tasks",
                    ContextLength = 200000,
                    SupportsStreaming = true,
                    SupportsFunctionCalling = true,
                    SupportsVision = true
                },
                new ModelInfo
                {
                    Id = "claude-3-haiku-20240307",
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
                    new Message
                    {
                        Role = RoleType.User,
                        Content = new List<ContentBase> { new TextContent { Text = "Hi" } }
                    }
                };

                var parameters = new MessageParameters
                {
                    Messages = messages,
                    MaxTokens = 10,
                    Model = "claude-3-haiku-20240307",
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
                StopSequences = options.StopSequences?.ToArray()
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

                var content = string.Join("", response.Content.OfType<TextContent>().Select(c => c.Text));

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
                        ["role"] = response.Role.ToString()
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
                StopSequences = options.StopSequences?.ToArray()
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
                // Check if this is a content delta with text
                if (response.Delta != null)
                {
                    // Try to get text from the delta
                    var deltaText = TryGetTextFromDelta(response.Delta);
                    if (!string.IsNullOrEmpty(deltaText))
                    {
                        yield return new LLMResponseChunk
                        {
                            Content = deltaText,
                            IsFinal = false,
                            Metadata = new Dictionary<string, object>
                            {
                                ["type"] = response.Type ?? string.Empty
                            }
                        };
                    }
                }

                // Check for stream end
                if (response.Type == "message_stop" || response.Type == "content_block_stop")
                {
                    yield return new LLMResponseChunk
                    {
                        Content = string.Empty,
                        IsFinal = true,
                        Metadata = new Dictionary<string, object>
                        {
                            ["type"] = response.Type
                        }
                    };
                    break;
                }
            }
        }

        private string? TryGetTextFromDelta(object delta)
        {
            // The delta object might have different types
            // Try to access it as a TextContent or similar type
            if (delta is TextContent textContent)
            {
                return textContent.Text;
            }

            // Try reflection to get Text property
            try
            {
                var textProperty = delta.GetType().GetProperty("Text");
                if (textProperty != null)
                {
                    return textProperty.GetValue(delta)?.ToString();
                }
            }
            catch
            {
                // Ignore reflection errors
            }

            return null;
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

                anthropicMessages.Add(new Message
                {
                    Role = role,
                    Content = new List<ContentBase> { new TextContent { Text = msg.Content } }
                });
            }

            return anthropicMessages;
        }
    }
}
