// File: JunoSidebar.Wpf/Services/LLM/Providers/LLMProviderFactory.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace JunoSidebar.Wpf.Services.LLM.Providers
{
    /// <summary>
    /// Factory for creating and managing LLM provider instances
    /// </summary>
    public class LLMProviderFactory
    {
        private readonly Dictionary<string, ILLMProvider> _providers = new();
        private readonly Dictionary<string, Func<ILLMProvider>> _providerFactories = new();
        private ProvidersConfiguration _configuration;

        public LLMProviderFactory(ProvidersConfiguration? configuration = null)
        {
            _configuration = configuration ?? CreateDefaultConfiguration();
            RegisterDefaultProviders();
        }

        /// <summary>
        /// Register a provider factory
        /// </summary>
        public void RegisterProviderFactory(string providerId, Func<ILLMProvider> factory)
        {
            _providerFactories[providerId.ToLowerInvariant()] = factory;
            Debug.WriteLine($"Registered provider factory: {providerId}");
        }

        /// <summary>
        /// Get or create a provider instance
        /// </summary>
        public async Task<ILLMProvider?> GetProviderAsync(string providerId, CancellationToken cancellationToken = default)
        {
            var normalizedId = providerId.ToLowerInvariant();

            // Return cached instance if available
            if (_providers.ContainsKey(normalizedId))
            {
                return _providers[normalizedId];
            }

            // Get configuration for this provider
            var config = _configuration.GetProvider(normalizedId);
            if (config == null || !config.Enabled)
            {
                Debug.WriteLine($"Provider {providerId} is not configured or not enabled");
                return null;
            }

            // Create new instance if factory exists
            if (!_providerFactories.ContainsKey(normalizedId))
            {
                Debug.WriteLine($"No factory registered for provider: {providerId}");
                return null;
            }

            try
            {
                var provider = _providerFactories[normalizedId]();
                await provider.InitializeAsync(config, cancellationToken);
                _providers[normalizedId] = provider;
                Debug.WriteLine($"Successfully initialized provider: {providerId}");
                return provider;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize provider {providerId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the default provider
        /// </summary>
        public async Task<ILLMProvider?> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
        {
            var defaultConfig = _configuration.GetDefaultProvider();
            if (defaultConfig == null)
            {
                Debug.WriteLine("No default provider configured");
                return null;
            }

            return await GetProviderAsync(defaultConfig.Id, cancellationToken);
        }

        /// <summary>
        /// Get all enabled providers
        /// </summary>
        public async Task<List<ILLMProvider>> GetEnabledProvidersAsync(CancellationToken cancellationToken = default)
        {
            var providers = new List<ILLMProvider>();

            foreach (var config in _configuration.GetEnabledProviders())
            {
                var provider = await GetProviderAsync(config.Id, cancellationToken);
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }

            return providers;
        }

        /// <summary>
        /// Update provider configuration
        /// </summary>
        public void UpdateConfiguration(ProvidersConfiguration configuration)
        {
            _configuration = configuration;

            // Clear cached providers that are no longer enabled
            var disabledProviders = _providers.Keys
                .Where(id => _configuration.GetProvider(id)?.Enabled != true)
                .ToList();

            foreach (var id in disabledProviders)
            {
                _providers.Remove(id);
            }
        }

        /// <summary>
        /// Test a provider's connection
        /// </summary>
        public async Task<bool> TestProviderAsync(string providerId, CancellationToken cancellationToken = default)
        {
            var provider = await GetProviderAsync(providerId, cancellationToken);
            if (provider == null)
            {
                return false;
            }

            return await provider.TestConnectionAsync(cancellationToken);
        }

        /// <summary>
        /// Get list of all registered provider IDs
        /// </summary>
        public IEnumerable<string> GetRegisteredProviderIds()
        {
            return _providerFactories.Keys;
        }

        private void RegisterDefaultProviders()
        {
            // Register Anthropic
            RegisterProviderFactory("anthropic", () => new AnthropicProvider());

            // Register OpenAI
            RegisterProviderFactory("openai", () => new OpenAIProvider());

            // Register LM Studio
            RegisterProviderFactory("lmstudio", () => new LMStudioProvider());

            // Register Ollama
            RegisterProviderFactory("ollama", () => new OllamaProvider());
        }

        private static ProvidersConfiguration CreateDefaultConfiguration()
        {
            return new ProvidersConfiguration
            {
                Providers = new List<ProviderConfiguration>
                {
                    new ProviderConfiguration
                    {
                        Id = "lmstudio",
                        Name = "LM Studio",
                        Enabled = true,
                        BaseUrl = "http://localhost:1234/v1",
                        DefaultModel = "local-model",
                        Priority = 10
                    },
                    new ProviderConfiguration
                    {
                        Id = "anthropic",
                        Name = "Anthropic (Claude)",
                        Enabled = false,
                        DefaultModel = "claude-sonnet-4-20250514",
                        Priority = 100
                    },
                    new ProviderConfiguration
                    {
                        Id = "openai",
                        Name = "OpenAI (ChatGPT)",
                        Enabled = false,
                        DefaultModel = "gpt-4-turbo",
                        Priority = 90
                    },
                    new ProviderConfiguration
                    {
                        Id = "ollama",
                        Name = "Ollama",
                        Enabled = false,
                        BaseUrl = "http://localhost:11434",
                        DefaultModel = "llama3.2",
                        Priority = 20
                    }
                },
                DefaultProvider = "lmstudio"
            };
        }
    }
}
