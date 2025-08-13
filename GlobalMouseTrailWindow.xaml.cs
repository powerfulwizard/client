using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerfulWizard.Services;

namespace PowerfulWizard
{
    public partial class GlobalMouseTrailWindow : Window
    {
        private readonly MouseTrailService _mouseTrailService;
        private readonly DispatcherTimer _mouseTracker;
        
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public GlobalMouseTrailWindow()
        {
            InitializeComponent();
            
            _mouseTrailService = new MouseTrailService();
            TrailCanvas.Children.Add(_mouseTrailService.TrailCanvas);
            
            // Track mouse position globally
            _mouseTracker = new DispatcherTimer();
            _mouseTracker.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            _mouseTracker.Tick += OnMouseTrackerTick;
            _mouseTracker.Start();
            
            // Load initial settings
            _mouseTrailService.LoadSettings();
            
            // Make the window completely click-through
            Loaded += OnWindowLoaded;
        }
        
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Make window click-through by setting extended window styles
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        
        private void OnMouseTrackerTick(object sender, EventArgs e)
        {
            if (GetCursorPos(out POINT point))
            {
                var screenPosition = new Point(point.X, point.Y);
                _mouseTrailService.AddTrailPoint(screenPosition);
            }
        }
        
        public void RefreshSettings()
        {
            _mouseTrailService.LoadSettings();
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _mouseTracker?.Stop();
            base.OnClosed(e);
        }
    }
}
