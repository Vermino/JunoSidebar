// File: JunoSidebar.Wpf\Services\Voice\VoiceService.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using NAudio.Wave;
using System.Windows;

namespace JunoSidebar.Wpf.Services.Voice
{
    public class VoiceService : IDisposable
    {
        private readonly WebService _webService;
        private readonly string _voiceDataDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        private WaveInEvent? _waveIn;
        private bool _inputEnabled = true;
        private bool _isListening = false;
        private CancellationTokenSource? _listeningCts;
        private readonly object _recognitionLock = new object();
        private IWavePlayer? _waveOut;
        private bool _outputEnabled = true;
        private string _wakeWord = "Hey Juno";
        private float _voiceSpeed = 1.0f;
        private string? _currentVoiceId;
        private List<AudioDevice> _inputDevices = new List<AudioDevice>();
        private List<AudioDevice> _outputDevices = new List<AudioDevice>();
        private int _selectedInputDeviceIndex = -1;
        private int _selectedOutputDeviceIndex = -1;
        private readonly Queue<float> _audioLevelBuffer = new Queue<float>();
        private const int AudioLevelBufferSize = 20;
        private readonly Random _random = new Random();
        private float _simulatedAudioLevel = 0f;
        private readonly Timer? _audioLevelTimer;

        public event EventHandler<EventArgs>? WakeWordDetected;
        public event EventHandler<SpeechRecognizedEventArgs>? SpeechRecognized;
        public event EventHandler<float[]>? AudioLevelChanged;

        public bool InputEnabled => _inputEnabled;
        public bool OutputEnabled => _outputEnabled;
        public string WakeWord => _wakeWord;
        public bool IsListening => _isListening;

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
            for (int i = 0; i < AudioLevelBufferSize; i++)
            {
                _audioLevelBuffer.Enqueue(0f);
            }

            Debug.WriteLine("Initializing VoiceService...");
            try
            {
                _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                LoadVoiceSettings();
                RefreshAudioDevices();
                
                // Initialize audio level simulation timer
                _audioLevelTimer = new Timer(SimulateAudioLevel, null, 0, 100);
                
                Debug.WriteLine("VoiceService initialization complete.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during VoiceService initialization: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Simulates audio level changes for UI visualization
        private void SimulateAudioLevel(object? state)
        {
            if (_isListening)
            {
                // More active levels when listening
                _simulatedAudioLevel = Math.Max(0f,
                    Math.Min(1f, _simulatedAudioLevel + ((float)_random.NextDouble() - 0.5f) * 0.3f));
            }
            else
            {
                // Quieter levels when not actively listening
                _simulatedAudioLevel = Math.Max(0f,
                    Math.Min(0.7f, _simulatedAudioLevel + ((float)_random.NextDouble() - 0.7f) * 0.1f));
            }

            _audioLevelBuffer.Enqueue(_simulatedAudioLevel);
            if (_audioLevelBuffer.Count > AudioLevelBufferSize)
            {
                _audioLevelBuffer.Dequeue();
            }

            AudioLevelChanged?.Invoke(this, _audioLevelBuffer.ToArray());
        }

        public void RefreshAudioDevices()
        {
            try
            {
                Debug.WriteLine("Refreshing audio devices...");
                
                // Clear and reset device lists with default device
                _inputDevices.Clear();
                _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                _outputDevices.Clear();
                _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                
                // Get input devices safely with try/catch around each individual device
                try
                {
                    for (int i = -1; i < WaveIn.DeviceCount; i++)
                    {
                        try
                        {
                            if (i == -1) continue; // Skip default device as it's already added
                            
                            var capabilities = WaveIn.GetCapabilities(i);
                            string name = capabilities.ProductName?.Trim() ?? "";
                            
                            if (string.IsNullOrEmpty(name))
                                name = $"Input Device {i}";
                                
                            Debug.WriteLine($"Found input device: {name} (Index: {i})");
                            _inputDevices.Add(new AudioDevice { Index = i, Name = name });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error getting input device {i}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error enumerating input devices: {ex.Message}");
                }
                
                // Get output devices safely with try/catch around each individual device
                try
                {
                    for (int i = -1; i < WaveOut.DeviceCount; i++)
                    {
                        try
                        {
                            if (i == -1) continue; // Skip default device as it's already added
                            
                            var capabilities = WaveOut.GetCapabilities(i);
                            string name = capabilities.ProductName?.Trim() ?? "";
                            
                            if (string.IsNullOrEmpty(name))
                                name = $"Output Device {i}";
                                
                            Debug.WriteLine($"Found output device: {name} (Index: {i})");
                            _outputDevices.Add(new AudioDevice { Index = i, Name = name });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error getting output device {i}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error enumerating output devices: {ex.Message}");
                }
                
                // Add mock devices for testing if no real devices found
                if (_inputDevices.Count <= 1)
                {
                    Debug.WriteLine("No real input devices found, adding mock devices");
                    _inputDevices.Add(new AudioDevice { Index = 1, Name = "Microphone (Integrated)" });
                    _inputDevices.Add(new AudioDevice { Index = 2, Name = "Microphone (Headset)" });
                }
                
                if (_outputDevices.Count <= 1)
                {
                    Debug.WriteLine("No real output devices found, adding mock devices");
                    _outputDevices.Add(new AudioDevice { Index = 1, Name = "Speakers (Integrated)" });
                    _outputDevices.Add(new AudioDevice { Index = 2, Name = "Headphones" });
                }
                
                Debug.WriteLine($"Audio device refresh complete. Found {_inputDevices.Count} input devices and {_outputDevices.Count} output devices.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical error refreshing audio devices: {ex.Message}");
                
                // Ensure we always have some devices listed
                if (_inputDevices.Count == 0)
                {
                    _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                    _inputDevices.Add(new AudioDevice { Index = 1, Name = "Microphone (Integrated)" });
                }
                
                if (_outputDevices.Count == 0)
                {
                    _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                    _outputDevices.Add(new AudioDevice { Index = 1, Name = "Speakers (Integrated)" });
                }
            }
        }

        public List<AudioDevice> GetInputDevices()
        {
            return _inputDevices;
        }

        public List<AudioDevice> GetOutputDevices()
        {
            return _outputDevices;
        }

        public void SetInputDevice(int deviceIndex)
        {
            if (_selectedInputDeviceIndex == deviceIndex)
                return;
                
            Debug.WriteLine($"Changing input device from {_selectedInputDeviceIndex} to {deviceIndex}");
            _selectedInputDeviceIndex = deviceIndex;
            SaveVoiceSettings();
            
            if (_inputEnabled && _waveIn != null)
            {
                StopAudioInput();
                StartAudioInput();
            }
        }

        public void SetOutputDevice(int deviceIndex)
        {
            if (_selectedOutputDeviceIndex == deviceIndex)
                return;
                
            Debug.WriteLine($"Changing output device from {_selectedOutputDeviceIndex} to {deviceIndex}");
            _selectedOutputDeviceIndex = deviceIndex;
            SaveVoiceSettings();
            
            if (_outputEnabled && _waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
        }

        public void InitializeSpeechRecognition()
        {
            try
            {
                Debug.WriteLine("Initializing speech recognition");
                if (_inputEnabled)
                {
                    StartAudioInput();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing speech recognition: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Could not initialize speech recognition: {ex.Message}\n\nThis is not critical - you can still use text input.",
                        "Speech Recognition Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }

        public void SetVoiceInputEnabled(bool enabled)
        {
            if (_inputEnabled == enabled)
                return;
                
            Debug.WriteLine($"Setting voice input enabled: {enabled}");
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

        public void SetVoiceOutputEnabled(bool enabled)
        {
            if (_outputEnabled == enabled)
                return;
                
            Debug.WriteLine($"Setting voice output enabled: {enabled}");
            _outputEnabled = enabled;
            
            if (!enabled)
            {
                StopSpeaking();
            }
            
            SaveVoiceSettings();
        }

        public void SetWakeWord(string wakeWord)
        {
            if (string.IsNullOrWhiteSpace(wakeWord) || _wakeWord == wakeWord)
                return;
                
            Debug.WriteLine($"Setting wake word: {wakeWord}");
            _wakeWord = wakeWord;
            SaveVoiceSettings();
        }

        public void SetVoice(string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId) || _currentVoiceId == voiceId)
                return;
                
            Debug.WriteLine($"Setting voice: {voiceId}");
            _currentVoiceId = voiceId;
            SaveVoiceSettings();
        }

        public void SetVoiceSpeed(float rate)
        {
            if (rate < 0.5f || rate > 2.0f || Math.Abs(rate - _voiceSpeed) < 0.01f)
                return;
                
            Debug.WriteLine($"Setting voice speed: {rate}");
            _voiceSpeed = rate;
            SaveVoiceSettings();
        }

        public void StartListening()
        {
            lock (_recognitionLock)
            {
                if (_isListening)
                    return;
                    
                if (!_inputEnabled)
                    return;
                    
                Debug.WriteLine("Starting listening...");
                _isListening = true;
                _listeningCts = new CancellationTokenSource();
                
                // In a real implementation, we would start active speech recognition here
                // For now, simulate with random recognition after a delay
                Task.Run(async () => 
                {
                    try 
                    {
                        if (_listeningCts.Token.IsCancellationRequested)
                            return;
                            
                        // Random delay of 2-5 seconds before "recognizing" speech
                        await Task.Delay(_random.Next(2000, 5000), _listeningCts.Token);
                        
                        if (_isListening && !_listeningCts.Token.IsCancellationRequested)
                        {
                            // Simulate recognition of a random phrase
                            string[] phrases = new[] 
                            {
                                "What time is it?",
                                "What's the weather like today?",
                                "Tell me a joke",
                                "Set a reminder for tomorrow",
                                "How does this work?"
                            };
                            
                            string recognizedText = phrases[_random.Next(phrases.Length)];
                            SpeechRecognized?.Invoke(this, new SpeechRecognizedEventArgs(recognizedText, 0.9f));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Listening was cancelled, this is normal
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in simulated speech recognition: {ex.Message}");
                    }
                });
            }
        }

        public void StopListening()
        {
            lock (_recognitionLock)
            {
                if (!_isListening)
                    return;
                    
                Debug.WriteLine("Stopping listening...");
                _isListening = false;
                _listeningCts?.Cancel();
                _listeningCts = null;
            }
        }

        public async Task SpeakAsync(string text)
        {
            if (!_outputEnabled || string.IsNullOrWhiteSpace(text))
                return;
                
            try
            {
                Debug.WriteLine($"Speaking: {text}");
                
                // Simulate speaking by waiting proportional to text length and speech rate
                await Task.Delay((int)(text.Length * 50 * (1.0 / _voiceSpeed)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error speaking text: {ex.Message}");
            }
        }

        public void StopSpeaking()
        {
            Debug.WriteLine("Stopping speech");
            // In a real implementation, we would stop the speech synthesis here
        }

        public async Task TestVoiceAsync()
        {
            Debug.WriteLine("Testing voice...");
            await SpeakAsync("Hello, I'm Juno, your AI assistant. How can I help you today?");
        }

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

        public List<VoiceInfo> GetAvailableVoices()
        {
            return new List<VoiceInfo>
            {
                new VoiceInfo { Id = "default", Name = "Default Voice", Gender = "Neutral", Age = "Adult", Culture = "en-US" },
                new VoiceInfo { Id = "male1", Name = "Male Voice 1", Gender = "Male", Age = "Adult", Culture = "en-US" },
                new VoiceInfo { Id = "female1", Name = "Female Voice 1", Gender = "Female", Age = "Adult", Culture = "en-US" }
            };
        }

        private void StartAudioInput()
        {
            try
            {
                Debug.WriteLine("Starting audio input");
                
                if (_waveIn != null)
                {
                    StopAudioInput();
                }
                
                // In a real implementation, we would initialize the WaveIn device here
                // For this implementation, we'll just use the timer to simulate audio levels
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting audio input: {ex.Message}");
                _waveIn = null;
            }
        }

        private void StopAudioInput()
        {
            if (_waveIn != null)
            {
                try
                {
                    Debug.WriteLine("Stopping audio input");
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping audio input: {ex.Message}");
                }
                finally
                {
                    _waveIn = null;
                }
            }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            float audioLevel = CalculateAudioLevel(e.Buffer, e.BytesRecorded);
            _audioLevelBuffer.Enqueue(audioLevel);
            if (_audioLevelBuffer.Count > AudioLevelBufferSize)
            {
                _audioLevelBuffer.Dequeue();
            }
            AudioLevelChanged?.Invoke(this, _audioLevelBuffer.ToArray());
            
            // In a real implementation, we would process the audio data for wake word detection
            // and speech recognition here
            
            if (_isListening && audioLevel > 0.5f)
            {
                if (_random.Next(60) == 0)
                {
                    string recognizedText = "Hello Juno, what's the weather today?";
                    SpeechRecognized?.Invoke(this, new SpeechRecognizedEventArgs(recognizedText, 0.9f));
                }
            }
            else if (!_isListening && audioLevel > 0.7f)
            {
                if (_random.Next(100) == 0)
                {
                    WakeWordDetected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private float CalculateAudioLevel(byte[] buffer, int bytesRecorded)
        {
            if (bytesRecorded == 0)
                return 0f;
                
            int samplesCount = bytesRecorded / 2;
            double sumSquared = 0;
            
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                double normalizedSample = sample / 32768.0; // Normalize to -1.0 to 1.0
                sumSquared += normalizedSample * normalizedSample;
            }
            
            double rms = Math.Sqrt(sumSquared / samplesCount);
            float level = (float)Math.Min(1.0, Math.Max(0.0, rms * 4.0));
            
            return level;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Debug.WriteLine($"Recording stopped with error: {e.Exception.Message}");
            }
        }

        private void LoadVoiceSettings()
        {
            string settingsPath = Path.Combine(_voiceDataDirectory, "voice_settings.json");
            if (!File.Exists(settingsPath))
                return;
                
            try
            {
                Debug.WriteLine("Loading voice settings from file");
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
                
                Debug.WriteLine("Voice settings loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading voice settings: {ex.Message}");
            }
        }

        private void SaveVoiceSettings()
        {
            string settingsPath = Path.Combine(_voiceDataDirectory, "voice_settings.json");
            try
            {
                Debug.WriteLine("Saving voice settings to file");
                
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
                
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                
                File.WriteAllText(settingsPath, json);
                Debug.WriteLine("Voice settings saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving voice settings: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopAudioInput();
            StopSpeaking();
            _listeningCts?.Cancel();
            _listeningCts?.Dispose();
            _audioLevelTimer?.Dispose();
        }
    }

    public class VoiceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Age { get; set; } = string.Empty;
        public string Culture { get; set; } = string.Empty;
    }

    public class AudioDevice
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SpeechRecognizedEventArgs : EventArgs
    {
        public string Text { get; }
        public float Confidence { get; }

        public SpeechRecognizedEventArgs(string text, float confidence)
        {
            Text = text;
            Confidence = confidence;
        }
    }
}