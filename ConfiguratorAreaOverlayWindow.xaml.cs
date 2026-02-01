using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PowerfulWizard.Models;

namespace PowerfulWizard
{
    public partial class ConfiguratorAreaOverlayWindow : Window
    {
        private readonly List<UIElement> _areaVisuals = new List<UIElement>();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        public ConfiguratorAreaOverlayWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            Loaded += OnWindowLoaded;
        }

        public void RefreshFromSequence(Sequence sequence)
        {
            OverlayCanvas.Children.Clear();
            _areaVisuals.Clear();

            if (sequence?.Steps == null) return;

            for (int i = 0; i < sequence.Steps.Count; i++)
            {
                var step = sequence.Steps[i];
                string label = $"{i + 1}: {step.Description}";

                if (step.TargetMode == TargetMode.ClickArea && step.ClickArea.Width > 0 && step.ClickArea.Height > 0)
                {
                    AddAreaVisual(step.ClickArea, label, Colors.Cyan, 50);
                }

                if (step.TargetMode == TargetMode.ColorClick && step.ColorSearchArea.Width > 0 && step.ColorSearchArea.Height > 0)
                {
                    AddAreaVisual(step.ColorSearchArea, label + " (color)", Colors.Orange, 40);
                }
            }
        }

        private void AddAreaVisual(Rect area, string labelText, Color borderColor, byte fillAlpha)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(fillAlpha, borderColor.R, borderColor.G, borderColor.B)),
                CornerRadius = new CornerRadius(2),
                Width = area.Width,
                Height = area.Height
            };
            Canvas.SetLeft(border, area.X);
            Canvas.SetTop(border, area.Y);

            var label = new TextBlock
            {
                Text = labelText,
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(label, area.X);
            Canvas.SetTop(label, Math.Max(0, area.Y - 22));

            OverlayCanvas.Children.Add(border);
            OverlayCanvas.Children.Add(label);
            _areaVisuals.Add(border);
            _areaVisuals.Add(label);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
    }
}
