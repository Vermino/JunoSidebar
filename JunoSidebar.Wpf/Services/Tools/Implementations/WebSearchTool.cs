// File: JunoSidebar/JunoSidebar.Wpf/Services/Tools/Implementations/WebSearchTool.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.Tools.Implementations
{
    /// <summary>
    /// Tool for performing web searches.
    /// </summary>
    public class WebSearchTool : ITool
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        
        /// <summary>
        /// Gets the unique identifier for this tool.
        /// </summary>
        public string Id => "web_search";
        
        /// <summary>
        /// Gets the display name of this tool.
        /// </summary>
        public string Name => "Web Search";
        
        /// <summary>
        /// Gets a description of what this tool does.
        /// </summary>
        public string Description => "Searches the web for information using a search engine.";
        
        /// <summary>
        /// Gets the version of this tool.
        /// </summary>
        public string Version => "1.0.0";
        
        /// <summary>
        /// Gets the list of permissions required by this tool.
        /// </summary>
        public IEnumerable<string> RequiredPermissions => new[] { "network.http" };
        
        /// <summary>
        /// Gets the schema defining the configuration options for this tool.
        /// </summary>
        public ToolConfigSchema ConfigSchema => new ToolConfigSchema
        {
            Properties = new List<ToolConfigProperty>
            {
                new ToolConfigProperty
                {
                    Name = "searchEngine",
                    DisplayName = "Search Engine",
                    Description = "The search engine to use for web searches.",
                    Type = ToolPropertyType.Enum,
                    DefaultValue = "duckduckgo",
                    Options = new List<EnumOption>
                    {
                        new EnumOption
                        {
                            Value = "duckduckgo",
                            Label = "DuckDuckGo"
                        },
                        new EnumOption
                        {
                            Value = "bing",
                            Label = "Bing"
                        }
                    }
                },
                new ToolConfigProperty
                {
                    Name = "resultsCount",
                    DisplayName = "Maximum Results",
                    Description = "The maximum number of search results to return.",
                    Type = ToolPropertyType.Integer,
                    DefaultValue = 5,
                    Validation = new ValidationConstraints
                    {
                        Minimum = 1,
                        Maximum = 10
                    }
                }
            },
            Required = new List<string> { "searchEngine", "resultsCount" }
        };
        
        /// <summary>
        /// Gets or sets the configuration for this tool.
        /// </summary>
        public Dictionary<string, object> Config { get; set; } = new Dictionary<string, object>
        {
            ["searchEngine"] = "duckduckgo",
            ["resultsCount"] = 5
        };
        
        /// <summary>
        /// Gets a value indicating whether this tool can be used without user confirmation.
        /// </summary>
        public bool AllowsAutomaticExecution => true;
        
        /// <summary>
        /// Gets a value indicating whether this tool requires a UI display for execution.
        /// </summary>
        public bool RequiresUserInterface => false;

        /// <summary>
        /// Initializes a new instance of the WebSearchTool class.
        /// </summary>
        public WebSearchTool()
        {
            _httpClient = new HttpClient();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Executes the tool with the given parameters.
        /// </summary>
        /// <param name="parameters">Parameters for the tool execution.</param>
        /// <returns>The result of the tool execution.</returns>
        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                // Check for required parameters
                if (!parameters.TryGetValue("query", out var queryObj) || queryObj is not string query || string.IsNullOrWhiteSpace(query))
                {
                    return ToolResult.CreateError("Search query is required.");
                }
                
                // Get configuration values
                string searchEngine = Config.TryGetValue("searchEngine", out var engineObj) && engineObj is string engine
                    ? engine
                    : "duckduckgo";
                
                int resultsCount = Config.TryGetValue("resultsCount", out var countObj) && countObj is int count
                    ? count
                    : 5;
                
                // This is a mock implementation - in a real tool, this would call an actual search API
                var results = await SimulateWebSearchAsync(query, searchEngine, resultsCount);
                
                return ToolResult.CreateSuccess(results, new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["engine"] = searchEngine,
                    ["count"] = resultsCount
                });
            }
            catch (Exception ex)
            {
                return ToolResult.CreateError($"Web search failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets help information about how to use this tool.
        /// </summary>
        /// <returns>Help text for this tool.</returns>
        public string GetHelp()
        {
            return @"Web Search Tool
-------------------
Searches the web for information using configured search engines.

Parameters:
- query (required): The search query to execute.

Example usage:
web_search(""Juno AI Assistant"")

Configuration:
- searchEngine: The search engine to use (DuckDuckGo or Bing).
- resultsCount: The maximum number of results to return (1-10).";
        }

        /// <summary>
        /// Gets the schema for the parameters this tool accepts.
        /// </summary>
        /// <returns>The parameter schema for this tool.</returns>
        public ToolParameterSchema GetParameterSchema()
        {
            return new ToolParameterSchema
            {
                Properties = new List<ToolParameterProperty>
                {
                    new ToolParameterProperty
                    {
                        Name = "query",
                        DisplayName = "Search Query",
                        Description = "The search query to execute.",
                        Type = ToolPropertyType.String,
                        Validation = new ValidationConstraints
                        {
                            MinLength = 1,
                            MaxLength = 200
                        }
                    }
                },
                Required = new List<string> { "query" }
            };
        }

        /// <summary>
        /// Validates the current configuration for this tool.
        /// </summary>
        /// <returns>True if the configuration is valid, false otherwise.</returns>
        public bool ValidateConfig()
        {
            // Check that searchEngine is one of the allowed values
            if (!Config.TryGetValue("searchEngine", out var engineObj) || engineObj is not string engine ||
                (engine != "duckduckgo" && engine != "bing"))
            {
                return false;
            }
            
            // Check that resultsCount is within the allowed range
            if (!Config.TryGetValue("resultsCount", out var countObj) || countObj is not int count ||
                count < 1 || count > 10)
            {
                return false;
            }
            
            return true;
        }

        // Private helper methods

        private async Task<List<SearchResult>> SimulateWebSearchAsync(string query, string engine, int count)
        {
            // This is a simulated search result - in a real tool, this would call an actual search API
            // For demonstration purposes, we're simulating a network delay and returning fake results
            
            await Task.Delay(500); // Simulate network delay
            
            var results = new List<SearchResult>();
            
            // Generate some fake results
            for (int i = 0; i < count; i++)
            {
                results.Add(new SearchResult
                {
                    Title = $"Result {i + 1} for '{query}'",
                    Url = $"https://example.com/result{i + 1}",
                    Snippet = $"This is a simulated search result for '{query}' using {engine}. " +
                              $"In a real implementation, this would contain actual content from the web.",
                    Source = "example.com"
                });
            }
            
            return results;
        }

        /// <summary>
        /// Represents a search result.
        /// </summary>
        public class SearchResult
        {
            /// <summary>
            /// Gets or sets the title of the search result.
            /// </summary>
            public string Title { get; set; } = string.Empty;
            
            /// <summary>
            /// Gets or sets the URL of the search result.
            /// </summary>
            public string Url { get; set; } = string.Empty;
            
            /// <summary>
            /// Gets or sets the snippet/description of the search result.
            /// </summary>
            public string Snippet { get; set; } = string.Empty;
            
            /// <summary>
            /// Gets or sets the source of the search result.
            /// </summary>
            public string Source { get; set; } = string.Empty;
        }
    }
}