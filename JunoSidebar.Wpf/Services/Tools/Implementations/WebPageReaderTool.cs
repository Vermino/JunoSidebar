// File: JunoSidebar.Wpf/Services/Tools/Implementations/WebPageReaderTool.cs

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using HtmlAgilityPack;

namespace JunoSidebar.Wpf.Services.Tools.Implementations
{
    /// <summary>
    /// Tool for fetching and parsing web page content
    /// </summary>
    public class WebPageReaderTool : ITool
    {
        private readonly HttpClient _httpClient;

        public string Id => "webpage_reader";
        public string Name => "Web Page Reader";
        public string Description => "Fetches and extracts text content from web pages";
        public string Version => "1.0.0";
        public IEnumerable<string> RequiredPermissions => new[] { "network.read" };
        public bool AllowsAutomaticExecution => false;
        public bool RequiresUserInterface => false;
        public Dictionary<string, object> Config { get; set; } = new();

        public ToolConfigSchema ConfigSchema => new ToolConfigSchema
        {
            Properties = new List<ToolConfigProperty>
            {
                new ToolConfigProperty
                {
                    Name = "timeout",
                    DisplayName = "Timeout (seconds)",
                    Description = "Request timeout in seconds",
                    Type = ToolPropertyType.Integer,
                    DefaultValue = 30
                },
                new ToolConfigProperty
                {
                    Name = "userAgent",
                    DisplayName = "User Agent",
                    Description = "User agent string for requests",
                    Type = ToolPropertyType.String,
                    DefaultValue = "JunoSidebar/1.0"
                }
            }
        };

        public WebPageReaderTool()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("JunoSidebar/1.0");
        }

        public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                if (!parameters.TryGetValue("url", out var urlObj) || string.IsNullOrEmpty(urlObj?.ToString()))
                {
                    return ToolResult.CreateError("URL parameter is required");
                }

                string url = urlObj.ToString()!;

                Debug.WriteLine($"Fetching web page: {url}");

                // Fetch the page
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();

                // Parse HTML
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Extract text content
                var textContent = ExtractTextContent(doc);

                // Extract metadata
                var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "Untitled";
                var metaDescription = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")
                    ?.GetAttributeValue("content", string.Empty);

                var result = new
                {
                    url = url,
                    title = title,
                    description = metaDescription,
                    content = textContent,
                    contentLength = textContent.Length,
                    fetchedAt = DateTime.UtcNow
                };

                Debug.WriteLine($"Successfully fetched page: {title} ({textContent.Length} characters)");

                return ToolResult.CreateSuccess(result, new Dictionary<string, object>
                {
                    ["status_code"] = (int)response.StatusCode,
                    ["content_type"] = response.Content.Headers.ContentType?.ToString() ?? string.Empty
                });
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Error fetching web page: {ex.Message}");
                return ToolResult.CreateError($"Failed to fetch web page: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading web page: {ex.Message}");
                return ToolResult.CreateError($"Error reading web page: {ex.Message}");
            }
        }

        public string GetHelp()
        {
            return @"Web Page Reader Tool

Fetches content from a web page and extracts readable text.

Parameters:
- url (required): The URL of the web page to fetch

Example:
{
  ""url"": ""https://example.com""
}

Returns:
- title: Page title
- description: Meta description if available
- content: Extracted text content
- contentLength: Length of content in characters";
        }

        public bool ValidateConfig()
        {
            return true;
        }

        public ToolParameterSchema GetParameterSchema()
        {
            return new ToolParameterSchema
            {
                Properties = new List<ToolParameterProperty>
                {
                    new ToolParameterProperty
                    {
                        Name = "url",
                        DisplayName = "URL",
                        Description = "The URL of the web page to fetch",
                        Type = ToolPropertyType.String,
                        ExposeToLLM = true
                    }
                },
                Required = new List<string> { "url" }
            };
        }

        private string ExtractTextContent(HtmlDocument doc)
        {
            // Remove script and style elements
            var elementsToRemove = doc.DocumentNode.SelectNodes("//script|//style|//nav|//footer|//header");
            if (elementsToRemove != null)
            {
                foreach (var element in elementsToRemove)
                {
                    element.Remove();
                }
            }

            // Extract text from main content areas
            var mainContent = doc.DocumentNode.SelectSingleNode("//main|//article|//body");
            var text = mainContent?.InnerText ?? doc.DocumentNode.InnerText;

            // Clean up whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            text = HtmlEntity.DeEntitize(text);

            return text.Trim();
        }
    }
}
