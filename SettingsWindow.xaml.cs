using System;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerfulWizard
{
    public partial class SettingsWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private uint _startModifiers;
        private uint _startKey;
        private uint _stopModifiers;
        private uint _stopKey;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;

        public SettingsWindow(MainWindow mainWindow, uint startModifiers, uint startKey, uint stopModifiers, uint stopKey)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _startModifiers = startModifiers;
            _startKey = startKey;
            _stopModifiers = stopModifiers;
            _stopKey = stopKey;

            // Initialize Start hotkey
            StartHotkeyInput.Text = KeyInterop.KeyFromVirtualKey((int)_startKey).ToString();
            StartCtrlCheck.IsChecked = (_startModifiers & MOD_CONTROL) != 0;
            StartShiftCheck.IsChecked = (_startModifiers & MOD_SHIFT) != 0;
            StartAltCheck.IsChecked = (_startModifiers & MOD_ALT) != 0;

            // Initialize Stop hotkey
            StopHotkeyInput.Text = KeyInterop.KeyFromVirtualKey((int)_stopKey).ToString();
            StopCtrlCheck.IsChecked = (_stopModifiers & MOD_CONTROL) != 0;
            StopShiftCheck.IsChecked = (_stopModifiers & MOD_SHIFT) != 0;
            StopAltCheck.IsChecked = (_stopModifiers & MOD_ALT) != 0;

            // Initialize Mouse Trail settings
            LoadMouseTrailSettings();
            
            // Initialize Click Validation settings
            LoadClickValidationSettings();
            
            // Update the button content to show current color
            var brush = TrailColorButton.Background as SolidColorBrush;
            if (brush != null)
            {
                TrailColorButton.Content = $"Color: {brush.Color.ToString().Substring(3)}";
            }
        }

        private void OnHotkeyInputKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Capture the pressed key and update the textbox
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key != Key.LeftCtrl && key != Key.RightCtrl &&
                    key != Key.LeftShift && key != Key.RightShift &&
                    key != Key.LeftAlt && key != Key.RightAlt)
                {
                    textBox.Text = key.ToString();
                    uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    if (textBox.Name == "StartHotkeyInput")
                    {
                        _startKey = virtualKey;
                    }
                    else if (textBox.Name == "StopHotkeyInput")
                    {
                        _stopKey = virtualKey;
                    }
                    e.Handled = true;
                }
            }
        }

        private void OnSaveButtonClick(object? sender, RoutedEventArgs e)
        {
            // Update Start modifiers
            _startModifiers = 0;
            if (StartCtrlCheck.IsChecked == true) _startModifiers |= MOD_CONTROL;
            if (StartShiftCheck.IsChecked == true) _startModifiers |= MOD_SHIFT;
            if (StartAltCheck.IsChecked == true) _startModifiers |= MOD_ALT;

            // Update Stop modifiers
            _stopModifiers = 0;
            if (StopCtrlCheck.IsChecked == true) _stopModifiers |= MOD_CONTROL;
            if (StopShiftCheck.IsChecked == true) _stopModifiers |= MOD_SHIFT;
            if (StopAltCheck.IsChecked == true) _stopModifiers |= MOD_ALT;

            // Validate hotkeys
            if (_startKey == 0 || _stopKey == 0)
            {
                MessageBox.Show("Please set valid hotkeys for both Start and Stop.", "Invalid Hotkey");
                return;
            }

            // Save hotkeys to configuration
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings.Remove("StartHotkeyModifiers");
                config.AppSettings.Settings.Remove("StartHotkeyKey");
                config.AppSettings.Settings.Remove("StopHotkeyModifiers");
                config.AppSettings.Settings.Remove("StopHotkeyKey");
                config.AppSettings.Settings.Add("StartHotkeyModifiers", _startModifiers.ToString());
                config.AppSettings.Settings.Add("StartHotkeyKey", _startKey.ToString());
                config.AppSettings.Settings.Add("StopHotkeyModifiers", _stopModifiers.ToString());
                config.AppSettings.Settings.Add("StopHotkeyKey", _stopKey.ToString());
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save hotkeys: {ex.Message}", "Error");
                return;
            }

            // Save mouse trail settings
            SaveMouseTrailSettings();
            
            // Save click validation settings
            SaveClickValidationSettings();

            // Update main window hotkeys
            _mainWindow.UpdateHotkeys(_startModifiers, _startKey, _stopModifiers, _stopKey);
            Close();
        }

        private void LoadMouseTrailSettings()
        {
            try
            {
                EnableMouseTrailsCheck.IsChecked = bool.TryParse(ConfigurationManager.AppSettings["EnableMouseTrails"], out bool enableTrails) && enableTrails;
                TrailLengthInput.Text = ConfigurationManager.AppSettings["TrailLength"] ?? "20";
                TrailFadeSpeedInput.Text = ConfigurationManager.AppSettings["TrailFadeSpeed"] ?? "50";
                RainbowTrailCheck.IsChecked = bool.TryParse(ConfigurationManager.AppSettings["RainbowTrail"], out bool rainbowTrail) && rainbowTrail;
                
                // Load trail color
                try
                {
                    var colorString = ConfigurationManager.AppSettings["TrailColor"];
                    if (!string.IsNullOrEmpty(colorString))
                    {
                        var trailColor = (Color)ColorConverter.ConvertFromString(colorString);
                        TrailColorButton.Background = new SolidColorBrush(trailColor);
                    }
                    else
                    {
                        TrailColorButton.Background = new SolidColorBrush(Colors.Cyan);
                    }
                }
                catch
                {
                    TrailColorButton.Background = new SolidColorBrush(Colors.Cyan);
                }
                
                // Update UI based on rainbow setting
                OnRainbowTrailChanged(this, new RoutedEventArgs());
            }
            catch
            {
                // Use defaults if loading fails
                EnableMouseTrailsCheck.IsChecked = false;
                TrailLengthInput.Text = "20";
                TrailFadeSpeedInput.Text = "50";
                RainbowTrailCheck.IsChecked = false;
                TrailColorButton.Background = new SolidColorBrush(Colors.Cyan);
            }
        }

        private void SaveMouseTrailSettings()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                
                // Save mouse trail settings
                config.AppSettings.Settings.Remove("EnableMouseTrails");
                config.AppSettings.Settings.Remove("TrailLength");
                config.AppSettings.Settings.Remove("TrailFadeSpeed");
                config.AppSettings.Settings.Remove("TrailColor");
                config.AppSettings.Settings.Remove("RainbowTrail");
                
                config.AppSettings.Settings.Add("EnableMouseTrails", EnableMouseTrailsCheck.IsChecked.ToString());
                config.AppSettings.Settings.Add("TrailLength", TrailLengthInput.Text);
                config.AppSettings.Settings.Add("TrailFadeSpeed", TrailFadeSpeedInput.Text);
                config.AppSettings.Settings.Add("RainbowTrail", RainbowTrailCheck.IsChecked.ToString());
                
                if (TrailColorButton.Background is System.Windows.Media.SolidColorBrush brush)
                {
                    config.AppSettings.Settings.Add("TrailColor", brush.Color.ToString());
                }
                
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save mouse trail settings: {ex.Message}", "Error");
            }
        }

        private void OnTrailColorButtonClick(object? sender, RoutedEventArgs e)
        {
            // Simple color picker - cycle through predefined colors
            var currentBrush = TrailColorButton.Background as SolidColorBrush;
            var currentColor = currentBrush?.Color ?? Colors.Cyan;
            
            Color newColor;
            if (currentColor == Colors.Cyan)
                newColor = Colors.Red;
            else if (currentColor == Colors.Red)
                newColor = Colors.Green;
            else if (currentColor == Colors.Green)
                newColor = Colors.Blue;
            else if (currentColor == Colors.Blue)
                newColor = Colors.Yellow;
            else if (currentColor == Colors.Yellow)
                newColor = Colors.Magenta;
            else if (currentColor == Colors.Magenta)
                newColor = Colors.Orange;
            else
                newColor = Colors.Cyan;
                
            TrailColorButton.Background = new SolidColorBrush(newColor);
            TrailColorButton.Content = $"Color: {newColor.ToString().Substring(3)}"; // Remove the #FF prefix
        }
        
        private void OnRainbowTrailChanged(object? sender, RoutedEventArgs e)
        {
            // Enable/disable color button based on rainbow setting
            bool isRainbow = RainbowTrailCheck.IsChecked == true;
            TrailColorButton.IsEnabled = !isRainbow;
            
            if (isRainbow)
            {
                TrailColorButton.Content = "Color: Rainbow";
            }
            else
            {
                var brush = TrailColorButton.Background as SolidColorBrush;
                if (brush != null)
                {
                    TrailColorButton.Content = $"Color: {brush.Color.ToString().Substring(3)}";
                }
            }
        }
        
        private void LoadClickValidationSettings()
        {
            try
            {
                EnableClickValidationCheck.IsChecked = bool.TryParse(ConfigurationManager.AppSettings["EnableClickValidation"], out bool enableValidation) && enableValidation;
                ValidationAreaSizeInput.Text = ConfigurationManager.AppSettings["ValidationAreaSize"] ?? "50";
            }
            catch
            {
                // Use defaults if loading fails
                EnableClickValidationCheck.IsChecked = false;
                ValidationAreaSizeInput.Text = "50";
            }
        }

        private void SaveClickValidationSettings()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                
                // Save click validation settings
                config.AppSettings.Settings.Remove("EnableClickValidation");
                config.AppSettings.Settings.Remove("ValidationAreaSize");
                
                config.AppSettings.Settings.Add("EnableClickValidation", EnableClickValidationCheck.IsChecked.ToString());
                config.AppSettings.Settings.Add("ValidationAreaSize", ValidationAreaSizeInput.Text);
                
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save click validation settings: {ex.Message}", "Error");
            }
        }
    }
}