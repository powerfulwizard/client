using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Runtime.InteropServices;
using PowerfulWizard.Services;

namespace PowerfulWizard
{
    public partial class ColorPickerWindow : Window
    {
        private bool _isPickingColor = false;
        private IntPtr _mouseHookId = IntPtr.Zero;
        
        // P/Invoke declarations for global mouse hook
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc? _mouseProc;
        
        public System.Windows.Media.Color SelectedColor { get; set; }
        public int ColorTolerance { get; set; }
        
        private readonly MouseTrailService? _mouseTrailService;

        public ColorPickerWindow(MouseTrailService? mouseTrailService = null)
        {
            _mouseTrailService = mouseTrailService;
            InitializeComponent();
            
            // Initialize with default values
            SelectedColor = Colors.Red;
            ColorTolerance = 30;
            
            // Set up event handlers
            ToleranceSlider.ValueChanged += OnToleranceChanged;
            
            // Update UI
            UpdateColorPreview();
            UpdateToleranceLabel();
        }

        private void OnPickColorClick(object sender, RoutedEventArgs e)
        {
            _isPickingColor = true;
            PickColorButton.Content = "Click anywhere on screen...";
            PickColorButton.IsEnabled = false;
            
            // Disable mouse trail to prevent interference
            _mouseTrailService?.TemporarilyDisable();
            
            // Hide this window temporarily
            this.WindowState = WindowState.Minimized;
            
            // Install global mouse hook
            InstallMouseHook();
        }

        private void InstallMouseHook()
        {
            _mouseProc = MouseHookCallback;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle("user32"), 0);
        }

        private void UninstallMouseHook()
        {
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN && _isPickingColor)
            {
                // Mouse left button clicked somewhere on screen
                _isPickingColor = false;
                
                // Use Dispatcher.BeginInvoke to ensure we're on the UI thread
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    PickColorFromScreen();
                }));
            }
            
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void PickColorFromScreen()
        {
            try
            {
                // Get the color at current mouse position
                SelectedColor = ColorDetectionService.GetColorAtCurrentPosition();
                
                // Update UI
                UpdateColorPreview();
                UpdateColorValues();
                
                // Reset button state
                PickColorButton.Content = "Pick Color from Screen";
                PickColorButton.IsEnabled = true;
                
                // Restore window
                this.WindowState = WindowState.Normal;
                this.Activate();
                
                // Re-enable mouse trail
                _mouseTrailService?.ReEnable();
                
                // Uninstall the mouse hook
                UninstallMouseHook();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error picking color: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Reset button state
                PickColorButton.Content = "Pick Color from Screen";
                PickColorButton.IsEnabled = true;
                _isPickingColor = false;
                
                // Restore window
                this.WindowState = WindowState.Normal;
                this.Activate();
                
                // Re-enable mouse trail
                _mouseTrailService?.ReEnable();
                
                // Uninstall the mouse hook
                UninstallMouseHook();
            }
        }

        public void UpdateColorPreview()
        {
            var brush = new SolidColorBrush(SelectedColor);
            ColorPreviewBorder.Background = brush;
        }

        public void UpdateColorValues()
        {
            RedValueTextBox.Text = SelectedColor.R.ToString();
            GreenValueTextBox.Text = SelectedColor.G.ToString();
            BlueValueTextBox.Text = SelectedColor.B.ToString();
        }

        private void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ColorTolerance = (int)e.NewValue;
            UpdateToleranceLabel();
        }

        private void UpdateToleranceLabel()
        {
            ToleranceValueLabel.Content = ColorTolerance.ToString();
        }

        private void OnOKClick(object sender, RoutedEventArgs e)
        {
            // Validate color values
            if (int.TryParse(RedValueTextBox.Text, out int r) &&
                int.TryParse(GreenValueTextBox.Text, out int g) &&
                int.TryParse(BlueValueTextBox.Text, out int b))
            {
                // Ensure values are in valid range
                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));
                
                SelectedColor = Color.FromRgb((byte)r, (byte)g, (byte)b);
                ColorTolerance = (int)ToleranceSlider.Value;
                
                // Update the preview before closing
                UpdateColorPreview();
                
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Please enter valid color values (0-255).", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            // Clean up mouse hook if it's still active
            if (_isPickingColor)
            {
                UninstallMouseHook();
            }
            
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Prevent window from being minimized when picking color
            if (_isPickingColor)
            {
                this.WindowState = WindowState.Normal;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up mouse hook when window is closed
            UninstallMouseHook();
            base.OnClosed(e);
        }

        private void OnColorValueChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Update color preview in real-time as user types
            if (int.TryParse(RedValueTextBox.Text, out int r) &&
                int.TryParse(GreenValueTextBox.Text, out int g) &&
                int.TryParse(BlueValueTextBox.Text, out int b))
            {
                // Ensure values are in valid range
                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));
                
                // Update the selected color
                SelectedColor = Color.FromRgb((byte)r, (byte)g, (byte)b);
                
                // Update the preview
                UpdateColorPreview();
            }
        }

        private void OnPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Only allow numeric input
            if (!int.TryParse(e.Text, out _))
            {
                e.Handled = true;
                return;
            }
            
            // Check if the resulting text would be a valid number
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null)
            {
                var newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);
                if (int.TryParse(newText, out int value))
                {
                    // Allow if it's a valid number between 0-255
                    if (value >= 0 && value <= 255)
                    {
                        e.Handled = false;
                    }
                    else
                    {
                        e.Handled = true;
                    }
                }
                else
                {
                    e.Handled = true;
                }
            }
        }
    }
}
