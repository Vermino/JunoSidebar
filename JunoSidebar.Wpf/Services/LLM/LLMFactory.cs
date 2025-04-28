// File: JunoSidebar/JunoSidebar.Wpf/Services/LLM/LLMFactory.cs
using System;
using System.Collections.Generic;

namespace JunoSidebar.Wpf.Services.LLM
{
    /// <summary>
    /// Factory class that creates appropriate LLM client instances based on provider type.
    /// This provides the abstraction layer to support multiple LLM providers.
    /// </summary>
    public class LLMFactory
    {
        /// <summary>
        /// Enum representing supported LLM providers.
        /// </summary>
        public enum LLMProvider
        {
            LMStudio,
            // Add other providers as they are implemented
            // OpenAI,
            // Anthropic,
            // OllamaLocal,
            // etc.
        }

        private readonly Dictionary<LLMProvider, Func<ILLMClient>> _clientFactories;

        /// <summary>
        /// Initializes a new instance of the LLMFactory class.
        /// </summary>
        public LLMFactory()
        {
            _clientFactories = new Dictionary<LLMProvider, Func<ILLMClient>>
            {
                { LLMProvider.LMStudio, () => new LMStudioClient() }
                // Add other providers as they are implemented
                // { LLMProvider.OpenAI, () => new OpenAIClient(apiKey) },
                // { LLMProvider.Anthropic, () => new AnthropicClient(apiKey) },
                // etc.
            };
        }

        /// <summary>
        /// Creates an LLM client for the specified provider with custom configuration.
        /// </summary>
        /// <param name="provider">The LLM provider to use.</param>
        /// <param name="config">Optional configuration parameters for the client.</param>
        /// <returns>An instance of an ILLMClient for the specified provider.</returns>
        public ILLMClient CreateClient(LLMProvider provider, Dictionary<string, string>? config = null)
        {
            if (!_clientFactories.TryGetValue(provider, out var factory))
            {
                throw new ArgumentException($"Unsupported LLM provider: {provider}");
            }

            var client = factory();

            // Apply any provider-specific configuration
            ApplyConfiguration(client, config);

            return client;
        }

        /// <summary>
        /// Gets a list of all supported LLM providers.
        /// </summary>
        /// <returns>An array of all supported LLM providers.</returns>
        public LLMProvider[] GetSupportedProviders()
        {
            return new List<LLMProvider>(_clientFactories.Keys).ToArray();
        }

        /// <summary>
        /// Gets the display name for a provider.
        /// </summary>
        /// <param name="provider">The provider enum value.</param>
        /// <returns>A user-friendly display name for the provider.</returns>
        public string GetProviderDisplayName(LLMProvider provider)
        {
            return provider switch
            {
                LLMProvider.LMStudio => "LM Studio (Local)",
                // Add other providers as they are implemented
                _ => provider.ToString()
            };
        }

        /// <summary>
        /// Gets the default configuration parameters for a provider.
        /// </summary>
        /// <param name="provider">The provider to get configuration for.</param>
        /// <returns>A dictionary of configuration key/value pairs.</returns>
        public Dictionary<string, string> GetDefaultConfiguration(LLMProvider provider)
        {
            return provider switch
            {
                LLMProvider.LMStudio => new Dictionary<string, string>
                {
                    { "BaseUrl", "http://localhost:1234/v1" },
                    { "DefaultModel", "local-model" }
                },
                // Add other providers as they are implemented
                _ => new Dictionary<string, string>()
            };
        }

        /// <summary>
        /// Gets the required configuration keys for a provider.
        /// </summary>
        /// <param name="provider">The provider to get configuration keys for.</param>
        /// <returns>An array of required configuration keys.</returns>
        public string[] GetRequiredConfigurationKeys(LLMProvider provider)
        {
            return provider switch
            {
                LLMProvider.LMStudio => new[] { "BaseUrl" },
                // Add other providers as they are implemented
                // LLMProvider.OpenAI => new[] { "ApiKey" },
                _ => Array.Empty<string>()
            };
        }

        // Private helper method to apply configuration to a client
        private void ApplyConfiguration(ILLMClient client, Dictionary<string, string>? config)
        {
            if (config == null || config.Count == 0)
                return;

            // Apply configuration specific to each client type
            switch (client)
            {
                case LMStudioClient lmStudioClient:
                    // Currently, LMStudio configuration is applied through constructor
                    // This is a placeholder for future configurability
                    break;
                // Add cases for other client types as they are implemented
                default:
                    break;
            }
        }
    }
}