using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PowerfulWizard
{
    public partial class ClickAreaWindow : Window
    {
        private Point startPoint;
        private bool isDrawing;
        public Rect SelectedArea { get; private set; }

        public ClickAreaWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            isDrawing = false;
            SelectionRectangle.Visibility = Visibility.Hidden;
        }

        private void OnCanvasMouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                DrawingCanvas.CaptureMouse();
                startPoint = e.GetPosition(DrawingCanvas);
                isDrawing = true;
                SelectionRectangle.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionRectangle, startPoint.X);
                Canvas.SetTop(SelectionRectangle, startPoint.Y);
                SelectionRectangle.Width = 0;
                SelectionRectangle.Height = 0;
                e.Handled = true;
            }
        }

        private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                Point currentPoint = e.GetPosition(DrawingCanvas);
                double x = Math.Min(startPoint.X, currentPoint.X);
                double y = Math.Min(startPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);
                Canvas.SetLeft(SelectionRectangle, x);
                Canvas.SetTop(SelectionRectangle, y);
                SelectionRectangle.Width = width;
                SelectionRectangle.Height = height;
                e.Handled = true;
            }
        }

        private void OnCanvasMouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (isDrawing && e.ChangedButton == MouseButton.Left)
            {
                isDrawing = false;
                DrawingCanvas.ReleaseMouseCapture();
                Point endPoint = e.GetPosition(DrawingCanvas);
                double x = Math.Min(startPoint.X, endPoint.X);
                double y = Math.Min(startPoint.Y, endPoint.Y);
                double width = Math.Abs(endPoint.X - startPoint.X);
                double height = Math.Abs(endPoint.Y - startPoint.Y);
                SelectedArea = new Rect(x, y, width, height);
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }
    }
}