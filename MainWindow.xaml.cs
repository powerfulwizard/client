using System;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PowerfulWizard
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer timer;
        private DispatcherTimer countdownTimer;
        private Random random = new Random();
        private const int HOTKEY_ID_START = 1;
        private const int HOTKEY_ID_STOP = 2;
        private uint startHotkeyModifiers;
        private uint startHotkeyKey;
        private uint stopHotkeyModifiers;
        private uint stopHotkeyKey;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;
        private const int VK_S = 0x53; // S key
        private const int VK_P = 0x50; // P key
        private const int WM_HOTKEY = 0x0312;
        private DateTime nextClickTime;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int INPUT_MOUSE = 0;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;

        public MainWindow()
        {
            InitializeComponent();
            timer = new DispatcherTimer();
            timer.Tick += OnTimerTick;
            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromMilliseconds(100);
            countdownTimer.Tick += OnCountdownTimerTick;
            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;

            // Load saved hotkeys or use defaults
            try
            {
                startHotkeyModifiers = uint.TryParse(ConfigurationManager.AppSettings["StartHotkeyModifiers"], out uint startMod) ? startMod : MOD_CONTROL | MOD_SHIFT;
                startHotkeyKey = uint.TryParse(ConfigurationManager.AppSettings["StartHotkeyKey"], out uint startKey) ? startKey : VK_S;
                stopHotkeyModifiers = uint.TryParse(ConfigurationManager.AppSettings["StopHotkeyModifiers"], out uint stopMod) ? stopMod : MOD_CONTROL | MOD_SHIFT;
                stopHotkeyKey = uint.TryParse(ConfigurationManager.AppSettings["StopHotkeyKey"], out uint stopKey) ? stopKey : VK_P;
            }
            catch
            {
                startHotkeyModifiers = MOD_CONTROL | MOD_SHIFT;
                startHotkeyKey = VK_S;
                stopHotkeyModifiers = MOD_CONTROL | MOD_SHIFT;
                stopHotkeyKey = VK_P;
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            RegisterHotKey(hWnd, HOTKEY_ID_START, startHotkeyModifiers, startHotkeyKey);
            RegisterHotKey(hWnd, HOTKEY_ID_STOP, stopHotkeyModifiers, stopHotkeyKey);
            HwndSource source = HwndSource.FromHwnd(hWnd);
            source.AddHook(WndProc);
            StatusLabel.Content = "Status: Stopped";
            NextClickLabel.Content = "Next Click: -- ms";
            StartHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(startHotkeyModifiers, startHotkeyKey)}";
            StopHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(stopHotkeyModifiers, stopHotkeyKey)}";
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hWnd, HOTKEY_ID_START);
            UnregisterHotKey(hWnd, HOTKEY_ID_STOP);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_START)
                {
                    OnStartButtonClick(this, new RoutedEventArgs());
                }
                else if (id == HOTKEY_ID_STOP)
                {
                    OnStopButtonClick(this, new RoutedEventArgs());
                }
            }
            return IntPtr.Zero;
        }

        public void UpdateHotkeys(uint startModifiers, uint startKey, uint stopModifiers, uint stopKey)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hWnd, HOTKEY_ID_START);
            UnregisterHotKey(hWnd, HOTKEY_ID_STOP);
            startHotkeyModifiers = startModifiers;
            startHotkeyKey = startKey;
            stopHotkeyModifiers = stopModifiers;
            stopHotkeyKey = stopKey;
            RegisterHotKey(hWnd, HOTKEY_ID_START, startModifiers, startKey);
            RegisterHotKey(hWnd, HOTKEY_ID_STOP, stopModifiers, stopKey);
            StartHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(startModifiers, startKey)}";
            StopHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(stopModifiers, stopKey)}";
        }

        private string GetHotkeyText(uint modifiers, uint key)
        {
            StringBuilder text = new StringBuilder();
            if ((modifiers & MOD_CONTROL) != 0) text.Append("Ctrl+");
            if ((modifiers & MOD_SHIFT) != 0) text.Append("Shift+");
            if ((modifiers & MOD_ALT) != 0) text.Append("Alt+");
            if (text.Length == 0) text.Append("None+");
            text.Append(KeyInterop.KeyFromVirtualKey((int)key));
            return text.ToString();
        }

        private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(this, startHotkeyModifiers, startHotkeyKey, stopHotkeyModifiers, stopHotkeyKey);
            settingsWindow.ShowDialog();
        }

        private void OnStartButtonClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(IntervalInput.Text, out int interval) && interval >= 100)
            {
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StatusLabel.Content = "Status: Running";
                nextClickTime = DateTime.Now.AddMilliseconds(interval);
                timer.Interval = TimeSpan.FromMilliseconds(interval);
                timer.Start();
                countdownTimer.Start();
            }
            else
            {
                MessageBox.Show("Please enter a valid interval (≥100 ms).", "Invalid Input");
            }
        }

        private void OnStopButtonClick(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            countdownTimer.Stop();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusLabel.Content = "Status: Stopped";
            NextClickLabel.Content = "Next Click: -- ms";
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (int.TryParse(IntervalInput.Text, out int interval) && int.TryParse(DeviationInput.Text, out int maxDeviation))
            {
                int deviation = random.Next(-maxDeviation, maxDeviation + 1);
                int nextInterval = Math.Max(100, interval + deviation);
                timer.Interval = TimeSpan.FromMilliseconds(nextInterval);
                nextClickTime = DateTime.Now.AddMilliseconds(nextInterval);
                SimulateMouseClick();
            }
            else
            {
                timer.Stop();
                countdownTimer.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Status: Stopped";
                NextClickLabel.Content = "Next Click: -- ms";
                MessageBox.Show("Invalid input. Please enter numeric values.", "Error");
            }
        }

        private void OnCountdownTimerTick(object sender, EventArgs e)
        {
            if (timer.IsEnabled)
            {
                double msRemaining = (nextClickTime - DateTime.Now).TotalMilliseconds;
                if (msRemaining > 0)
                {
                    NextClickLabel.Content = $"Next Click: {Math.Ceiling(msRemaining)} ms";
                }
                else
                {
                    NextClickLabel.Content = "Next Click: 0 ms";
                }
            }
        }

        private void SimulateMouseClick()
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = "0";
            }
        }
    }
}