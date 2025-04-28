// File: JunoSidebar/JunoSidebar.Wpf/Services/Tools/ToolRegistry.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.Json;

namespace JunoSidebar.Wpf.Services.Tools
{
    /// <summary>
    /// Registry for managing and discovering tools in the Juno AI Assistant.
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new();
        private readonly string _toolsDirectory;
        private readonly PermissionManager _permissionManager;
        private readonly JsonSerializerOptions _jsonOptions;
        
        /// <summary>
        /// Event raised when the collection of available tools changes.
        /// </summary>
        public event EventHandler<ToolsCollectionChangedEventArgs>? ToolsCollectionChanged;
        
        /// <summary>
        /// Initializes a new instance of the ToolRegistry class.
        /// </summary>
        /// <param name="permissionManager">The permission manager for tool access control.</param>
        /// <param name="toolsDirectory">The directory where external tool definitions are stored.</param>
        public ToolRegistry(PermissionManager permissionManager, string? toolsDirectory = null)
        {
            _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
            _toolsDirectory = toolsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "Tools");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            // Ensure the tools directory exists
            if (!Directory.Exists(_toolsDirectory))
            {
                Directory.CreateDirectory(_toolsDirectory);
            }
        }
        
        /// <summary>
        /// Initializes the tool registry by discovering and loading all available tools.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InitializeAsync()
        {
            // Discover and register built-in tools
            RegisterBuiltInTools();
            
            // Load external tools
            await LoadExternalToolsAsync();
            
            // Notify listeners
            OnToolsCollectionChanged(new ToolsCollectionChangedEventArgs());
        }
        
        /// <summary>
        /// Gets all registered tools.
        /// </summary>
        /// <returns>A collection of all registered tools.</returns>
        public IReadOnlyCollection<ITool> GetAllTools()
        {
            return _tools.Values;
        }
        
        /// <summary>
        /// Gets a tool by its ID.
        /// </summary>
        /// <param name="id">The ID of the tool to retrieve.</param>
        /// <returns>The tool with the specified ID, or null if not found.</returns>
        public ITool? GetTool(string id)
        {
            return _tools.TryGetValue(id, out var tool) ? tool : null;
        }
        
        /// <summary>
        /// Gets all tools that are available for a given personality.
        /// </summary>
        /// <param name="personalityId">The ID of the personality.</param>
        /// <returns>A collection of tools available for the specified personality.</returns>
        public IEnumerable<ITool> GetToolsForPersonality(string personalityId)
        {
            // In a more complex implementation, this would filter tools based on
            // personality preferences and permissions. For now, we return all tools.
            return _tools.Values;
        }
        
        /// <summary>
        /// Registers a new tool in the registry.
        /// </summary>
        /// <param name="tool">The tool to register.</param>
        /// <returns>True if the tool was registered successfully, false if a tool with the same ID already exists.</returns>
        public bool RegisterTool(ITool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }
            
            if (_tools.ContainsKey(tool.Id))
            {
                return false;
            }
            
            _tools[tool.Id] = tool;
            OnToolsCollectionChanged(new ToolsCollectionChangedEventArgs(tool, ToolChangeType.Added));
            return true;
        }
        
        /// <summary>
        /// Unregisters a tool from the registry.
        /// </summary>
        /// <param name="id">The ID of the tool to unregister.</param>
        /// <returns>True if the tool was unregistered successfully, false if the tool was not found.</returns>
        public bool UnregisterTool(string id)
        {
            if (!_tools.TryGetValue(id, out var tool))
            {
                return false;
            }
            
            _tools.Remove(id);
            OnToolsCollectionChanged(new ToolsCollectionChangedEventArgs(tool, ToolChangeType.Removed));
            return true;
        }
        
        /// <summary>
        /// Checks if the current user has permission to execute a specific tool.
        /// </summary>
        /// <param name="toolId">The ID of the tool to check.</param>
        /// <returns>True if the user has permission to execute the tool, false otherwise.</returns>
        public bool CanExecuteTool(string toolId)
        {
            if (!_tools.TryGetValue(toolId, out var tool))
            {
                return false;
            }
            
            // Check if the user has all the required permissions
            return tool.RequiredPermissions.All(permission => _permissionManager.HasPermission(permission));
        }
        
        /// <summary>
        /// Executes a tool with the given parameters.
        /// </summary>
        /// <param name="toolId">The ID of the tool to execute.</param>
        /// <param name="parameters">The parameters for tool execution.</param>
        /// <returns>The result of the tool execution.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the tool is not found or the user doesn't have permission.</exception>
        public async Task<ToolResult> ExecuteToolAsync(string toolId, Dictionary<string, object> parameters)
        {
            if (!_tools.TryGetValue(toolId, out var tool))
            {
                throw new InvalidOperationException($"Tool with ID '{toolId}' not found.");
            }
            
            if (!CanExecuteTool(toolId))
            {
                throw new InvalidOperationException($"Permission denied to execute tool '{tool.Name}'.");
            }
            
            // Execute the tool in a sandbox
            return await ToolSandbox.ExecuteInSandboxAsync(tool, parameters);
        }
        
        /// <summary>
        /// Updates the configuration for a specific tool.
        /// </summary>
        /// <param name="toolId">The ID of the tool to update.</param>
        /// <param name="config">The new configuration for the tool.</param>
        /// <returns>True if the configuration was updated successfully, false if the tool was not found.</returns>
        public bool UpdateToolConfig(string toolId, Dictionary<string, object> config)
        {
            if (!_tools.TryGetValue(toolId, out var tool))
            {
                return false;
            }
            
            tool.Config = config;
            
            // Save the configuration for external tools
            SaveToolConfiguration(tool);
            
            OnToolsCollectionChanged(new ToolsCollectionChangedEventArgs(tool, ToolChangeType.Updated));
            return true;
        }
        
        // Private helper methods
        
        private void RegisterBuiltInTools()
        {
            // In a real implementation, built-in tools would be discovered through
            // reflection or a plugin system. For now, we'll register some placeholder tools.
            // This would be replaced with actual tool implementations.
            
            // Example built-in tools:
            RegisterPlaceholderTool("web_search", "Web Search", "Searches the web for information.");
            RegisterPlaceholderTool("note_taking", "Note Taking", "Creates and manages notes.");
            RegisterPlaceholderTool("calculator", "Calculator", "Performs mathematical calculations.");
            RegisterPlaceholderTool("system_info", "System Info", "Provides information about the system.");
            RegisterPlaceholderTool("scheduler", "Scheduler", "Manages calendar events and reminders.");
        }
        
        private void RegisterPlaceholderTool(string id, string name, string description)
        {
            var tool = new PlaceholderTool(id, name, description);
            RegisterTool(tool);
        }
        
        private async Task LoadExternalToolsAsync()
        {
            // Look for tool definition files in the tools directory
            foreach (var file in Directory.GetFiles(_toolsDirectory, "*.json"))
            {
                try
                {
                    var toolDefinition = await LoadToolDefinitionAsync(file);
                    if (toolDefinition != null)
                    {
                        // In a real implementation, we would create the appropriate tool
                        // based on the definition. For now, we'll create placeholder tools.
                        var tool = new PlaceholderTool(
                            toolDefinition.Id,
                            toolDefinition.Name,
                            toolDefinition.Description);
                        
                        RegisterTool(tool);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading tool from {file}: {ex.Message}");
                }
            }
        }
        
        private async Task<ToolDefinition?> LoadToolDefinitionAsync(string filePath)
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                var toolDefinition = await JsonSerializer.DeserializeAsync<ToolDefinition>(fileStream, _jsonOptions);
                return toolDefinition;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading tool definition from {filePath}: {ex.Message}");
                return null;
            }
        }
        
        private void SaveToolConfiguration(ITool tool)
        {
            try
            {
                string configPath = Path.Combine(_toolsDirectory, $"{tool.Id}_config.json");
                string json = JsonSerializer.Serialize(tool.Config, _jsonOptions);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving tool configuration: {ex.Message}");
            }
        }
        
        private void OnToolsCollectionChanged(ToolsCollectionChangedEventArgs args)
        {
            ToolsCollectionChanged?.Invoke(this, args);
        }
        
        // Nested types for data structures
        
        /// <summary>
        /// Represents a tool definition loaded from a file.
        /// </summary>
        private class ToolDefinition
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Version { get; set; } = "1.0.0";
            public List<string> RequiredPermissions { get; set; } = new List<string>();
            public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>();
        }
    }
    
    /// <summary>
    /// Event args for tool collection changes.
    /// </summary>
    public class ToolsCollectionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the tool that was changed.
        /// </summary>
        public ITool? Tool { get; }
        
        /// <summary>
        /// Gets the type of change that occurred.
        /// </summary>
        public ToolChangeType? ChangeType { get; }
        
        /// <summary>
        /// Initializes a new instance of the ToolsCollectionChangedEventArgs class.
        /// </summary>
        /// <param name="tool">The tool that was changed.</param>
        /// <param name="changeType">The type of change that occurred.</param>
        public ToolsCollectionChangedEventArgs(ITool? tool = null, ToolChangeType? changeType = null)
        {
            Tool = tool;
            ChangeType = changeType;
        }
    }
    
    /// <summary>
    /// Enum representing types of changes to the tool collection.
    /// </summary>
    public enum ToolChangeType
    {
        /// <summary>
        /// A tool was added to the collection.
        /// </summary>
        Added,
        
        /// <summary>
        /// A tool was removed from the collection.
        /// </summary>
        Removed,
        
        /// <summary>
        /// A tool was updated.
        /// </summary>
        Updated
    }
    
    /// <summary>
    /// A placeholder implementation of ITool for demonstration purposes.
    /// </summary>
    internal class PlaceholderTool : ITool
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Version => "1.0.0";
        public IEnumerable<string> RequiredPermissions => Array.Empty<string>();
        public ToolConfigSchema ConfigSchema => new ToolConfigSchema();
        public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>();
        public bool AllowsAutomaticExecution => true;
        public bool RequiresUserInterface => false;
        
        public PlaceholderTool(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }
        
        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            // This is a placeholder implementation
            return Task.FromResult(ToolResult.CreateSuccess($"Executed {Name} with {parameters.Count} parameters"));
        }
        
        public string GetHelp()
        {
            return $"Help for {Name}:\n{Description}\n\nThis is a placeholder tool for demonstration purposes.";
        }
        
        public ToolParameterSchema GetParameterSchema()
        {
            return new ToolParameterSchema();
        }
        
        public bool ValidateConfig()
        {
            return true;
        }
    }
}