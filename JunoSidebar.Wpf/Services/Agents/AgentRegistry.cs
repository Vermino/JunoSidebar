// File: JunoSidebar.Wpf/Services/Agents/AgentRegistry.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JunoSidebar.Wpf.Services.Agents
{
    /// <summary>
    /// Registry for managing available agents
    /// </summary>
    public class AgentRegistry
    {
        private readonly Dictionary<string, IAgent> _agents;
        private readonly Dictionary<string, Func<IAgent>> _agentFactories;
        private readonly string _agentsDirectory;
        private readonly IDeserializer _yamlDeserializer;

        public AgentRegistry(string? agentsDirectory = null)
        {
            _agents = new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase);
            _agentFactories = new Dictionary<string, Func<IAgent>>(StringComparer.OrdinalIgnoreCase);

            _agentsDirectory = agentsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "Agents");

            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            Directory.CreateDirectory(_agentsDirectory);

            Debug.WriteLine($"AgentRegistry initialized with directory: {_agentsDirectory}");

            RegisterDefaultAgentFactories();
        }

        /// <summary>
        /// Register an agent factory
        /// </summary>
        public void RegisterAgentFactory(string agentType, Func<IAgent> factory)
        {
            _agentFactories[agentType] = factory;
            Debug.WriteLine($"Registered agent factory: {agentType}");
        }

        /// <summary>
        /// Register an agent instance
        /// </summary>
        public void RegisterAgent(IAgent agent)
        {
            _agents[agent.Id] = agent;
            Debug.WriteLine($"Registered agent: {agent.Id} ({agent.Name})");
        }

        /// <summary>
        /// Get agent by ID
        /// </summary>
        public IAgent? GetAgent(string agentId)
        {
            return _agents.TryGetValue(agentId, out var agent) ? agent : null;
        }

        /// <summary>
        /// Get all registered agents
        /// </summary>
        public List<IAgent> GetAllAgents()
        {
            return _agents.Values.ToList();
        }

        /// <summary>
        /// Get agents by capability
        /// </summary>
        public List<IAgent> GetAgentsByCapability(string capability)
        {
            return _agents.Values
                .Where(a => a.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Load agents from YAML configuration files
        /// </summary>
        public async Task LoadAgentsFromDirectoryAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Directory.Exists(_agentsDirectory))
                {
                    Debug.WriteLine($"Agents directory does not exist: {_agentsDirectory}");
                    return;
                }

                var yamlFiles = Directory.GetFiles(_agentsDirectory, "*.yaml", SearchOption.TopDirectoryOnly);
                Debug.WriteLine($"Found {yamlFiles.Length} agent configuration files");

                foreach (var file in yamlFiles)
                {
                    try
                    {
                        await LoadAgentFromFileAsync(file, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading agent from {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading agents from directory: {ex.Message}");
            }
        }

        /// <summary>
        /// Load agent from YAML file
        /// </summary>
        public async Task<IAgent?> LoadAgentFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"Loading agent from: {filePath}");

                var yaml = await File.ReadAllTextAsync(filePath, cancellationToken);
                var config = _yamlDeserializer.Deserialize<AgentConfiguration>(yaml);

                if (config == null)
                {
                    Debug.WriteLine($"Failed to deserialize agent configuration from {filePath}");
                    return null;
                }

                // Determine agent type from config or file name
                string agentType = config.Settings.CustomSettings?.GetValueOrDefault("type")?.ToString()
                    ?? "general";

                if (!_agentFactories.ContainsKey(agentType))
                {
                    Debug.WriteLine($"No factory registered for agent type: {agentType}");
                    return null;
                }

                var agent = _agentFactories[agentType]();
                await agent.InitializeAsync(config, cancellationToken);

                RegisterAgent(agent);

                Debug.WriteLine($"Successfully loaded agent: {agent.Name}");
                return agent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading agent from file {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save agent configuration to YAML file
        /// </summary>
        public async Task SaveAgentAsync(AgentConfiguration config, CancellationToken cancellationToken = default)
        {
            try
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                var yaml = serializer.Serialize(config);
                var fileName = $"{config.Id}.yaml";
                var filePath = Path.Combine(_agentsDirectory, fileName);

                await File.WriteAllTextAsync(filePath, yaml, cancellationToken);

                Debug.WriteLine($"Saved agent configuration: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving agent: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Remove agent from registry
        /// </summary>
        public bool RemoveAgent(string agentId)
        {
            if (_agents.Remove(agentId))
            {
                Debug.WriteLine($"Removed agent: {agentId}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Create default agents
        /// </summary>
        public async Task CreateDefaultAgentsAsync(CancellationToken cancellationToken = default)
        {
            // Create general agent config
            var generalConfig = new AgentConfiguration
            {
                Id = "general-assistant",
                Name = "General Assistant",
                Description = "A versatile assistant for general tasks",
                Capabilities = new List<string> { "general", "conversation", "assistance" },
                LlmProvider = "lmstudio",
                LlmModel = "local-model",
                SystemPrompt = @"You are a helpful AI assistant. You can help with a variety of tasks including answering questions, providing information, and assisting with problem-solving.

When given a task:
1. Analyze what is being asked
2. Break down complex tasks into steps
3. Use available tools when appropriate
4. Provide clear, helpful responses",
                Tools = new List<string>(),
                Settings = new AgentSettings
                {
                    MaxIterations = 10,
                    TimeoutSeconds = 300,
                    AutoApproveTools = false
                }
            };

            await SaveAgentAsync(generalConfig, cancellationToken);

            // Create research agent config
            var researchConfig = new AgentConfiguration
            {
                Id = "research-assistant",
                Name = "Research Assistant",
                Description = "Specialized in web research and information gathering",
                Capabilities = new List<string> { "research", "web-search", "summarization" },
                LlmProvider = "lmstudio",
                LlmModel = "local-model",
                SystemPrompt = @"You are a research assistant specialized in gathering and synthesizing information.

When given a research task:
1. Break the topic into specific research questions
2. Use web search to find relevant information
3. Read and analyze the content from web pages
4. Synthesize findings into a coherent summary
5. Cite sources when appropriate

Always be thorough and provide well-researched, accurate information.",
                Tools = new List<string> { "web_search", "webpage_reader", "note_taker" },
                Settings = new AgentSettings
                {
                    MaxIterations = 15,
                    TimeoutSeconds = 600,
                    AutoApproveTools = false
                }
            };

            await SaveAgentAsync(researchConfig, cancellationToken);

            Debug.WriteLine("Created default agent configurations");
        }

        private void RegisterDefaultAgentFactories()
        {
            // Register general agent factory
            RegisterAgentFactory("general", () => new GeneralAgent());

            // Register research agent factory (will implement later)
            // RegisterAgentFactory("research", () => new ResearchAgent());

            // Register code agent factory (will implement later)
            // RegisterAgentFactory("code", () => new CodeAgent());
        }
    }
}
