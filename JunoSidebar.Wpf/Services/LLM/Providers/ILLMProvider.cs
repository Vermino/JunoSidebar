// File: JunoSidebar.Wpf/Services/LLM/Providers/ILLMProvider.cs

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    /// <summary>
    /// Base interface for all LLM providers (Anthropic, OpenAI, LM Studio, Ollama, etc.)
    /// </summary>
    public interface ILLMProvider
    {
        /// <summary>
        /// Provider identifier (e.g., "anthropic", "openai", "lmstudio")
        /// </summary>
        string ProviderId { get; }

        /// <summary>
        /// Human-readable provider name
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Provider capabilities (streaming, function calling, vision, etc.)
        /// </summary>
        ModelCapabilities Capabilities { get; }

        /// <summary>
        /// Initialize the provider with configuration
        /// </summary>
        Task InitializeAsync(ProviderConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get list of available models from this provider
        /// </summary>
        Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Test connection to the provider
        /// </summary>
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a non-streaming chat completion
        /// </summary>
        Task<LLMResponse> GetChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            LLMRequestOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get a streaming chat completion
        /// </summary>
        IAsyncEnumerable<LLMResponseChunk> GetStreamingChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            LLMRequestOptions? options = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Model information returned by providers
    /// </summary>
    public class ModelInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? ContextLength { get; set; }
        public bool SupportsStreaming { get; set; } = true;
        public bool SupportsFunctionCalling { get; set; } = false;
        public bool SupportsVision { get; set; } = false;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Request options for LLM completions
    /// </summary>
    public class LLMRequestOptions
    {
        public float Temperature { get; set; } = 0.7f;
        public int? MaxTokens { get; set; }
        public float? TopP { get; set; }
        public float? FrequencyPenalty { get; set; }
        public float? PresencePenalty { get; set; }
        public List<string>? StopSequences { get; set; }
        public string? SystemPrompt { get; set; }
        public Dictionary<string, object>? AdditionalParameters { get; set; }
    }

    /// <summary>
    /// Model capabilities supported by a provider
    /// </summary>
    public class ModelCapabilities
    {
        public bool SupportsStreaming { get; set; } = true;
        public bool SupportsFunctionCalling { get; set; } = false;
        public bool SupportsVision { get; set; } = false;
        public bool SupportsSystemPrompt { get; set; } = true;
        public bool SupportsTemperature { get; set; } = true;
        public bool SupportsTopP { get; set; } = true;
        public bool SupportsStopSequences { get; set; } = true;
        public int DefaultMaxTokens { get; set; } = 4096;
        public int MaxContextLength { get; set; } = 32000;
    }
}
