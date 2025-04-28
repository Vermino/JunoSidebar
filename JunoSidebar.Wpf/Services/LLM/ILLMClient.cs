// File: JunoSidebar/JunoSidebar.Wpf/Services/LLM/ILLMClient.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.LLM
{
    /// <summary>
    /// Interface for LLM clients that defines common operations for interacting with
    /// different LLM providers.
    /// </summary>
    public interface ILLMClient
    {
        /// <summary>
        /// Gets the available models from the LLM provider.
        /// </summary>
        /// <returns>A list of available model identifiers.</returns>
        Task<IEnumerable<string>> GetAvailableModelsAsync();

        /// <summary>
        /// Sends a chat completion request to the LLM provider.
        /// </summary>
        /// <param name="messages">The list of messages in the conversation.</param>
        /// <param name="model">The model identifier to use.</param>
        /// <param name="temperature">The temperature parameter for controlling randomness.</param>
        /// <param name="maxTokens">The maximum number of tokens to generate.</param>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <returns>The response from the LLM.</returns>
        Task<LLMResponse> GetChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            float temperature = 0.7f,
            int? maxTokens = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a streaming chat completion request to the LLM provider.
        /// </summary>
        /// <param name="messages">The list of messages in the conversation.</param>
        /// <param name="model">The model identifier to use.</param>
        /// <param name="temperature">The temperature parameter for controlling randomness.</param>
        /// <param name="maxTokens">The maximum number of tokens to generate.</param>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <returns>An asynchronous stream of response chunks from the LLM.</returns>
        IAsyncEnumerable<LLMResponseChunk> GetStreamingChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            float temperature = 0.7f,
            int? maxTokens = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the provider name for this LLM client.
        /// </summary>
        string ProviderName { get; }
    }

    /// <summary>
    /// Represents a message in a conversation with an LLM.
    /// </summary>
    public class LLMMessage
    {
        /// <summary>
        /// The role of the message sender (e.g., "system", "user", "assistant").
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// The content of the message.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Optional name of the sender for multi-user conversations.
        /// </summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Represents a response from an LLM.
    /// </summary>
    public class LLMResponse
    {
        /// <summary>
        /// The generated content from the LLM.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// The ID of the model used to generate the response.
        /// </summary>
        public string ModelId { get; set; } = string.Empty;

        /// <summary>
        /// The number of prompt tokens processed.
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// The number of completion tokens generated.
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// The total number of tokens processed.
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Any additional provider-specific data.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Represents a chunk of a streaming response from an LLM.
    /// </summary>
    public class LLMResponseChunk
    {
        /// <summary>
        /// The content of this chunk.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Indicates if this is the final chunk in the response.
        /// </summary>
        public bool IsFinal { get; set; }

        /// <summary>
        /// Optional choice index for multiple completions.
        /// </summary>
        public int? ChoiceIndex { get; set; }

        /// <summary>
        /// Any additional provider-specific data.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}