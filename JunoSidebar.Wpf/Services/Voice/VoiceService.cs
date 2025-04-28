using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JunoSidebar.Wpf.Models;

namespace JunoSidebar.Wpf.Services.Voice
{
    public class VoiceService : IDisposable
    {
        private readonly SpeechSynthesizer _synthesizer;
        private SpeechRecognitionEngine? _recognizer;
        private readonly WebService _webService;
        private readonly string _voiceDataDirectory;
        private readonly JsonSerializerOptions _jsonOptions;
        private bool _inputEnabled = true;
        private bool _outputEnabled = true;
        private string _wakeWord = "Hey Juno";
        private bool _isListening = false;
        private CancellationTokenSource? _listeningCts;
        private string? _currentVoiceId;
        private float _voiceSpeed = 1.0f;
        private readonly object _recognitionLock = new object();

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
            _synthesizer = new SpeechSynthesizer();
            LoadVoiceSettings();
        }

        public void InitializeSpeechRecognition()
        {
            try
            {
                if (SpeechRecognitionEngine.InstalledRecognizers().Count == 0)
                {
                    Console.WriteLine("No speech recognizers installed.");
                    return;
                }
                _recognizer = new SpeechRecognitionEngine();
                _recognizer.SetInputToDefaultAudioDevice();
                var wakeWordGrammar = new Grammar(new GrammarBuilder(_wakeWord))
                {
                    Name = "WakeWord",
                    Priority = 1
                };
                _recognizer.LoadGrammar(wakeWordGrammar);
                var dictationGrammar = new DictationGrammar()
                {
                    Name = "Dictation",
                    Priority = 0
                };
                _recognizer.LoadGrammar(dictationGrammar);
                
                // These need to be bound to methods that match the expected delegate signatures
                _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
                _recognizer.SpeechDetected += Recognizer_SpeechDetected;
                _recognizer.AudioLevelUpdated += Recognizer_AudioLevelUpdated;
                
                if (_inputEnabled)
                {
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing speech recognition: {ex.Message}");
            }
        }

        // Add these methods to match the expected delegate signatures
        private void Recognizer_SpeechRecognized(object? sender, System.Speech.Recognition.SpeechRecognizedEventArgs e)
        {
            OnSpeechRecognized(sender, e);
        }

        private void Recognizer_SpeechDetected(object? sender, System.Speech.Recognition.SpeechDetectedEventArgs e)
        {
            OnSpeechDetected(sender, e);
        }

        private void Recognizer_AudioLevelUpdated(object? sender, System.Speech.Recognition.AudioLevelUpdatedEventArgs e)
        {
            OnAudioLevelUpdated(sender, e);
        }

        public void SetVoiceInputEnabled(bool enabled)
        {
            if (_inputEnabled == enabled)
                return;
            _inputEnabled = enabled;
            if (_recognizer != null)
            {
                if (enabled)
                {
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                }
                else
                {
                    _recognizer.RecognizeAsyncStop();
                }
            }
            SaveVoiceSettings();
        }

        public void SetVoiceOutputEnabled(bool enabled)
        {
            if (_outputEnabled == enabled)
                return;
            _outputEnabled = enabled;
            if (!enabled && _synthesizer.State == SynthesizerState.Speaking)
            {
                _synthesizer.SpeakAsyncCancelAll();
            }
            SaveVoiceSettings();
        }

        public void SetWakeWord(string wakeWord)
        {
            if (string.IsNullOrWhiteSpace(wakeWord) || _wakeWord == wakeWord)
                return;
            _wakeWord = wakeWord;
            if (_recognizer != null)
            {
                _recognizer.RecognizeAsyncStop();
                var oldGrammar = _recognizer.Grammars.FirstOrDefault(g => g.Name == "WakeWord");
                if (oldGrammar != null)
                {
                    _recognizer.UnloadGrammar(oldGrammar);
                }
                var newGrammar = new Grammar(new GrammarBuilder(_wakeWord))
                {
                    Name = "WakeWord",
                    Priority = 1
                };
                _recognizer.LoadGrammar(newGrammar);
                if (_inputEnabled)
                {
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                }
            }
            SaveVoiceSettings();
        }

        public void SetVoice(string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId) || _currentVoiceId == voiceId)
                return;
            try
            {
                var voice = _synthesizer.GetInstalledVoices()
                    .FirstOrDefault(v => v.VoiceInfo.Name == voiceId || v.VoiceInfo.Id == voiceId);
                if (voice != null)
                {
                    _synthesizer.SelectVoice(voice.VoiceInfo.Name);
                    _currentVoiceId = voiceId;
                    SaveVoiceSettings();
                }
                else
                {
                    Console.WriteLine($"Voice '{voiceId}' not found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting voice: {ex.Message}");
            }
        }

        public void SetVoiceSpeed(float rate)
        {
            if (rate < 0.5f || rate > 2.0f || Math.Abs(rate - _voiceSpeed) < 0.01f)
                return;
            _voiceSpeed = rate;
            _synthesizer.Rate = (int)((rate - 1.0f) * 10.0f);
            SaveVoiceSettings();
        }

        public void StartListening()
        {
            lock (_recognitionLock)
            {
                if (_isListening)
                    return;
                if (_recognizer == null || !_inputEnabled)
                    return;
                _isListening = true;
                _listeningCts = new CancellationTokenSource();
            }
        }

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

        public async Task SpeakAsync(string text)
        {
            if (!_outputEnabled || string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                await Task.Run(() => _synthesizer.SpeakAsync(text));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error speaking text: {ex.Message}");
            }
        }

        public void StopSpeaking()
        {
            _synthesizer.SpeakAsyncCancelAll();
        }

        public async Task TestVoiceAsync()
        {
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
                ["speed"] = _voiceSpeed
            };
        }

        public List<VoiceInfo> GetAvailableVoices()
        {
            var voices = new List<VoiceInfo>();
            try
            {
                foreach (var voice in _synthesizer.GetInstalledVoices())
                {
                    if (voice.Enabled)
                    {
                        voices.Add(new VoiceInfo
                        {
                            Id = voice.VoiceInfo.Id,
                            Name = voice.VoiceInfo.Name,
                            Gender = voice.VoiceInfo.Gender.ToString(),
                            Age = voice.VoiceInfo.Age.ToString(),
                            Culture = voice.VoiceInfo.Culture.Name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting available voices: {ex.Message}");
            }
            return voices;
        }

        private void OnSpeechRecognized(object? sender, System.Speech.Recognition.SpeechRecognizedEventArgs e)
        {
            if (e.Result == null)
                return;
            string recognizedText = e.Result.Text;
            if (string.IsNullOrWhiteSpace(recognizedText))
                return;
            if (recognizedText.Equals(_wakeWord, StringComparison.OrdinalIgnoreCase))
            {
                StartListening();
                WakeWordDetected?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (_isListening)
            {
                // Convert the system SpeechRecognizedEventArgs to our custom version
                SpeechRecognized?.Invoke(this, new SpeechRecognizedEventArgs(recognizedText, e.Result.Confidence));
            }
        }

        private void OnSpeechDetected(object? sender, System.Speech.Recognition.SpeechDetectedEventArgs e)
        {
            // This method is used for the event handler, but has no implementation in the original code
        }

        private void OnAudioLevelUpdated(object? sender, System.Speech.Recognition.AudioLevelUpdatedEventArgs e)
        {
            const int levelCount = 20;
            var levels = new float[levelCount];
            float normalizedLevel = Math.Min(1.0f, Math.Max(0.0f, e.AudioLevel / 100.0f));
            for (int i = 0; i < levelCount; i++)
            {
                levels[i] = Math.Max(0, normalizedLevel * 0.5f + normalizedLevel * 0.5f * (float)new Random().NextDouble());
            }
            AudioLevelChanged?.Invoke(this, levels);
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
                    if (!string.IsNullOrEmpty(_currentVoiceId))
                    {
                        SetVoice(_currentVoiceId);
                    }
                }
                if (settings.TryGetValue("speed", out var speed))
                {
                    _voiceSpeed = speed.GetSingle();
                    SetVoiceSpeed(_voiceSpeed);
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
                    ["speed"] = _voiceSpeed
                };
                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving voice settings: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _synthesizer?.Dispose();
            if (_recognizer != null)
            {
                _recognizer.RecognizeAsyncStop();
                _recognizer.Dispose();
            }
            _listeningCts?.Cancel();
            _listeningCts?.Dispose();
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