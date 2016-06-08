namespace CustomWindow
{
    using System;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Effects;
    using System.Windows.Shell;

    public partial class Borderless : Window
    {
        private const int BlurRadius = 5;
        private const int BorderWidth = 6;

        /// <summary>
        /// Indicate if the window was snapped on one side
        /// so it can be restored correctly
        /// </summary>
        private bool IsSnapped { get; set; }

        /// <summary>
        /// Indicate if the window is maximized from a mouse 
        /// double click action
        /// </summary>
        private bool IsMaximizing { get; set; }

        public void Close(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public void Minimize(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        public void Maximize(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ?
                               WindowState.Normal : WindowState.Maximized;
        }

        public void RestoreOrMaximize(object sender, MouseButtonEventArgs e)
        {
            if (this.IsSnapped)
            {
                this.Top = this.RestoreBounds.Top;
                this.Left = this.RestoreBounds.Left;
                this.Width = this.RestoreBounds.Width;
                this.Height = this.RestoreBounds.Height;

                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = this.WindowState == WindowState.Maximized ?
                                   WindowState.Normal : WindowState.Maximized;

                // Adding guard to fix issue with OnMouseMove handler being called
                // on a double click maximize action 
                this.IsMaximizing = this.WindowState == WindowState.Maximized;
            }            
        }

        /// <summary>
        /// Support for custom window drag move and 
        /// restore from maximized window state
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (e.LeftButton == MouseButtonState.Pressed && !this.IsMaximizing)
            {                
                if (this.WindowState == WindowState.Maximized)
                {
                    IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                    MonitorArea monitorArea = GetMonitorArea(hwnd);

                    // When passing null to GetPosition, we get the mouse position 
                    // relative to the containing window.
                    Point position = e.GetPosition(null); //e.MouseDevice.GetPosition(null);
                    Point screen = this.PointToScreen(position);
                    Point point = this.PointFromScreen(position);

                    // X coordinate can be negative on multi monitor setups
                    point.X = point.X > 0 ? point.X : (monitorArea.Work.Width + point.X);

                    var statusBar = sender as StatusBar;

                    double leftMargin = screen.X - this.PointToScreen(e.GetPosition(statusBar)).X;
                    double rightMargin = (monitorArea.Work.Width - statusBar.ActualWidth - leftMargin);

                    double dragAreaWidth = monitorArea.Work.Width - leftMargin - rightMargin;
                    double restoreDragAreaWidth = this.RestoreBounds.Width - leftMargin - rightMargin;
                    double x = restoreDragAreaWidth * point.X / dragAreaWidth;
                                        
                    double leftBound = monitorArea.Work.Left + this.RestoreBounds.Width - rightMargin;
                    double rightBound = monitorArea.Work.Left + monitorArea.Work.Width - this.RestoreBounds.Width;

                    // The restore bounds width is within the left bound region width
                    if (screen.X < leftBound) 
                    {
                        this.Left = monitorArea.Work.Left + monitorArea.Offset.x;
                    }
                    // The restore bounds width is shorter than the left / right bound region width
                    else if ((screen.X > leftBound) && (screen.X < rightBound))
                    {
                        this.Left = screen.X - monitorArea.Offset.x - leftMargin - x;
                    }
                    // The restore bounds width is within the right bound region width
                    else if (screen.X > rightBound)
                    {
                        this.Left = rightBound;
                    }

                    // Take into account the DropShadow effect BlurRadius width
                    this.Left -= BlurRadius;
                    this.Top = monitorArea.Offset.y - BlurRadius;

                    this.WindowState = WindowState.Normal;
                }
                this.DragMove();
            }

            this.IsMaximizing = false;

            e.Handled = true;
        }

        public Borderless()
        {            
            // The InitializeComponent method is being created at compile time 
            // by the XAML Parser and needs to be called at runtime to load the
            // compiled XAML page of a component.            
            var initializer = this.GetType().GetMethod("InitializeComponent", 
                                             BindingFlags.Public | BindingFlags.Instance);

            // The method exists at runtime but not at design-time, so
            // make sure it is not called during design time (it won't compile)
            // Can also use -> if (!DesignerProperties.GetIsInDesignMode(this))
            if (initializer != null)
            {
                initializer.Invoke(this, null);
            }

            // Adding hook to WndProc
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            HwndSource source = HwndSource.FromHwnd(hwnd);
            source.AddHook(new HwndSourceHook(WndProc));

            EnableDropShadow(this);
            EnableResizeBorder(this);

            this.BorderBrush = Brushes.Transparent;

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextHintingMode(this, TextHintingMode.Auto);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);

            // For better icon rendering -> RenderOptions.BitmapScalingMode="HighQuality" (same as Fant mode)
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        /// <summary>
        /// Removing the WndProc hook when window is closing
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            IntPtr hwnd = (new WindowInteropHelper(this)).Handle;
            HwndSource src = HwndSource.FromHwnd(hwnd);
            src.RemoveHook(new HwndSourceHook(WndProc));

            base.OnClosing(e);
        }

        [DllImport("user32")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [DllImport("user32")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 4)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
            public POINT(int x, int y) { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private class MonitorArea
        {
            public struct Region
            {
                public int Left;
                public int Right;
                public int Top;
                public int Bottom;
                public int Width;
                public int Height;
            }

            public Region Work;
            public Region Display;

            public POINT Offset;

            public MonitorArea(RECT display, RECT work)
            {
                Display.Left   = display.left;
                Display.Right  = display.right;
                Display.Top    = display.top;
                Display.Bottom = display.bottom;
                Display.Width  = Math.Abs(display.right - display.left);
                Display.Height = Math.Abs(display.bottom - display.top);

                Work.Left   = work.left;
                Work.Right  = work.right;
                Work.Top    = work.top;
                Work.Bottom = work.bottom;
                Work.Width  = Math.Abs(work.right - work.left);
                Work.Height = Math.Abs(work.bottom - work.top);

                Offset = new POINT(Math.Abs(work.left - display.left),
                                   Math.Abs(work.top - display.top));
            }
        }

        private const Int32 WM_WINDOWPOSCHANGING = 0x0046;
        private const Int32 SWP_NOSIZE = 0x0001;
        private const Int32 WM_GETMINMAXINFO = 0x0024;
        private const Int32 MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const Int32 WM_SIZE = 0x0005;
        private const Int32 SIZE_RESTORED = 0;
        private const Int32 SIZE_MINIMIZED = 1;
        private const Int32 SIZE_MAXIMIZED = 2;

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                // Toggle the DropShadowEffect when window is snapped or maximized
                case WM_SIZE:
                {
                    int resizing = (int)wParam;

                    if (resizing == SIZE_RESTORED)
                    {
                        MonitorArea monitorArea = GetMonitorArea(hwnd);

                        if (monitorArea != null)
                        {
                            // LOWORD
                            int width = ((int)lParam & 0x0000ffff); 
                                
                            // HIWORD
                            int height = (int)((int)lParam & 0xffff0000) >> 16;

                            Window window = GetWindow(hwnd);

                            // Detect if window was snapped to screen side of current monitor
                            // or if spanning multiple monitors
                            if (height == monitorArea.Work.Height || 
                                width == SystemParameters.VirtualScreenWidth ||
                                height == SystemParameters.VirtualScreenHeight)
                            {
                                (window as Borderless).IsSnapped = true;
                                                                     
                                DisableDropShadow(window);

                                UpdateResizeBorder(window, monitorArea,window.Left, window.Top, width, height);
                            }
                            else
                            {
                                (window as Borderless).IsSnapped = false;

                                EnableDropShadow(window);
                                EnableResizeBorder(window);
                            }                           
                        }
                    }
                    else if (resizing == SIZE_MAXIMIZED)
                    {
                        Window window = GetWindow(hwnd);

                        DisableDropShadow(window);
                        DisableResizeBorder(window);
                    }
                }
                break;


                // To handle proper resizing of the custom window
                case WM_GETMINMAXINFO:
                {
                    MonitorArea monitorArea = GetMonitorArea(hwnd);

                    if (monitorArea != null)
                    {
                        MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                            
                        Window window = GetWindow(hwnd);

                        mmi.ptMaxPosition.x  = monitorArea.Offset.x;
                        mmi.ptMaxPosition.y  = monitorArea.Offset.y;
                        mmi.ptMaxSize.x      = monitorArea.Work.Width;
                        mmi.ptMaxSize.y      = monitorArea.Work.Height;

                        // To support minimum window size
                        mmi.ptMinTrackSize.x = (int)window.MinWidth;
                        mmi.ptMinTrackSize.y = (int)window.MinHeight;

                        Marshal.StructureToPtr(mmi, lParam, true);
                        handled = true;
                    }
                }
                break;

                // To activate/deactivate border resize handles from window position
                case WM_WINDOWPOSCHANGING:
                {
                    WINDOWPOS windowPos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));

                    if ((windowPos.flags & SWP_NOSIZE) != SWP_NOSIZE)
                    {
                        MonitorArea monitorArea = GetMonitorArea(hwnd);

                        if (monitorArea != null)
                        {
                            Window window = GetWindow(hwnd);

                            UpdateResizeBorder(window, monitorArea, windowPos.x, windowPos.y, windowPos.cx, windowPos.cy);
                        }
                    }

                }
                break;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Activate or deactivate resize handles given 
        /// the current window position and size
        /// </summary>
        /// <remarks>
        /// The resize border maximum size is the virtual screen limits
        /// </remarks>
        /// <param name="window"></param>
        /// <param name="monitorArea"></param>
        /// <param name="left"></param>
        /// <param name="top"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private static void UpdateResizeBorder(Window window, MonitorArea monitorArea, double left, double top, double width, double height)
        {
            double leftBorder   = left <= monitorArea.Offset.x ? 0 : BorderWidth;
            double rightBorder  = left + width >= SystemParameters.VirtualScreenWidth ? 0 : BorderWidth;
            double topBorder    = top <= monitorArea.Offset.y ? 0 : BorderWidth;
            double bottomBorder = top + height >= SystemParameters.VirtualScreenHeight ? 0 : BorderWidth;

            EnableResizeBorder(window, leftBorder, topBorder, rightBorder, bottomBorder);            
        }

        /// <summary>
        /// Get the window reference given a window handle
        /// </summary>
        /// <param name="hwnd">The Window handle</param>
        /// <returns>A Window instance or null if no match</returns>
        private static Window GetWindow(IntPtr hwnd)
        {
            HwndSource hwndSource = HwndSource.FromHwnd(hwnd);
            return hwndSource.RootVisual as Window;
        }

        /// <summary>
        /// Get the current monitor area of the Window         
        /// </summary>
        /// <param name="hwnd"></param>
        /// <returns></returns>
        private static MonitorArea GetMonitorArea(IntPtr hwnd)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);

                return new MonitorArea(monitorInfo.rcMonitor, monitorInfo.rcWork);
            }

            return null;
        }

        private static void EnableDropShadow(Window window)
        {
            var dropShadowEffect = window.Effect as DropShadowEffect;

            if (dropShadowEffect == null)
            {
                dropShadowEffect             = new DropShadowEffect();
                dropShadowEffect.BlurRadius  = BlurRadius;
                dropShadowEffect.ShadowDepth = 0;
                dropShadowEffect.Opacity     = 0.8;
                dropShadowEffect.Color       = Colors.Black;

                window.Effect = dropShadowEffect;
            }

            dropShadowEffect.Opacity = 0.8;

            window.BorderThickness = new Thickness(dropShadowEffect.BlurRadius);            
        }

        private static void DisableDropShadow(Window window)
        {
            window.BorderThickness = new Thickness(0);
        }

        private static void EnableResizeBorder(Window window)
        {
            EnableResizeBorder(window, BorderWidth, BorderWidth, BorderWidth, BorderWidth);
        }

        private static void EnableResizeBorder(Window window, double left, double top, double right, double bottom)
        {
            var chrome = WindowChrome.GetWindowChrome(window);

            if (chrome == null)
            {
                chrome = new WindowChrome();
                chrome.CaptionHeight = 0;
            }

            chrome.ResizeBorderThickness = new Thickness(left, top, right, bottom);

            WindowChrome.SetWindowChrome(window, chrome);
        }
        
        private static void DisableResizeBorder(Window window)
        {
            var chrome = WindowChrome.GetWindowChrome(window);

            chrome.ResizeBorderThickness = new Thickness(0);

            WindowChrome.SetWindowChrome(window, chrome);
        }

        #region System Menu

        private const Int32 WM_SYSCOMMAND = 0x112;
        private const UInt32 TPM_LEFTALIGN = 0x0000;
        private const UInt32 TPM_RETURNCMD = 0x0100;
        private const UInt32 MF_ENABLED = 0x00000000;
        private const UInt32 MF_GRAYED = 0x00000001;
        private const UInt32 MF_DISABLED = 0x00000002;
        private const UInt32 SC_MAXIMIZE = 0xF030;
        private const UInt32 SC_RESTORE = 0xF120;
        private const UInt32 SC_MOVE = 0xF010;
        private const UInt32 SC_SIZE = 0xF000;
        private const UInt32 SC_CLOSE = 0xF060;
        private const UInt32 MF_BYCOMMAND = 0x00000000;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        [DllImport("user32.dll")]
        static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

        public void ShowSystemMenu(object sender, MouseButtonEventArgs e)
        {
            Point position = e.GetPosition(null);

            if (sender is DockPanel)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    ShowSystemMenu(position);
                }
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2) 
                {
                    // The default behavior on a double mouse click over the
                    // window icon is to close the application
                    this.Close();
                }
                else 
                {
                    ShowSystemMenu(position);
                }
            }
        }

        private void ShowSystemMenu(Point position, int offset = 10) 
        {
            WindowInteropHelper helper = new WindowInteropHelper(this);
            IntPtr callingWindow = helper.Handle;
            IntPtr wMenu = GetSystemMenu(callingWindow, false);

            // The resizing is not supported (but still needs to be defined otherwise the window cannot be resized)
            EnableMenuItem(wMenu, SC_SIZE, MF_GRAYED);

            // The move command is not supported
            DeleteMenu(wMenu, SC_MOVE, MF_BYCOMMAND);

            // Display the menu
            if (this.WindowState == WindowState.Maximized)
            {
                EnableMenuItem(wMenu, SC_MAXIMIZE, MF_GRAYED);
            }
            else
            {
                EnableMenuItem(wMenu, SC_MAXIMIZE, MF_ENABLED);
            }

            Point point = this.PointToScreen(position);

            int value = TrackPopupMenuEx(wMenu, TPM_LEFTALIGN | TPM_RETURNCMD, (int)point.X, (int)point.Y + offset, callingWindow, IntPtr.Zero);

            // On error of if user cancel, value returned by TrackPopupMenuEx is 0
            if (value == 0)
                return;

            PostMessage(callingWindow, WM_SYSCOMMAND, new IntPtr(value), IntPtr.Zero);
        }

        #endregion System Menu
    }
}
