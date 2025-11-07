// File: JunoSidebar.Wpf/Services/Voice/WhisperProcessor.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace JunoSidebar.Wpf.Services.Voice
{
    /// <summary>
    /// Whisper.NET-based speech-to-text processor
    /// </summary>
    public class WhisperProcessor : IVoiceProcessor, IDisposable
    {
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private readonly string _modelPath;
        private readonly string _modelsDirectory;
        private bool _isInitialized = false;

        public string Name => "Whisper.NET";
        public bool IsAvailable => _isInitialized && _factory != null;

        /// <summary>
        /// Create a new Whisper processor
        /// </summary>
        /// <param name="modelsDirectory">Directory where Whisper models are stored</param>
        /// <param name="modelType">Type of model to use (default: Base)</param>
        public WhisperProcessor(string? modelsDirectory = null, GgmlType modelType = GgmlType.Base)
        {
            _modelsDirectory = modelsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "WhisperModels");

            _modelPath = GetModelPath(modelType);
            Directory.CreateDirectory(_modelsDirectory);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"Initializing Whisper processor with model: {_modelPath}");

                // Check if model file exists, download if necessary
                if (!File.Exists(_modelPath))
                {
                    Debug.WriteLine($"Whisper model not found at {_modelPath}, downloading...");
                    await DownloadModelAsync(GgmlType.Base, cancellationToken);
                }

                // Initialize Whisper factory
                _factory = WhisperFactory.FromPath(_modelPath);

                // Create processor builder with optimal settings
                var builder = _factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithThreads(Environment.ProcessorCount)
                    .WithSingleSegment(false)
                    .WithTokenTimestamps(false);

                _processor = builder.Build();
                _isInitialized = true;

                Debug.WriteLine("Whisper processor initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing Whisper processor: {ex.Message}");
                _isInitialized = false;
                throw new Exception($"Failed to initialize Whisper: {ex.Message}", ex);
            }
        }

        public async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized || _processor == null)
            {
                return new TranscriptionResult
                {
                    Success = false,
                    ErrorMessage = "Whisper processor not initialized"
                };
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Convert audio bytes to float array (Whisper expects float samples)
                var samples = ConvertBytesToFloatSamples(audioData);

                // Process with Whisper
                var segments = new System.Collections.Generic.List<string>();

                await foreach (var segment in _processor.ProcessAsync(samples, cancellationToken))
                {
                    segments.Add(segment.Text);
                }

                stopwatch.Stop();

                var fullText = string.Join(" ", segments).Trim();

                return new TranscriptionResult
                {
                    Text = fullText,
                    Confidence = 0.9f, // Whisper doesn't provide confidence scores directly
                    Language = "en",
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error transcribing audio: {ex.Message}");
                return new TranscriptionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<TranscriptionResult> TranscribeFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized || _processor == null)
            {
                return new TranscriptionResult
                {
                    Success = false,
                    ErrorMessage = "Whisper processor not initialized"
                };
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    return new TranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}"
                    };
                }

                var stopwatch = Stopwatch.StartNew();

                // Read audio file
                using var fileStream = File.OpenRead(filePath);

                var segments = new System.Collections.Generic.List<string>();

                await foreach (var segment in _processor.ProcessAsync(fileStream, cancellationToken))
                {
                    segments.Add(segment.Text);
                }

                stopwatch.Stop();

                var fullText = string.Join(" ", segments).Trim();

                return new TranscriptionResult
                {
                    Text = fullText,
                    Confidence = 0.9f,
                    Language = "en",
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error transcribing file: {ex.Message}");
                return new TranscriptionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<bool> TestProcessorAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                try
                {
                    await InitializeAsync(cancellationToken);
                }
                catch
                {
                    return false;
                }
            }

            // Create a simple test audio sample (silence)
            var testSamples = new float[16000]; // 1 second of silence at 16kHz

            try
            {
                await foreach (var segment in _processor!.ProcessAsync(testSamples, cancellationToken))
                {
                    // If we can process without error, the processor is working
                    break;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Whisper test failed: {ex.Message}");
                return false;
            }
        }

        private async Task DownloadModelAsync(GgmlType modelType, CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine($"Downloading Whisper {modelType} model...");

                var modelFileName = GetModelFileName(modelType);
                var downloadPath = Path.Combine(_modelsDirectory, modelFileName);

                // Use Whisper.NET's built-in model downloader
                using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(modelType, cancellationToken);
                using var fileStream = File.Create(downloadPath);
                await modelStream.CopyToAsync(fileStream, cancellationToken);

                Debug.WriteLine($"Whisper model downloaded to: {downloadPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading Whisper model: {ex.Message}");
                throw;
            }
        }

        private string GetModelPath(GgmlType modelType)
        {
            var fileName = GetModelFileName(modelType);
            return Path.Combine(_modelsDirectory, fileName);
        }

        private string GetModelFileName(GgmlType modelType)
        {
            return $"ggml-{modelType.ToString().ToLower()}.bin";
        }

        private float[] ConvertBytesToFloatSamples(byte[] audioBytes)
        {
            // Assume audioBytes is 16-bit PCM
            int sampleCount = audioBytes.Length / 2;
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(audioBytes, i * 2);
                samples[i] = sample / 32768f; // Normalize to -1.0 to 1.0
            }

            return samples;
        }

        public void Dispose()
        {
            _processor?.Dispose();
            _factory?.Dispose();
            _isInitialized = false;
        }
    }
}
