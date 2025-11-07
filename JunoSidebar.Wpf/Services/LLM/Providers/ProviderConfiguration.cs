// File: JunoSidebar.Wpf/Services/LLM/Providers/ProviderConfiguration.cs

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    /// <summary>
    /// Configuration for an LLM provider
    /// </summary>
    public class ProviderConfiguration
    {
        /// <summary>
        /// Provider identifier (e.g., "anthropic", "openai", "lmstudio")
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable provider name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether this provider is enabled
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// API key for the provider (if required)
        /// </summary>
        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }

        /// <summary>
        /// Base URL for the provider API
        /// </summary>
        [JsonPropertyName("baseUrl")]
        public string? BaseUrl { get; set; }

        /// <summary>
        /// Organization ID (for providers like OpenAI)
        /// </summary>
        [JsonPropertyName("organization")]
        public string? Organization { get; set; }

        /// <summary>
        /// Default model to use with this provider
        /// </summary>
        [JsonPropertyName("defaultModel")]
        public string? DefaultModel { get; set; }

        /// <summary>
        /// Default temperature setting
        /// </summary>
        [JsonPropertyName("defaultTemperature")]
        public float DefaultTemperature { get; set; } = 0.7f;

        /// <summary>
        /// Default max tokens
        /// </summary>
        [JsonPropertyName("defaultMaxTokens")]
        public int? DefaultMaxTokens { get; set; }

        /// <summary>
        /// Request timeout in seconds
        /// </summary>
        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Maximum retry attempts for failed requests
        /// </summary>
        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Custom headers to include in requests
        /// </summary>
        [JsonPropertyName("customHeaders")]
        public Dictionary<string, string>? CustomHeaders { get; set; }

        /// <summary>
        /// Provider-specific settings
        /// </summary>
        [JsonPropertyName("providerSettings")]
        public Dictionary<string, object>? ProviderSettings { get; set; }

        /// <summary>
        /// Priority for this provider (higher = higher priority)
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;
    }

    /// <summary>
    /// Collection of provider configurations with management methods
    /// </summary>
    public class ProvidersConfiguration
    {
        [JsonPropertyName("providers")]
        public List<ProviderConfiguration> Providers { get; set; } = new();

        [JsonPropertyName("defaultProvider")]
        public string? DefaultProvider { get; set; }

        [JsonPropertyName("fallbackProviders")]
        public List<string>? FallbackProviders { get; set; }

        /// <summary>
        /// Get provider configuration by ID
        /// </summary>
        public ProviderConfiguration? GetProvider(string providerId)
        {
            return Providers.Find(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all enabled providers
        /// </summary>
        public IEnumerable<ProviderConfiguration> GetEnabledProviders()
        {
            return Providers.FindAll(p => p.Enabled);
        }

        /// <summary>
        /// Get default provider configuration
        /// </summary>
        public ProviderConfiguration? GetDefaultProvider()
        {
            if (!string.IsNullOrEmpty(DefaultProvider))
            {
                return GetProvider(DefaultProvider);
            }

            // Return first enabled provider
            return Providers.Find(p => p.Enabled);
        }

        /// <summary>
        /// Add or update a provider configuration
        /// </summary>
        public void AddOrUpdateProvider(ProviderConfiguration config)
        {
            var existing = GetProvider(config.Id);
            if (existing != null)
            {
                Providers.Remove(existing);
            }
            Providers.Add(config);

            // Sort by priority
            Providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
    }
}
