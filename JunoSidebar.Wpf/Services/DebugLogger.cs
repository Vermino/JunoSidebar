// File: JunoSidebar.Wpf/Services/DebugLogger.cs

using System;
using System.Diagnostics;

namespace JunoSidebar.Wpf.Services
{
    /// <summary>
    /// Singleton service for logging debug messages to the debug console
    /// </summary>
    public class DebugLogger
    {
        private static DebugLogger? _instance;
        private static readonly object _lock = new object();
        private DebugConsole? _console;

        private DebugLogger() { }

        public static DebugLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DebugLogger();
                        }
                    }
                }
                return _instance;
            }
        }

        public void Initialize(DebugConsole console)
        {
            _console = console;
            Log("Debug Logger initialized", "System", LogLevel.Info);
        }

        public void Log(string message, string category = "General", LogLevel level = LogLevel.Info)
        {
            // Always write to Debug output
            Debug.WriteLine($"[{category}] {message}");

            // Also send to console if available
            if (_console != null && App.IsDebugMode)
            {
                try
                {
                    _console.AddLog(message, category, level);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error logging to debug console: {ex.Message}");
                }
            }
        }

        public void LogVoice(string message, LogLevel level = LogLevel.Info)
        {
            Log(message, "Voice", level);
        }

        public void LogVAD(string message, LogLevel level = LogLevel.Debug)
        {
            Log(message, "VAD", level);
        }

        public void LogWhisper(string message, LogLevel level = LogLevel.Info)
        {
            Log(message, "Whisper", level);
        }

        public void LogError(string message, string category = "General")
        {
            Log(message, category, LogLevel.Error);
        }

        public void SetStatus(string status, bool isActive = true)
        {
            if (_console != null)
            {
                try
                {
                    _console.SetStatus(status, isActive);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting status in debug console: {ex.Message}");
                }
            }
        }
    }
}
