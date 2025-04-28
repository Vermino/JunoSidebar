// File: JunoSidebar/JunoSidebar.Wpf/Services/Voice/VoiceService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using System.Windows;

namespace JunoSidebar.Wpf.Services.Voice
{
    /// <summary>
    /// Service for handling voice input and output capabilities.
    /// </summary>
    public class VoiceService : IDisposable
    {
        private readonly WebService _webService;
        private readonly string _voiceDataDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        
        // Audio input properties
        private WaveInEvent? _waveIn;
        private bool _inputEnabled = true;
        private bool _isListening = false;
        private CancellationTokenSource? _listeningCts;
        private readonly object _recognitionLock = new object();

        // Audio output properties
        private IWavePlayer? _waveOut;
        private bool _outputEnabled = true;
        private string _wakeWord = "Hey Juno";
        private float _voiceSpeed = 1.0f;
        private string? _currentVoiceId;
        
        // Audio devices
        private List<AudioDevice> _inputDevices = new List<AudioDevice>();
        private List<AudioDevice> _outputDevices = new List<AudioDevice>();
        private int _selectedInputDeviceIndex = -1;
        private int _selectedOutputDeviceIndex = -1;
        
        // Audio processing buffer for visualization
        private readonly Queue<float> _audioLevelBuffer = new Queue<float>();
        private const int AudioLevelBufferSize = 20;

        /// <summary>
        /// Event raised when the wake word is detected.
        /// </summary>
        public event EventHandler<EventArgs>? WakeWordDetected;
        
        /// <summary>
        /// Event raised when speech input is recognized.
        /// </summary>
        public event EventHandler<SpeechRecognizedEventArgs>? SpeechRecognized;
        
        /// <summary>
        /// Event raised when audio levels change during listening.
        /// </summary>
        public event EventHandler<float[]>? AudioLevelChanged;
        
        /// <summary>
        /// Gets whether voice input is currently enabled.
        /// </summary>
        public bool InputEnabled => _inputEnabled;
        
        /// <summary>
        /// Gets whether voice output is currently enabled.
        /// </summary>
        public bool OutputEnabled => _outputEnabled;
        
        /// <summary>
        /// Gets the wake word currently in use.
        /// </summary>
        public string WakeWord => _wakeWord;
        
        /// <summary>
        /// Gets whether the service is currently actively listening.
        /// </summary>
        public bool IsListening => _isListening;

/// <summary>
        /// Initializes a new instance of the VoiceService class.
        /// </summary>
        /// <param name="webService">The web service for accessing external voice APIs.</param>
        /// <param name="voiceDataDirectory">The directory for storing voice data.</param>
        public VoiceService(WebService webService, string? voiceDataDirectory = null)
        {
            _webService = webService ?? throw new ArgumentNullException(nameof(webService));
            
            _voiceDataDirectory = voiceDataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "VoiceData");
            
            if (!Directory.Exists(_voiceDataDirectory))
            {
                Directory.CreateDirectory(_voiceDataDirectory);
            }
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            // Initialize default audio levels
            for (int i = 0; i < AudioLevelBufferSize; i++)
            {
                _audioLevelBuffer.Enqueue(0f);
            }
            
            Console.WriteLine("Initializing VoiceService...");
            
            try
            {
                // Initialize audio devices with defaults
                _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                
                // Load saved voice settings
                LoadVoiceSettings();
                
                // Then try to refresh audio devices
                RefreshAudioDevices();
                
                Console.WriteLine("VoiceService initialization complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during VoiceService initialization: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Refreshes the list of available audio devices.
        /// </summary>
        public void RefreshAudioDevices()
        {
            try
            {
                // Log the start of device refresh
                Console.WriteLine("Refreshing audio devices...");
                
                // Get input devices
                _inputDevices.Clear();
                
                // Add default device
                _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                
                try {
                    for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                    {
                        try {
                            var capabilities = WaveInEvent.GetCapabilities(i);
                            string name = capabilities.ProductName;
                            if (string.IsNullOrEmpty(name))
                                name = $"Input Device {i}";
                                
                            Console.WriteLine($"Found input device: {name} (Index: {i})");
                            _inputDevices.Add(new AudioDevice { Index = i, Name = name });
                        }
                        catch (Exception ex) {
                            Console.WriteLine($"Error getting input device {i}: {ex.Message}");
                        }
                    }
                } 
                catch (Exception ex) {
                    Console.WriteLine($"Error enumerating input devices: {ex.Message}");
                }

                // Get output devices
                _outputDevices.Clear();
                
                // Add default device
                _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                
                try {
                    for (int i = 0; i < WaveOut.DeviceCount; i++)
                    {
                        try {
                            var capabilities = WaveOut.GetCapabilities(i);
                            string name = capabilities.ProductName;
                            if (string.IsNullOrEmpty(name))
                                name = $"Output Device {i}";
                                
                            Console.WriteLine($"Found output device: {name} (Index: {i})");
                            _outputDevices.Add(new AudioDevice { Index = i, Name = name });
                        }
                        catch (Exception ex) {
                            Console.WriteLine($"Error getting output device {i}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error enumerating output devices: {ex.Message}");
                }

                // Set defaults if no devices are selected
                if (_selectedInputDeviceIndex == -1 && _inputDevices.Count > 0)
                {
                    _selectedInputDeviceIndex = -1; // Default device
                    Console.WriteLine("Using default input device");
                }

                if (_selectedOutputDeviceIndex == -1 && _outputDevices.Count > 0)
                {
                    _selectedOutputDeviceIndex = -1; // Default device
                    Console.WriteLine("Using default output device");
                }
                
                Console.WriteLine($"Audio device refresh complete. Found {_inputDevices.Count} input devices and {_outputDevices.Count} output devices.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical error refreshing audio devices: {ex.Message}");
                // Ensure we at least have the default device
                if (_inputDevices.Count == 0)
                {
                    _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                }
                if (_outputDevices.Count == 0)
                {
                    _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                }
            }
        }

        /// <summary>
        /// Gets a list of available input devices.
        /// </summary>
        /// <returns>A list of input devices.</returns>
        public List<AudioDevice> GetInputDevices()
        {
            return _inputDevices;
        }

        /// <summary>
        /// Gets a list of available output devices.
        /// </summary>
        /// <returns>A list of output devices.</returns>
        public List<AudioDevice> GetOutputDevices()
        {
            return _outputDevices;
        }

        /// <summary>
        /// Sets the input device to use.
        /// </summary>
        /// <param name="deviceIndex">The index of the device to use.</param>
        public void SetInputDevice(int deviceIndex)
        {
            if (_selectedInputDeviceIndex == deviceIndex)
                return;

            _selectedInputDeviceIndex = deviceIndex;
            SaveVoiceSettings();

            // Restart audio input if it's active
            if (_inputEnabled && _waveIn != null)
            {
                StopAudioInput();
                StartAudioInput();
            }
        }

        /// <summary>
        /// Sets the output device to use.
        /// </summary>
        /// <param name="deviceIndex">The index of the device to use.</param>
        public void SetOutputDevice(int deviceIndex)
        {
            if (_selectedOutputDeviceIndex == deviceIndex)
                return;

            _selectedOutputDeviceIndex = deviceIndex;
            SaveVoiceSettings();

            // Recreate output device if needed
            if (_outputEnabled && _waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
        }

        /// <summary>
        /// Initializes the speech recognition engine.
        /// </summary>
        public void InitializeSpeechRecognition()
        {
            try
            {
                StartAudioInput();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing speech recognition: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Could not initialize speech recognition: {ex.Message}\n\nPlease check your microphone and try again.",
                        "Speech Recognition Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }

        /// <summary>
        /// Enables or disables voice input.
        /// </summary>
        /// <param name="enabled">Whether voice input should be enabled.</param>
        public void SetVoiceInputEnabled(bool enabled)
        {
            if (_inputEnabled == enabled)
                return;
            
            _inputEnabled = enabled;
            
            if (enabled)
            {
                StartAudioInput();
            }
            else
            {
                StopAudioInput();
            }
            
            SaveVoiceSettings();
        }

        /// <summary>
        /// Enables or disables voice output.
        /// </summary>
        /// <param name="enabled">Whether voice output should be enabled.</param>
        public void SetVoiceOutputEnabled(bool enabled)
        {
            if (_outputEnabled == enabled)
                return;
            
            _outputEnabled = enabled;
            
            if (!enabled)
            {
                StopSpeaking();
            }
            
            SaveVoiceSettings();
        }

        /// <summary>
        /// Sets the wake word for voice activation.
        /// </summary>
        /// <param name="wakeWord">The new wake word.</param>
        public void SetWakeWord(string wakeWord)
        {
            if (string.IsNullOrWhiteSpace(wakeWord) || _wakeWord == wakeWord)
                return;
            
            _wakeWord = wakeWord;
            SaveVoiceSettings();
        }

        /// <summary>
        /// Sets the voice to use for speech synthesis.
        /// </summary>
        /// <param name="voiceId">The ID of the voice to use.</param>
        public void SetVoice(string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId) || _currentVoiceId == voiceId)
                return;
            
            _currentVoiceId = voiceId;
            SaveVoiceSettings();
        }

        /// <summary>
        /// Sets the speech rate for voice output.
        /// </summary>
        /// <param name="rate">The speed factor for speech output (1.0 = normal speed).</param>
        public void SetVoiceSpeed(float rate)
        {
            if (rate < 0.5f || rate > 2.0f || Math.Abs(rate - _voiceSpeed) < 0.01f)
                return;
            
            _voiceSpeed = rate;
            SaveVoiceSettings();
        }

        /// <summary>
        /// Starts actively listening for voice input.
        /// </summary>
        public void StartListening()
        {
            lock (_recognitionLock)
            {
                if (_isListening)
                    return;
                
                if (!_inputEnabled)
                    return;
                
                _isListening = true;
                _listeningCts = new CancellationTokenSource();
                
                // In a real implementation, this would start more focused listening
                // or activate a dedicated speech recognition service
            }
        }

        /// <summary>
        /// Stops actively listening for user input.
        /// </summary>
        public void StopListening()
        {
            lock (_recognitionLock)
            {
                if (!_isListening)
                    return;
                
                _isListening = false;
                _listeningCts?.Cancel();
                _listeningCts = null;
            }
        }

        /// <summary>
        /// Speaks the provided text using the configured voice settings.
        /// </summary>
        /// <param name="text">The text to speak.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SpeakAsync(string text)
        {
            if (!_outputEnabled || string.IsNullOrWhiteSpace(text))
                return;
            
            try
            {
                // For simplicity, we'll use the external ElevenLabs TTS service as a fallback
                // In a real implementation, this would check if System.Speech.Synthesis is available first
                
                // This simulates the TTS operation for now
                await Task.Delay(500); // Simulated processing time
                
                // Notify that speech has completed
                await Task.Delay((int)(text.Length * 50 * (1.0 / _voiceSpeed))); // Simulate speech duration
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error speaking text: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops any ongoing speech output.
        /// </summary>
        public void StopSpeaking()
        {
            // In a real implementation, this would stop the speech synthesizer
            // For now, we'll just cancel any ongoing operations
        }

        /// <summary>
        /// Tests the voice output with a sample message.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task TestVoiceAsync()
        {
            await SpeakAsync("Hello, I'm Juno, your AI assistant. How can I help you today?");
        }

        /// <summary>
        /// Gets the current voice settings.
        /// </summary>
        /// <returns>A dictionary containing the current voice settings.</returns>
        public Dictionary<string, object> GetVoiceSettings()
        {
            return new Dictionary<string, object>
            {
                ["inputEnabled"] = _inputEnabled,
                ["outputEnabled"] = _outputEnabled,
                ["wakeWord"] = _wakeWord,
                ["voiceId"] = _currentVoiceId ?? "default",
                ["speed"] = _voiceSpeed,
                ["inputDeviceIndex"] = _selectedInputDeviceIndex,
                ["outputDeviceIndex"] = _selectedOutputDeviceIndex
            };
        }

        /// <summary>
        /// Gets information about available voices.
        /// </summary>
        /// <returns>A list of available voices.</returns>
        public List<VoiceInfo> GetAvailableVoices()
        {
            // In a real implementation, this would query available system voices
            // For now, we'll return a simple list of example voices
            
            return new List<VoiceInfo>
            {
                new VoiceInfo { Id = "default", Name = "Default Voice", Gender = "Neutral", Age = "Adult", Culture = "en-US" },
                new VoiceInfo { Id = "male1", Name = "Male Voice 1", Gender = "Male", Age = "Adult", Culture = "en-US" },
                new VoiceInfo { Id = "female1", Name = "Female Voice 1", Gender = "Female", Age = "Adult", Culture = "en-US" }
            };
        }

        // Private helper methods for audio handling

        private void StartAudioInput()
        {
            try
            {
                if (_waveIn != null)
                {
                    StopAudioInput();
                }
                
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = _selectedInputDeviceIndex,
                    WaveFormat = new WaveFormat(16000, 1) // 16kHz mono, suitable for speech recognition
                };
                
                _waveIn.DataAvailable += OnAudioDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting audio input: {ex.Message}");
                _waveIn = null;
            }
        }

        private void StopAudioInput()
        {
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping audio input: {ex.Message}");
                }
                finally
                {
                    _waveIn = null;
                }
            }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            // Calculate audio level using RMS (Root Mean Square)
            float audioLevel = CalculateAudioLevel(e.Buffer, e.BytesRecorded);
            
            // Add to buffer and remove oldest value to maintain fixed size
            _audioLevelBuffer.Enqueue(audioLevel);
            if (_audioLevelBuffer.Count > AudioLevelBufferSize)
            {
                _audioLevelBuffer.Dequeue();
            }
            
            // Send audio levels to UI for visualization
            AudioLevelChanged?.Invoke(this, _audioLevelBuffer.ToArray());
            
            // In a real implementation, this would pass the audio data to a speech recognition engine
            // For demonstration, we'll simulate speech recognition occasionally

            // If audio level exceeds threshold, simulate speech detection
            if (_isListening && audioLevel > 0.5f)
            {
                // This is just a placeholder for real speech recognition
                // In a real application, this would use a speech recognition engine
                // We're just simulating here for demonstration purposes
                
                // Occasionally simulate recognition of a test phrase (about 1 in 20 chance when level is high)
                if (new Random().Next(60) == 0)
                {
                    string recognizedText = "Hello Juno, what's the weather today?";
                    SpeechRecognized?.Invoke(this, new SpeechRecognizedEventArgs(recognizedText, 0.9f));
                }
            }
            else if (!_isListening && audioLevel > 0.7f)
            {
                // Occasionally simulate wake word detection
                if (new Random().Next(100) == 0)
                {
                    WakeWordDetected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private float CalculateAudioLevel(byte[] buffer, int bytesRecorded)
        {
            // Calculate RMS (Root Mean Square) value as audio level
            // Formula: sqrt(sum(x^2) / n) where x are audio samples
            
            if (bytesRecorded == 0)
                return 0f;
            
            // Process 16-bit samples
            int samplesCount = bytesRecorded / 2;
            double sumSquared = 0;
            
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                double normalizedSample = sample / 32768.0; // Normalize to -1.0 to 1.0
                sumSquared += normalizedSample * normalizedSample;
            }
            
            // Calculate RMS
            double rms = Math.Sqrt(sumSquared / samplesCount);
            
            // Normalize to 0.0 to 1.0 range with some emphasis on lower values
            float level = (float)Math.Min(1.0, Math.Max(0.0, rms * 4.0));
            
            return level;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Console.WriteLine($"Recording stopped with error: {e.Exception.Message}");
            }
        }

        private void LoadVoiceSettings()
        {
            string settingsPath = Path.Combine(_voiceDataDirectory, "voice_settings.json");
            
            if (!File.Exists(settingsPath))
                return;
            
            try
            {
                string json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOptions);
                
                if (settings == null)
                    return;
                
                if (settings.TryGetValue("inputEnabled", out var inputEnabled))
                {
                    _inputEnabled = inputEnabled.GetBoolean();
                }
                
                if (settings.TryGetValue("outputEnabled", out var outputEnabled))
                {
                    _outputEnabled = outputEnabled.GetBoolean();
                }
                
                if (settings.TryGetValue("wakeWord", out var wakeWord))
                {
                    _wakeWord = wakeWord.GetString() ?? "Hey Juno";
                }
                
                if (settings.TryGetValue("voiceId", out var voiceId))
                {
                    _currentVoiceId = voiceId.GetString();
                }
                
                if (settings.TryGetValue("speed", out var speed))
                {
                    _voiceSpeed = speed.GetSingle();
                }
                
                if (settings.TryGetValue("inputDeviceIndex", out var inputDeviceIndex))
                {
                    _selectedInputDeviceIndex = inputDeviceIndex.GetInt32();
                }
                
                if (settings.TryGetValue("outputDeviceIndex", out var outputDeviceIndex))
                {
                    _selectedOutputDeviceIndex = outputDeviceIndex.GetInt32();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading voice settings: {ex.Message}");
            }
        }

        private void SaveVoiceSettings()
        {
            string settingsPath = Path.Combine(_voiceDataDirectory, "voice_settings.json");
            
            try
            {
                var settings = new Dictionary<string, object>
                {
                    ["inputEnabled"] = _inputEnabled,
                    ["outputEnabled"] = _outputEnabled,
                    ["wakeWord"] = _wakeWord,
                    ["voiceId"] = _currentVoiceId ?? "default",
                    ["speed"] = _voiceSpeed,
                    ["inputDeviceIndex"] = _selectedInputDeviceIndex,
                    ["outputDeviceIndex"] = _selectedOutputDeviceIndex
                };
                
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving voice settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes the resources used by the voice service.
        /// </summary>
        public void Dispose()
        {
            StopAudioInput();
            StopSpeaking();
            
            _listeningCts?.Cancel();
            _listeningCts?.Dispose();
        }
    }

    /// <summary>
    /// Information about a voice for speech synthesis.
    /// </summary>
    public class VoiceInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for the voice.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the display name of the voice.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the gender of the voice.
        /// </summary>
        public string Gender { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the age category of the voice.
        /// </summary>
        public string Age { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the culture/language of the voice.
        /// </summary>
        public string Culture { get; set; } = string.Empty;
    }

    /// <summary>
    /// Information about an audio device.
    /// </summary>
    public class AudioDevice
    {
        /// <summary>
        /// Gets or sets the device index.
        /// </summary>
        public int Index { get; set; }
        
        /// <summary>
        /// Gets or sets the device name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event args for speech recognition events.
    /// </summary>
    public class SpeechRecognizedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the recognized text.
        /// </summary>
        public string Text { get; }
        
        /// <summary>
        /// Gets the confidence score of the recognition.
        /// </summary>
        public float Confidence { get; }
        
        /// <summary>
        /// Initializes a new instance of the SpeechRecognizedEventArgs class.
        /// </summary>
        /// <param name="text">The recognized text.</param>
        /// <param name="confidence">The confidence score.</param>
        public SpeechRecognizedEventArgs(string text, float confidence)
        {
            Text = text;
            Confidence = confidence;
        }
    }
}