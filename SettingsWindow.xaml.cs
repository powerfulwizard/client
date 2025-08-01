using System;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        private void OnSaveButtonClick(object sender, RoutedEventArgs e)
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

            // Update main window hotkeys
            _mainWindow.UpdateHotkeys(_startModifiers, _startKey, _stopModifiers, _stopKey);
            Close();
        }
    }
}