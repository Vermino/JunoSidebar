// File: JunoSidebar.Wpf/Services/WebViewService.cs
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunoSidebar.Wpf.Services
{
    public class WebViewService
    {
        private readonly CoreWebView2 _webView;
        private readonly Action<bool> _setExpandedState;
        private readonly JsonSerializerOptions _jsonOptions;
        
        public WebViewService(CoreWebView2 webView, Action<bool> setExpandedState)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _setExpandedState = setExpandedState ?? throw new ArgumentNullException(nameof(setExpandedState));
            
            // Configure JSON options to be case-insensitive
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }
        
        public void HandleWebMessage(string messageJson)
        {
            try
            {
                Debug.WriteLine($"Received raw message: {messageJson}");
                
                // Try to parse the message - it might be a JSON string containing a JSON object
                string jsonToProcess = messageJson;
                
                // Check if it's a JSON string that needs to be unescaped
                if (messageJson.StartsWith("\"") && messageJson.EndsWith("\""))
                {
                    try {
                        // This will unescape the inner JSON string
                        jsonToProcess = JsonSerializer.Deserialize<string>(messageJson);
                        Debug.WriteLine($"Unescaped inner JSON: {jsonToProcess}");
                    }
                    catch {
                        // If this fails, use the original string
                        Debug.WriteLine("Failed to unescape, using original JSON");
                    }
                }
                
                // Now parse the actual JSON content with case-insensitive option
                var jsonObj = JsonSerializer.Deserialize<MessageObject>(jsonToProcess, _jsonOptions);
                
                if (jsonObj != null && !string.IsNullOrEmpty(jsonObj.Action))
                {
                    Debug.WriteLine($"Processing action: {jsonObj.Action}");
                    
                    switch (jsonObj.Action.ToLowerInvariant())
                    {
                        case "toggleexpanded":
                        case "setexpanded":
                            if (jsonObj.Expanded.HasValue)
                            {
                                bool expanded = jsonObj.Expanded.Value;
                                Debug.WriteLine($"Setting expanded state to: {expanded}");
                                _setExpandedState(expanded);
                            }
                            break;
                            
                        case "getstate":
                            SendApplicationState();
                            break;
                            
                        default:
                            Debug.WriteLine($"Unknown action: {jsonObj.Action}");
                            break;
                    }
                }
                else
                {
                    Debug.WriteLine("Invalid message format or missing action");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing WebView message: {ex.Message}");
                Debug.WriteLine($"Exception details: {ex}");
                
                // Print the JSON that caused the problem for debugging
                Debug.WriteLine($"Problem JSON: {messageJson}");
            }
        }
        
        public void SendApplicationState()
        {
            try
            {
                string json = JsonSerializer.Serialize(new 
                { 
                    action = "appState", 
                    version = "1.0.0" 
                });
                
                _webView.PostWebMessageAsJson(json);
                Debug.WriteLine($"Sent application state");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending application state: {ex.Message}");
            }
        }
        
        public void SendMessage(string action, object data = null)
        {
            try
            {
                string json;
                
                if (data == null)
                {
                    json = JsonSerializer.Serialize(new { action });
                }
                else
                {
                    // Create a dynamic object that includes all properties
                    var props = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["action"] = action
                    };
                    
                    // Add all properties from the data object
                    foreach (var prop in data.GetType().GetProperties())
                    {
                        props[prop.Name.ToLowerInvariant()] = prop.GetValue(data) ?? "";
                    }
                    
                    json = JsonSerializer.Serialize(props);
                }
                
                _webView.PostWebMessageAsJson(json);
                Debug.WriteLine($"Sent message: {json}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending message: {ex.Message}");
            }
        }
        
        // Simple class to deserialize WebView messages
        private class MessageObject
        {
            public string Action { get; set; }
            public bool? Expanded { get; set; }
            
            // Extra properties we might encounter in messages
            public string Version { get; set; }
        }
    }
}