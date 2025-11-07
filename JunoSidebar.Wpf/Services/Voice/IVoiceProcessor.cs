// File: JunoSidebar.Wpf/Services/Voice/IVoiceProcessor.cs

using System.Threading;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.Voice
{
    /// <summary>
    /// Interface for voice/speech processing engines (Whisper, Azure Speech, etc.)
    /// </summary>
    public interface IVoiceProcessor
    {
        /// <summary>
        /// Processor name (e.g., "Whisper", "Azure Speech")
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this processor is available and ready to use
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Initialize the voice processor
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Transcribe audio data to text
        /// </summary>
        /// <param name="audioData">Raw audio bytes (WAV format, 16kHz, 16-bit, mono)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transcription result</returns>
        Task<TranscriptionResult> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Transcribe audio from a file
        /// </summary>
        Task<TranscriptionResult> TranscribeFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Test if the processor is working correctly
        /// </summary>
        Task<bool> TestProcessorAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a transcription operation
    /// </summary>
    public class TranscriptionResult
    {
        /// <summary>
        /// Transcribed text
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Confidence score (0.0 to 1.0)
        /// </summary>
        public float Confidence { get; set; } = 0.0f;

        /// <summary>
        /// Language detected (if supported by processor)
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// Duration of transcription in milliseconds
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Whether the transcription was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if transcription failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
