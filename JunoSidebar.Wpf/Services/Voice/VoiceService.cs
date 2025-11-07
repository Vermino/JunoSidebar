// File: JunoSidebar.Wpf/Services/Voice/VoiceService.cs

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
    /// Voice service with real Whisper transcription and VAD wake word detection
    /// </summary>
    public class VoiceService : IDisposable
    {
        private readonly WebService _webService;
        private readonly string _voiceDataDirectory;
        private readonly JsonSerializerOptions _jsonOptions;

        // Audio devices and settings
        private WaveInEvent? _waveIn;
        private IWavePlayer? _waveOut;
        private List<AudioDevice> _inputDevices = new List<AudioDevice>();
        private List<AudioDevice> _outputDevices = new List<AudioDevice>();
        private int _selectedInputDeviceIndex = -1;
        private int _selectedOutputDeviceIndex = -1;

        // Voice settings
        private bool _inputEnabled = true;
        private bool _outputEnabled = true;
        private bool _isOnline = false; // Power state
        private string _wakeWord = "Hey Juno";
        private float _voiceSpeed = 1.0f;
        private string? _currentVoiceId;

        // Speech processing
        private WhisperProcessor? _whisperProcessor;
        private VoiceActivityDetector? _vad;
        private bool _isListeningForWakeWord = false;
        private bool _isRecordingCommand = false;
        private CancellationTokenSource? _listeningCts;
        private readonly object _stateLock = new object();

        // Audio buffering
        private readonly MemoryStream _audioBuffer = new MemoryStream();
        private readonly Queue<float> _audioLevelBuffer = new Queue<float>();
        private const int AudioLevelBufferSize = 20;
        private const int SampleRate = 16000;
        private const int Channels = 1;
        private const int BitsPerSample = 16;

        // Events
        public event EventHandler<EventArgs>? WakeWordDetected;
        public event EventHandler<SpeechRecognizedEventArgs>? SpeechRecognized;
        public event EventHandler<float[]>? AudioLevelChanged;

        // Properties
        public bool InputEnabled => _inputEnabled;
        public bool OutputEnabled => _outputEnabled;
        public bool IsOnline => _isOnline;
        public string WakeWord => _wakeWord;
        public bool IsListening => _isRecordingCommand;

        public VoiceService(WebService webService, string? voiceDataDirectory = null)
        {
            _webService = webService ?? throw new ArgumentNullException(nameof(webService));
            _voiceDataDirectory = voiceDataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "VoiceData");

            Directory.CreateDirectory(_voiceDataDirectory);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            // Initialize audio level buffer
            for (int i = 0; i < AudioLevelBufferSize; i++)
            {
                _audioLevelBuffer.Enqueue(0f);
            }

            DebugLogger.Instance.LogVoice("VoiceService initializing...");

            try
            {
                _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });

                LoadVoiceSettings();
                RefreshAudioDevices();

                // Initialize Whisper and VAD
                InitializeProcessors();

                DebugLogger.Instance.LogVoice("VoiceService initialized successfully");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error initializing VoiceService: {ex.Message}", "Voice");
            }
        }

        private async void InitializeProcessors()
        {
            try
            {
                DebugLogger.Instance.LogVoice("Initializing Whisper and VAD...");

                // Initialize Whisper processor
                _whisperProcessor = new WhisperProcessor();
                await _whisperProcessor.InitializeAsync();

                // Initialize VAD (16kHz, 30ms frames)
                _vad = new VoiceActivityDetector(
                    sampleRate: SampleRate,
                    threshold: 0.03f,
                    frameSizeMs: 30,
                    minSpeechFrames: 5,
                    minSilenceFrames: 15
                );

                DebugLogger.Instance.LogVoice("Whisper and VAD initialized successfully");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Failed to initialize processors: {ex.Message}", "Voice");
            }
        }

        #region Power and State Management

        /// <summary>
        /// Power on - start always-on listening for wake word
        /// </summary>
        public void PowerOn()
        {
            lock (_stateLock)
            {
                if (_isOnline)
                    return;

                DebugLogger.Instance.LogVoice("Powering ON - Starting always-on wake word detection");
                _isOnline = true;
                DebugLogger.Instance.SetStatus("Listening for wake word", true);

                if (_inputEnabled)
                {
                    StartWakeWordListening();
                }
            }
        }

        /// <summary>
        /// Power off - stop all listening
        /// </summary>
        public void PowerOff()
        {
            lock (_stateLock)
            {
                if (!_isOnline)
                    return;

                DebugLogger.Instance.LogVoice("Powering OFF - Stopping all audio processing");
                _isOnline = false;
                DebugLogger.Instance.SetStatus("Offline", false);

                StopAllListening();
            }
        }

        #endregion

        #region Wake Word Detection

        /// <summary>
        /// Start always-on listening for wake word
        /// </summary>
        private void StartWakeWordListening()
        {
            lock (_stateLock)
            {
                if (_isListeningForWakeWord || _waveIn != null)
                    return;

                DebugLogger.Instance.LogVoice("Starting wake word listening mode...");

                try
                {
                    _isListeningForWakeWord = true;
                    _listeningCts = new CancellationTokenSource();

                    // Start audio input
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = _selectedInputDeviceIndex,
                        WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                        BufferMilliseconds = 100
                    };

                    _waveIn.DataAvailable += OnAudioDataForWakeWord;
                    _waveIn.RecordingStopped += (s, e) =>
                    {
                        if (e.Exception != null)
                        {
                            DebugLogger.Instance.LogError($"Recording stopped with error: {e.Exception.Message}", "Voice");
                        }
                    };

                    _waveIn.StartRecording();
                    _vad?.Reset();

                    DebugLogger.Instance.LogVoice("Microphone started - listening for wake word");
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError($"Error starting wake word listening: {ex.Message}", "Voice");
                    _isListeningForWakeWord = false;
                    _waveIn = null;
                }
            }
        }

        /// <summary>
        /// Process audio for wake word detection
        /// </summary>
        private void OnAudioDataForWakeWord(object? sender, WaveInEventArgs e)
        {
            try
            {
                // Calculate and send audio levels to UI
                float audioLevel = CalculateAudioLevel(e.Buffer, e.BytesRecorded);
                UpdateAudioLevels(audioLevel);

                // Run VAD to detect speech
                bool isSpeaking = _vad?.ProcessFrame(e.Buffer, e.BytesRecorded) ?? false;

                // If speech detected and we're not already recording a command
                if (isSpeaking && !_isRecordingCommand)
                {
                    // Buffer the audio
                    _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);

                    // Check if wake word might be present (simplified - in production use wake word spotting model)
                    // For now, after speech is detected, we'll transcribe and check for wake word
                }
                else if (!isSpeaking && _audioBuffer.Length > 0)
                {
                    // Speech ended - check if it was the wake word
                    Task.Run(async () => await CheckForWakeWord());
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error processing wake word audio: {ex.Message}", "Voice");
            }
        }

        /// <summary>
        /// Check if recorded audio contains wake word
        /// </summary>
        private async Task CheckForWakeWord()
        {
            if (_audioBuffer.Length == 0 || _whisperProcessor == null)
                return;

            try
            {
                byte[] audioData = _audioBuffer.ToArray();
                _audioBuffer.SetLength(0);
                _audioBuffer.Position = 0;

                // Skip if audio is too short (less than 0.5 seconds)
                int minBytes = (SampleRate * BitsPerSample / 8 * Channels) / 2;
                if (audioData.Length < minBytes)
                    return;

                DebugLogger.Instance.LogVoice($"Checking for wake word in {audioData.Length} bytes of audio...");

                // Transcribe the audio
                string transcription = await _whisperProcessor.TranscribeAsync(audioData);

                if (string.IsNullOrWhiteSpace(transcription))
                    return;

                DebugLogger.Instance.LogVoice($"Wake word check transcription: \"{transcription}\"");

                // Check if wake word is present (case-insensitive, fuzzy match)
                if (ContainsWakeWord(transcription))
                {
                    DebugLogger.Instance.LogVoice("✓ WAKE WORD DETECTED!");
                    OnWakeWordDetected();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error checking wake word: {ex.Message}", "Voice");
            }
        }

        /// <summary>
        /// Check if text contains wake word
        /// </summary>
        private bool ContainsWakeWord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Normalize both strings
            string normalizedText = text.ToLower().Trim();
            string normalizedWakeWord = _wakeWord.ToLower().Trim();

            return normalizedText.Contains(normalizedWakeWord) ||
                   normalizedText.Contains("hey juno") ||
                   normalizedText.Contains("hi juno") ||
                   normalizedText.Contains("hello juno");
        }

        /// <summary>
        /// Wake word detected - start recording command
        /// </summary>
        private void OnWakeWordDetected()
        {
            lock (_stateLock)
            {
                if (_isRecordingCommand)
                    return;

                DebugLogger.Instance.LogVoice("Starting command recording...");
                _isRecordingCommand = true;
                _audioBuffer.SetLength(0);
                _audioBuffer.Position = 0;
                _vad?.Reset();

                DebugLogger.Instance.SetStatus("Recording command", true);

                // Notify UI
                WakeWordDetected?.Invoke(this, EventArgs.Empty);

                // Set timeout to stop recording after 10 seconds
                Task.Run(async () =>
                {
                    await Task.Delay(10000);
                    if (_isRecordingCommand)
                    {
                        await StopRecordingAndTranscribe();
                    }
                });
            }
        }

        #endregion

        #region Command Recording and Transcription

        /// <summary>
        /// Manually start recording (when user clicks mic button)
        /// </summary>
        public void StartListening()
        {
            lock (_stateLock)
            {
                if (!_isOnline || _isRecordingCommand)
                    return;

                DebugLogger.Instance.LogVoice("Manual listening started");
                OnWakeWordDetected(); // Reuse wake word detection logic
            }
        }

        /// <summary>
        /// Stop recording and transcribe
        /// </summary>
        public void StopListening()
        {
            // Fire and forget - don't block the caller
            Task.Run(async () => await StopRecordingAndTranscribe());
        }

        /// <summary>
        /// Stop recording command and transcribe
        /// </summary>
        private async Task StopRecordingAndTranscribe()
        {
            byte[] audioData;

            lock (_stateLock)
            {
                if (!_isRecordingCommand)
                    return;

                DebugLogger.Instance.LogVoice("Stopping command recording...");
                _isRecordingCommand = false;
                DebugLogger.Instance.SetStatus("Processing speech", true);

                audioData = _audioBuffer.ToArray();
                _audioBuffer.SetLength(0);
                _audioBuffer.Position = 0;
            }

            // Transcribe the audio
            if (audioData.Length > 0 && _whisperProcessor != null)
            {
                try
                {
                    DebugLogger.Instance.LogVoice($"Transcribing command ({audioData.Length} bytes)...");

                    string transcription = await _whisperProcessor.TranscribeAsync(audioData);

                    if (!string.IsNullOrWhiteSpace(transcription))
                    {
                        DebugLogger.Instance.LogVoice($"✓ Command transcribed: \"{transcription}\"");

                        // Remove wake word from transcription if present
                        transcription = RemoveWakeWord(transcription);

                        // Notify UI
                        SpeechRecognized?.Invoke(this, new SpeechRecognizedEventArgs(transcription, 1.0f));
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError($"Error transcribing command: {ex.Message}", "Voice");
                }
            }

            // Return to wake word listening
            DebugLogger.Instance.SetStatus("Listening for wake word", true);
        }

        /// <summary>
        /// Remove wake word from transcription
        /// </summary>
        private string RemoveWakeWord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string[] wakeWords = { "hey juno", "hi juno", "hello juno", _wakeWord.ToLower() };

            foreach (var ww in wakeWords)
            {
                int index = text.ToLower().IndexOf(ww);
                if (index >= 0)
                {
                    text = text.Substring(index + ww.Length).Trim();
                    // Remove common separators
                    text = text.TrimStart(',', '.', '!', '?').Trim();
                    break;
                }
            }

            return text;
        }

        #endregion

        #region Audio Levels

        /// <summary>
        /// Calculate RMS audio level
        /// </summary>
        private float CalculateAudioLevel(byte[] buffer, int bytesRecorded)
        {
            if (bytesRecorded == 0)
                return 0f;

            int samplesCount = bytesRecorded / 2;
            double sumSquared = 0;

            for (int i = 0; i < bytesRecorded - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                double normalizedSample = sample / 32768.0;
                sumSquared += normalizedSample * normalizedSample;
            }

            double rms = Math.Sqrt(sumSquared / samplesCount);
            return (float)Math.Min(1.0, Math.Max(0.0, rms * 4.0));
        }

        /// <summary>
        /// Update audio level buffer and notify UI
        /// </summary>
        private void UpdateAudioLevels(float level)
        {
            _audioLevelBuffer.Enqueue(level);
            if (_audioLevelBuffer.Count > AudioLevelBufferSize)
            {
                _audioLevelBuffer.Dequeue();
            }

            AudioLevelChanged?.Invoke(this, _audioLevelBuffer.ToArray());
        }

        #endregion

        #region Audio Device Management

        public void RefreshAudioDevices()
        {
            try
            {
                DebugLogger.Instance.LogVoice("Refreshing audio devices...");

                _inputDevices.Clear();
                _inputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });
                _outputDevices.Clear();
                _outputDevices.Add(new AudioDevice { Index = -1, Name = "Default Device" });

                // Get input devices
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    try
                    {
                        var capabilities = WaveIn.GetCapabilities(i);
                        string name = capabilities.ProductName?.Trim() ?? $"Input Device {i}";
                        _inputDevices.Add(new AudioDevice { Index = i, Name = name });
                        DebugLogger.Instance.LogVoice($"Found input device: {name}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogVoice($"Error getting input device {i}: {ex.Message}", LogLevel.Warning);
                    }
                }

                // Get output devices
                for (int i = 0; i < WaveOut.DeviceCount; i++)
                {
                    try
                    {
                        var capabilities = WaveOut.GetCapabilities(i);
                        string name = capabilities.ProductName?.Trim() ?? $"Output Device {i}";
                        _outputDevices.Add(new AudioDevice { Index = i, Name = name });
                        DebugLogger.Instance.LogVoice($"Found output device: {name}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogVoice($"Error getting output device {i}: {ex.Message}", LogLevel.Warning);
                    }
                }

                DebugLogger.Instance.LogVoice($"Found {_inputDevices.Count} input and {_outputDevices.Count} output devices");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error refreshing audio devices: {ex.Message}", "Voice");
            }
        }

        public List<AudioDevice> GetInputDevices() => _inputDevices;
        public List<AudioDevice> GetOutputDevices() => _outputDevices;

        public void SetInputDevice(int deviceIndex)
        {
            if (_selectedInputDeviceIndex == deviceIndex)
                return;

            DebugLogger.Instance.LogVoice($"Changing input device to index {deviceIndex}");
            _selectedInputDeviceIndex = deviceIndex;
            SaveVoiceSettings();

            // Restart audio if currently listening
            if (_isListeningForWakeWord)
            {
                StopAllListening();
                StartWakeWordListening();
            }
        }

        public void SetOutputDevice(int deviceIndex)
        {
            if (_selectedOutputDeviceIndex == deviceIndex)
                return;

            DebugLogger.Instance.LogVoice($"Changing output device to index {deviceIndex}");
            _selectedOutputDeviceIndex = deviceIndex;
            SaveVoiceSettings();
        }

        #endregion

        #region Settings

        /// <summary>
        /// Initialize speech recognition (called during startup)
        /// </summary>
        public void InitializeSpeechRecognition()
        {
            // Initialization is now done in constructor and InitializeProcessors
            // This method is kept for compatibility with CoreEngine
            DebugLogger.Instance.LogVoice("InitializeSpeechRecognition called (already initialized)");
        }

        public void SetVoiceInputEnabled(bool enabled)
        {
            if (_inputEnabled == enabled)
                return;

            DebugLogger.Instance.LogVoice($"Voice input {(enabled ? "enabled" : "disabled")}");
            _inputEnabled = enabled;

            if (!enabled)
            {
                StopAllListening();
            }
            else if (_isOnline)
            {
                StartWakeWordListening();
            }

            SaveVoiceSettings();
        }

        public void SetVoiceOutputEnabled(bool enabled)
        {
            if (_outputEnabled == enabled)
                return;

            DebugLogger.Instance.LogVoice($"Voice output {(enabled ? "enabled" : "disabled")}");
            _outputEnabled = enabled;
            SaveVoiceSettings();
        }

        public void SetWakeWord(string wakeWord)
        {
            if (string.IsNullOrWhiteSpace(wakeWord) || _wakeWord == wakeWord)
                return;

            DebugLogger.Instance.LogVoice($"Wake word changed to: {wakeWord}");
            _wakeWord = wakeWord;
            SaveVoiceSettings();
        }

        public void SetVoice(string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId) || _currentVoiceId == voiceId)
                return;

            DebugLogger.Instance.LogVoice($"Voice changed to: {voiceId}");
            _currentVoiceId = voiceId;
            SaveVoiceSettings();
        }

        public void SetVoiceSpeed(float speed)
        {
            if (speed < 0.5f || speed > 2.0f || Math.Abs(speed - _voiceSpeed) < 0.01f)
                return;

            DebugLogger.Instance.LogVoice($"Voice speed changed to: {speed}");
            _voiceSpeed = speed;
            SaveVoiceSettings();
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
                ["outputDeviceIndex"] = _selectedOutputDeviceIndex,
                ["isOnline"] = _isOnline
            };
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
                    _inputEnabled = inputEnabled.GetBoolean();

                if (settings.TryGetValue("outputEnabled", out var outputEnabled))
                    _outputEnabled = outputEnabled.GetBoolean();

                if (settings.TryGetValue("wakeWord", out var wakeWord))
                    _wakeWord = wakeWord.GetString() ?? "Hey Juno";

                if (settings.TryGetValue("voiceId", out var voiceId))
                    _currentVoiceId = voiceId.GetString();

                if (settings.TryGetValue("speed", out var speed))
                    _voiceSpeed = speed.GetSingle();

                if (settings.TryGetValue("inputDeviceIndex", out var inputDeviceIndex))
                    _selectedInputDeviceIndex = inputDeviceIndex.GetInt32();

                if (settings.TryGetValue("outputDeviceIndex", out var outputDeviceIndex))
                    _selectedOutputDeviceIndex = outputDeviceIndex.GetInt32();

                DebugLogger.Instance.LogVoice("Settings loaded from file");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error loading settings: {ex.Message}", "Voice");
            }
        }

        private void SaveVoiceSettings()
        {
            string settingsPath = Path.Combine(_voiceDataDirectory, "voice_settings.json");
            try
            {
                var settings = GetVoiceSettings();
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(settingsPath, json);
                DebugLogger.Instance.LogVoice("Settings saved to file");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Error saving settings: {ex.Message}", "Voice");
            }
        }

        #endregion

        #region Cleanup

        private void StopAllListening()
        {
            lock (_stateLock)
            {
                _isListeningForWakeWord = false;
                _isRecordingCommand = false;
                _listeningCts?.Cancel();

                if (_waveIn != null)
                {
                    try
                    {
                        _waveIn.StopRecording();
                        _waveIn.Dispose();
                    }
                    catch { }
                    _waveIn = null;
                }

                _audioBuffer.SetLength(0);
                _audioBuffer.Position = 0;
            }
        }

        public void Dispose()
        {
            DebugLogger.Instance.LogVoice("VoiceService disposing...");
            StopAllListening();
            _whisperProcessor?.Dispose();
            _audioBuffer?.Dispose();
            _listeningCts?.Dispose();
        }

        #endregion

        // Voice output placeholders (for future TTS integration)
        public async Task SpeakAsync(string text)
        {
            DebugLogger.Instance.LogVoice($"Speaking: {text}");
            await Task.Delay(text.Length * 50);
        }

        public void StopSpeaking() => DebugLogger.Instance.LogVoice("Speech stopped");

        public async Task TestVoiceAsync()
        {
            DebugLogger.Instance.LogVoice("Testing voice...");
            await SpeakAsync("Hello, I'm Juno, your AI assistant.");
        }

        public List<VoiceInfo> GetAvailableVoices()
        {
            return new List<VoiceInfo>
            {
                new VoiceInfo { Id = "default", Name = "Default Voice", Gender = "Neutral", Age = "Adult", Culture = "en-US" }
            };
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
