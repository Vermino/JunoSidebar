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
                string jsonToProcess = messageJson;
                
                // Handle potential double-encoded JSON
                if (messageJson.StartsWith("\"") && messageJson.EndsWith("\""))
                {
                    try {
                        jsonToProcess = JsonSerializer.Deserialize<string>(messageJson);
                        Debug.WriteLine($"Unescaped inner JSON: {jsonToProcess}");
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"Failed to unescape JSON: {ex.Message}");
                        Debug.WriteLine("Using original JSON");
                    }
                }
                
                // Attempt to parse the message
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
                        case "error":
                            // Log errors from the WebView
                            if (jsonObj.Message != null)
                            {
                                Debug.WriteLine($"WebView error: {jsonObj.Message}");
                            }
                            break;
                        case "log":
                            // Log general messages from the WebView
                            if (jsonObj.Message != null)
                            {
                                Debug.WriteLine($"WebView log: {jsonObj.Message}");
                            }
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
                Debug.WriteLine($"Problem JSON: {messageJson}");
                
                // Try to determine if this is a core engine message
                try
                {
                    // See if we can still extract any useful information from the message
                    if (messageJson.Contains("\"action\"") && messageJson.Contains("\"data\""))
                    {
                        Debug.WriteLine("Might be a core engine message, passing along");
                        // The CoreEngine class will handle this message
                        return;
                    }
                }
                catch (Exception innerEx)
                {
                    Debug.WriteLine($"Error analyzing message further: {innerEx.Message}");
                }
            }
        }

        public void SendApplicationState()
        {
            try
            {
                string version = App.AppVersion;
                string json = JsonSerializer.Serialize(new 
                { 
                    action = "appState", 
                    version = version,
                    isDebugMode = App.IsDebugMode
                });
                _webView.PostWebMessageAsJson(json);
                Debug.WriteLine($"Sent application state with version: {version}");
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
                    var props = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["action"] = action
                    };
                    foreach (var prop in data.GetType().GetProperties())
                    {
                        props[prop.Name.ToLowerInvariant()] = prop.GetValue(data) ?? "";
                    }
                    json = JsonSerializer.Serialize(props);
                }
                _webView.PostWebMessageAsJson(json);
                Debug.WriteLine($"Sent message: {action}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        public void InjectInitialJavaScript()
        {
            try
            {
                string script = @"
                    if (!window.junoInitialized) {
                        window.junoInitialized = true;
                        console.log('Juno WebView initialization script running');
                        
                        // Set up error listeners
                        window.addEventListener('error', function(e) {
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({
                                    action: 'error',
                                    message: 'ERROR: ' + e.message,
                                    file: e.filename,
                                    line: e.lineno,
                                    stack: e.error ? e.error.stack : ''
                                });
                            }
                            console.error('Caught error:', e.message);
                        });
                        
                        window.addEventListener('unhandledrejection', function(e) {
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({
                                    action: 'error',
                                    message: 'UNHANDLED PROMISE: ' + (e.reason ? (e.reason.message || e.reason) : 'Unknown error'),
                                    stack: e.reason && e.reason.stack ? e.reason.stack : ''
                                });
                            }
                            console.error('Unhandled promise rejection:', e.reason);
                        });
                        
                        // Override console methods to relay logs to C#
                        console.originalLog = console.log;
                        console.originalWarn = console.warn;
                        console.originalError = console.error;
                        
                        console.log = function() {
                            console.originalLog.apply(console, arguments);
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({
                                    action: 'log',
                                    level: 'info',
                                    message: Array.from(arguments).map(arg => 
                                        typeof arg === 'object' ? JSON.stringify(arg) : arg
                                    ).join(' ')
                                });
                            }
                        };
                        
                        console.warn = function() {
                            console.originalWarn.apply(console, arguments);
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({
                                    action: 'log',
                                    level: 'warn',
                                    message: Array.from(arguments).map(arg => 
                                        typeof arg === 'object' ? JSON.stringify(arg) : arg
                                    ).join(' ')
                                });
                            }
                        };
                        
                        console.error = function() {
                            console.originalError.apply(console, arguments);
                            if (window.chrome && window.chrome.webview) {
                                window.chrome.webview.postMessage({
                                    action: 'error',
                                    message: Array.from(arguments).map(arg => 
                                        typeof arg === 'object' ? JSON.stringify(arg) : arg
                                    ).join(' ')
                                });
                            }
                        };
                        
                        // Perform additional diagnostics
                        function performReactDiagnostics() {
                            console.log('Running React diagnostic checks...');
                            var rootElement = document.getElementById('root');
                            if (rootElement) {
                                console.log('Root element found with children:', rootElement.childNodes.length);
                                if (rootElement.childNodes.length === 0) {
                                    console.log('Root element is empty, React may not have mounted');
                                    
                                    // Create a helper function that tries to mount a basic component
                                    if (typeof React !== 'undefined' && typeof ReactDOM !== 'undefined') {
                                        console.log('React and ReactDOM are defined, attempting to mount test component');
                                        try {
                                            ReactDOM.render(
                                                React.createElement('div', { 
                                                    style: { padding: '20px', fontFamily: 'sans-serif' } 
                                                }, 'Test Component'),
                                                document.createElement('div')
                                            );
                                            console.log('Test mount successful, React is working');
                                        } catch (e) {
                                            console.error('Test mount failed:', e);
                                        }
                                    } else {
                                        console.warn('React or ReactDOM is not defined');
                                    }
                                }
                            } else {
                                console.error('Root element not found!');
                            }
                        }
                        
                        // Run diagnostics after a delay
                        setTimeout(performReactDiagnostics, 1500);
                        
                        // Send initialization confirmation
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage({
                                action: 'log',
                                level: 'info',
                                message: 'WebView initialization script completed successfully'
                            });
                        }
                    } else {
                        console.log('Juno WebView already initialized');
                    }
                ";
                
                _webView.ExecuteScriptAsync(script);
                Debug.WriteLine("Injected initialization JavaScript");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to inject initialization JavaScript: {ex.Message}");
            }
        }

        private class MessageObject
        {
            [JsonPropertyName("action")]
            public string Action { get; set; }
            
            [JsonPropertyName("expanded")]
            public bool? Expanded { get; set; }
            
            [JsonPropertyName("message")]
            public string Message { get; set; }
            
            [JsonPropertyName("version")]
            public string Version { get; set; }
        }
    }
}