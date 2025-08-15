using System.Configuration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
        private uint startHotkeyModifiers;
        private uint startHotkeyKey;
        private uint stopHotkeyModifiers;
        private uint stopHotkeyKey;
        private bool useRandomPosition;
        private Rect clickArea;
        private OverlayWindow? overlayWindow;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;
        private const int VK_S = 0x53; // S key
        private const int VK_P = 0x50; // P key
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
                useRandomPosition = bool.TryParse(ConfigurationManager.AppSettings["UseRandomPosition"], out bool useRandom) && useRandom;
                double x = double.TryParse(ConfigurationManager.AppSettings["ClickAreaX"], out double cx) ? cx : 0;
                double y = double.TryParse(ConfigurationManager.AppSettings["ClickAreaY"], out double cy) ? cy : 0;
                double width = double.TryParse(ConfigurationManager.AppSettings["ClickAreaWidth"], out double cw) ? cw : 100;
                double height = double.TryParse(ConfigurationManager.AppSettings["ClickAreaHeight"], out double ch) ? ch : 100;
                clickArea = new Rect(x, y, width, height);
                currentClickType = Enum.TryParse(ConfigurationManager.AppSettings["ClickType"], out ClickType clickType) ? clickType : ClickType.LeftClick;
            }
            catch
            {
                startHotkeyModifiers = MOD_CONTROL | MOD_SHIFT;
                startHotkeyKey = VK_S;
                stopHotkeyModifiers = MOD_CONTROL | MOD_SHIFT;
                stopHotkeyKey = VK_P;
                useRandomPosition = false;
                clickArea = new Rect(0, 0, 100, 100);
                currentClickType = ClickType.LeftClick;
            }

            UseRandomPositionCheck.IsChecked = useRandomPosition;
            SetClickAreaButton.IsEnabled = useRandomPosition;
            ClickTypeComboBox.SelectedIndex = (int)currentClickType;
            
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
            
            if (clickArea.Width > 0 && clickArea.Height > 0)
            {
                overlayWindow = new OverlayWindow(clickArea);
                overlayWindow.Show();
            }
            
            // Global mouse trail window is already initialized
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
            MovementSpeedLabel.Content = "Movement Speed: -- ms";
            StartHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(startHotkeyModifiers, startHotkeyKey)}";
            StopHotkeyLabel.Content = $"Hotkey: {GetHotkeyText(stopHotkeyModifiers, stopHotkeyKey)}";
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hWnd, HOTKEY_ID_START);
            UnregisterHotKey(hWnd, HOTKEY_ID_STOP);
            overlayWindow?.Close();
            globalMouseTrailWindow?.Close();
            sequenceRunner?.StopSequence();
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

        private void OnUseRandomPositionChecked(object? sender, RoutedEventArgs e)
        {
            useRandomPosition = UseRandomPositionCheck.IsChecked == true;
            SetClickAreaButton.IsEnabled = useRandomPosition;
            if (!useRandomPosition && overlayWindow != null)
            {
                overlayWindow.Close();
                overlayWindow = null!;
            }
            SaveSettings();
        }

        private void OnSetClickAreaClick(object? sender, RoutedEventArgs e)
        {
            var clickAreaWindow = new ClickAreaWindow(this);
            if (clickAreaWindow.ShowDialog() == true)
            {
                clickArea = clickAreaWindow.SelectedArea;
                if (overlayWindow == null)
                {
                    overlayWindow = new OverlayWindow(clickArea);
                    overlayWindow.Show();
                }
                else
                {
                    overlayWindow.UpdateRectangle(clickArea);
                }
                SaveSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings.Remove("UseRandomPosition");
                config.AppSettings.Settings.Remove("ClickAreaX");
                config.AppSettings.Settings.Remove("ClickAreaY");
                config.AppSettings.Settings.Remove("ClickAreaWidth");
                config.AppSettings.Settings.Remove("ClickAreaHeight");
                config.AppSettings.Settings.Remove("ClickType");
                config.AppSettings.Settings.Remove("MovementSpeed");
                config.AppSettings.Settings.Remove("CustomMovementSpeed");
                config.AppSettings.Settings.Add("UseRandomPosition", useRandomPosition.ToString());
                config.AppSettings.Settings.Add("ClickAreaX", clickArea.X.ToString());
                config.AppSettings.Settings.Add("ClickAreaY", clickArea.Y.ToString());
                config.AppSettings.Settings.Add("ClickAreaWidth", clickArea.Width.ToString());
                config.AppSettings.Settings.Add("ClickAreaHeight", clickArea.Height.ToString());
                config.AppSettings.Settings.Add("ClickType", currentClickType.ToString());
                
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
                    if (useRandomPosition && (clickArea.Width <= 0 || clickArea.Height <= 0))
                    {
                        MessageBox.Show("Please set a valid click area for random position.", "Invalid Click Area");
                        return;
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
            if (useRandomPosition && clickArea.Width > 0 && clickArea.Height > 0)
            {
                GetCursorPos(out POINT currentPos);
                currentPosition = new Point(currentPos.X, currentPos.Y);
                targetPosition = new Point(
                    clickArea.X + random.NextDouble() * clickArea.Width,
                    clickArea.Y + random.NextDouble() * clickArea.Height
                );

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
            else
            {
                PerformMouseClick();
            }
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
    }
}