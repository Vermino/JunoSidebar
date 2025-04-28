// File: JunoSidebar/JunoSidebar.Wpf/Services/CoreEngine.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using JunoSidebar.Wpf.Models;
using JunoSidebar.Wpf.Services.LLM;
using JunoSidebar.Wpf.Services.Tools;
using JunoSidebar.Wpf.Services.Voice;
using Microsoft.Web.WebView2.Core;

namespace JunoSidebar.Wpf.Services
{
    /// <summary>
    /// Core engine that coordinates all services and handles communication with the UI.
    /// </summary>
    public class CoreEngine
    {
        private readonly CoreWebView2 _webView;
        private readonly LLMFactory _llmFactory;
        private readonly PersonalityManager _personalityManager;
        private readonly ToolRegistry _toolRegistry;
        private readonly PermissionManager _permissionManager;
        private readonly VoiceService _voiceService;
        private readonly ConversationService _conversationService;
        private readonly ContextManager _contextManager;
        private readonly WebService _webService;
        private readonly ILLMClient _llmClient;
        
        private readonly JsonSerializerOptions _jsonOptions;
        private bool _isInitialized = false;

        /// <summary>
        /// Initializes a new instance of the CoreEngine class.
        /// </summary>
        /// <param name="webView">The WebView2 instance used for the UI.</param>
        public CoreEngine(CoreWebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            // Create services
            _webService = new WebService();
            _permissionManager = new PermissionManager();
            _llmFactory = new LLMFactory();
            _llmClient = _llmFactory.CreateClient(LLMFactory.LLMProvider.LMStudio);
            _contextManager = new ContextManager();
            _personalityManager = new PersonalityManager();
            _toolRegistry = new ToolRegistry(_permissionManager);
            _voiceService = new VoiceService(_webService);
            
            // Create the conversation service with all dependencies
            _conversationService = new ConversationService(
                _llmClient,
                _contextManager,
                _personalityManager,
                _toolRegistry,
                _voiceService
            );
            
            // Set up event handlers
            _webView.WebMessageReceived += OnWebMessageReceived;
            _conversationService.AssistantStateChanged += OnAssistantStateChanged;
            _conversationService.QueryUpdated += OnQueryUpdated;
            _conversationService.ResponseUpdated += OnResponseUpdated;
            _conversationService.ToolExecuted += OnToolExecuted;
            _personalityManager.PersonalityChanged += OnPersonalityChanged;
            _voiceService.AudioLevelChanged += OnAudioLevelChanged;
        }

        /// <summary>
        /// Initializes all services.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;
            
            try
            {
                // Initialize all services in the correct order
                await _permissionManager.InitializeAsync();
                await _personalityManager.InitializeAsync();
                await _toolRegistry.InitializeAsync();
                _voiceService.InitializeSpeechRecognition();
                
                // Mark as initialized
                _isInitialized = true;
                
                // Send initialization complete message to UI
                SendMessageToUI("initialized", new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing services: {ex.Message}");
                SendMessageToUI("initializationError", new { error = ex.Message });
            }
        }

        // Private helper methods for handling messages from UI

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.WebMessageAsJson;
                var messageObj = JsonSerializer.Deserialize<WebMessage>(message, _jsonOptions);
                
                if (messageObj == null || string.IsNullOrEmpty(messageObj.Action))
                {
                    return;
                }
                
                // Handle the message based on the action
                switch (messageObj.Action.ToLowerInvariant())
                {
                    case "getpersonalities":
                        HandleGetPersonalities();
                        break;
                    
                    case "setpersonality":
                        if (messageObj.TryGetProperty("id", out string? personalityId) && personalityId != null)
                        {
                            HandleSetPersonality(personalityId);
                        }
                        break;
                    
                    case "startlistening":
                        _conversationService.StartListening();
                        break;
                    
                    case "stoplistening":
                        _conversationService.StopListening();
                        break;
                    
                    case "stopresponding":
                        _conversationService.CancelCurrentConversation();
                        break;
                        
                    case "setassistantstate":
                        if (messageObj.TryGetProperty("state", out string? state) && state != null)
                        {
                            if (Enum.TryParse<AssistantState>(state, true, out var assistantState))
                            {
                                _conversationService.SetCurrentState(assistantState);
                            }
                        }
                        break;
                    
                    case "submitquery":
                        if (messageObj.TryGetProperty("query", out string? query) && query != null)
                        {
                            Task.Run(() => _conversationService.ProcessQueryAsync(query));
                        }
                        break;
                    
                    case "executetool":
                        if (messageObj.TryGetProperty("id", out string? toolId) && toolId != null)
                        {
                            var parameters = messageObj.TryGetProperty("parameters", out Dictionary<string, object>? toolParams) ? 
                                toolParams : new Dictionary<string, object>();
                            
                            HandleExecuteTool(toolId, parameters ?? new Dictionary<string, object>());
                        }
                        break;
                    
                    case "getvoicesettings":
                        HandleGetVoiceSettings();
                        break;
                    
                    case "setvoiceinput":
                        if (messageObj.TryGetProperty("enabled", out bool? inputEnabled) && inputEnabled.HasValue)
                        {
                            _voiceService.SetVoiceInputEnabled(inputEnabled.Value);
                            SendMessageToUI("voiceSettings", _voiceService.GetVoiceSettings());
                        }
                        break;
                    
                    case "setvoiceoutput":
                        if (messageObj.TryGetProperty("enabled", out bool? outputEnabled) && outputEnabled.HasValue)
                        {
                            _voiceService.SetVoiceOutputEnabled(outputEnabled.Value);
                            SendMessageToUI("voiceSettings", _voiceService.GetVoiceSettings());
                        }
                        break;
                    
                    case "testvoice":
                        Task.Run(() => _voiceService.TestVoiceAsync());
                        break;
                        
                    case "getaudiodevices":
                        HandleGetAudioDevices();
                        break;
                        
                    case "refreshaudiodevices":
                        _voiceService.RefreshAudioDevices();
                        HandleGetAudioDevices();
                        break;
                        
                    case "setinputdevice":
                        if (messageObj.TryGetProperty("index", out int? inputDeviceIndex) && inputDeviceIndex.HasValue)
                        {
                            _voiceService.SetInputDevice(inputDeviceIndex.Value);
                        }
                        break;
                        
                    case "setoutputdevice":
                        if (messageObj.TryGetProperty("index", out int? outputDeviceIndex) && outputDeviceIndex.HasValue)
                        {
                            _voiceService.SetOutputDevice(outputDeviceIndex.Value);
                        }
                        break;
                    
                    case "getsettings":
                        HandleGetSettings();
                        break;
                    
                    case "savesettings":
                        if (messageObj.TryGetProperty("settings", out Dictionary<string, object>? settings) && settings != null)
                        {
                            HandleSaveSettings(settings);
                        }
                        break;
                    
                    case "getllmproviders":
                        HandleGetLLMProviders();
                        break;
                    
                    case "getllmmodels":
                        HandleGetLLMModels();
                        break;
                    
                    case "testllmconnection":
                        Task.Run(() => TestLLMConnectionAsync());
                        break;
                    
                    case "clearconversationhistory":
                        _contextManager.ClearHistory();
                        SendMessageToUI("conversationCleared", new { success = true });
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling web message: {ex.Message}");
            }
        }

        private void HandleGetPersonalities()
        {
            var personalities = _personalityManager.GetAllPersonalities();
            var currentPersonality = _personalityManager.CurrentPersonality;
            
            SendMessageToUI("personalitiesData", new
            {
                personalities,
                currentPersonalityId = currentPersonality?.Id
            });
        }

        private async void HandleSetPersonality(string personalityId)
        {
            var success = await _personalityManager.SetActivePersonalityAsync(personalityId);
            
            if (success)
            {
                var personality = _personalityManager.CurrentPersonality;
                SendMessageToUI("personalityChanged", new { personality });
            }
        }

        private async void HandleExecuteTool(string toolId, Dictionary<string, object> parameters)
        {
            var result = await _conversationService.ExecuteToolAsync(toolId, parameters);
            
            SendMessageToUI("toolExecuted", new
            {
                toolId,
                success = result.Success,
                result = result.Output,
                error = result.Error
            });
        }

        private void HandleGetVoiceSettings()
        {
            SendMessageToUI("voiceSettings", _voiceService.GetVoiceSettings());
        }
        
        private void HandleGetAudioDevices()
        {
            try
            {
                var inputDevices = _voiceService.GetInputDevices();
                var outputDevices = _voiceService.GetOutputDevices();
                
                Console.WriteLine($"Sending audio devices to UI: {inputDevices.Count} input devices, {outputDevices.Count} output devices");
                foreach (var device in inputDevices)
                {
                    Console.WriteLine($"Input device: {device.Name} (Index: {device.Index})");
                }
                
                SendMessageToUI("audioDevicesData", new
                {
                    inputDevices,
                    outputDevices
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting audio devices: {ex.Message}");
                
                // Send at least the default device as fallback
                SendMessageToUI("audioDevicesData", new
                {
                    inputDevices = new[] { new { index = -1, name = "Default Device" } },
                    outputDevices = new[] { new { index = -1, name = "Default Device" } }
                });
            }
        }

        private void HandleGetSettings()
        {
            // Get settings from various services
            var settings = new
            {
                llm = new
                {
                    provider = "lmstudio",
                    model = "local-model",
                    baseUrl = "http://localhost:1234/v1",
                    apiKey = "",
                    temperature = 0.7f,
                    maxTokens = 1024
                },
                voice = _voiceService.GetVoiceSettings(),
                ui = new
                {
                    startupBehavior = "remember",
                    theme = "light",
                    alwaysOnTop = true
                }
            };
            
            SendMessageToUI("settingsData", new { settings });
        }

        private void HandleSaveSettings(Dictionary<string, object> settings)
        {
            // This would update settings across all services
            // For now, we'll just acknowledge receipt
            
            SendMessageToUI("settingsSaved", new { success = true });
        }

        private void HandleGetLLMProviders()
        {
            var providers = _llmFactory.GetSupportedProviders();
            var providersList = new List<object>();
            
            foreach (var provider in providers)
            {
                providersList.Add(new
                {
                    id = provider.ToString().ToLowerInvariant(),
                    displayName = _llmFactory.GetProviderDisplayName(provider)
                });
            }
            
            SendMessageToUI("llmProvidersData", new { providers = providersList });
        }

        private async void HandleGetLLMModels()
        {
            try
            {
                var models = await _llmClient.GetAvailableModelsAsync();
                var modelsList = new List<object>();
                
                foreach (var model in models)
                {
                    modelsList.Add(new
                    {
                        id = model,
                        displayName = model
                    });
                }
                
                SendMessageToUI("llmModelsData", new { models = modelsList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting LLM models: {ex.Message}");
                SendMessageToUI("llmModelsData", new
                {
                    models = new[]
                    {
                        new { id = "local-model", displayName = "Default Local Model" }
                    }
                });
            }
        }

        private async Task TestLLMConnectionAsync()
        {
            try
            {
                // Simple test message
                var messages = new List<LLMMessage>
                {
                    new LLMMessage { Role = "user", Content = "Hello, are you working?" }
                };
                
                var response = await _llmClient.GetChatCompletionAsync(messages, "local-model");
                
                SendMessageToUI("llmConnectionTested", new
                {
                    success = true,
                    message = "LLM connection successful"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing LLM connection: {ex.Message}");
                SendMessageToUI("llmConnectionTested", new
                {
                    success = false,
                    message = $"LLM connection failed: {ex.Message}"
                });
            }
        }

        // Event handlers

        private void OnAssistantStateChanged(object? sender, AssistantStateChangedEventArgs e)
        {
            var stateString = e.State.ToString().ToLowerInvariant();
            SendMessageToUI("assistantStateChanged", new { state = stateString });
        }

        private void OnQueryUpdated(object? sender, string query)
        {
            SendMessageToUI("queryUpdate", new { query });
        }

        private void OnResponseUpdated(object? sender, ResponseUpdateEventArgs e)
        {
            SendMessageToUI("responseUpdate", new
            {
                response = e.Response,
                isComplete = e.IsComplete,
                state = e.IsComplete ? "idle" : "responding"
            });
        }

        private void OnToolExecuted(object? sender, ToolExecutedEventArgs e)
        {
            SendMessageToUI("toolExecuted", new
            {
                toolId = e.ToolId,
                success = e.Result.Success,
                result = e.Result.Output,
                error = e.Result.Error
            });
        }

        private void OnPersonalityChanged(object? sender, PersonalityChangedEventArgs e)
        {
            SendMessageToUI("personalityChanged", new { personality = e.NewPersonality });
        }

        private void OnAudioLevelChanged(object? sender, float[] levels)
        {
            SendMessageToUI("audioLevels", new { levels });
        }

        /// <summary>
        /// Sends a message to the UI.
        /// </summary>
        /// <param name="action">The action type.</param>
        /// <param name="data">The data to send.</param>
        private void SendMessageToUI(string action, object data)
        {
            try
            {
                var message = new { action, data };
                string json = JsonSerializer.Serialize(message, _jsonOptions);
                _webView.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message to UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper class for deserializing messages from the UI.
        /// </summary>
        private class WebMessage
        {
            /// <summary>
            /// Gets or sets the action of the message.
            /// </summary>
            public string Action { get; set; } = string.Empty;
            
            /// <summary>
            /// Gets the data element of the message.
            /// </summary>
            public JsonElement Data { get; set; }
            
            /// <summary>
            /// Tries to get a property from the data element.
            /// </summary>
            /// <typeparam name="T">The type to convert the property to.</typeparam>
            /// <param name="propertyName">The name of the property.</param>
            /// <param name="value">The output value if successful.</param>
            /// <returns>True if the property was found and converted successfully, false otherwise.</returns>
            public bool TryGetProperty<T>(string propertyName, out T? value)
            {
                value = default;
                
                if (!Data.TryGetProperty(propertyName, out var property))
                {
                    return false;
                }
                
                try
                {
                    value = property.Deserialize<T>();
                    return value != null;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}