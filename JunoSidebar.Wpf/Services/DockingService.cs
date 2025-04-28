using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Diagnostics;
using System.Threading.Tasks;

namespace JunoSidebar.Wpf.Services
{
    public class DockingService
    {
        // Win32 API declarations
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
        
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowCallback enumProc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindow(IntPtr hWnd);
        
        // Unique callback delegate for enumerating windows
        private delegate bool EnumWindowCallback(IntPtr hWnd, IntPtr lParam);
        
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
        
        // Window state constants
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;
        private const int SW_SHOWMAXIMIZED = 3;
        
        // Window relationship constants
        private const uint GW_OWNER = 4;
        
        private readonly IntPtr _sidebarHwnd;
        private readonly Func<double> _getWidth;
        private readonly Func<double> _getLeft;
        private const double EXPANDED_WIDTH = 320;
        private const double COLLAPSED_WIDTH = 80;
        private double _previousWidth = 0;
        private double _expandedLeftPosition = 0;
        private bool _hasExpandedOnce = false;
        private bool _isCurrentlyExpanded = false;
        private Dictionary<IntPtr, RECT> _originalWindowStates = new Dictionary<IntPtr, RECT>();
        private Dictionary<IntPtr, RECT> _preCollapseStates = new Dictionary<IntPtr, RECT>();
        private HashSet<IntPtr> _affectedWindows = new HashSet<IntPtr>();
        private List<IntPtr> _candidateWindows = new List<IntPtr>();
        
        // Constants for space calculation
        private const int EXPANSION_SPACE = 240; // 320 - 80 = 240px of space to reclaim
        
        public DockingService(IntPtr sidebarHwnd, Func<double> getWidth, Func<double> getLeft)
        {
            _sidebarHwnd = sidebarHwnd;
            _getWidth = getWidth;
            _getLeft = getLeft;
            _previousWidth = getWidth();
        }
        
        /// <summary>
        /// Initial adjustment of all windows at startup
        /// </summary>
        public void InitialAdjustment()
        {
            Debug.WriteLine("Performing initial window adjustment at startup");
            
            // Force immediate adjustment regardless of current state
            _isCurrentlyExpanded = true;
            _hasExpandedOnce = true;
            _expandedLeftPosition = _getLeft();
            
            // Directly shrink windows without waiting for state change detection
            ShrinkWindowsForExpandedSidebar(true);
        }
        
        /// <summary>
        /// Adjusts all applicable windows that might overlap with the sidebar
        /// </summary>
        public void AdjustAllWindows(bool forceAdjust = false)
        {
            double currentWidth = _getWidth();
            double currentLeft = _getLeft();
            
            // Determine if the sidebar is expanding or collapsing
            bool isExpanding = currentWidth > _previousWidth && Math.Abs(currentWidth - EXPANDED_WIDTH) < 20;
            bool isCollapsing = currentWidth < _previousWidth && Math.Abs(currentWidth - COLLAPSED_WIDTH) < 20;
            
            Debug.WriteLine($"Adjusting all windows. Width: {currentWidth}, Previous: {_previousWidth}, Expanding: {isExpanding}, Collapsing: {isCollapsing}, ForceAdjust: {forceAdjust}");
            
            if (isExpanding || (Math.Abs(currentWidth - EXPANDED_WIDTH) < 20 && forceAdjust))
            {
                _isCurrentlyExpanded = true;
                // Store the position when expanded for later reference
                _expandedLeftPosition = currentLeft;
                _hasExpandedOnce = true;
                Debug.WriteLine($"Sidebar is expanded. Left position: {_expandedLeftPosition}");
                
                // Take a snapshot of window positions before expanding
                // This is important for collapse restoration
                CaptureWindowStates();
                
                // Handle windows when sidebar expands
                ShrinkWindowsForExpandedSidebar(forceAdjust);
            }
            else if (isCollapsing || (Math.Abs(currentWidth - COLLAPSED_WIDTH) < 20 && forceAdjust))
            {
                _isCurrentlyExpanded = false;
                Debug.WriteLine($"Sidebar is collapsed. Previous expanded left: {_expandedLeftPosition}");
                
                // Handle windows when sidebar collapses - restore them their space
                ExpandWindowsForCollapsedSidebar();
            }
            
            _previousWidth = currentWidth;
        }
        
        /// <summary>
        /// Force relayout of all windows when sidebar size changes
        /// </summary>
        public void SidebarSizeChanged(bool isExpanded)
        {
            Debug.WriteLine($"Sidebar size changed explicitly to: {(isExpanded ? "Expanded" : "Collapsed")}");
            
            if (isExpanded != _isCurrentlyExpanded)
            {
                _isCurrentlyExpanded = isExpanded;
                
                if (isExpanded)
                {
                    // Remember the expanded position
                    _expandedLeftPosition = _getLeft();
                    _hasExpandedOnce = true;
                    
                    // Take a snapshot before we expand
                    CaptureWindowStates();
                    
                    // Shrink windows to make space for sidebar
                    ShrinkWindowsForExpandedSidebar(true);
                }
                else
                {
                    // When collapsing, aggressively restore windows
                    ExpandWindowsForCollapsedSidebar();
                }
            }
        }
        
        /// <summary>
        /// Capture current state of all windows
        /// </summary>
        private void CaptureWindowStates()
        {
            _preCollapseStates.Clear();
            
            // Capture all window states before collapse
            _candidateWindows.Clear();
            EnumWindows(EnumWindowsProc, IntPtr.Zero);
            
            foreach (IntPtr hWnd in _candidateWindows)
            {
                RECT windowRect;
                if (GetWindowRect(hWnd, out windowRect))
                {
                    string title = GetWindowTitle(hWnd);
                    
                    // Save original state if we don't have it
                    if (!_originalWindowStates.ContainsKey(hWnd))
                    {
                        _originalWindowStates[hWnd] = windowRect;
                        Debug.WriteLine($"Captured original state for: {title}, Width: {windowRect.Right - windowRect.Left}");
                    }
                    
                    // Always save the pre-collapse state for all windows
                    _preCollapseStates[hWnd] = windowRect;
                    Debug.WriteLine($"Captured pre-collapse state for: {title}, Width: {windowRect.Right - windowRect.Left}");
                }
            }
        }
        
        /// <summary>
        /// Shrink windows to make space for the expanded sidebar
        /// </summary>
        private void ShrinkWindowsForExpandedSidebar(bool forceAdjust = false)
        {
            double sidebarLeft = _getLeft();
            double sidebarWidth = _getWidth();
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            
            Debug.WriteLine($"Shrinking windows for expanded sidebar. Left: {sidebarLeft}, Width: {sidebarWidth}, Force: {forceAdjust}");
            
            _candidateWindows.Clear();
            EnumWindows(EnumWindowsProc, IntPtr.Zero);
            
            foreach (IntPtr hWnd in _candidateWindows)
            {
                try
                {
                    RECT windowRect;
                    if (!GetWindowRect(hWnd, out windowRect))
                        continue;
                        
                    int windowWidth = windowRect.Right - windowRect.Left;
                    int windowHeight = windowRect.Bottom - windowRect.Top;
                    string title = GetWindowTitle(hWnd);
                    
                    // If window extends into the sidebar area or is near the edge
                    if (windowRect.Right > sidebarLeft - 25)
                    {
                        // Save original state if we don't have it
                        if (!_originalWindowStates.ContainsKey(hWnd))
                        {
                            _originalWindowStates[hWnd] = windowRect;
                            Debug.WriteLine($"Saved original state for window: {title}, Original width: {windowWidth}");
                        }
                        
                        // Add to affected windows so we remember to expand it
                        _affectedWindows.Add(hWnd);
                        
                        // Calculate new width
                        int newWidth = (int)sidebarLeft - windowRect.Left - 5; // 5px margin
                        
                        // Don't make windows too small
                        if (newWidth > 100)
                        {
                            Debug.WriteLine($"[EXPAND SIDEBAR] Shrinking window: {title} from width {windowWidth} to {newWidth}");
                            
                            MoveWindow(
                                hWnd,
                                windowRect.Left,
                                windowRect.Top,
                                newWidth,
                                windowHeight,
                                true
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adjusting window: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Expand windows when sidebar collapses to reclaim space
        /// </summary>
        private void ExpandWindowsForCollapsedSidebar()
        {
            double sidebarLeft = _getLeft();
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            
            // The amount of space to reclaim is based on expanded vs collapsed width
            int spaceToReclaim = EXPANSION_SPACE; // Hard-coded to ensure consistency
            
            Debug.WriteLine($"Starting window expansion. Space to reclaim: {spaceToReclaim}px");
            
            // For all affected windows, now expand them
            _candidateWindows.Clear();
            EnumWindows(EnumWindowsProc, IntPtr.Zero);
            
            // First try to expand windows in our affected list
            bool anyWindowsExpanded = false;
            
            // First expansion attempt with a small delay to ensure it happens after collapse
            Task.Run(async () => 
            {
                await Task.Delay(100); // Short delay to let UI update
                
                foreach (IntPtr hWnd in _candidateWindows)
                {
                    // Skip if not affected or no original state
                    if (!_affectedWindows.Contains(hWnd) && !_originalWindowStates.ContainsKey(hWnd))
                        continue;
                    
                    try
                    {
                        RECT windowRect;
                        if (!GetWindowRect(hWnd, out windowRect))
                            continue;
                            
                        int windowWidth = windowRect.Right - windowRect.Left;
                        int windowHeight = windowRect.Bottom - windowRect.Top;
                        string title = GetWindowTitle(hWnd);
                        
                        // Get original size if available
                        int originalWidth = windowWidth;
                        if (_originalWindowStates.TryGetValue(hWnd, out RECT originalRect))
                        {
                            originalWidth = originalRect.Right - originalRect.Left;
                        }
                        
                        // Get pre-collapse state if available
                        int preCollapseWidth = windowWidth;
                        if (_preCollapseStates.TryGetValue(hWnd, out RECT preCollapseRect))
                        {
                            preCollapseWidth = preCollapseRect.Right - preCollapseRect.Left;
                        }
                        
                        // Calculate new width - use aggressive approach to reclaim space
                        int newWidth = Math.Min(originalWidth, windowWidth + spaceToReclaim);
                        
                        // Don't make window larger than screen permits
                        newWidth = Math.Min(newWidth, (int)screenWidth - windowRect.Left - 85);
                        
                        // Only expand if we're actually increasing the size
                        if (newWidth > windowWidth + 5)
                        {
                            Debug.WriteLine($"[COLLAPSE SIDEBAR] Expanding window: {title} from width {windowWidth} to {newWidth}, original was {originalWidth}");
                            
                            bool success = MoveWindow(
                                hWnd,
                                windowRect.Left,
                                windowRect.Top,
                                newWidth,
                                windowHeight,
                                true
                            );
                            
                            if (success)
                            {
                                Debug.WriteLine($"Successfully expanded window: {title}");
                                anyWindowsExpanded = true;
                            }
                            else
                            {
                                Debug.WriteLine($"Failed to expand window: {title}");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Skipping window expansion for: {title}, gain would only be {newWidth - windowWidth}px");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error expanding window: {ex.Message}");
                    }
                }
                
                // Second pass - try to expand ALL windows that might be aligned with sidebar
                if (!anyWindowsExpanded)
                {
                    Debug.WriteLine("No windows expanded in first pass, trying second pass with all windows");
                    
                    await Task.Delay(100); // Short delay
                    
                    foreach (IntPtr hWnd in _candidateWindows)
                    {
                        try
                        {
                            RECT windowRect;
                            if (!GetWindowRect(hWnd, out windowRect))
                                continue;
                                
                            int windowWidth = windowRect.Right - windowRect.Left;
                            int windowHeight = windowRect.Bottom - windowRect.Top;
                            string title = GetWindowTitle(hWnd);
                            
                            // Is this window aligned with where the sidebar was?
                            if (Math.Abs(windowRect.Right - _expandedLeftPosition) < 50)
                            {
                                int newWidth = windowWidth + spaceToReclaim;
                                
                                // Don't make window larger than screen permits
                                newWidth = Math.Min(newWidth, (int)screenWidth - windowRect.Left - 85);
                                
                                Debug.WriteLine($"[SECOND PASS] Found aligned window: {title}, expanding from {windowWidth} to {newWidth}");
                                
                                MoveWindow(
                                    hWnd,
                                    windowRect.Left,
                                    windowRect.Top,
                                    newWidth,
                                    windowHeight,
                                    true
                                );
                                
                                // Add this to our affected windows list for next time
                                _affectedWindows.Add(hWnd);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in second pass: {ex.Message}");
                        }
                    }
                }
                
                Debug.WriteLine("Window expansion process completed");
            });
        }
        
        /// <summary>
        /// Adjusts only the current foreground window
        /// </summary>
        public void AdjustForegroundWindow()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == _sidebarHwnd || foregroundWindow == IntPtr.Zero)
                return;
                
            AdjustWindowIfNeeded(foregroundWindow);
        }
        
        /// <summary>
        /// Callback for EnumWindows to collect candidate windows
        /// </summary>
        private bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam)
        {
            // Skip our own window
            if (hWnd == _sidebarHwnd)
                return true;
                
            // Skip windows that aren't visible
            if (!IsWindowVisible(hWnd))
                return true;
                
            // Skip windows that have owners (child windows)
            IntPtr owner = GetWindow(hWnd, GW_OWNER);
            if (owner != IntPtr.Zero)
                return true;
                
            // Check window state
            WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);
            if (!GetWindowPlacement(hWnd, ref placement))
                return true;
                
            // Only interested in normal windows, not minimized or maximized
            if (placement.showCmd != SW_SHOWNORMAL)
                return true;
                
            // Get window title
            string title = GetWindowTitle(hWnd);
            
            // Skip windows with empty titles or system windows
            if (string.IsNullOrEmpty(title) || 
                title == "Program Manager" || 
                title == "Windows Shell Experience Host")
                return true;
                
            // Add this window to our candidate list
            _candidateWindows.Add(hWnd);
            
            return true; // Continue enumeration
        }
        
        /// <summary>
        /// Adjusts a single window if it needs resizing
        /// </summary>
        private void AdjustWindowIfNeeded(IntPtr hWnd)
        {
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd))
                return;
                
            var title = GetWindowTitle(hWnd);
            
            if (string.IsNullOrEmpty(title) || title == "Program Manager" || 
                title == "Windows Shell Experience Host")
                return;
                
            WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);
            if (!GetWindowPlacement(hWnd, ref placement))
                return;
                
            if (placement.showCmd != SW_SHOWNORMAL)
                return;
                
            try
            {
                double sidebarLeft = _getLeft();
                double sidebarWidth = _getWidth();
                
                RECT windowRect;
                if (!GetWindowRect(hWnd, out windowRect))
                    return;
                    
                int windowWidth = windowRect.Right - windowRect.Left;
                int windowHeight = windowRect.Bottom - windowRect.Top;
                
                // Check if window extends into the sidebar area
                if (windowRect.Right > sidebarLeft - 25)
                {
                    // Save original state if we don't have it
                    if (!_originalWindowStates.ContainsKey(hWnd))
                    {
                        _originalWindowStates[hWnd] = windowRect;
                        _affectedWindows.Add(hWnd);
                        Debug.WriteLine($"Saved original state for foreground window: {title}, Original width: {windowWidth}");
                    }
                    
                    // Calculate new width
                    int newWidth = (int)sidebarLeft - windowRect.Left - 5; // 5px margin
                    
                    // Don't resize if it would be too small
                    if (newWidth > 100)
                    {
                        Debug.WriteLine($"[Foreground] Resizing window: {title} from width {windowWidth} to {newWidth}");
                        
                        MoveWindow(
                            hWnd,
                            windowRect.Left,
                            windowRect.Top,
                            newWidth,
                            windowHeight,
                            true
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adjusting foreground window: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Restore all windows to their original size that were affected by the sidebar
        /// </summary>
        public void RestoreWindowsOnExit()
        {
            Debug.WriteLine("Restoring windows to original state before exit");
            
            foreach (var kvp in _originalWindowStates)
            {
                IntPtr hWnd = kvp.Key;
                RECT originalRect = kvp.Value;
                
                if (IsWindow(hWnd) && IsWindowVisible(hWnd))
                {
                    try
                    {
                        string title = GetWindowTitle(hWnd);
                        int width = originalRect.Right - originalRect.Left;
                        int height = originalRect.Bottom - originalRect.Top;
                        
                        Debug.WriteLine($"Restoring window: {title} to original size: {width}");
                        
                        MoveWindow(
                            hWnd,
                            originalRect.Left,
                            originalRect.Top,
                            width,
                            height,
                            true
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error restoring window: {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the title of a window
        /// </summary>
        private string GetWindowTitle(IntPtr hWnd)
        {
            var titleBuilder = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            return titleBuilder.ToString();
        }
    }
}