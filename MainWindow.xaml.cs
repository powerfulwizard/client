using System.Configuration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using PowerfulWizard.Models;
using PowerfulWizard.Services;

namespace PowerfulWizard
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer timer;
        private DispatcherTimer countdownTimer;
        private DispatcherTimer movementTimer;
        private readonly Random random = Random.Shared;
        private const int HOTKEY_ID_START = 1;
        private const int HOTKEY_ID_STOP = 2;
        private const int HOTKEY_ID_RECORD = 3;
        private const int HOTKEY_ID_PLAY = 4;
        private uint startHotkeyModifiers;
        private uint startHotkeyKey;
        private uint stopHotkeyModifiers;
        private uint stopHotkeyKey;
        private uint recordHotkeyModifiers;
        private uint recordHotkeyKey;
        private uint playHotkeyModifiers;
        private uint playHotkeyKey;
        private Rect clickArea;
        private Rect colorSearchArea; // New field for color search area
        private OverlayWindow? overlayWindow;
        private System.Windows.Media.Color targetColor = System.Windows.Media.Colors.Red;
        private int colorTolerance = 30;
        private System.Windows.Point? cachedColorTarget; // Cache the found color target
        private bool isColorDetectionRunning = false;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;
        private const int VK_S = 0x53; // S key
        private const int VK_P = 0x50; // P key
        private const int VK_F8 = 0x77; // F8 key
        private const int VK_F9 = 0x78; // F9 key
        private const int WM_HOTKEY = 0x0312;
        private DateTime nextClickTime;
        private Point targetPosition;
        private Point currentPosition;
        private Point bezierControlPoint;
        private int movementSteps;
        private int currentStep;
        private int movementDuration;
        private List<double> _speedIntervals = new();
        private const int MIN_MOVEMENT_DURATION_MS = 100;
        private const int MAX_MOVEMENT_DURATION_MS = 250;
        private const int MOVEMENT_STEPS = 10;
        
        // Enhanced movement speed randomization
        private const int MIN_BASE_DURATION_MS = 80;
        private const int MAX_BASE_DURATION_MS = 300;
        private const double SPEED_VARIATION_FACTOR = 0.3; // 30% speed variation within movement
        private const double DISTANCE_SPEED_FACTOR = 0.15; // Distance affects speed by 15%
        
        // Sequence functionality
        private SequenceRunner sequenceRunner = null!;
        private Sequence? currentSequence;
        private bool isSequenceMode = false;
        
        // Mouse trail functionality
        private GlobalMouseTrailWindow globalMouseTrailWindow;
        
        // Mouse recording functionality
        private MouseRecordingService mouseRecordingService;
        private MouseRecording? currentRecording;
        private double currentPlaybackSpeed = 1.0;

        /// <summary>Debounce same start/stop hotkey so key/mouse repeat doesn't stop-then-start.</summary>
        private DateTime _lastSameHotkeyToggleUtc = DateTime.MinValue;
        private const int SAME_HOTKEY_DEBOUNCE_MS = 400;

        
        public enum ClickType
        {
            LeftClick,
            RightClick,
            MiddleClick,
            DoubleClick
        }
        

        
        private ClickType currentClickType = ClickType.LeftClick;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();
        
        // Mouse hook for recording clicks
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int LLMHF_INJECTED = 0x0001;
        private const uint VK_XBUTTON1 = 0x05;
        private const uint VK_XBUTTON2 = 0x06;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_ALT = 0x12;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private static bool IsMouseButtonKey(uint vk)
        {
            return vk == 0x01 || vk == 0x02 || vk == 0x04 || vk == VK_XBUTTON1 || vk == VK_XBUTTON2;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc? _mouseProc;
        private IntPtr _mouseHookId = IntPtr.Zero;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int INPUT_MOUSE = 0;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;

        public MainWindow()
        {
            InitializeComponent();
            timer = new DispatcherTimer();
            timer.Tick += OnTimerTick;
            countdownTimer = new DispatcherTimer();
            countdownTimer.Interval = TimeSpan.FromMilliseconds(100);
            countdownTimer.Tick += OnCountdownTimerTick;
            movementTimer = new DispatcherTimer();
            movementTimer.Tick += OnMovementTimerTick;
            
            // Initialize sequence runner
            sequenceRunner = new SequenceRunner();
            sequenceRunner.ProgressChanged += OnSequenceProgressChanged;
            sequenceRunner.SequenceCompleted += OnSequenceCompleted;
            sequenceRunner.StepExecuted += OnSequenceStepExecuted;
            sequenceRunner.CountdownTick += OnSequenceCountdownTick;
            sequenceRunner.MovementStarted += OnSequenceMovementStarted;
            
            // Initialize global mouse trail window
            globalMouseTrailWindow = new GlobalMouseTrailWindow();
            globalMouseTrailWindow.Show();
            
            // Initialize mouse recording service
            mouseRecordingService = new MouseRecordingService();
            mouseRecordingService.RecordingStarted += OnRecordingStarted;
            mouseRecordingService.RecordingStopped += OnRecordingStopped;
            mouseRecordingService.PlaybackStarted += OnPlaybackStarted;
            mouseRecordingService.PlaybackStopped += OnPlaybackStopped;
            mouseRecordingService.PlaybackProgressChanged += OnPlaybackProgressChanged;
            
            // Setup mouse hook for recording clicks
            SetupMouseHook();
            
            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;
            
            // Add mouse movement tracking for trails
            MouseMove += OnMouseMove;

            try
            {
                startHotkeyModifiers = uint.TryParse(ConfigurationManager.AppSettings["StartHotkeyModifiers"], out uint startMod) ? startMod : MOD_CONTROL | MOD_SHIFT;
                startHotkeyKey = uint.TryParse(ConfigurationManager.AppSettings["StartHotkeyKey"], out uint startKey) ? startKey : VK_S;
                stopHotkeyModifiers = uint.TryParse(ConfigurationManager.AppSettings["StopHotkeyModifiers"], out uint stopMod) ? stopMod : MOD_CONTROL | MOD_SHIFT;
                stopHotkeyKey = uint.TryParse(ConfigurationManager.AppSettings["StopHotkeyKey"], out uint stopKey) ? stopKey : VK_P;
                recordHotkeyModifiers = uint.TryParse(ConfigurationManager.AppSettings["RecordHotkeyModifiers"], out uint recordMod) ? recordMod : 0; // No modifiers for F8
                recordHotkeyKey = uint.TryParse(ConfigurationManager.AppSettings["RecordHotkeyKey"], out uint recordKey) ? recordKey : VK_F8;
                playHotkeyModifiers = uint.TryParse(ConfigurationManager.AppSettings["PlayHotkeyModifiers"], out uint playMod) ? playMod : 0; // No modifiers for F9
                playHotkeyKey = uint.TryParse(ConfigurationManager.AppSettings["PlayHotkeyKey"], out uint playKey) ? playKey : VK_F9;
                double x = double.TryParse(ConfigurationManager.AppSettings["ClickAreaX"], out double cx) ? cx : 0;
                double y = double.TryParse(ConfigurationManager.AppSettings["ClickAreaY"], out double cy) ? cy : 0;
                double width = double.TryParse(ConfigurationManager.AppSettings["ClickAreaWidth"], out double cw) ? cw : 100;
                double height = double.TryParse(ConfigurationManager.AppSettings["ClickAreaHeight"], out double ch) ? ch : 100;
                clickArea = new Rect(x, y, width, height);
                currentClickType = Enum.TryParse(ConfigurationManager.AppSettings["ClickType"], out ClickType clickType) ? clickType : ClickType.LeftClick;
                
                // Load color settings
                if (System.Windows.Media.ColorConverter.ConvertFromString(ConfigurationManager.AppSettings["TargetColor"]) is System.Windows.Media.Color color)
                {
                    targetColor = color;
                }
                colorTolerance = int.TryParse(ConfigurationManager.AppSettings["ColorTolerance"], out int tolerance) ? tolerance : 30;
                
                // Load color search area settings
                double searchX = double.TryParse(ConfigurationManager.AppSettings["ColorSearchAreaX"], out double csx) ? csx : 0;
                double searchY = double.TryParse(ConfigurationManager.AppSettings["ColorSearchAreaY"], out double csy) ? csy : 0;
                double searchWidth = double.TryParse(ConfigurationManager.AppSettings["ColorSearchAreaWidth"], out double csw) ? csw : 0;
                double searchHeight = double.TryParse(ConfigurationManager.AppSettings["ColorSearchAreaHeight"], out double csh) ? csh : 0;
                colorSearchArea = new Rect(searchX, searchY, searchWidth, searchHeight);
            }
            catch
            {
                startHotkeyModifiers = MOD_CONTROL | MOD_SHIFT;
                startHotkeyKey = VK_S;
                stopHotkeyModifiers = MOD_CONTROL | MOD_SHIFT;
                stopHotkeyKey = VK_P;
                recordHotkeyModifiers = 0; // No modifiers for F8
                recordHotkeyKey = VK_F8;
                playHotkeyModifiers = 0; // No modifiers for F9
                playHotkeyKey = VK_F9;
                clickArea = new Rect(0, 0, 100, 100);
                colorSearchArea = new Rect(0, 0, 0, 0); // Empty search area
                currentClickType = ClickType.LeftClick;
                targetColor = System.Windows.Media.Colors.Red;
                colorTolerance = 30;
            }

            // UI initialization will be done in OnWindowLoaded after XAML is loaded
            // Click area overlay is only shown when Target Mode is Click Area (see UpdateTargetModeVisibilityAndState)
            
            // Global mouse trail window is already initialized
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Initialize UI elements after XAML is fully loaded
            ClickTypeComboBox.SelectedIndex = (int)currentClickType;
            
            // Initialize TargetModeComboBox (default to Mouse position - first in list)
            TargetModeComboBox.SelectedIndex = 0;
            UpdateTargetModeVisibilityAndState();
            
            // Load movement speed settings
            try
            {
                int movementSpeedIndex = int.TryParse(ConfigurationManager.AppSettings["MovementSpeed"], out int msIndex) ? msIndex : 1; // Default to Medium
                MovementSpeedComboBox.SelectedIndex = movementSpeedIndex;
                
                string customSpeed = ConfigurationManager.AppSettings["CustomMovementSpeed"] ?? "150";
                CustomSpeedInput.Text = customSpeed;
                
                // Enable/disable custom speed input based on selection
                bool isCustom = movementSpeedIndex == 3; // Custom is index 3
                CustomSpeedInput.IsEnabled = isCustom;
            }
            catch
            {
                MovementSpeedComboBox.SelectedIndex = 1; // Default to Medium
                CustomSpeedInput.Text = "150";
                CustomSpeedInput.IsEnabled = false;
            }
            
            // Initialize PlaybackSpeedComboBox (default to 1.0x)
            PlaybackSpeedComboBox.SelectedIndex = 1;
            
            // Register hotkeys and setup hooks
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            bool sameKbHotkey = !IsMouseButtonKey(startHotkeyKey) && !IsMouseButtonKey(stopHotkeyKey) &&
                IsSameHotkey(startHotkeyModifiers, startHotkeyKey, stopHotkeyModifiers, stopHotkeyKey);
            if (!IsMouseButtonKey(startHotkeyKey))
                RegisterHotKey(hWnd, HOTKEY_ID_START, startHotkeyModifiers, startHotkeyKey);
            if (!IsMouseButtonKey(stopHotkeyKey) && !sameKbHotkey)
                RegisterHotKey(hWnd, HOTKEY_ID_STOP, stopHotkeyModifiers, stopHotkeyKey);
            RegisterHotKey(hWnd, HOTKEY_ID_RECORD, recordHotkeyModifiers, recordHotkeyKey);
            RegisterHotKey(hWnd, HOTKEY_ID_PLAY, playHotkeyModifiers, playHotkeyKey);
            HwndSource source = HwndSource.FromHwnd(hWnd);
            source.AddHook(WndProc);
            
            // Update status labels
            StatusLabel.Content = "Status: Stopped";
            NextClickLabel.Content = "Next Click: -- ms";
            MovementSpeedLabel.Content = "Movement Speed: -- ms";
            StartHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(startHotkeyModifiers, startHotkeyKey)}";
            StopHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(stopHotkeyModifiers, stopHotkeyKey)}";
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            if (!IsMouseButtonKey(startHotkeyKey))
                UnregisterHotKey(hWnd, HOTKEY_ID_START);
            if (!IsMouseButtonKey(stopHotkeyKey))
                UnregisterHotKey(hWnd, HOTKEY_ID_STOP);
            UnregisterHotKey(hWnd, HOTKEY_ID_RECORD);
            UnregisterHotKey(hWnd, HOTKEY_ID_PLAY);
            overlayWindow?.Close();
            globalMouseTrailWindow?.Close();
            sequenceRunner?.StopSequence();
            
            // Unhook mouse hook
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
            }
        }

        private bool IsSameHotkey(uint mod1, uint key1, uint mod2, uint key2)
        {
            return mod1 == mod2 && key1 == key2;
        }

        /// <summary>True when either sequence mode or simple timer mode is actively running.</summary>
        private bool IsClickingOrSequenceRunning()
        {
            return sequenceRunner.IsRunning || timer.IsEnabled;
        }

        /// <summary>True if we should ignore this same-hotkey press (within debounce to avoid repeat = restart).</summary>
        private bool ShouldDebounceSameHotkey()
        {
            return (DateTime.UtcNow - _lastSameHotkeyToggleUtc).TotalMilliseconds < SAME_HOTKEY_DEBOUNCE_MS;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                bool sameHotkey = IsSameHotkey(startHotkeyModifiers, startHotkeyKey, stopHotkeyModifiers, stopHotkeyKey);

                if (id == HOTKEY_ID_START)
                {
                    if (sameHotkey)
                    {
                        if (ShouldDebounceSameHotkey())
                            return IntPtr.Zero;
                        _lastSameHotkeyToggleUtc = DateTime.UtcNow;
                        if (IsClickingOrSequenceRunning())
                            OnStopButtonClick(this, new RoutedEventArgs());
                        else
                            OnStartButtonClick(this, new RoutedEventArgs());
                    }
                    else
                        OnStartButtonClick(this, new RoutedEventArgs());
                }
                else if (id == HOTKEY_ID_STOP)
                {
                    if (sameHotkey)
                    {
                        if (ShouldDebounceSameHotkey())
                            return IntPtr.Zero;
                        _lastSameHotkeyToggleUtc = DateTime.UtcNow;
                        if (IsClickingOrSequenceRunning())
                            OnStopButtonClick(this, new RoutedEventArgs());
                    }
                    else if (!sameHotkey)
                        OnStopButtonClick(this, new RoutedEventArgs());
                }
                else if (id == HOTKEY_ID_RECORD)
                {
                    // Toggle recording - start if not recording, stop if recording
                    if (mouseRecordingService.IsRecording)
                    {
                        // Stop recording
                        mouseRecordingService.PauseRecording();
                        mouseRecordingService.StopRecording();
                    }
                    else
                    {
                        // Start recording
                        mouseRecordingService.StartRecording();
                    }
                }
                else if (id == HOTKEY_ID_PLAY)
                {
                    OnPlayButtonClick(this, new RoutedEventArgs());
                }
            }
            return IntPtr.Zero;
        }

        public void UpdateHotkeys(uint startModifiers, uint startKey, uint stopModifiers, uint stopKey)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            bool sameKbHotkey = !IsMouseButtonKey(startHotkeyKey) && !IsMouseButtonKey(stopHotkeyKey) &&
                IsSameHotkey(startHotkeyModifiers, startHotkeyKey, stopHotkeyModifiers, stopHotkeyKey);
            if (!IsMouseButtonKey(startHotkeyKey))
                UnregisterHotKey(hWnd, HOTKEY_ID_START);
            if (!IsMouseButtonKey(stopHotkeyKey) && !sameKbHotkey)
                UnregisterHotKey(hWnd, HOTKEY_ID_STOP);
            startHotkeyModifiers = startModifiers;
            startHotkeyKey = startKey;
            stopHotkeyModifiers = stopModifiers;
            stopHotkeyKey = stopKey;
            sameKbHotkey = !IsMouseButtonKey(startKey) && !IsMouseButtonKey(stopKey) &&
                IsSameHotkey(startModifiers, startKey, stopModifiers, stopKey);
            if (!IsMouseButtonKey(startKey))
                RegisterHotKey(hWnd, HOTKEY_ID_START, startModifiers, startKey);
            if (!IsMouseButtonKey(stopKey) && !sameKbHotkey)
                RegisterHotKey(hWnd, HOTKEY_ID_STOP, stopModifiers, stopKey);
            StartHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(startModifiers, startKey)}";
            StopHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(stopModifiers, stopKey)}";
        }

        private string GetHotkeyText(uint modifiers, uint key)
        {
            string keyPart = key switch
            {
                0x01 => "Mouse1",
                0x02 => "Mouse2",
                0x04 => "Mouse3",
                VK_XBUTTON1 => "Mouse4",
                VK_XBUTTON2 => "Mouse5",
                _ => KeyInterop.KeyFromVirtualKey((int)key).ToString()
            };
            StringBuilder text = new StringBuilder();
            if ((modifiers & MOD_CONTROL) != 0) text.Append("Ctrl+");
            if ((modifiers & MOD_SHIFT) != 0) text.Append("Shift+");
            if ((modifiers & MOD_ALT) != 0) text.Append("Alt+");
            text.Append(keyPart);
            return text.ToString();
        }

        private void OnClickTypeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ClickTypeComboBox?.SelectedIndex >= 0)
            {
                currentClickType = (ClickType)ClickTypeComboBox.SelectedIndex;
                SaveSettings();
            }
        }

        private void OnMovementSpeedChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (MovementSpeedComboBox?.SelectedIndex >= 0 && CustomSpeedInput != null)
            {
                // Enable/disable custom speed input based on selection
                bool isCustom = MovementSpeedComboBox.SelectedIndex == 3; // Custom is index 3
                CustomSpeedInput.IsEnabled = isCustom;
                
                SaveSettings();
            }
        }

        private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(this, startHotkeyModifiers, startHotkeyKey, stopHotkeyModifiers, stopHotkeyKey);
            settingsWindow.ShowDialog();
            
            // Reload mouse trail settings after settings change
            globalMouseTrailWindow?.RefreshSettings();
        }
        
        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            // Mouse tracking is now handled globally by GlobalMouseTrailWindow
        }

        private void OnSequenceButtonClick(object? sender, RoutedEventArgs e)
        {
            // Debug: Show what currentSequence contains
            string debugInfo = currentSequence == null ? "null" : $"'{currentSequence.Name}' with {currentSequence.Steps.Count} steps";
            System.Diagnostics.Debug.WriteLine($"Opening configurator with sequence: {debugInfo}");
            
            var sequenceWindow = new SequenceConfiguratorWindow(currentSequence);
            if (sequenceWindow.ShowDialog() == true)
            {
                currentSequence = sequenceWindow.CurrentSequence;
                isSequenceMode = true;
                
                // Update UI to show sequence mode
                StatusLabel.Content = $"Status: Sequence Mode - {currentSequence.Name}";
                StartButton.Content = "Start Sequence";
                StopButton.Content = "Stop Sequence";
            }
        }



        private void OnTargetModeChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTargetModeVisibilityAndState();
        }

        private void UpdateTargetModeVisibilityAndState()
        {
            if (SetColorButton == null || SetAreaButton == null) return;

            int index = TargetModeComboBox?.SelectedIndex ?? 0;
            // 0 = Mouse position, 1 = Click Area, 2 = Color Click
            if (index == 0) // Mouse position - hide Set Area and Set Color
            {
                SetColorButton.Visibility = Visibility.Collapsed;
                SetColorButton.IsEnabled = false;
                SetAreaButton.Visibility = Visibility.Collapsed;
                SetAreaButton.IsEnabled = false;
                HideClickAreaOverlay();
            }
            else if (index == 1) // Click Area
            {
                SetColorButton.Visibility = Visibility.Collapsed;
                SetColorButton.IsEnabled = false;
                SetAreaButton.Visibility = Visibility.Visible;
                SetAreaButton.IsEnabled = true;
                if (clickArea.Width > 0 && clickArea.Height > 0)
                    ShowClickAreaOverlay();
                else
                    HideClickAreaOverlay();
            }
            else // index == 2: Color Click
            {
                SetColorButton.Visibility = Visibility.Visible;
                SetColorButton.IsEnabled = true;
                SetAreaButton.Visibility = Visibility.Visible;
                SetAreaButton.IsEnabled = true;
                HideClickAreaOverlay();
            }
        }

        private void ShowClickAreaOverlay()
        {
            if (clickArea.Width <= 0 || clickArea.Height <= 0) return;
            if (overlayWindow == null)
            {
                overlayWindow = new OverlayWindow(clickArea);
                overlayWindow.Show();
            }
            else
            {
                overlayWindow.UpdateRectangle(clickArea);
                overlayWindow.Show();
            }
        }

        private void HideClickAreaOverlay()
        {
            if (overlayWindow != null)
            {
                overlayWindow.Close();
                overlayWindow = null!;
            }
        }

        private void OnSetAreaClick(object? sender, RoutedEventArgs e)
        {
            if (TargetModeComboBox.SelectedIndex == 1) // Click Area
                OnSetClickAreaClick(sender, e);
            else if (TargetModeComboBox.SelectedIndex == 2) // Color Click
                OnSetSearchAreaClick(sender, e);
        }

        private void OnSetColorClick(object sender, RoutedEventArgs e)
        {
            var colorPickerWindow = new ColorPickerWindow(GlobalMouseTrailWindow.CurrentMouseTrailService);
            
            // Set the current color and tolerance in the window
            colorPickerWindow.SelectedColor = targetColor;
            colorPickerWindow.ColorTolerance = colorTolerance;
            colorPickerWindow.UpdateColorValues();
            colorPickerWindow.UpdateColorPreview();
            colorPickerWindow.ToleranceSlider.Value = colorTolerance;
            
            if (colorPickerWindow.ShowDialog() == true)
            {
                targetColor = colorPickerWindow.SelectedColor;
                colorTolerance = colorPickerWindow.ColorTolerance;
                System.Diagnostics.Debug.WriteLine($"Color picker set: R={targetColor.R}, G={targetColor.G}, B={targetColor.B}, Tolerance={colorTolerance}");
            }
        }

        private void OnSetClickAreaClick(object? sender, RoutedEventArgs e)
        {
            var clickAreaWindow = new ClickAreaWindow(this);
            if (clickAreaWindow.ShowDialog() == true)
            {
                clickArea = clickAreaWindow.SelectedArea;
                if (TargetModeComboBox.SelectedIndex == 1) // Click Area mode - show overlay
                    ShowClickAreaOverlay();
                SaveSettings();
            }
        }

        private void OnSetSearchAreaClick(object? sender, RoutedEventArgs e)
        {
            var searchAreaWindow = new ClickAreaWindow(this);
            searchAreaWindow.Title = "Set Color Search Area";
            if (searchAreaWindow.ShowDialog() == true)
            {
                colorSearchArea = searchAreaWindow.SelectedArea;
                System.Diagnostics.Debug.WriteLine($"Search area set: {colorSearchArea}");
                
                // Show visual indicator of search area
                ShowSearchAreaIndicator(colorSearchArea);
                
                SaveSettings();
            }
        }

        private async void StartBackgroundColorDetection()
        {
            if (isColorDetectionRunning) return;
            
            isColorDetectionRunning = true;
            cachedColorTarget = null;
            
            // Run color detection in background
            await Task.Run(async () =>
            {
                while (isColorDetectionRunning)
                {
                    try
                    {
                        Rect searchArea;
                        if (colorSearchArea.Width > 0 && colorSearchArea.Height > 0)
                        {
                            searchArea = colorSearchArea;
                        }
                        else
                        {
                            // Fallback to screen area
                            searchArea = new Rect(0, 0, System.Windows.SystemParameters.PrimaryScreenWidth, System.Windows.SystemParameters.PrimaryScreenHeight);
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Background search using: R={targetColor.R}, G={targetColor.G}, B={targetColor.B}, Tolerance={colorTolerance}");
                        
                        // Debug the color detection for troubleshooting
                        ColorDetectionService.DebugColorDetection(targetColor, searchArea, colorTolerance);
                        
                        var matchingPoint = await ColorDetectionService.GetRandomMatchingPointAsync(targetColor, colorTolerance, searchArea);
                        
                        if (matchingPoint.HasValue)
                        {
                            // Update the cached target on the UI thread
                            await Dispatcher.InvokeAsync(() =>
                            {
                                cachedColorTarget = matchingPoint.Value;
                                StatusLabel.Content = $"Status: Running - Color target found at ({matchingPoint.Value.X}, {matchingPoint.Value.Y})";
                            });
                        }
                        else
                        {
                            // Update status when no color is found
                            await Dispatcher.InvokeAsync(() =>
                            {
                                StatusLabel.Content = "Status: Running - Searching for color...";
                            });
                        }
                        
                        // Wait a bit before next search
                        await Task.Delay(50); // Faster updates
                    }
                    catch (Exception)
                    {
                        // Ignore errors and continue
                        await Task.Delay(50);
                    }
                }
            });
        }

        private void StopBackgroundColorDetection()
        {
            isColorDetectionRunning = false;
            cachedColorTarget = null;
        }

        private void ShowClickIndicator(Point position)
        {
            // Click indicator removed to prevent interference with color detection
            System.Diagnostics.Debug.WriteLine($"Click at ({position.X}, {position.Y}) - indicator disabled");
        }

        private void TestColorAtMousePosition()
        {
            var color = ColorDetectionService.GetColorAtCurrentPosition();
            System.Diagnostics.Debug.WriteLine($"Color at current mouse position: R={color.R}, G={color.G}, B={color.B}");
            
            // Test color matching directly
            int rDiff = Math.Abs(color.R - targetColor.R);
            int gDiff = Math.Abs(color.G - targetColor.G);
            int bDiff = Math.Abs(color.B - targetColor.B);
            bool isMatch = rDiff <= colorTolerance && gDiff <= colorTolerance && bDiff <= colorTolerance;
            
            System.Diagnostics.Debug.WriteLine($"Color match test: Target({targetColor.R},{targetColor.G},{targetColor.B}) vs Mouse({color.R},{color.G},{color.B}) - Diffs({rDiff},{gDiff},{bDiff}) - Match: {isMatch}");
            
            // Also test if the search area is valid
            if (colorSearchArea.Width > 0 && colorSearchArea.Height > 0)
            {
                var testColor = ColorDetectionService.GetPixelAt((int)colorSearchArea.Left, (int)colorSearchArea.Top);
                System.Diagnostics.Debug.WriteLine($"Color at search area top-left ({colorSearchArea.Left}, {colorSearchArea.Top}): R={testColor.R}, G={testColor.G}, B={testColor.B}");
                
                // Test a few more points in the search area
                var centerColor = ColorDetectionService.GetPixelAt((int)(colorSearchArea.Left + colorSearchArea.Width/2), (int)(colorSearchArea.Top + colorSearchArea.Height/2));
                var bottomRightColor = ColorDetectionService.GetPixelAt((int)(colorSearchArea.Right - 10), (int)(colorSearchArea.Bottom - 10));
                
                System.Diagnostics.Debug.WriteLine($"Color at search area center: R={centerColor.R}, G={centerColor.G}, B={centerColor.B}");
                System.Diagnostics.Debug.WriteLine($"Color at search area bottom-right: R={bottomRightColor.R}, G={bottomRightColor.G}, B={bottomRightColor.B}");
                
                // Test color matching for these points too
                TestColorMatch("top-left", testColor);
                TestColorMatch("center", centerColor);
                TestColorMatch("bottom-right", bottomRightColor);
                
                MessageBox.Show($"Mouse position: R={color.R}, G={color.G}, B={color.B}\nSearch area top-left: R={testColor.R}, G={testColor.G}, B={testColor.B}\nSearch area center: R={centerColor.R}, G={centerColor.G}, B={centerColor.B}\nSearch area bottom-right: R={bottomRightColor.R}, G={bottomRightColor.G}, B={bottomRightColor.B}", "Color Test");
            }
            else
            {
                MessageBox.Show($"Color at mouse position: R={color.R}, G={color.G}, B={color.B}", "Color Test");
            }
        }

        private void TestColorMatch(string location, System.Windows.Media.Color color)
        {
            int rDiff = Math.Abs(color.R - targetColor.R);
            int gDiff = Math.Abs(color.G - targetColor.G);
            int bDiff = Math.Abs(color.B - targetColor.B);
            bool isMatch = rDiff <= colorTolerance && gDiff <= colorTolerance && bDiff <= colorTolerance;
            
            System.Diagnostics.Debug.WriteLine($"Color match test ({location}): Target({targetColor.R},{targetColor.G},{targetColor.B}) vs Found({color.R},{color.G},{color.B}) - Diffs({rDiff},{gDiff},{bDiff}) - Match: {isMatch}");
        }



        private void ShowSearchAreaIndicator(Rect searchArea)
        {
            // Create a temporary window to show the search area
            var indicator = new System.Windows.Window
            {
                Width = searchArea.Width,
                Height = searchArea.Height,
                Left = searchArea.Left,
                Top = searchArea.Top,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Yellow,
                Opacity = 0.3,
                Topmost = true,
                ShowInTaskbar = false
            };
            
            indicator.Show();
            
            // Hide after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                indicator.Close();
                timer.Stop();
            };
            timer.Start();
        }

        private void SaveSettings()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings.Remove("ClickAreaX");
                config.AppSettings.Settings.Remove("ClickAreaY");
                config.AppSettings.Settings.Remove("ClickAreaWidth");
                config.AppSettings.Settings.Remove("ClickAreaHeight");
                config.AppSettings.Settings.Remove("ClickType");
                config.AppSettings.Settings.Remove("MovementSpeed");
                config.AppSettings.Settings.Remove("CustomMovementSpeed");
                config.AppSettings.Settings.Remove("TargetColor");
                config.AppSettings.Settings.Remove("ColorTolerance");
                config.AppSettings.Settings.Remove("ColorSearchAreaX");
                config.AppSettings.Settings.Remove("ColorSearchAreaY");
                config.AppSettings.Settings.Remove("ColorSearchAreaWidth");
                config.AppSettings.Settings.Remove("ColorSearchAreaHeight");
                config.AppSettings.Settings.Add("ClickAreaX", clickArea.X.ToString());
                config.AppSettings.Settings.Add("ClickAreaY", clickArea.Y.ToString());
                config.AppSettings.Settings.Add("ClickAreaWidth", clickArea.Width.ToString());
                config.AppSettings.Settings.Add("ClickAreaHeight", clickArea.Height.ToString());
                config.AppSettings.Settings.Add("ClickType", currentClickType.ToString());
                config.AppSettings.Settings.Add("TargetColor", targetColor.ToString());
                config.AppSettings.Settings.Add("ColorTolerance", colorTolerance.ToString());
                config.AppSettings.Settings.Add("ColorSearchAreaX", colorSearchArea.X.ToString());
                config.AppSettings.Settings.Add("ColorSearchAreaY", colorSearchArea.Y.ToString());
                config.AppSettings.Settings.Add("ColorSearchAreaWidth", colorSearchArea.Width.ToString());
                config.AppSettings.Settings.Add("ColorSearchAreaHeight", colorSearchArea.Height.ToString());
                
                // Add null checks for movement speed settings
                if (MovementSpeedComboBox?.SelectedIndex >= 0)
                {
                    config.AppSettings.Settings.Add("MovementSpeed", MovementSpeedComboBox.SelectedIndex.ToString());
                }
                
                if (CustomSpeedInput?.Text != null)
                {
                    config.AppSettings.Settings.Add("CustomMovementSpeed", CustomSpeedInput.Text);
                }
                
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error");
            }
        }

        private void OnStartButtonClick(object? sender, RoutedEventArgs e)
        {
            if (isSequenceMode)
            {
                if (currentSequence == null || currentSequence.Steps.Count == 0)
                {
                    MessageBox.Show("Please configure a sequence with at least one step.", "No Sequence");
                    return;
                }
                
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                sequenceRunner.StartSequence(currentSequence);
            }
            else
            {
                if (int.TryParse(IntervalInput.Text, out int interval) && interval >= 100)
                {
                    if (TargetModeComboBox.SelectedIndex == 1 && (clickArea.Width <= 0 || clickArea.Height <= 0)) // Click Area mode
                    {
                        MessageBox.Show("Please set a valid click area.", "Invalid Click Area");
                        return;
                    }
                    
                    // Start background color detection if in color click mode
                    if (TargetModeComboBox.SelectedIndex == 2) // Color Click
                    {
                        StartBackgroundColorDetection();
                    }
                    
                    StartButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    StatusLabel.Content = "Status: Running";
                    nextClickTime = DateTime.Now.AddMilliseconds(interval);
                    timer.Interval = TimeSpan.FromMilliseconds(interval);
                    timer.Start();
                    countdownTimer.Start();
                    if (overlayWindow != null)
                    {
                        overlayWindow.SetRunning(true);
                    }
                }
                else
                {
                    MessageBox.Show("Please enter a valid interval (≥100 ms).", "Invalid Input");
                }
            }
        }

        private void OnStopButtonClick(object? sender, EventArgs e)
        {
            if (isSequenceMode)
            {
                sequenceRunner.StopSequence();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Status: Sequence Stopped";
                NextClickLabel.Content = "Next Click: -- ms";
                MovementSpeedLabel.Content = "Movement Speed: -- ms";
            }
            else
            {
                timer.Stop();
                countdownTimer.Stop();
                movementTimer.Stop();
                StopBackgroundColorDetection(); // Stop background color detection
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Status: Stopped";
                NextClickLabel.Content = "Next Click: -- ms";
                MovementSpeedLabel.Content = "Movement Speed: -- ms";
                if (overlayWindow != null)
                {
                    overlayWindow.SetRunning(false);
                }
            }
            
            // Focus the window when stopping
            this.Activate();
        }

        private void OnTimerTick(object? sender, EventArgs e)
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
                movementTimer.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Status: Stopped";
                NextClickLabel.Content = "Next Click: -- ms";
                MovementSpeedLabel.Content = "Movement Speed: -- ms";
                if (overlayWindow != null)
                {
                    overlayWindow.SetRunning(false);
                }
                MessageBox.Show("Invalid input. Please enter numeric values.", "Error");
            }
        }

        private void OnCountdownTimerTick(object? sender, EventArgs e)
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

        private void OnMovementTimerTick(object? sender, EventArgs e)
        {
            if (currentStep >= movementSteps)
            {
                movementTimer.Stop();
                PerformMouseClick();
                return;
            }

            // Calculate t from 0.0 to 1.0 properly
            double t = (double)currentStep / (movementSteps - 1);
            
            // Quadratic Bézier: B(t) = (1-t)^2 * P0 + 2*(1-t)*t * P1 + t^2 * P2
            double oneMinusT = 1 - t;
            double x = oneMinusT * oneMinusT * currentPosition.X + 
                       2 * oneMinusT * t * bezierControlPoint.X + 
                       t * t * targetPosition.X;
            double y = oneMinusT * oneMinusT * currentPosition.Y + 
                       2 * oneMinusT * t * bezierControlPoint.Y + 
                       t * t * targetPosition.Y;

            bool result = SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            
            if (!result)
            {
                // Only log actual failures, not spam every movement
                Console.WriteLine($"SetCursorPos failed at t={t:F2}: Error={GetLastError()}");
            }
            // Trail points are now handled globally by GlobalMouseTrailWindow

            currentStep++;
            
            // Update timer interval for next step if we have more steps
            if (currentStep < movementSteps && _speedIntervals.Count > currentStep)
            {
                movementTimer.Interval = TimeSpan.FromMilliseconds(_speedIntervals[currentStep]);
            }
        }

        private List<double> GenerateVariableSpeedIntervals(int totalDuration, int steps)
        {
            var intervals = new List<double>();
            var random = Random.Shared;
            
            // Generate base intervals with some variation
            double baseInterval = (double)totalDuration / steps;
            
            for (int i = 0; i < steps; i++)
            {
                // Add random variation to each interval (±30% by default)
                double variation = 1.0 + (random.NextDouble() - 0.5) * SPEED_VARIATION_FACTOR * 2;
                
                // Slight acceleration/deceleration pattern (start slower, accelerate, then slow down)
                double patternMultiplier = 1.0;
                if (i < steps * 0.3) // First 30% - slower start
                    patternMultiplier = 1.2 + (i / (steps * 0.3)) * 0.3;
                else if (i > steps * 0.7) // Last 30% - slower finish
                    patternMultiplier = 1.0 - ((i - steps * 0.7) / (steps * 0.3)) * 0.4;
                else // Middle 40% - faster
                    patternMultiplier = 0.8 + random.NextDouble() * 0.2;
                
                double interval = baseInterval * variation * patternMultiplier;
                
                // Ensure minimum interval for smooth movement
                interval = Math.Max(5.0, interval);
                
                intervals.Add(interval);
            }
            
            // Normalize to maintain total duration
            double totalGenerated = intervals.Sum();
            double normalizationFactor = totalDuration / totalGenerated;
            
            for (int i = 0; i < intervals.Count; i++)
            {
                intervals[i] *= normalizationFactor;
            }
            
            return intervals;
        }

        private void SimulateMouseClick()
        {
            GetCursorPos(out POINT currentPos);
            currentPosition = new Point(currentPos.X, currentPos.Y);
            
            // Determine target position based on target mode
            if (TargetModeComboBox.SelectedIndex == 0) // Mouse position - click at current cursor position, no movement at all
            {
                StatusLabel.Content = $"Status: Running - Clicking at current position ({currentPosition.X}, {currentPosition.Y})";
                PerformMouseClick();
                return; // Do not start movement timer - no Bézier, no SetCursorPos, no jitter
            }
            else if (TargetModeComboBox.SelectedIndex == 2) // Color Click
            {
                // Use cached color target if available, otherwise fallback to random position
                if (cachedColorTarget.HasValue)
                {
                    targetPosition = cachedColorTarget.Value;
                    cachedColorTarget = null; // Use it once
                    StatusLabel.Content = $"Status: Running - Clicking color target at ({targetPosition.X}, {targetPosition.Y})";
                    
                    // Show a temporary visual indicator where we're clicking
                    ShowClickIndicator(targetPosition);
                }
                else
                {
                    // Fallback to random position in search area
                    Rect searchArea;
                    if (colorSearchArea.Width > 0 && colorSearchArea.Height > 0)
                    {
                        searchArea = colorSearchArea;
                    }
                    else
                    {
                        // Fallback to searching around current mouse position
                        GetCursorPos(out POINT currentMousePos);
                        double searchRadius = 200;
                        
                        searchArea = new Rect(
                            Math.Max(0, currentMousePos.X - searchRadius),
                            Math.Max(0, currentMousePos.Y - searchRadius),
                            Math.Min(searchRadius * 2, System.Windows.SystemParameters.PrimaryScreenWidth - Math.Max(0, currentMousePos.X - searchRadius)),
                            Math.Min(searchRadius * 2, System.Windows.SystemParameters.PrimaryScreenHeight - Math.Max(0, currentMousePos.Y - searchRadius))
                        );
                    }
                    
                    var random = new Random();
                    targetPosition = new Point(
                        searchArea.X + random.NextDouble() * searchArea.Width,
                        searchArea.Y + random.NextDouble() * searchArea.Height
                    );
                    StatusLabel.Content = $"Status: Running - No color found, clicking random at ({targetPosition.X}, {targetPosition.Y})";
                }
            }
            else if (TargetModeComboBox.SelectedIndex == 1 && clickArea.Width > 0 && clickArea.Height > 0)
            {
                // Click Area: random position within click area
                targetPosition = new Point(
                    clickArea.X + random.NextDouble() * clickArea.Width,
                    clickArea.Y + random.NextDouble() * clickArea.Height
                );
            }
            else
            {
                // No valid target - pause
                timer.Stop();
                countdownTimer.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusLabel.Content = "Status: Paused - No valid target area";
                return;
            }

            // Simpler, more natural control point generation
            // Take the midpoint and add some controlled randomness
            double midX = (currentPosition.X + targetPosition.X) / 2;
            double midY = (currentPosition.Y + targetPosition.Y) / 2;
                
            // Add random offset - max 30% of the distance or 40px, whichever is smaller
            double distance = Math.Sqrt(Math.Pow(targetPosition.X - currentPosition.X, 2) + 
                                      Math.Pow(targetPosition.Y - currentPosition.Y, 2));
            double maxOffset = Math.Min(distance * 0.3, 40);
            
            double offsetX = (random.NextDouble() - 0.5) * maxOffset * 2;
            double offsetY = (random.NextDouble() - 0.5) * maxOffset * 2;
            
            // Keep control point within reasonable bounds
            bezierControlPoint = new Point(
                Math.Clamp(midX + offsetX, 
                          Math.Min(currentPosition.X, targetPosition.X) - 20,
                          Math.Max(currentPosition.X, targetPosition.X) + 20),
                Math.Clamp(midY + offsetY,
                          Math.Min(currentPosition.Y, targetPosition.Y) - 20, 
                          Math.Max(currentPosition.Y, targetPosition.Y) + 20)
            );

            currentStep = 0;
            movementSteps = MOVEMENT_STEPS;
            
            // Calculate base movement duration with distance-based adjustment
            double movementDistance = Math.Sqrt(Math.Pow(targetPosition.X - currentPosition.X, 2) + 
                                              Math.Pow(targetPosition.Y - currentPosition.Y, 2));
            
            // Get movement duration from UI settings
            int baseDuration;
            if (MovementSpeedComboBox?.SelectedIndex >= 0)
            {
                switch (MovementSpeedComboBox.SelectedIndex)
                {
                    case 0: // Fast
                        baseDuration = random.Next(80, 150);
                        break;
                    case 1: // Medium
                        baseDuration = random.Next(150, 250);
                        break;
                    case 2: // Slow
                        baseDuration = random.Next(250, 400);
                        break;
                    case 3: // Custom
                        if (CustomSpeedInput?.Text != null && int.TryParse(CustomSpeedInput.Text, out int customSpeed))
                            baseDuration = customSpeed;
                        else
                            baseDuration = 150;
                        break;
                    default:
                        baseDuration = random.Next(150, 250); // Default to Medium
                        break;
                }
            }
            else
            {
                // Default to Medium if UI not ready
                baseDuration = random.Next(150, 250);
            }
            
            // Adjust duration based on distance (longer distance = slightly faster movement)
            double distanceAdjustment = 1.0 + (movementDistance / 1000.0) * DISTANCE_SPEED_FACTOR;
            movementDuration = (int)(baseDuration / distanceAdjustment);
            
            // Ensure duration stays within reasonable bounds
            movementDuration = Math.Max(MIN_MOVEMENT_DURATION_MS, Math.Min(MAX_MOVEMENT_DURATION_MS, movementDuration));
            
            // Create variable speed intervals for more human-like movement
            var speedIntervals = GenerateVariableSpeedIntervals(movementDuration, movementSteps);
            
            // Store the intervals for use in movement timer
            _speedIntervals = speedIntervals;
            
            // Start with first interval
            movementTimer.Interval = TimeSpan.FromMilliseconds(speedIntervals[0]);
            
            MovementSpeedLabel.Content = $"Movement Speed: {movementDuration} ms";
            movementTimer.Start();
        }

        private void PerformMouseClick()
        {
            switch (currentClickType)
            {
                case ClickType.LeftClick:
                    PerformSingleClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                    break;
                case ClickType.RightClick:
                    PerformSingleClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                    break;
                case ClickType.MiddleClick:
                    PerformSingleClick(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                    break;
                case ClickType.DoubleClick:
                    PerformSingleClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                    System.Threading.Thread.Sleep(50); // Short delay between clicks
                    PerformSingleClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                    break;
            }
        }

        private void PerformSingleClick(uint downFlag, uint upFlag)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = downFlag;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = upFlag;
            uint result = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            if (result != 2)
            {
                Console.WriteLine($"SendInput failed: Expected 2, got {result}");
            }
        }

        private void OnPreviewTextInput(object? sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = "0";
            }
        }

        // Sequence event handlers
        private void OnSequenceProgressChanged(object? sender, SequenceProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.TotalLoops > 0)
                {
                    StatusLabel.Content = $"Status: Sequence Running - Loop {e.CurrentLoop + 1}/{e.TotalLoops}";
                }
                else if (e.TotalLoops == -1)
                {
                    StatusLabel.Content = $"Status: Sequence Running - Loop {e.CurrentLoop + 1} (infinite)";
                }
                else
                {
                    StatusLabel.Content = "Status: Sequence Running";
                }
                
                var timeUntilNext = e.NextActionTime - DateTime.Now;
                if (timeUntilNext.TotalMilliseconds > 0)
                {
                    NextClickLabel.Content = $"Next Action: {timeUntilNext.TotalMilliseconds:F0} ms";
                }
            });
        }

        private void OnSequenceCompleted(object? sender, SequenceCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Content = $"Status: Sequence Completed - {e.TotalLoops} loops, {e.TotalSteps} steps";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StartButton.Content = "Start Sequence"; // Keep sequence mode UI
                StopButton.Content = "Stop Sequence";
                NextClickLabel.Content = "Next Click: -- ms";
                MovementSpeedLabel.Content = "Movement Speed: -- ms";
                // DON'T reset sequence mode or clear currentSequence - keep it for reuse
                // isSequenceMode = false;
                // currentSequence = null;
            });
        }

        private void OnSequenceStepExecuted(object? sender, SequenceStepEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                NextClickLabel.Content = $"Last Action: {e.Step.Description} ({e.Step.ClickType}) - {e.ActualDelay}ms";
            });
        }
        
        private void OnSequenceCountdownTick(object? sender, SequenceCountdownEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.MillisecondsRemaining > 0)
                {
                    NextClickLabel.Content = $"Next Action: {Math.Ceiling(e.MillisecondsRemaining)} ms";
                }
                else
                {
                    NextClickLabel.Content = "Next Action: 0 ms";
                }
            });
        }
        
        private void OnSequenceMovementStarted(object? sender, MovementStartedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                MovementSpeedLabel.Content = $"Movement Speed: {e.MovementDurationMs} ms";
            });
        }
        
        // Recording event handlers
        private void OnRecordingStarted(object? sender, EventArgs e)
        {
            Console.WriteLine("OnRecordingStarted event fired");
            Dispatcher.Invoke(() =>
            {
                RecordButton.Content = "Stop Recording";
                RecordButton.IsEnabled = true;
                PlayButton.IsEnabled = false;
                RecordingStatusLabel.Content = "Recording...";
                RecordingInfoLabel.Content = "Recording in progress...";
                Console.WriteLine("UI updated for recording started");
            });
        }
        
        private void OnRecordingStopped(object? sender, EventArgs e)
        {
            Console.WriteLine("OnRecordingStopped event fired");
            Dispatcher.Invoke(() =>
            {
                RecordButton.Content = "Start Recording";
                RecordButton.IsEnabled = true;
                PlayButton.IsEnabled = mouseRecordingService.CurrentRecording != null;
                RecordingStatusLabel.Content = "Ready to Record";
                currentRecording = mouseRecordingService.CurrentRecording;
                
                Console.WriteLine($"Current recording: {currentRecording?.Actions.Count ?? 0} actions");
                
                if (currentRecording != null && currentRecording.Actions.Count > 0)
                {
                    var duration = TimeSpan.FromMilliseconds(currentRecording.TotalDuration);
                    RecordingInfoLabel.Content = $"Recording: {currentRecording.Actions.Count} actions, {duration.TotalSeconds:F1}s";
                }
                else
                {
                    RecordingInfoLabel.Content = "No recording loaded";
                }
                Console.WriteLine("UI updated for recording stopped");
            });
        }
        
        private void OnPlaybackStarted(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                PlayButton.Content = "Stop Playback";
                PlayButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x4A, 0x1E)); // Dark green
                RecordButton.IsEnabled = false;
                RecordingStatusLabel.Content = "Playing...";
            });
        }
        
        private void OnPlaybackStopped(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                PlayButton.Content = "Play Recording";
                PlayButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33)); // Default dark gray
                RecordButton.IsEnabled = true;
                RecordingStatusLabel.Content = "Ready to Record";
            });
        }
        
        private void OnPlaybackProgressChanged(object? sender, int currentActionIndex)
        {
            if (currentRecording != null)
            {
                var progress = (double)currentActionIndex / currentRecording.Actions.Count * 100;
                RecordingStatusLabel.Content = $"Playing... {progress:F0}%";
            }
        }
        
        // Recording button event handler - toggles between start and stop
        private void OnRecordButtonClick(object sender, RoutedEventArgs e)
        {
            if (mouseRecordingService.IsRecording)
            {
                Console.WriteLine("Stop Recording button clicked");
                // Pause recording before stopping to avoid recording the stop button click
                mouseRecordingService.PauseRecording();
                mouseRecordingService.StopRecording();
            }
            else
            {
                Console.WriteLine("Start Recording button clicked");
                
                // Re-setup mouse hook to ensure it's working properly for new recording
                ReSetupMouseHook();
                
                mouseRecordingService.StartRecording();
            }
        }
        
        private void OnPlayButtonClick(object sender, RoutedEventArgs e)
        {
            if (mouseRecordingService.IsPlaying)
            {
                mouseRecordingService.StopPlayback();
            }
            else if (currentRecording != null)
            {
                mouseRecordingService.StartPlayback(currentRecording, currentPlaybackSpeed);
            }
        }
        
        // Playback speed change handler
        private void OnPlaybackSpeedChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaybackSpeedComboBox.SelectedIndex >= 0)
            {
                switch (PlaybackSpeedComboBox.SelectedIndex)
                {
                    case 0: currentPlaybackSpeed = 0.5; break;
                    case 1: currentPlaybackSpeed = 1.0; break;
                    case 2: currentPlaybackSpeed = 1.5; break;
                    case 3: currentPlaybackSpeed = 2.0; break;
                }
            }
        }
        

        

        
        // Mouse hook setup and handling
        private void SetupMouseHook()
        {
            Console.WriteLine("Setting up mouse hook...");
            _mouseProc = MouseHookProc;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc!, GetModuleHandle(string.Empty)!, 0);
            
            if (_mouseHookId == IntPtr.Zero)
            {
                Console.WriteLine("ERROR: Failed to set up mouse hook!");
            }
            else
            {
                Console.WriteLine($"Mouse hook set up successfully. Hook ID: {_mouseHookId}");
            }
        }
        
        private void ReSetupMouseHook()
        {
            Console.WriteLine("Re-setting up mouse hook...");
            
            // Unhook existing hook if any
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
                Console.WriteLine("Existing mouse hook unhooked");
            }
            
            // Set up new hook
            SetupMouseHook();
        }
        
        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int wmMessage = wParam.ToInt32();
                
                // Debug: Log all mouse messages to see what's being received
                // Console.WriteLine($"MOUSE HOOK: Message={wmMessage:X4} (0x{wmMessage:X4}), Recording={mouseRecordingService.IsRecording}");
                
                // Get current mouse position for burst effect and recording
                Point mousePosition = new Point();
                if (GetCursorPos(out POINT point))
                {
                    mousePosition = new Point(point.X, point.Y);
                }
                
                // Handle mouse button events for recording
                if (wmMessage == WM_LBUTTONDOWN && mouseRecordingService.IsRecording)
                {
                                            // Console.WriteLine($"MOUSE HOOK: LEFT BUTTON DOWN detected at {mousePosition}");
                    mouseRecordingService.RecordButtonDown(RecordedActionType.LeftClick);
                }
                else if (wmMessage == WM_RBUTTONDOWN && mouseRecordingService.IsRecording)
                {
                                            // Console.WriteLine($"MOUSE HOOK: RIGHT BUTTON DOWN detected at {mousePosition}");
                    mouseRecordingService.RecordButtonDown(RecordedActionType.RightClick);
                }
                else if (wmMessage == WM_MBUTTONDOWN && mouseRecordingService.IsRecording)
                {
                                            // Console.WriteLine($"MOUSE HOOK: MIDDLE BUTTON DOWN detected at {mousePosition}");
                    mouseRecordingService.RecordButtonDown(RecordedActionType.MiddleClick);
                }
                else if (wmMessage == WM_LBUTTONUP && mouseRecordingService.IsRecording)
                {
                                            // Console.WriteLine($"MOUSE HOOK: LEFT BUTTON UP detected at {mousePosition}");
                    mouseRecordingService.RecordButtonUp(RecordedActionType.LeftClick);
                }
                else if (wmMessage == WM_RBUTTONUP && mouseRecordingService.IsRecording)
                {
                                            // Console.WriteLine($"MOUSE HOOK: RIGHT BUTTON UP detected at {mousePosition}");
                    mouseRecordingService.RecordButtonUp(RecordedActionType.RightClick);
                }
                else if (wmMessage == WM_MBUTTONUP && mouseRecordingService.IsRecording)
                {
                                            // Console.WriteLine($"MOUSE HOOK: MIDDLE BUTTON UP detected at {mousePosition}");
                    mouseRecordingService.RecordButtonUp(RecordedActionType.MiddleClick);
                }
                
                // Handle mouse movement for drag detection during recording
                if (wmMessage == WM_MOUSEMOVE && mouseRecordingService.IsRecording)
                {
                    // The MouseRecordingService will handle drag detection in its timer
                    // This ensures we capture all mouse movement during recording
                }

                // Mouse hotkey: Start/Stop via any mouse button (ignore injected playback)
                if (wmMessage == WM_LBUTTONDOWN || wmMessage == WM_RBUTTONDOWN || wmMessage == WM_MBUTTONDOWN || wmMessage == WM_XBUTTONDOWN)
                {
                    var msll = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    uint buttonVk = wmMessage switch
                    {
                        WM_LBUTTONDOWN => 0x01u,
                        WM_RBUTTONDOWN => 0x02u,
                        WM_MBUTTONDOWN => 0x04u,
                        WM_XBUTTONDOWN => ((msll.mouseData >> 16) & 0xFFFF) == 1 ? VK_XBUTTON1 : (((msll.mouseData >> 16) & 0xFFFF) == 2 ? VK_XBUTTON2 : 0u),
                        _ => 0u
                    };
                    if (buttonVk != 0 && (msll.dwFlags & LLMHF_INJECTED) == 0 && !mouseRecordingService.IsRecording)
                        {
                            uint mods = 0;
                            if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) mods |= MOD_CONTROL;
                            if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) mods |= MOD_SHIFT;
                            if ((GetAsyncKeyState(VK_ALT) & 0x8000) != 0) mods |= MOD_ALT;

                            bool matchStart = IsMouseButtonKey(startHotkeyKey) && startHotkeyKey == buttonVk && startHotkeyModifiers == mods;
                            bool matchStop = IsMouseButtonKey(stopHotkeyKey) && stopHotkeyKey == buttonVk && stopHotkeyModifiers == mods;
                            if (matchStart && matchStop)
                            {
                                // Same hotkey: toggle (debounce so repeat doesn't stop-then-start)
                                Dispatcher.Invoke(() =>
                                {
                                    if (ShouldDebounceSameHotkey())
                                        return;
                                    _lastSameHotkeyToggleUtc = DateTime.UtcNow;
                                    if (IsClickingOrSequenceRunning())
                                        OnStopButtonClick(this, new RoutedEventArgs());
                                    else
                                        OnStartButtonClick(this, new RoutedEventArgs());
                                });
                            }
                            else if (matchStart)
                            {
                                Dispatcher.Invoke(() => OnStartButtonClick(this, new RoutedEventArgs()));
                            }
                            else if (matchStop)
                            {
                                Dispatcher.Invoke(() => OnStopButtonClick(this, new RoutedEventArgs()));
                            }
                    }
                }
                
                // Create burst effect for all mouse clicks (not just during recording)
                if (wmMessage == WM_LBUTTONDOWN || wmMessage == WM_RBUTTONDOWN || wmMessage == WM_MBUTTONDOWN)
                {
                    // Use Dispatcher to ensure UI updates happen on the UI thread
                    Dispatcher.Invoke(() =>
                    {
                        globalMouseTrailWindow?.CreateBurstEffect(mousePosition);
                    });
                }
            }
            
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }
        

    }
}