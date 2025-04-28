// File: JunoSidebar.Wpf\Services\ResourceBundling\ReactDevServer.cs

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services.ResourceBundling
{
    public static class ReactDevServer
    {
        private const string DEFAULT_DEV_SERVER_URL = "http://localhost:3000";
        private const int CONNECTION_TIMEOUT_MS = 2000;
        private static bool? _isServerRunning = null;
        
        public static async Task<bool> IsRunningAsync()
        {
            // If we've already checked and found it running, return the cached result
            if (_isServerRunning.HasValue && _isServerRunning.Value)
                return true;
            
            Debug.WriteLine("ReactDevServer: Checking if development server is running at " + DEFAULT_DEV_SERVER_URL);
            
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMilliseconds(CONNECTION_TIMEOUT_MS);
                
                var response = await httpClient.GetAsync(DEFAULT_DEV_SERVER_URL);
                bool isRunning = response.IsSuccessStatusCode;
                
                if (isRunning)
                {
                    Debug.WriteLine("ReactDevServer: Development server is running");
                    _isServerRunning = true;
                }
                else
                {
                    Debug.WriteLine($"ReactDevServer: Server responded with status code {response.StatusCode}");
                    _isServerRunning = false;
                }
                
                return isRunning;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ReactDevServer: Error checking development server: {ex.Message}");
                _isServerRunning = false;
                return false;
            }
        }
        
        public static string GetUrl()
        {
            return DEFAULT_DEV_SERVER_URL;
        }
        
        public static async Task<bool> TryStartServerAsync()
        {
            // This method would attempt to start the React dev server if it's not running
            // This is a placeholder for future implementation
            Debug.WriteLine("ReactDevServer: Attempting to start development server");
            
            // For now, just check if it's already running
            return await IsRunningAsync();
        }
    }
}