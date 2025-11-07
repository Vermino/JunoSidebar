// File: JunoSidebar.Wpf/Services/Voice/VoiceActivityDetector.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace JunoSidebar.Wpf.Services.Voice
{
    /// <summary>
    /// Detects voice activity in audio streams using energy-based algorithm
    /// </summary>
    public class VoiceActivityDetector
    {
        private readonly float _energyThreshold;
        private readonly int _minSpeechDurationMs;
        private readonly int _maxSilenceDurationMs;
        private readonly int _sampleRate;
        private readonly Queue<float> _energyBuffer;
        private readonly int _bufferSize;

        private bool _isSpeaking = false;
        private int _speechDurationMs = 0;
        private int _silenceDurationMs = 0;

        /// <summary>
        /// Current state of voice activity
        /// </summary>
        public bool IsSpeaking => _isSpeaking;

        /// <summary>
        /// Energy threshold for speech detection (0.0 - 1.0)
        /// </summary>
        public float EnergyThreshold => _energyThreshold;

        public VoiceActivityDetector(
            float energyThreshold = 0.02f,
            int minSpeechDurationMs = 300,
            int maxSilenceDurationMs = 700,
            int sampleRate = 16000)
        {
            _energyThreshold = energyThreshold;
            _minSpeechDurationMs = minSpeechDurationMs;
            _maxSilenceDurationMs = maxSilenceDurationMs;
            _sampleRate = sampleRate;
            _bufferSize = 10;
            _energyBuffer = new Queue<float>(_bufferSize);

            Debug.WriteLine($"VAD initialized: threshold={energyThreshold}, minSpeech={minSpeechDurationMs}ms, maxSilence={maxSilenceDurationMs}ms");
        }

        /// <summary>
        /// Process audio samples and update voice activity state
        /// </summary>
        /// <param name="audioSamples">Audio samples (16-bit PCM)</param>
        /// <param name="sampleCount">Number of samples</param>
        /// <returns>Current voice activity state</returns>
        public VoiceActivityState ProcessAudio(short[] audioSamples, int sampleCount)
        {
            if (sampleCount == 0)
                return new VoiceActivityState { IsSpeaking = _isSpeaking };

            // Calculate energy of this audio chunk
            float energy = CalculateEnergy(audioSamples, sampleCount);

            // Add to buffer for smoothing
            _energyBuffer.Enqueue(energy);
            if (_energyBuffer.Count > _bufferSize)
            {
                _energyBuffer.Dequeue();
            }

            // Use average energy from buffer
            float avgEnergy = _energyBuffer.Average();

            // Calculate duration of this chunk in milliseconds
            int chunkDurationMs = (int)((sampleCount * 1000.0) / _sampleRate);

            bool speechDetected = avgEnergy > _energyThreshold;

            if (speechDetected)
            {
                _speechDurationMs += chunkDurationMs;
                _silenceDurationMs = 0;

                // Start speaking if we've detected speech for minimum duration
                if (!_isSpeaking && _speechDurationMs >= _minSpeechDurationMs)
                {
                    _isSpeaking = true;
                    Debug.WriteLine($"Speech started (energy: {avgEnergy:F4})");
                    return new VoiceActivityState
                    {
                        IsSpeaking = true,
                        SpeechStarted = true,
                        Energy = avgEnergy
                    };
                }
            }
            else
            {
                _silenceDurationMs += chunkDurationMs;
                _speechDurationMs = 0;

                // Stop speaking if silence has lasted long enough
                if (_isSpeaking && _silenceDurationMs >= _maxSilenceDurationMs)
                {
                    _isSpeaking = false;
                    Debug.WriteLine($"Speech ended (silence: {_silenceDurationMs}ms)");
                    return new VoiceActivityState
                    {
                        IsSpeaking = false,
                        SpeechEnded = true,
                        Energy = avgEnergy
                    };
                }
            }

            return new VoiceActivityState
            {
                IsSpeaking = _isSpeaking,
                Energy = avgEnergy
            };
        }

        /// <summary>
        /// Process audio from byte buffer
        /// </summary>
        public VoiceActivityState ProcessAudioBytes(byte[] audioBytes, int byteCount)
        {
            int sampleCount = byteCount / 2;
            short[] samples = new short[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToInt16(audioBytes, i * 2);
            }

            return ProcessAudio(samples, sampleCount);
        }

        /// <summary>
        /// Reset the detector state
        /// </summary>
        public void Reset()
        {
            _isSpeaking = false;
            _speechDurationMs = 0;
            _silenceDurationMs = 0;
            _energyBuffer.Clear();
            Debug.WriteLine("VAD reset");
        }

        /// <summary>
        /// Calculate RMS energy of audio samples
        /// </summary>
        private float CalculateEnergy(short[] samples, int count)
        {
            if (count == 0)
                return 0f;

            double sumSquared = 0;
            for (int i = 0; i < count; i++)
            {
                double normalized = samples[i] / 32768.0;
                sumSquared += normalized * normalized;
            }

            double rms = Math.Sqrt(sumSquared / count);
            return (float)rms;
        }

        /// <summary>
        /// Automatically calibrate threshold based on background noise
        /// </summary>
        public float CalibrateThreshold(short[][] noiseSamples)
        {
            var energies = new List<float>();

            foreach (var samples in noiseSamples)
            {
                float energy = CalculateEnergy(samples, samples.Length);
                energies.Add(energy);
            }

            // Set threshold to 3x the average background noise
            float avgNoise = energies.Average();
            float calibratedThreshold = avgNoise * 3f;

            Debug.WriteLine($"VAD calibrated: avgNoise={avgNoise:F4}, newThreshold={calibratedThreshold:F4}");

            return calibratedThreshold;
        }
    }

    /// <summary>
    /// Voice activity state information
    /// </summary>
    public class VoiceActivityState
    {
        /// <summary>
        /// Whether speech is currently detected
        /// </summary>
        public bool IsSpeaking { get; set; }

        /// <summary>
        /// True if speech just started in this frame
        /// </summary>
        public bool SpeechStarted { get; set; }

        /// <summary>
        /// True if speech just ended in this frame
        /// </summary>
        public bool SpeechEnded { get; set; }

        /// <summary>
        /// Current audio energy level
        /// </summary>
        public float Energy { get; set; }
    }
}
