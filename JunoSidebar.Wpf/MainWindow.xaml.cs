// File: JunoSidebar.Wpf/MainWindow.xaml.cs

using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using JunoSidebar.Wpf.Services;
using JunoSidebar.Wpf.Services.Tools;
using JunoSidebar.Wpf.Services.ResourceBundling;

namespace JunoSidebar.Wpf
{
    public partial class MainWindow : Window
    {
        private bool _isDragging = false;
        private Point _startPoint;
        private double _originalWidth;
        private IntPtr _hwnd;
        private bool _isExpanded = false;
        private const double COLLAPSED_WIDTH = 80;
        private const double EXPANDED_WIDTH = 320;
        private DockingService _dockingService;
        private WebViewService _webViewService;
        private CoreEngine _coreEngine;
        private DispatcherTimer _windowAdjustmentTimer;
        private readonly int _maxInitializationAttempts = 3;
        private int _initializationAttempts = 0;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOMOVE = 0x0002;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            MouseRightButtonUp += MainWindow_MouseRightButtonUp;
            Closing += MainWindow_Closing;
            _windowAdjustmentTimer = new DispatcherTimer();
            _windowAdjustmentTimer.Interval = TimeSpan.FromMilliseconds(500);
            _windowAdjustmentTimer.Tick += (s, e) => AdjustOtherWindows();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_dockingService != null)
            {
                _dockingService.RestoreWindowsOnExit();
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width;
                Top = workArea.Top;
                Height = workArea.Height; 
                Debug.WriteLine($"Initializing window: Left={Left}, Top={Top}, Height={Height}, Width={Width}");
                
                await InitializeWebView();
                
                _dockingService = new DockingService(
                    _hwnd,
                    () => Width,
                    () => Left
                );
                
                if (WebView.CoreWebView2 != null)
                {
                    _webViewService = new WebViewService(
                        WebView.CoreWebView2,
                        SetExpandedState
                    );
                    
                    _coreEngine = new CoreEngine(WebView.CoreWebView2);
                    await _coreEngine.InitializeAsync();
                }
                else
                {
                    Debug.WriteLine("Error: CoreWebView2 is null after initialization");
                    ShowWebViewFailureMessage("CoreWebView2 initialization failed");
                }
                
                SetTopmost(true);
                _dockingService.InitialAdjustment();
                _windowAdjustmentTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MainWindow_Loaded: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                ShowWebViewFailureMessage($"Error during initialization: {ex.Message}");
            }
        }

        private async Task InitializeWebView()
        {
            _initializationAttempts++;
            try
            {
                Debug.WriteLine("Initializing WebView2...");
                string webViewUserDataFolder = App.WebViewUserDataFolder;
                Debug.WriteLine($"WebView2 user data folder: {webViewUserDataFolder}");
                
                // Ensure the user data folder exists
                if (!Directory.Exists(webViewUserDataFolder))
                {
                    Directory.CreateDirectory(webViewUserDataFolder);
                }
                
                // Create environment options with additional flags
                var options = new CoreWebView2EnvironmentOptions();
                options.AdditionalBrowserArguments = "--disable-web-security";
                
                var webView2Environment = await CoreWebView2Environment.CreateAsync(null, webViewUserDataFolder, options);
                await WebView.EnsureCoreWebView2Async(webView2Environment);
                
                Debug.WriteLine("WebView2 core initialized successfully");
                
                // Configure WebView2 settings
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = App.IsDebugMode;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = App.IsDebugMode;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                WebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                WebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                
                // Add event handlers
                WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                WebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                WebView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
                WebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
                
                // Resolve resource path based on mode
                string resourcePath = await ResourceBundler.ExtractAndGetResourcePath();
                Debug.WriteLine($"Resource path resolved: {resourcePath}");
                
                // Navigate to the resolved resource path
                WebView.CoreWebView2.Navigate(resourcePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView initialization error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                if (_initializationAttempts < _maxInitializationAttempts)
                {
                    Debug.WriteLine($"Retrying WebView initialization (attempt {_initializationAttempts + 1}/{_maxInitializationAttempts})...");
                    await Task.Delay(1000); // Wait a bit before retrying
                    await InitializeWebView();
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to initialize WebView2: {ex.Message}\n\nPlease ensure WebView2 Runtime is installed.", 
                        "Error", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                    
                    ShowWebViewFailureMessage($"WebView2 initialization failed: {ex.Message}");
                }
            }
        }

        private void CoreWebView2_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Debug.WriteLine($"WebView2 process failed: {e.ProcessFailedKind}");
            
            // Handle different failure types
            switch (e.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    Debug.WriteLine("Browser process exited unexpectedly");
                    break;
                    
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                    Debug.WriteLine("Render process exited unexpectedly");
                    break;
                    
                case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                    Debug.WriteLine("Render process is unresponsive");
                    break;
            }
            
            // Attempt recovery
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    Debug.WriteLine("Attempting WebView recovery...");
                    if (_initializationAttempts < _maxInitializationAttempts)
                    {
                        ShowWebViewFailureMessage("Reloading WebView due to process failure...");
                        await Task.Delay(1000);
                        await InitializeWebView();
                    }
                    else
                    {
                        ShowWebViewFailureMessage("WebView process failed repeatedly. Please restart the application.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during WebView recovery: {ex.Message}");
                }
            });
        }

        private void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            // Log resource requests to help debug loading issues
            Debug.WriteLine($"WebResource requested: {e.Request.Uri}");
        }

        private string GetFileUri(string filePath)
        {
            return new Uri(filePath).AbsoluteUri;
        }

        private void SetTopmost(bool isTopmost)
        {
            SetWindowPos(
                _hwnd,
                isTopmost ? HWND_TOPMOST : HWND_NOTOPMOST,
                0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
            );
        }

        private void AdjustOtherWindows()
        {
            if (_dockingService != null)
            {
                _dockingService.AdjustAllWindows();
            }
        }

        private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);
            _originalWidth = Width;
            CaptureMouse();
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(this);
                double deltaX = currentPosition.X - _startPoint.X;
                double newWidth = Math.Max(COLLAPSED_WIDTH, _originalWidth - deltaX);
                RECT windowRect;
                if (GetWindowRect(_hwnd, out windowRect))
                {
                    int newLeft = windowRect.Right - (int)newWidth;
                    SetWindowPos(
                        _hwnd,
                        IntPtr.Zero,
                        newLeft,
                        windowRect.Top,
                        (int)newWidth,
                        windowRect.Bottom - windowRect.Top,
                        SWP_NOZORDER | SWP_NOACTIVATE
                    );
                    Width = newWidth;
                    Left = newLeft;
                }
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { action = "resize", width = newWidth })
                    );
                }
                AdjustOtherWindows();
            }
        }

        private void DragHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                double targetWidth;
                bool newExpandedState;
                if (Width < (COLLAPSED_WIDTH + EXPANDED_WIDTH) / 2)
                {
                    targetWidth = COLLAPSED_WIDTH;
                    newExpandedState = false;
                }
                else
                {
                    targetWidth = EXPANDED_WIDTH;
                    newExpandedState = true;
                }
                if (newExpandedState != _isExpanded)
                {
                    _isExpanded = newExpandedState;
                    _dockingService.SidebarSizeChanged(_isExpanded);
                }
                RECT windowRect;
                if (GetWindowRect(_hwnd, out windowRect))
                {
                    int newLeft = windowRect.Right - (int)targetWidth;
                    SetWindowPos(
                        _hwnd,
                        IntPtr.Zero,
                        newLeft,
                        windowRect.Top,
                        (int)targetWidth,
                        windowRect.Bottom - windowRect.Top,
                        SWP_NOZORDER | SWP_NOACTIVATE
                    );
                    Width = targetWidth;
                    Left = newLeft;
                }
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { action = "setExpanded", expanded = _isExpanded })
                    );
                }
                AdjustOtherWindows();
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                if (_webViewService != null)
                {
                    _webViewService.HandleWebMessage(json);
                }
                else
                {
                    Debug.WriteLine("WebViewService not initialized, can't handle message");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing WebView message: {ex.Message}");
            }
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                Debug.WriteLine("WebView navigation completed successfully!");
                
                if (WebView.CoreWebView2 != null && _webViewService != null)
                {
                    // Inject error handling and console redirection script
                    _webViewService.InjectInitialJavaScript();
                    
                    // Send initial state
                    WebView.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { action = "init", expanded = _isExpanded })
                    );
                    
                    Debug.WriteLine("WebView initialization completed");
                }
            }
            else
            {
                Debug.WriteLine($"Navigation failed with error code: {e.WebErrorStatus}");
                ShowWebViewFailureMessage($"Navigation failed: {e.WebErrorStatus}");
            }
        }

        private void ShowWebViewFailureMessage(string message)
        {
            try
            {
                // Show a friendly error message in the WebView if navigation failed
                string errorHtml = $@"
                    <html>
                    <head>
                        <style>
                            body {{ 
                                font-family: 'Segoe UI', Arial, sans-serif; 
                                padding: 20px; 
                                background-color: #f8f9fa;
                                color: #333;
                                line-height: 1.6;
                            }}
                            .error-container {{
                                max-width: 500px;
                                margin: 40px auto;
                                background: white;
                                border-radius: 8px;
                                padding: 20px;
                                box-shadow: 0 2px 10px rgba(0,0,0,0.1);
                                border-left: 4px solid #e53e3e;
                            }}
                            h2 {{ color: #e53e3e; margin-top: 0; }}
                            p {{ margin: 10px 0; }}
                            .details {{
                                background-color: #f0f0f0;
                                padding: 10px;
                                border-radius: 4px;
                                margin-top: 20px;
                                overflow-wrap: break-word;
                                white-space: pre-wrap;
                            }}
                            button {{
                                background-color: #4299e1;
                                color: white;
                                border: none;
                                padding: 8px 15px;
                                border-radius: 4px;
                                cursor: pointer;
                                margin-top: 20px;
                            }}
                            button:hover {{ background-color: #3182ce; }}
                        </style>
                    </head>
                    <body>
                        <div class='error-container'>
                            <h2>Juno UI Loading Error</h2>
                            <p>The Juno Assistant interface couldn't be loaded properly.</p>
                            <div class='details'>{message}</div>
                            <button onclick='location.reload()'>Try Again</button>
                        </div>
                    </body>
                    </html>
                ";
                
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.NavigateToString(errorHtml);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing failure message: {ex.Message}");
            }
        }

        private void MainWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            SidebarContextMenu.IsOpen = true;
        }

        private void AlwaysOnTopMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem != null)
            {
                bool isTopmost = menuItem.IsChecked;
                SetTopmost(isTopmost);
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.PostWebMessageAsJson(
                    JsonSerializer.Serialize(new { action = "showSettings" })
                );
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        public void SetExpandedState(bool expanded)
        {
            Dispatcher.Invoke(() => 
            {
                Debug.WriteLine($"Setting expanded state: {expanded}, changing width from {Width} to {(expanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH)}");
                bool isStateChange = _isExpanded != expanded;
                _isExpanded = expanded;
                double targetWidth = expanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH;
                RECT windowRect;
                if (GetWindowRect(_hwnd, out windowRect))
                {
                    int currentHeight = windowRect.Bottom - windowRect.Top;
                    int newLeft = windowRect.Right - (int)targetWidth;
                    SetWindowPos(
                        _hwnd,
                        IntPtr.Zero,
                        newLeft,
                        windowRect.Top,
                        (int)targetWidth,
                        currentHeight,
                        SWP_NOZORDER | SWP_NOACTIVATE
                    );
                    Width = targetWidth;
                    Left = newLeft;
                    Debug.WriteLine($"Window resized using Win32 API to width: {targetWidth}, new left: {newLeft}");
                }
                if (WebView.CoreWebView2 != null)
                {
                    string json = JsonSerializer.Serialize(new { action = "setExpanded", expanded = _isExpanded });
                    WebView.CoreWebView2.PostWebMessageAsJson(json);
                    Debug.WriteLine($"Sent expanded state back to WebView: {_isExpanded}");
                }
                if (isStateChange)
                {
                    _dockingService.SidebarSizeChanged(_isExpanded);
                }
                else
                {
                    AdjustOtherWindows();
                }
            });
        }
    }
}