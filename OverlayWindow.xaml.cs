using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PowerfulWizard
{
    public partial class OverlayWindow : Window
    {
        private bool isRunning;

        public OverlayWindow(Rect area)
        {
            InitializeComponent();
            UpdateRectangle(area);
        }

        public void UpdateRectangle(Rect area)
        {
            Left = area.X;
            Top = area.Y;
            Width = area.Width;
            Height = area.Height;
            OverlayRectangle.Width = area.Width;
            OverlayRectangle.Height = area.Height;
            Canvas.SetLeft(OverlayRectangle, 0);
            Canvas.SetTop(OverlayRectangle, 0);
            OverlayRectangle.Stroke = isRunning ? Brushes.Green : Brushes.Red;
            Console.WriteLine($"OverlayWindow: Left={Left}, Top={Top}, Width={Width}, Height={Height}");
            Console.WriteLine($"OverlayRectangle: X={area.X}, Y={area.Y}, Width={area.Width}, Height={area.Height}, Color={(isRunning ? "Green" : "Red")}");
        }

        public void SetRunning(bool running)
        {
            isRunning = running;
            OverlayRectangle.Stroke = isRunning ? Brushes.Green : Brushes.Red;
            Console.WriteLine($"OverlayRectangle Color: {(isRunning ? "Green" : "Red")}");
        }
    }
}