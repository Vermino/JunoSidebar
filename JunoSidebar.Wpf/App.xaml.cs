// File: JunoSidebar.Wpf\App.xaml.cs

using System;
using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf
{
    public partial class App : Application
    {
        public static string AppDataDirectory { get; private set; } = string.Empty;
        public static string WebViewUserDataFolder { get; private set; } = string.Empty;
        public static string LogsDirectory { get; private set; } = string.Empty;
        public static bool IsDebugMode { get; private set; }
        public static string AppVersion { get; private set; } = "1.0.0";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Set up application directories
            AppDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar");
            WebViewUserDataFolder = Path.Combine(AppDataDirectory, "WebView2Data");
            LogsDirectory = Path.Combine(AppDataDirectory, "Logs");
            
            // Ensure all necessary directories exist
            EnsureDirectoriesExist();
            
            // Determine if running in debug mode
            IsDebugMode = Debugger.IsAttached;
            
            // Set up global exception handling
            SetupExceptionHandling();
            
            // Get application version
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var assemblyName = assembly.GetName();
                if (assemblyName.Version != null)
                {
                    AppVersion = assemblyName.Version.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting application version: {ex.Message}");
            }
            
            Debug.WriteLine($"Application starting: Version={AppVersion}, Debug={IsDebugMode}");
        }

        private void EnsureDirectoriesExist()
        {
            var directories = new[]
            {
                AppDataDirectory,
                WebViewUserDataFolder,
                LogsDirectory,
                Path.Combine(AppDataDirectory, "Conversations"),
                Path.Combine(AppDataDirectory, "Personalities"),
                Path.Combine(AppDataDirectory, "Tools"),
                Path.Combine(AppDataDirectory, "VoiceData"),
                Path.Combine(AppDataDirectory, "Settings")
            };
            
            foreach (var directory in directories)
            {
                try
                {
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        Debug.WriteLine($"Created directory: {directory}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating directory {directory}: {ex.Message}");
                    // Continue with other directories even if one fails
                }
            }
        }

        private void SetupExceptionHandling()
        {
            // Handle UI thread exceptions
            Application.Current.DispatcherUnhandledException += (s, args) =>
            {
                HandleException(args.Exception, "Dispatcher");
                args.Handled = true; // Prevent application from crashing
            };
            
            // Handle non-UI thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                HandleException(args.ExceptionObject as Exception, "AppDomain");
            };
            
            // Handle exceptions from async/await and Tasks
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                HandleException(args.Exception, "Task");
                args.SetObserved(); // Prevent application from crashing
            };
        }

        private void HandleException(Exception? exception, string source)
        {
            if (exception == null) return;
            
            try
            {
                // Log the exception to a file
                string logPath = Path.Combine(LogsDirectory, "error_log.txt");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string message = $"[{timestamp}] {source} Exception: {exception.Message}\r\n" +
                                 $"Stack Trace: {exception.StackTrace}\r\n\r\n";
                
                File.AppendAllText(logPath, message);
                Debug.WriteLine($"Exception logged: {exception.Message}");
                
                // Show more detailed error in debug mode
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
                    // Show simplified error in release mode
                    MessageBox.Show(
                        $"An error occurred: {exception.Message}\n\nDetails have been logged.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                // If error logging itself fails, show a critical error
                Debug.WriteLine($"Critical error in exception handler: {ex.Message}");
                
                MessageBox.Show(
                    $"A critical error occurred: {exception.Message}\n\nAdditional error: {ex.Message}",
                    "Critical Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}