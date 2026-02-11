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
        
        // Static access to the mouse trail service
        public static MouseTrailService CurrentMouseTrailService { get; private set; } = null!;
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
            CurrentMouseTrailService = _mouseTrailService;
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
        
        private void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            // Cover the full virtual screen (all monitors) so trail coordinates from GetCursorPos match.
            // GetCursorPos returns virtual screen coords; a primary-only maximized window would show offset trails on multi-monitor.
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            // Make window click-through by setting extended window styles
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        
        private void OnMouseTrackerTick(object? sender, EventArgs e)
        {
            if (GetCursorPos(out POINT point))
            {
                // Convert virtual screen coords to window-relative (window covers virtual screen with possible non-zero origin)
                var windowRelative = new Point(
                    point.X - SystemParameters.VirtualScreenLeft,
                    point.Y - SystemParameters.VirtualScreenTop);
                _mouseTrailService.AddTrailPoint(windowRelative);
            }
        }
        
        public void RefreshSettings()
        {
            _mouseTrailService.LoadSettings();
        }
        
        public void CreateBurstEffect(Point screenPosition)
        {
            // Convert virtual screen coords to window-relative for correct placement on multi-monitor
            var windowRelative = new Point(
                screenPosition.X - SystemParameters.VirtualScreenLeft,
                screenPosition.Y - SystemParameters.VirtualScreenTop);
            _mouseTrailService.CreateBurstEffect(windowRelative);
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _mouseTracker?.Stop();
            base.OnClosed(e);
        }
    }
}
