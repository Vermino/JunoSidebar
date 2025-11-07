// File: JunoSidebar.Wpf/Services/CoreEngine.cs

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Linq;
using JunoSidebar.Wpf.Models;
using JunoSidebar.Wpf.Services.LLM;
using JunoSidebar.Wpf.Services.Tools;
using JunoSidebar.Wpf.Services.Voice;
using Microsoft.Web.WebView2.Core;

namespace JunoSidebar.Wpf.Services
{
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
        private readonly string _settingsPath;
        private readonly string _llmSettingsPath;
        private readonly string _uiSettingsPath;
        private bool _isInitialized = false;
        private bool _isSavingSettings = false;

        public CoreEngine(CoreWebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            
            // Initialize paths
            string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string junoDir = Path.Combine(appDataDir, "JunoSidebar");
            
            _settingsPath = Path.Combine(junoDir, "settings.json");
            _llmSettingsPath = Path.Combine(junoDir, "llm_settings.json");
            _uiSettingsPath = Path.Combine(junoDir, "ui_settings.json");
            
            // Ensure directories exist
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
            
            // Initialize services
            _webService = new WebService();
            _permissionManager = new PermissionManager();
            _llmFactory = new LLMFactory();
            _llmClient = _llmFactory.CreateClient(LLMFactory.LLMProvider.LMStudio);
            _contextManager = new ContextManager();
            _personalityManager = new PersonalityManager();
            _toolRegistry = new ToolRegistry(_permissionManager);
            _voiceService = new VoiceService(_webService);
            _conversationService = new ConversationService(
                _llmClient,
                _contextManager,
                _personalityManager,
                _toolRegistry,
                _voiceService
            );
            
            // Register for events
            _webView.WebMessageReceived += OnWebMessageReceived;
            _conversationService.AssistantStateChanged += OnAssistantStateChanged;
            _conversationService.QueryUpdated += OnQueryUpdated;
            _conversationService.ResponseUpdated += OnResponseUpdated;
            _conversationService.ToolExecuted += OnToolExecuted;
            _personalityManager.PersonalityChanged += OnPersonalityChanged;
            _voiceService.AudioLevelChanged += OnAudioLevelChanged;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;
            
            try
            {
                Debug.WriteLine("CoreEngine: Initializing services...");
                
                // Initialize services in order
                await _permissionManager.InitializeAsync();
                await _personalityManager.InitializeAsync();
                await _toolRegistry.InitializeAsync();
                await ToolRegistryInitializer.RegisterBuiltInToolsAsync(_toolRegistry);
                
                _voiceService.InitializeSpeechRecognition();
                
                // Apply saved settings
                await ApplyLLMSettings();
                
                _isInitialized = true;
                
                SendMessageToUI("initialized", new { success = true });
                Debug.WriteLine("CoreEngine: Initialization completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error initializing services: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                SendMessageToUI("initializationError", new { error = ex.Message });
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Use WebMessageAsString instead of WebMessageAsJson to avoid double-encoding
                string message = e.TryGetWebMessageAsString();
                Debug.WriteLine($"CoreEngine: Received message from UI: {message}");

                // Log raw message to Debug Console for troubleshooting
                DebugLogger.Instance.Log($"RAW MESSAGE: {message}", "System", LogLevel.Debug);

                var messageObj = JsonSerializer.Deserialize<WebMessage>(message, _jsonOptions);
                if (messageObj == null || string.IsNullOrEmpty(messageObj.Action))
                {
                    Debug.WriteLine("CoreEngine: Invalid message received: null or empty action");
                    DebugLogger.Instance.LogError("Invalid message received: null or empty action", "System");
                    return;
                }

                string action = messageObj.Action.ToLowerInvariant();
                DebugLogger.Instance.Log($"Received UI message: {action}", "System", LogLevel.Info);

                switch (action)
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
                        
                    case "poweron":
                        _voiceService.PowerOn();
                        SendMessageToUI("voiceSettings", _voiceService.GetVoiceSettings());
                        break;

                    case "poweroff":
                        _voiceService.PowerOff();
                        SendMessageToUI("voiceSettings", _voiceService.GetVoiceSettings());
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
                        DebugLogger.Instance.LogVoice("Received request for audio devices");
                        HandleGetAudioDevices();
                        break;

                    case "refreshaudiodevices":
                        DebugLogger.Instance.LogVoice("Refreshing audio devices");
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
                        
                    case "managetools":
                        HandleManageTools();
                        break;
                        
                    default:
                        Debug.WriteLine($"CoreEngine: Unknown action received: {messageObj.Action}");
                        DebugLogger.Instance.Log($"Unknown action received: {action}", "System", LogLevel.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error handling web message: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void HandleManageTools()
        {
            var tools = _toolRegistry.GetAllTools();
            var toolsList = new List<object>();
            
            foreach (var tool in tools)
            {
                toolsList.Add(new
                {
                    id = tool.Id,
                    name = tool.Name,
                    description = tool.Description,
                    version = tool.Version,
                    enabled = true,
                    requiredPermissions = tool.RequiredPermissions,
                    hasPermission = tool.RequiredPermissions.All(p => _permissionManager.HasPermission(p))
                });
            }
            
            SendMessageToUI("toolsData", new { tools = toolsList });
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
            try
            {
                Debug.WriteLine($"CoreEngine: Executing tool: {toolId} with parameters: {JsonSerializer.Serialize(parameters, _jsonOptions)}");
                
                var result = await _conversationService.ExecuteToolAsync(toolId, parameters);
                
                SendMessageToUI("toolExecuted", new
                {
                    toolId,
                    success = result.Success,
                    result = result.Output ?? "No output",
                    error = result.Error ?? ""
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error executing tool {toolId}: {ex.Message}");
                
                SendMessageToUI("toolExecuted", new
                {
                    toolId,
                    success = false,
                    result = "Error",  
                    error = $"Error executing tool: {ex.Message}"
                });
            }
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

                DebugLogger.Instance.LogVoice($"Sending {inputDevices.Count} input and {outputDevices.Count} output devices to UI");
                Debug.WriteLine($"CoreEngine: Sending audio devices to UI: {inputDevices.Count} input devices, {outputDevices.Count} output devices");

                foreach (var device in inputDevices)
                {
                    DebugLogger.Instance.LogVoice($"  Input: {device.Name} (Index: {device.Index})");
                    Debug.WriteLine($"CoreEngine: Input device: {device.Name} (Index: {device.Index})");
                }

                var message = new
                {
                    inputDevices,
                    outputDevices
                };

                // Log the serialized JSON for debugging
                string debugJson = JsonSerializer.Serialize(message, _jsonOptions);
                DebugLogger.Instance.LogVoice($"Audio devices JSON: {debugJson}");

                SendMessageToUI("audioDevicesData", message);

                DebugLogger.Instance.LogVoice("Audio devices data sent to UI");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error getting audio devices: {ex.Message}", "Voice");
                Debug.WriteLine($"CoreEngine: Error getting audio devices: {ex.Message}");

                SendMessageToUI("audioDevicesData", new
                {
                    inputDevices = new[] { new { index = -1, name = "Default Device" } },
                    outputDevices = new[] { new { index = -1, name = "Default Device" } }
                });
            }
        }

        private void HandleGetSettings()
        {
            try
            {
                var llmSettings = LoadLLMSettings();
                var voiceSettings = _voiceService.GetVoiceSettings();
                var uiSettings = LoadUISettings();
                
                var settings = new
                {
                    llm = llmSettings,
                    voice = voiceSettings,
                    ui = uiSettings
                };
                
                SendMessageToUI("settingsData", new { settings });
                Debug.WriteLine("CoreEngine: Settings sent to UI");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error getting settings: {ex.Message}");
                SendMessageToUI("settingsError", new { error = $"Failed to load settings: {ex.Message}" });
            }
        }

        private async void HandleSaveSettings(Dictionary<string, object> settings)
        {
            if (_isSavingSettings)
            {
                Debug.WriteLine("CoreEngine: Save operation already in progress, ignoring duplicate request");
                return;
            }
            
            _isSavingSettings = true;
            
            try
            {
                Debug.WriteLine($"CoreEngine: Saving settings...");
                string previousProvider = "";
                
                if (settings.TryGetValue("llm", out var llmObj))
                {
                    Dictionary<string, object>? llmSettings = null;
                    
                    // Handle different types that might come from deserialization
                    if (llmObj is JsonElement llmElem)
                    {
                        llmSettings = llmElem.Deserialize<Dictionary<string, object>>(_jsonOptions);
                    }
                    else if (llmObj is Dictionary<string, object> dictObj)
                    {
                        llmSettings = dictObj;
                    }
                    
                    if (llmSettings != null)
                    {
                        // Get previous provider before saving
                        var currentSettings = LoadLLMSettings();
                        if (currentSettings.TryGetValue("provider", out var providerObj))
                        {
                            previousProvider = providerObj.ToString() ?? "";
                        }
                        
                        SaveLLMSettings(llmSettings);
                    }
                }
                
                if (settings.TryGetValue("voice", out var voiceObj))
                {
                    Dictionary<string, object>? voiceSettings = null;
                    
                    if (voiceObj is JsonElement voiceElem)
                    {
                        voiceSettings = voiceElem.Deserialize<Dictionary<string, object>>(_jsonOptions);
                    }
                    else if (voiceObj is Dictionary<string, object> dictObj)
                    {
                        voiceSettings = dictObj;
                    }
                    
                    if (voiceSettings != null)
                    {
                        SaveVoiceSettings(voiceSettings);
                    }
                }
                
                if (settings.TryGetValue("ui", out var uiObj))
                {
                    Dictionary<string, object>? uiSettings = null;
                    
                    if (uiObj is JsonElement uiElem)
                    {
                        uiSettings = uiElem.Deserialize<Dictionary<string, object>>(_jsonOptions);
                    }
                    else if (uiObj is Dictionary<string, object> dictObj)
                    {
                        uiSettings = dictObj;
                    }
                    
                    if (uiSettings != null)
                    {
                        SaveUISettings(uiSettings);
                    }
                }
                
                // Save complete settings
                string settingsDir = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }
                
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                await File.WriteAllTextAsync(_settingsPath, json);
                
                // Apply LLM settings
                await ApplyLLMSettings();
                
                // Send success notification with previous provider for comparison
                SendMessageToUI("settingsSaved", new { 
                    success = true,
                    previousProvider = previousProvider
                });
                
                Debug.WriteLine("CoreEngine: Settings saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error saving settings: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                SendMessageToUI("settingsSaved", new { success = false, error = ex.Message });
            }
            finally
            {
                _isSavingSettings = false;
            }
        }

        private void SaveLLMSettings(Dictionary<string, object> llmSettings)
        {
            if (llmSettings == null)
                return;
            
            Debug.WriteLine("CoreEngine: Saving LLM settings");
            
            // Extract properties with error handling
            string? provider = GetStringValue(llmSettings, "provider");
            string? model = GetStringValue(llmSettings, "model");
            string? baseUrl = GetStringValue(llmSettings, "baseUrl");
            string? apiKey = GetStringValue(llmSettings, "apiKey");
            float temperature = GetFloatValue(llmSettings, "temperature", 0.7f);
            int maxTokens = GetIntValue(llmSettings, "maxTokens", 1024);
            
            // Create cleaned settings dictionary
            Dictionary<string, object> settingsCache = new Dictionary<string, object>
            {
                ["provider"] = provider ?? "lmstudio",
                ["model"] = model ?? "local-model",
                ["baseUrl"] = baseUrl ?? "http://localhost:1234/v1",
                ["apiKey"] = apiKey ?? "",
                ["temperature"] = temperature,
                ["maxTokens"] = maxTokens
            };
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_llmSettingsPath));
            
            // Serialize and save
            string json = JsonSerializer.Serialize(settingsCache, _jsonOptions);
            File.WriteAllText(_llmSettingsPath, json);
            
            Debug.WriteLine("CoreEngine: LLM settings saved");
        }

        private void SaveVoiceSettings(Dictionary<string, object> voiceSettings)
        {
            if (voiceSettings == null)
                return;
            
            Debug.WriteLine("CoreEngine: Saving voice settings");
            
            // Extract boolean values
            bool inputEnabled = GetBoolValue(voiceSettings, "inputEnabled", true);
            bool outputEnabled = GetBoolValue(voiceSettings, "outputEnabled", true);
            
            // Extract other settings
            string wakeWord = GetStringValue(voiceSettings, "wakeWord") ?? "Hey Juno";
            string voiceId = GetStringValue(voiceSettings, "voiceId") ?? "default";
            float speed = GetFloatValue(voiceSettings, "speed", 1.0f);
            
            // Apply settings to voice service
            _voiceService.SetVoiceInputEnabled(inputEnabled);
            _voiceService.SetVoiceOutputEnabled(outputEnabled);
            _voiceService.SetWakeWord(wakeWord);
            _voiceService.SetVoice(voiceId);
            _voiceService.SetVoiceSpeed(speed);
            
            // Handle device settings
            int inputDeviceIndex = GetIntValue(voiceSettings, "inputDeviceIndex", -1);
            int outputDeviceIndex = GetIntValue(voiceSettings, "outputDeviceIndex", -1);
            
            _voiceService.SetInputDevice(inputDeviceIndex);
            _voiceService.SetOutputDevice(outputDeviceIndex);
            
            Debug.WriteLine("CoreEngine: Voice settings saved");
        }

        private void SaveUISettings(Dictionary<string, object> uiSettings)
        {
            if (uiSettings == null)
                return;
            
            Debug.WriteLine("CoreEngine: Saving UI settings");
            
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_uiSettingsPath));
            
            // Serialize and save
            string json = JsonSerializer.Serialize(uiSettings, _jsonOptions);
            File.WriteAllText(_uiSettingsPath, json);
            
            Debug.WriteLine("CoreEngine: UI settings saved");
        }

        private Dictionary<string, object> LoadLLMSettings()
        {
            if (File.Exists(_llmSettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_llmSettingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CoreEngine: Error loading LLM settings: {ex.Message}");
                }
            }
            
            // Return default settings if file doesn't exist or loading fails
            return new Dictionary<string, object>
            {
                ["provider"] = "lmstudio",
                ["model"] = "local-model",
                ["baseUrl"] = "http://localhost:1234/v1",
                ["apiKey"] = "",
                ["temperature"] = 0.7f,
                ["maxTokens"] = 1024
            };
        }

        private Dictionary<string, object> LoadUISettings()
        {
            if (File.Exists(_uiSettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_uiSettingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CoreEngine: Error loading UI settings: {ex.Message}");
                }
            }
            
            // Return default settings if file doesn't exist or loading fails
            return new Dictionary<string, object>
            {
                ["startupBehavior"] = "remember",
                ["theme"] = "light",
                ["alwaysOnTop"] = true
            };
        }

        private async Task ApplyLLMSettings()
        {
            try
            {
                var settings = LoadLLMSettings();
                
                string provider = GetStringValue(settings, "provider") ?? "lmstudio";
                string baseUrl = GetStringValue(settings, "baseUrl") ?? "http://localhost:1234/v1";
                string apiKey = GetStringValue(settings, "apiKey") ?? "";
                
                // Check if provider enum conversion is possible
                if (Enum.TryParse<LLMFactory.LLMProvider>(provider, true, out var providerEnum))
                {
                    // Create configuration dictionary
                    var config = new Dictionary<string, string>
                    {
                        ["BaseUrl"] = baseUrl,
                        ["ApiKey"] = apiKey
                    };
                    
                    // Initialize a new client with the settings
                    // Note: In a real implementation, we would recreate the client
                    // but for now just log the intention
                    Debug.WriteLine($"CoreEngine: Would apply LLM settings: Provider={provider}, BaseUrl={baseUrl}");
                    
                    // Force model refresh if client is a LMStudioClient
                    if (_llmClient is LMStudioClient lmStudioClient)
                    {
                        _ = await lmStudioClient.GetAvailableModelsAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error applying LLM settings: {ex.Message}");
            }
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
                Debug.WriteLine($"CoreEngine: Error getting LLM models: {ex.Message}");
                
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
                Debug.WriteLine("CoreEngine: Testing LLM connection...");
                
                if (_llmClient is LMStudioClient lmStudioClient)
                {
                    var success = await lmStudioClient.TestConnectionAsync();
                    Debug.WriteLine($"CoreEngine: Connection test result: {success}");
                    
                    SendMessageToUI("llmConnectionTested", new
                    {
                        success,
                        message = success ? "LLM connection successful" : "Could not connect to LM Studio"
                    });
                }
                else
                {
                    var models = await _llmClient.GetAvailableModelsAsync();
                    bool success = models.Any();
                    
                    SendMessageToUI("llmConnectionTested", new
                    {
                        success,
                        message = success ? "LLM connection successful" : "Could not retrieve models from LLM provider"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error testing LLM connection: {ex.Message}");
                
                SendMessageToUI("llmConnectionTested", new
                {
                    success = false,
                    message = $"LLM connection failed: {ex.Message}"
                });
            }
        }

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

        private void SendMessageToUI(string action, object data)
        {
            try
            {
                // Create a wrapper object with the action and data
                object messageObject;
                
                if (data.GetType().GetProperties().Length > 0)
                {
                    // Add the properties from the data object to a dictionary
                    var props = new Dictionary<string, object>
                    {
                        ["action"] = action
                    };
                    
                    foreach (var prop in data.GetType().GetProperties())
                    {
                        props[prop.Name] = prop.GetValue(data) ?? "";
                    }
                    
                    messageObject = props;
                }
                else
                {
                    // If data has no properties, just use action
                    messageObject = new { action };
                }
                
                // Serialize the message and send it
                string json = JsonSerializer.Serialize(messageObject, _jsonOptions);
                _webView.PostWebMessageAsJson(json);
                
                // Log only the action to avoid filling the log with large payloads
                Debug.WriteLine($"CoreEngine: Sent message to UI: {action}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CoreEngine: Error sending message to UI: {ex.Message}");
            }
        }

        // Helper methods for extracting values from dictionaries with type conversion
        private string? GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is JsonElement elem)
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        return elem.GetString();
                    }
                }
                else if (value is string strValue)
                {
                    return strValue;
                }
            }
            return null;
        }

        private bool GetBoolValue(Dictionary<string, object> dict, string key, bool defaultValue = false)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is JsonElement elem)
                {
                    if (elem.ValueKind == JsonValueKind.True)
                    {
                        return true;
                    }
                    else if (elem.ValueKind == JsonValueKind.False)
                    {
                        return false;
                    }
                    else if (elem.ValueKind == JsonValueKind.Number)
                    {
                        return elem.GetInt32() != 0;
                    }
                }
                else if (value is bool boolValue)
                {
                    return boolValue;
                }
                else if (value is int intValue)
                {
                    return intValue != 0;
                }
            }
            return defaultValue;
        }

        private int GetIntValue(Dictionary<string, object> dict, string key, int defaultValue = 0)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is JsonElement elem)
                {
                    if (elem.ValueKind == JsonValueKind.Number)
                    {
                        return elem.GetInt32();
                    }
                }
                else if (value is int intValue)
                {
                    return intValue;
                }
                else if (int.TryParse(value.ToString(), out int parsedValue))
                {
                    return parsedValue;
                }
            }
            return defaultValue;
        }

        private float GetFloatValue(Dictionary<string, object> dict, string key, float defaultValue = 0f)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is JsonElement elem)
                {
                    if (elem.ValueKind == JsonValueKind.Number)
                    {
                        return elem.GetSingle();
                    }
                }
                else if (value is float floatValue)
                {
                    return floatValue;
                }
                else if (value is double doubleValue)
                {
                    return (float)doubleValue;
                }
                else if (float.TryParse(value.ToString(), out float parsedValue))
                {
                    return parsedValue;
                }
            }
            return defaultValue;
        }

        private class WebMessage
        {
            public string Action { get; set; } = string.Empty;
            public JsonElement Data { get; set; }
            
            public bool TryGetProperty<T>(string propertyName, out T? value)
            {
                value = default;
                
                try
                {
                    // Try to find the property in the Data element
                    if (Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(propertyName, out var property))
                    {
                        value = property.Deserialize<T>(new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        return value != null;
                    }
                    
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}