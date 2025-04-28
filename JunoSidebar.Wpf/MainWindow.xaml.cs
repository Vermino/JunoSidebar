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
        private DispatcherTimer _windowAdjustmentTimer;

        // Win32 API declarations for window management
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            MouseRightButtonUp += MainWindow_MouseRightButtonUp;
            _windowAdjustmentTimer = new DispatcherTimer();
            _windowAdjustmentTimer.Interval = TimeSpan.FromMilliseconds(500);
            _windowAdjustmentTimer.Tick += (s, e) => AdjustOtherWindows();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            
            // Position window at right edge of screen
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
            
            _webViewService = new WebViewService(
                WebView.CoreWebView2,
                SetExpandedState
            );
            
            SetTopmost(true);
            _windowAdjustmentTimer.Start();
        }

        private async Task InitializeWebView()
        {
            try
            {
                string webViewUserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "JunoSidebar",
                    "WebView2Data");
                    
                var webView2Environment = await CoreWebView2Environment.CreateAsync(null, webViewUserDataFolder);
                await WebView.EnsureCoreWebView2Async(webView2Environment);
                
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = Debugger.IsAttached;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                
                WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                WebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                
                // For development, use localhost
                if (Debugger.IsAttached)
                {
                    WebView.CoreWebView2.Navigate("http://localhost:3000");
                }
                else
                {
                    // For production, use embedded resources
                    string htmlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
                    if (File.Exists(htmlFilePath))
                    {
                        WebView.CoreWebView2.Navigate(new Uri(htmlFilePath).AbsoluteUri);
                    }
                    else
                    {
                        MessageBox.Show("Could not find the UI files. Please reinstall the application.", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        Application.Current.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                _dockingService.AdjustForegroundWindow();
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
                
                // Update the window width and position using SetWindowPos
                RECT windowRect;
                if (GetWindowRect(_hwnd, out windowRect))
                {
                    // Calculate new left position to keep the right edge fixed
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
                    
                    // Keep WPF properties in sync
                    Width = newWidth;
                    Left = newLeft;
                }
                
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { action = "resize", width = newWidth })
                    );
                }
            }
        }

        private void DragHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                
                double targetWidth;
                if (Width < (COLLAPSED_WIDTH + EXPANDED_WIDTH) / 2)
                {
                    targetWidth = COLLAPSED_WIDTH;
                    _isExpanded = false;
                }
                else
                {
                    targetWidth = EXPANDED_WIDTH;
                    _isExpanded = true;
                }
                
                // Resize window using Win32 API and reposition to keep right edge fixed
                RECT windowRect;
                if (GetWindowRect(_hwnd, out windowRect))
                {
                    // Calculate new left position to keep the right edge fixed
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
                    
                    // Keep WPF properties in sync
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
            _webViewService?.HandleWebMessage(e.WebMessageAsJson);
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { action = "init", expanded = _isExpanded })
                    );
                }
            }
            else
            {
                Debug.WriteLine($"Navigation failed with error code: {e.WebErrorStatus}");
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

        // Re-added this constant that was removed in the previous update
        const uint SWP_NOMOVE = 0x0002;

        public void SetExpandedState(bool expanded)
        {
            Dispatcher.Invoke(() => 
            {
                Debug.WriteLine($"Setting expanded state: {expanded}, changing width from {Width} to {(expanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH)}");
                _isExpanded = expanded;
                
                double targetWidth = expanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH;
                
                // Use Win32 API to resize the window directly while preserving the right edge position
                RECT windowRect;
                if (GetWindowRect(_hwnd, out windowRect))
                {
                    int currentHeight = windowRect.Bottom - windowRect.Top;
                    
                    // Calculate new left position to keep the right edge fixed
                    int newLeft = windowRect.Right - (int)targetWidth;
                    
                    // Use SetWindowPos to change both size and position
                    SetWindowPos(
                        _hwnd,
                        IntPtr.Zero,
                        newLeft,
                        windowRect.Top,
                        (int)targetWidth,
                        currentHeight,
                        SWP_NOZORDER | SWP_NOACTIVATE
                    );
                    
                    // Update the WPF properties to keep them in sync
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
                
                AdjustOtherWindows();
            });
        }
    }
}