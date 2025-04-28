// File: JunoSidebar.Wpf/Services/DockingService.cs
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Diagnostics;

namespace JunoSidebar.Wpf.Services
{
    public class DockingService
    {
        #region Win32 API
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        
        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindowVisible(IntPtr hWnd);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_SHOWMAXIMIZED = 3;
        #endregion
        
        private readonly IntPtr _sidebarHwnd;
        private readonly Func<double> _getWidth;
        private readonly Func<double> _getLeft;
        private HashSet<IntPtr> _processedWindows = new HashSet<IntPtr>();
        
        public DockingService(IntPtr sidebarHwnd, Func<double> getWidth, Func<double> getLeft)
        {
            _sidebarHwnd = sidebarHwnd;
            _getWidth = getWidth;
            _getLeft = getLeft;
        }
        
        public void AdjustForegroundWindow()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            
            // Don't adjust our own window
            if (foregroundWindow == _sidebarHwnd || foregroundWindow == IntPtr.Zero)
                return;
                
            // Check if window is visible
            if (!IsWindowVisible(foregroundWindow))
                return;
                
            // Get window title for debugging
            var titleBuilder = new System.Text.StringBuilder(256);
            GetWindowText(foregroundWindow, titleBuilder, titleBuilder.Capacity);
            var windowTitle = titleBuilder.ToString();
            
            try
            {
                // Get sidebar position and size
                double sidebarLeft = _getLeft();
                double sidebarWidth = _getWidth();
                
                // Skip certain system windows that shouldn't be resized
                if (string.IsNullOrEmpty(windowTitle) || windowTitle == "Program Manager" || 
                    windowTitle == "Windows Shell Experience Host")
                    return;
                
                // Get window placement to check if maximized
                WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf(placement);
                GetWindowPlacement(foregroundWindow, ref placement);
                
                // Only adjust normal windows, not minimized or maximized
                if (placement.showCmd != SW_SHOWNORMAL)
                    return;
                
                // Get foreground window dimensions
                RECT windowRect;
                if (!GetWindowRect(foregroundWindow, out windowRect))
                    return;
                    
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;
                
                // Check if window is in sidebar area (touches or crosses right edge)
                int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
                
                if (windowRect.Right >= screenWidth - sidebarWidth - 5)
                {
                    // Resize window to stop at sidebar
                    int newWidth = (int)sidebarLeft - windowRect.Left;
                    if (newWidth > 100) // Ensure minimum sensible width
                    {
                        MoveWindow(
                            foregroundWindow,
                            windowRect.Left,
                            windowRect.Top,
                            newWidth,
                            windowHeight,
                            true
                        );
                        
                        Debug.WriteLine($"Adjusted window: {windowTitle}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adjusting window: {ex.Message}");
            }
        }
    }
}