// File: JunoSidebar.Wpf/DebugConsole.xaml.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JunoSidebar.Wpf
{
    public partial class DebugConsole : Window
    {
        private readonly List<LogEntry> _allLogs = new List<LogEntry>();
        private string _currentFilter = "All";

        public DebugConsole()
        {
            InitializeComponent();
            Closing += (s, e) =>
            {
                // Don't actually close, just hide
                e.Cancel = true;
                Hide();
            };
        }

        public void AddLog(string message, string category = "General", LogLevel level = LogLevel.Info)
        {
            Dispatcher.Invoke(() =>
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    Category = category,
                    Level = level
                };

                _allLogs.Add(entry);

                // Apply filter
                if (ShouldShowLog(entry))
                {
                    AppendLogToTextBox(entry);
                }

                // Update stats
                UpdateStats();

                // Auto-scroll if enabled
                if (AutoScrollCheckBox.IsChecked == true)
                {
                    LogScrollViewer.ScrollToEnd();
                }
            });
        }

        private bool ShouldShowLog(LogEntry entry)
        {
            return _currentFilter switch
            {
                "Voice Only" => entry.Category.Contains("Voice", StringComparison.OrdinalIgnoreCase),
                "VAD Only" => entry.Category.Contains("VAD", StringComparison.OrdinalIgnoreCase),
                "Whisper Only" => entry.Category.Contains("Whisper", StringComparison.OrdinalIgnoreCase),
                "Errors Only" => entry.Level == LogLevel.Error,
                _ => true
            };
        }

        private void AppendLogToTextBox(LogEntry entry)
        {
            string timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
            string levelPrefix = entry.Level switch
            {
                LogLevel.Error => "[ERROR]",
                LogLevel.Warning => "[WARN] ",
                LogLevel.Info => "[INFO] ",
                LogLevel.Debug => "[DEBUG]",
                _ => "[LOG]  "
            };

            string color = entry.Level switch
            {
                LogLevel.Error => "#F48771",
                LogLevel.Warning => "#CED68D",
                LogLevel.Info => "#4EC9B0",
                LogLevel.Debug => "#858585",
                _ => "#D4D4D4"
            };

            // Simple text append (no rich formatting in TextBox, but we'll use color indicators)
            string logLine = $"[{timestamp}] {levelPrefix} [{entry.Category}] {entry.Message}\n";
            LogTextBox.AppendText(logLine);
        }

        private void UpdateStats()
        {
            StatsTextBlock.Text = $"{_allLogs.Count} log entries";
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _allLogs.Clear();
            LogTextBox.Clear();
            UpdateStats();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilterComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                _currentFilter = selectedItem.Content.ToString()?.Replace(" Logs", "").Replace(" Only", "") ?? "All";
                RefreshLogDisplay();
            }
        }

        private void RefreshLogDisplay()
        {
            LogTextBox.Clear();
            foreach (var entry in _allLogs.Where(ShouldShowLog))
            {
                AppendLogToTextBox(entry);
            }

            if (AutoScrollCheckBox.IsChecked == true)
            {
                LogScrollViewer.ScrollToEnd();
            }
        }

        public void SetStatus(string status, bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
                StatusIndicator.Fill = new SolidColorBrush(isActive
                    ? (Color)ColorConverter.ConvertFromString("#4EC9B0")
                    : (Color)ColorConverter.ConvertFromString("#858585"));
                StatusText.Foreground = new SolidColorBrush(isActive
                    ? (Color)ColorConverter.ConvertFromString("#4EC9B0")
                    : (Color)ColorConverter.ConvertFromString("#858585"));
            });
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Message { get; set; } = string.Empty;
            public string Category { get; set; } = "General";
            public LogLevel Level { get; set; }
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
