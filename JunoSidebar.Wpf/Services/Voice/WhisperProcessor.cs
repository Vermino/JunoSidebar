// File: JunoSidebar.Wpf/Services/Voice/WhisperProcessor.cs

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace JunoSidebar.Wpf.Services.Voice
{
    /// <summary>
    /// Handles speech-to-text transcription using Whisper.NET
    /// </summary>
    public class WhisperProcessor : IDisposable
    {
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private readonly string _modelDirectory;
        private readonly string _modelPath;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        public bool IsInitialized => _isInitialized;

        public WhisperProcessor(string? modelDirectory = null)
        {
            _modelDirectory = modelDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "WhisperModels");

            Directory.CreateDirectory(_modelDirectory);
            _modelPath = Path.Combine(_modelDirectory, "ggml-base.bin");
        }

        /// <summary>
        /// Initialize Whisper model. Downloads if necessary.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            lock (_lock)
            {
                if (_isInitialized)
                    return;

                DebugLogger.Instance.LogWhisper("Initializing Whisper processor...");

                try
                {
                    // Check if model exists, download if not
                    if (!File.Exists(_modelPath))
                    {
                        DebugLogger.Instance.LogWhisper("Whisper model not found. Downloading base model (~75MB)...");
                        DownloadModel();
                    }

                    // Load the Whisper model
                    DebugLogger.Instance.LogWhisper($"Loading Whisper model from: {_modelPath}");
                    _factory = WhisperFactory.FromPath(_modelPath);

                    _isInitialized = true;
                    DebugLogger.Instance.LogWhisper("Whisper processor initialized successfully");
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError($"Failed to initialize Whisper: {ex.Message}", "Whisper");
                    throw;
                }
            }
        }

        /// <summary>
        /// Download Whisper model
        /// </summary>
        private void DownloadModel()
        {
            try
            {
                DebugLogger.Instance.LogWhisper("Starting model download...");

                // Use WhisperGgmlDownloader to download the base model
                using var modelStream = WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base).Result;
                using var fileStream = File.OpenWrite(_modelPath);

                modelStream.CopyTo(fileStream);

                DebugLogger.Instance.LogWhisper($"Model downloaded successfully to: {_modelPath}");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Failed to download Whisper model: {ex.Message}", "Whisper");
                throw;
            }
        }

        /// <summary>
        /// Transcribe audio data to text
        /// </summary>
        /// <param name="audioData">16-bit PCM audio at 16kHz, mono</param>
        /// <returns>Transcribed text</returns>
        public async Task<string> TranscribeAsync(byte[] audioData)
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            if (_factory == null)
            {
                throw new InvalidOperationException("Whisper processor not initialized");
            }

            try
            {
                DebugLogger.Instance.LogWhisper($"Transcribing audio ({audioData.Length} bytes)...");

                // Convert byte array to float array (Whisper expects float samples)
                float[] samples = ConvertBytesToFloats(audioData);

                DebugLogger.Instance.LogWhisper($"Converted to {samples.Length} float samples");

                // Create processor and transcribe
                using var processor = _factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithPrompt("Transcribe the following speech.")
                    .Build();

                var result = "";
                await foreach (var segment in processor.ProcessAsync(samples))
                {
                    result += segment.Text;
                    DebugLogger.Instance.LogWhisper($"Segment: {segment.Text} (confidence: {segment.Probability:F2})");
                }

                result = result.Trim();
                DebugLogger.Instance.LogWhisper($"Transcription complete: \"{result}\"");

                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError($"Transcription error: {ex.Message}", "Whisper");
                return string.Empty;
            }
        }

        /// <summary>
        /// Transcribe audio from WAV file
        /// </summary>
        public async Task<string> TranscribeFileAsync(string wavFilePath)
        {
            if (!File.Exists(wavFilePath))
            {
                throw new FileNotFoundException($"Audio file not found: {wavFilePath}");
            }

            // Read WAV file and extract audio data
            byte[] audioData = ExtractWavAudioData(wavFilePath);
            return await TranscribeAsync(audioData);
        }

        /// <summary>
        /// Convert 16-bit PCM bytes to float samples
        /// </summary>
        private float[] ConvertBytesToFloats(byte[] audioData)
        {
            int sampleCount = audioData.Length / 2;
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(audioData, i * 2);
                samples[i] = sample / 32768f; // Normalize to [-1.0, 1.0]
            }

            return samples;
        }

        /// <summary>
        /// Extract audio data from WAV file (skip header)
        /// </summary>
        private byte[] ExtractWavAudioData(string wavFilePath)
        {
            byte[] allBytes = File.ReadAllBytes(wavFilePath);

            // Simple WAV parser - skip 44 byte header
            // This assumes standard WAV format (44 byte header)
            const int wavHeaderSize = 44;
            if (allBytes.Length <= wavHeaderSize)
            {
                return Array.Empty<byte>();
            }

            byte[] audioData = new byte[allBytes.Length - wavHeaderSize];
            Array.Copy(allBytes, wavHeaderSize, audioData, 0, audioData.Length);

            return audioData;
        }

        public void Dispose()
        {
            _factory?.Dispose();
            _factory = null;
            _isInitialized = false;
            DebugLogger.Instance.LogWhisper("Whisper processor disposed");
        }
    }
}
