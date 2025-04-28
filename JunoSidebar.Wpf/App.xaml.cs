// File: JunoSidebar.Wpf/App.xaml.cs
using System.IO;
using System.Windows;

namespace JunoSidebar.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure WebView2 user data folder exists
            string webViewUserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JunoSidebar",
                "WebView2Data");

            if (!Directory.Exists(webViewUserDataFolder))
            {
                Directory.CreateDirectory(webViewUserDataFolder);
            }

            // Set unhandled exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Exception ex = (Exception)args.ExceptionObject;
                MessageBox.Show($"An unhandled exception occurred: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            };

            Application.Current.DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"An unhandled exception occurred: {args.Exception.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}