// File: JunoSidebar.Wpf\Services\LLM\ILLMClient.cs

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.LLM
{
    public interface ILLMClient
    {
        Task<IEnumerable<string>> GetAvailableModelsAsync();
        
        Task<bool> TestConnectionAsync();
        
        Task<LLMResponse> GetChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            float temperature = 0.7f,
            int? maxTokens = null,
            CancellationToken cancellationToken = default);
            
        IAsyncEnumerable<LLMResponseChunk> GetStreamingChatCompletionAsync(
            IEnumerable<LLMMessage> messages,
            string model,
            float temperature = 0.7f,
            int? maxTokens = null,
            CancellationToken cancellationToken = default);
            
        string ProviderName { get; }
    }

    public class LLMMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    public class LLMResponse
    {
        public string Content { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class LLMResponseChunk
    {
        public string Content { get; set; } = string.Empty;
        public bool IsFinal { get; set; }
        public int? ChoiceIndex { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}