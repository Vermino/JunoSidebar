// File: JunoSidebar/JunoSidebar.Wpf/App.xaml.cs
using System;
using System.IO;
using System.Windows;

namespace JunoSidebar.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// The base directory for application data.
        /// </summary>
        public static string AppDataDirectory { get; private set; } = string.Empty;

        /// <summary>
        /// The directory for WebView2 user data.
        /// </summary>
        public static string WebViewUserDataFolder { get; private set; } = string.Empty;

        /// <summary>
        /// Gets whether the application is running in debug mode.
        /// </summary>
        public static bool IsDebugMode { get; private set; }

        /// <summary>
        /// Application startup handler.
        /// </summary>
        /// <param name="e">Startup event arguments.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up application data directory
            AppDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar");

            // Set up WebView2 user data folder
            WebViewUserDataFolder = Path.Combine(AppDataDirectory, "WebView2Data");

            // Create directories if they don't exist
            EnsureDirectoriesExist();

            // Check if we're in debug mode
            IsDebugMode = System.Diagnostics.Debugger.IsAttached;

            // Set up global exception handling
            SetupExceptionHandling();
        }

        /// <summary>
        /// Creates necessary application directories if they don't exist.
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            var directories = new[]
            {
                AppDataDirectory,
                WebViewUserDataFolder,
                Path.Combine(AppDataDirectory, "Conversations"),
                Path.Combine(AppDataDirectory, "Personalities"),
                Path.Combine(AppDataDirectory, "Tools"),
                Path.Combine(AppDataDirectory, "VoiceData")
            };

            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }

        /// <summary>
        /// Sets up global exception handling for the application.
        /// </summary>
        private void SetupExceptionHandling()
        {
            // Handle exceptions in the dispatcher
            Application.Current.DispatcherUnhandledException += (s, args) =>
            {
                HandleException(args.Exception, "Dispatcher");
                args.Handled = true;
            };

            // Handle exceptions in the AppDomain
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                HandleException(args.ExceptionObject as Exception, "AppDomain");
            };

            // Handle exceptions in tasks
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                HandleException(args.Exception, "Task");
                args.SetObserved();
            };
        }

        /// <summary>
        /// Handles an exception by logging it and showing a message to the user.
        /// </summary>
        /// <param name="exception">The exception to handle.</param>
        /// <param name="source">The source of the exception.</param>
        private void HandleException(Exception? exception, string source)
        {
            if (exception == null) return;

            try
            {
                // Log the exception
                string logPath = Path.Combine(AppDataDirectory, "error_log.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string message = $"[{timestamp}] {source} Exception: {exception.Message}\n{exception.StackTrace}\n\n";
                
                File.AppendAllText(logPath, message);

                // Show error message to user
                if (IsDebugMode)
                {
                    MessageBox.Show(
                        $"An error occurred: {exception.Message}\n\nStack Trace:\n{exception.StackTrace}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show(
                        $"An error occurred: {exception.Message}\n\nDetails have been logged.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch
            {
                // If logging fails, show a simpler message
                MessageBox.Show(
                    $"A critical error occurred: {exception.Message}",
                    "Critical Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}