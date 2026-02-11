using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace PowerfulWizard.Services
{
    /// <summary>
    /// Converts between WPF (device-independent pixels) and physical screen coordinates
    /// used by GetCursorPos/SetCursorPos. Needed because the click area is captured in
    /// WPF DIPs (e.g. from ClickAreaWindow) but SetCursorPos expects physical coordinates.
    /// </summary>
    public static class ScreenCoordinateHelper
    {
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        /// <summary>
        /// Converts a point from WPF/device-independent pixels to physical screen coordinates
        /// (the same space as GetCursorPos/SetCursorPos). Use this for click area coordinates
        /// before calling SetCursorPos so the cursor goes to the correct place on scaled/multi-monitor setups.
        /// </summary>
        public static Point DipToPhysical(Point dipPoint)
        {
            // Prefer WPF's transform when we have a valid source (most accurate for per-monitor DPI).
            if (Application.Current?.MainWindow != null)
            {
                var source = PresentationSource.FromVisual(Application.Current.MainWindow);
                if (source?.CompositionTarget != null)
                {
                    var m = source.CompositionTarget.TransformToDevice;
                    return new Point(dipPoint.X * m.M11, dipPoint.Y * m.M22);
                }
            }

            // Fallback: scale using primary monitor (DPI-unaware metrics vs WPF DIPs).
            double scaleX = (double)GetSystemMetrics(SM_CXSCREEN) / SystemParameters.PrimaryScreenWidth;
            double scaleY = (double)GetSystemMetrics(SM_CYSCREEN) / SystemParameters.PrimaryScreenHeight;
            return new Point(dipPoint.X * scaleX, dipPoint.Y * scaleY);
        }
    }
}
