using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using PowerfulWizard.Models;

namespace PowerfulWizard
{
    public partial class SequenceVisualOverlayWindow : Window
    {
        private readonly Sequence _sequence;
        private readonly Dictionary<int, (Border border, TextBlock label)> _stepVisuals;
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        public SequenceVisualOverlayWindow(Sequence sequence)
        {
            InitializeComponent();
            _sequence = sequence;
            _stepVisuals = new Dictionary<int, (Border border, TextBlock label)>();
            
            // Make the window cover the entire screen
            WindowState = WindowState.Maximized;
            
            // Create visual elements for each step
            CreateStepVisuals();
            
            // Handle keyboard events
            KeyDown += OnKeyDown;
            
            // Make the window click-through
            Loaded += OnWindowLoaded;
        }

        private void CreateStepVisuals()
        {
            // Clear any existing visuals first
            ClearAllVisuals();
            
            for (int i = 0; i < _sequence.Steps.Count; i++)
            {
                var step = _sequence.Steps[i];
                if (step.TargetMode == TargetMode.ClickArea && step.ClickArea.Width > 0 && step.ClickArea.Height > 0)
                {
                    CreateStepVisual(i, step);
                }
            }
        }

        private void CreateStepVisual(int stepIndex, SequenceStep step)
        {
            // Create a border for the click area
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.Cyan),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(50, 0, 255, 255)),
                CornerRadius = new CornerRadius(3)
            };

            // Create a label for the step
            var label = new TextBlock
            {
                Text = step.Description,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Colors.Black),
                Padding = new Thickness(5, 2, 5, 2),
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };

            // Position the border and label
            Canvas.SetLeft(border, step.ClickArea.X);
            Canvas.SetTop(border, step.ClickArea.Y);
            border.Width = step.ClickArea.Width;
            border.Height = step.ClickArea.Height;

            // Position the label above the border
            Canvas.SetLeft(label, step.ClickArea.X);
            Canvas.SetTop(label, Math.Max(0, step.ClickArea.Y - 25));

            // Add to canvas
            OverlayCanvas.Children.Add(border);
            OverlayCanvas.Children.Add(label);

            // Store references for later updates
            _stepVisuals[stepIndex] = (border, label);
        }

        public void HighlightStep(int stepIndex)
        {
            // Reset all steps to default appearance
            foreach (var (border, label) in _stepVisuals.Values)
            {
                border.BorderBrush = new SolidColorBrush(Colors.Cyan);
                border.Background = new SolidColorBrush(Color.FromArgb(50, 0, 255, 255));
            }

            // Highlight the current step
            if (_stepVisuals.TryGetValue(stepIndex, out var current))
            {
                current.border.BorderBrush = new SolidColorBrush(Colors.Yellow);
                current.border.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0));
            }
        }

        public void RemoveStepVisual(int stepIndex)
        {
            if (_stepVisuals.TryGetValue(stepIndex, out var visual))
            {
                OverlayCanvas.Children.Remove(visual.border);
                OverlayCanvas.Children.Remove(visual.label);
                _stepVisuals.Remove(stepIndex);
            }
        }

        public void ClearAllVisuals()
        {
            OverlayCanvas.Children.Clear();
            _stepVisuals.Clear();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Make window click-through by setting extended window styles
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Allow Escape key to close the overlay
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        }
    }
}
