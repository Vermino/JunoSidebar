// File: JunoSidebar.Wpf/Services/Voice/VoiceActivityDetector.cs

using System;
using System.Linq;

namespace JunoSidebar.Wpf.Services.Voice
{
    /// <summary>
    /// Simple energy-based Voice Activity Detector
    /// Detects speech vs silence based on audio energy levels
    /// </summary>
    public class VoiceActivityDetector
    {
        private readonly float _threshold;
        private readonly int _sampleRate;
        private readonly int _frameSizeMs;
        private readonly int _minSpeechFrames;
        private readonly int _minSilenceFrames;

        private int _speechFrameCount = 0;
        private int _silenceFrameCount = 0;
        private bool _isSpeaking = false;
        private readonly CircularBuffer<float> _energyHistory;

        public bool IsSpeaking => _isSpeaking;

        public VoiceActivityDetector(
            int sampleRate = 16000,
            float threshold = 0.03f,
            int frameSizeMs = 30,
            int minSpeechFrames = 5,
            int minSilenceFrames = 15)
        {
            _sampleRate = sampleRate;
            _threshold = threshold;
            _frameSizeMs = frameSizeMs;
            _minSpeechFrames = minSpeechFrames;
            _minSilenceFrames = minSilenceFrames;
            _energyHistory = new CircularBuffer<float>(50); // Keep last 50 frames
        }

        /// <summary>
        /// Process audio samples and detect voice activity
        /// </summary>
        /// <param name="audioData">16-bit PCM audio samples</param>
        /// <returns>True if speech is detected, false otherwise</returns>
        public bool ProcessFrame(byte[] audioData, int length)
        {
            // Calculate energy of the frame
            float energy = CalculateEnergy(audioData, length);
            _energyHistory.Add(energy);

            // Adaptive threshold: use average of recent frames
            float adaptiveThreshold = _threshold;
            if (_energyHistory.Count > 10)
            {
                float avgEnergy = _energyHistory.ToArray().Average();
                adaptiveThreshold = Math.Max(_threshold, avgEnergy * 0.5f);
            }

            bool hasVoiceActivity = energy > adaptiveThreshold;

            if (hasVoiceActivity)
            {
                _speechFrameCount++;
                _silenceFrameCount = 0;

                // Start of speech detected
                if (!_isSpeaking && _speechFrameCount >= _minSpeechFrames)
                {
                    _isSpeaking = true;
                    DebugLogger.Instance.LogVAD($"Speech started (energy: {energy:F4}, threshold: {adaptiveThreshold:F4})");
                }
            }
            else
            {
                _silenceFrameCount++;
                _speechFrameCount = 0;

                // End of speech detected
                if (_isSpeaking && _silenceFrameCount >= _minSilenceFrames)
                {
                    _isSpeaking = false;
                    DebugLogger.Instance.LogVAD($"Speech ended (silence frames: {_silenceFrameCount})");
                }
            }

            return _isSpeaking;
        }

        /// <summary>
        /// Calculate RMS energy of audio frame
        /// </summary>
        private float CalculateEnergy(byte[] buffer, int length)
        {
            if (length == 0)
                return 0f;

            double sumSquared = 0;
            int sampleCount = length / 2; // 16-bit samples = 2 bytes per sample

            for (int i = 0; i < length - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                double normalized = sample / 32768.0; // Normalize to [-1, 1]
                sumSquared += normalized * normalized;
            }

            return (float)Math.Sqrt(sumSquared / sampleCount);
        }

        /// <summary>
        /// Reset the detector state
        /// </summary>
        public void Reset()
        {
            _speechFrameCount = 0;
            _silenceFrameCount = 0;
            _isSpeaking = false;
            _energyHistory.Clear();
            DebugLogger.Instance.LogVAD("VAD reset");
        }
    }

    /// <summary>
    /// Simple circular buffer for keeping recent energy history
    /// </summary>
    internal class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _index = 0;
        private int _count = 0;

        public int Count => _count;

        public CircularBuffer(int capacity)
        {
            _buffer = new T[capacity];
        }

        public void Add(T item)
        {
            _buffer[_index] = item;
            _index = (_index + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }

        public T[] ToArray()
        {
            T[] result = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                int idx = (_index - _count + i + _buffer.Length) % _buffer.Length;
                result[i] = _buffer[idx];
            }
            return result;
        }

        public void Clear()
        {
            _count = 0;
            _index = 0;
        }
    }
}
