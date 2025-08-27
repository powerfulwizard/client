using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PowerfulWizard.Models;
using System.Windows.Media;

namespace PowerfulWizard.Services
{
    public class SequenceRunner
    {
        private DispatcherTimer _sequenceTimer;
        private DispatcherTimer _movementTimer;
        private DispatcherTimer _countdownTimer;
        private readonly Random _random = Random.Shared;
        private Sequence? _currentSequence;
        private int _currentStepIndex = 0;
        private int _currentLoop = 0;
        private int _totalLoops = 0;
        private bool _isRunning = false;
        private DateTime _nextActionTime;
        private SequenceVisualOverlayWindow? _overlayWindow;
        private int _pendingStepDelay;
        
        // Movement-related fields
        private Point _currentPosition;
        private Point _targetPosition;
        private Point _bezierControlPoint;
        private int _movementSteps;
        private int _currentStep;
        private int _movementDuration;
        private List<double> _speedIntervals = new();
        private const int MIN_MOVEMENT_DURATION_MS = 100;
        private const int MAX_MOVEMENT_DURATION_MS = 250;
        private const int MOVEMENT_STEPS = 10;
        
        // Enhanced movement speed randomization
        private const int MIN_BASE_DURATION_MS = 80;
        private const int MAX_BASE_DURATION_MS = 300;
        private const double SPEED_VARIATION_FACTOR = 0.3; // 30% speed variation within movement
        private const double DISTANCE_SPEED_FACTOR = 0.15; // Distance affects speed by 15%

        public event EventHandler<SequenceProgressEventArgs>? ProgressChanged;
        public event EventHandler<SequenceCompletedEventArgs>? SequenceCompleted;
        public event EventHandler<SequenceStepEventArgs>? StepExecuted;
        public event EventHandler<SequenceCountdownEventArgs>? CountdownTick;
        public event EventHandler<MovementStartedEventArgs>? MovementStarted;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

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

        public SequenceRunner()
        {
            _sequenceTimer = new DispatcherTimer();
            _sequenceTimer.Tick += OnSequenceTimerTick;
            
            _movementTimer = new DispatcherTimer();
            _movementTimer.Tick += OnMovementTimerTick;
            
            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromMilliseconds(50); // Update every 50ms
            _countdownTimer.Tick += OnCountdownTimerTick;
        }

        public bool IsRunning => _isRunning;

        public void StartSequence(Sequence sequence)
        {
            if (_isRunning)
            {
                StopSequence();
            }

            _currentSequence = sequence;
            _currentStepIndex = 0;
            _currentLoop = 0;
            _isRunning = true;

            // Show the visual overlay
            ShowOverlay();

            // Calculate total loops
            switch (_currentSequence.LoopMode)
            {
                case LoopMode.Once:
                    _totalLoops = 1;
                    break;
                case LoopMode.Forever:
                    _totalLoops = -1; // Infinite
                    break;
                case LoopMode.Count:
                    _totalLoops = _currentSequence.LoopCount;
                    break;
            }

            ExecuteCurrentStep();
        }

        public void StopSequence()
        {
            _isRunning = false;
            _sequenceTimer.Stop();
            _movementTimer.Stop();
            _countdownTimer.Stop();
            _currentSequence = null!;
            
            // Hide the visual overlay
            HideOverlay();
        }

        private void ShowOverlay()
        {
            try
            {
                // Hide any existing overlay
                HideOverlay();
                
                // Create and show new overlay
                if (_currentSequence != null)
                {
                    _overlayWindow = new SequenceVisualOverlayWindow(_currentSequence);
                    _overlayWindow.Show();
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash the sequence
                System.Diagnostics.Debug.WriteLine($"Error showing overlay: {ex.Message}");
            }
        }

        private void HideOverlay()
        {
            try
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow = null!;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding overlay: {ex.Message}");
            }
        }

        private void UpdateOverlayStep(int stepIndex)
        {
            try
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.HighlightStep(stepIndex);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating overlay: {ex.Message}");
            }
        }

        private void ExecuteCurrentStep()
        {
            if (!_isRunning || _currentSequence == null || _currentStepIndex >= _currentSequence.Steps.Count)
            {
                CompleteLoop();
                return;
            }

            var step = _currentSequence.Steps[_currentStepIndex];

            // Calculate delay with deviation
            int actualDelay = step.DelayMs;
            if (step.DeviationMs > 0)
            {
                actualDelay += _random.Next(-step.DeviationMs, step.DeviationMs + 1);
                actualDelay = Math.Max(0, actualDelay);
            }

            // Store the delay for after movement/click completes
            _pendingStepDelay = actualDelay;

            // Execute the click (this may start movement)
            ExecuteClick(step);

            // Update the overlay to highlight current step
            UpdateOverlayStep(_currentStepIndex);

            // Update progress
            UpdateProgress();
        }

        private void ExecuteClick(SequenceStep step)
        {
            // Determine click position
            Point clickPosition;
            
            if (step.TargetMode == TargetMode.ColorClick)
            {
                // Find color in the specified search area
                var colorPosition = ColorDetectionService.FindMatchingColors(
                    step.TargetColor, 
                    step.ColorTolerance, 
                    step.ColorSearchArea);
                
                if (colorPosition.HasValue)
                {
                    clickPosition = colorPosition.Value;
                    System.Diagnostics.Debug.WriteLine($"Color found at: ({clickPosition.X}, {clickPosition.Y})");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Color not found, using random position in search area");
                    // Fallback to random position in search area if color not found
                    int x = _random.Next((int)step.ColorSearchArea.X, (int)(step.ColorSearchArea.X + step.ColorSearchArea.Width));
                    int y = _random.Next((int)step.ColorSearchArea.Y, (int)(step.ColorSearchArea.Y + step.ColorSearchArea.Height));
                    clickPosition = new Point(x, y);
                }
                
                // Always use smooth movement for color clicks
                StartSmoothMovement(clickPosition, step);
            }
            else if (step.UseRandomPosition)
            {
                // Generate random position within the click area
                int x = _random.Next((int)step.ClickArea.X, (int)(step.ClickArea.X + step.ClickArea.Width));
                int y = _random.Next((int)step.ClickArea.Y, (int)(step.ClickArea.Y + step.ClickArea.Height));
                clickPosition = new Point(x, y);
                
                // Start smooth movement if using random position
                StartSmoothMovement(clickPosition, step);
            }
            else
            {
                // Use current cursor position
                GetCursorPos(out POINT currentPos);
                clickPosition = new Point(currentPos.X, currentPos.Y);
                
                // Perform the click immediately at current position
                PerformClick(step);
                
                // Validate click result and retry if needed - this will handle retries synchronously
                HandleClickValidationAndRetry(step);
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

        private void StartSmoothMovement(Point targetPosition, SequenceStep step)
        {
            // Get current cursor position
            GetCursorPos(out POINT currentPos);
            _currentPosition = new Point(currentPos.X, currentPos.Y);
            _targetPosition = targetPosition;

            // Generate Bezier control point for natural movement
            double midX = (_currentPosition.X + _targetPosition.X) / 2;
            double midY = (_currentPosition.Y + _targetPosition.Y) / 2;
            
            // Add random offset - max 30% of the distance or 40px, whichever is smaller
            double distance = Math.Sqrt(Math.Pow(_targetPosition.X - _currentPosition.X, 2) + 
                                      Math.Pow(_targetPosition.Y - _currentPosition.Y, 2));
            double maxOffset = Math.Min(distance * 0.3, 40);
            
            double offsetX = (_random.NextDouble() - 0.5) * maxOffset * 2;
            double offsetY = (_random.NextDouble() - 0.5) * maxOffset * 2;
            
            // Keep control point within reasonable bounds
            _bezierControlPoint = new Point(
                Math.Clamp(midX + offsetX, 
                      Math.Min(_currentPosition.X, _targetPosition.X) - 20,
                      Math.Max(_currentPosition.X, _targetPosition.X) + 20),
                Math.Clamp(midY + offsetY,
                      Math.Min(_currentPosition.Y, _targetPosition.Y) - 20, 
                      Math.Max(_currentPosition.Y, _targetPosition.Y) + 20)
            );

            _currentStep = 0;
            _movementSteps = MOVEMENT_STEPS;
            
            // Calculate base movement duration with distance-based adjustment
            double movementDistance = Math.Sqrt(Math.Pow(_targetPosition.X - _currentPosition.X, 2) + 
                                              Math.Pow(_targetPosition.Y - _currentPosition.Y, 2));
            
            // Calculate movement duration based on the step's movement speed setting
            switch (step.MovementSpeed)
            {
                case MovementSpeed.Fast:
                    _movementDuration = _random.Next(80, 121); // 80-120ms
                    break;
                case MovementSpeed.Medium:
                    _movementDuration = _random.Next(120, 201); // 120-200ms
                    break;
                case MovementSpeed.Slow:
                    _movementDuration = _random.Next(200, 301); // 200-300ms
                    break;
                case MovementSpeed.Custom:
                    _movementDuration = step.CustomMovementDurationMs;
                    break;
                default:
                    _movementDuration = _random.Next(120, 201); // Default to medium
                    break;
            }
            
            // Apply distance-based adjustment for more natural movement
            double distanceAdjustment = 1.0 + (movementDistance / 1000.0) * DISTANCE_SPEED_FACTOR;
            _movementDuration = (int)(_movementDuration / distanceAdjustment);
            
            // Ensure duration stays within reasonable bounds
            _movementDuration = Math.Max(MIN_MOVEMENT_DURATION_MS, Math.Min(MAX_MOVEMENT_DURATION_MS, _movementDuration));
            
            // Create variable speed intervals for more human-like movement
            _speedIntervals = GenerateVariableSpeedIntervals(_movementDuration, _movementSteps);
            
            // Set timer interval for first step
            _movementTimer.Interval = TimeSpan.FromMilliseconds(_speedIntervals[0]);
            _movementTimer.Start();
            
            // Notify about movement start
            MovementStarted?.Invoke(this, new MovementStartedEventArgs
            {
                MovementDurationMs = _movementDuration,
                TargetPosition = _targetPosition
            });
        }
        
        private void OnMovementTimerTick(object? sender, EventArgs e)
        {
            if (_currentStep >= _movementSteps)
            {
                _movementTimer.Stop();
                // Movement complete, now perform the click
                if (_currentSequence != null && _currentStepIndex < _currentSequence.Steps.Count)
                {
                    var step = _currentSequence.Steps[_currentStepIndex];
                    PerformClick(step);
                    
                    // Validate click result and retry if needed - this will handle retries synchronously
                    HandleClickValidationAndRetry(step);
                }
                return;
            }

            // Calculate t from 0.0 to 1.0 properly
            double t = (double)_currentStep / (_movementSteps - 1);
            
            // Quadratic Bézier: B(t) = (1-t)^2 * P0 + 2*(1-t)*t * P1 + t^2 * P2
            double oneMinusT = 1 - t;
            double x = oneMinusT * oneMinusT * _currentPosition.X + 
                       2 * oneMinusT * t * _bezierControlPoint.X + 
                       t * t * _targetPosition.X;
            double y = oneMinusT * oneMinusT * _currentPosition.Y + 
                       2 * oneMinusT * t * _bezierControlPoint.Y + 
                       t * t * _targetPosition.Y;

            SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            
            // Add trail point for automated cursor movement
            var screenPosition = new Point(Math.Round(x), Math.Round(y));
            // Note: We'll need to access the mouse trail service from here
            // For now, we'll add this functionality later
            
            _currentStep++;
            
            // Update timer interval for next step if we have more steps
            if (_currentStep < _movementSteps && _speedIntervals.Count > _currentStep)
            {
                _movementTimer.Interval = TimeSpan.FromMilliseconds(_speedIntervals[_currentStep]);
            }
        }
        
        private void StartStepDelayTimer(SequenceStep step)
        {
            // Notify step executed (moved here from ExecuteCurrentStep)
            StepExecuted?.Invoke(this, new SequenceStepEventArgs
            {
                Step = step,
                StepIndex = _currentStepIndex,
                LoopIndex = _currentLoop,
                ActualDelay = _pendingStepDelay
            });

            // Schedule next step with the calculated delay
            _nextActionTime = DateTime.Now.AddMilliseconds(_pendingStepDelay);
            _sequenceTimer.Interval = TimeSpan.FromMilliseconds(_pendingStepDelay);
            _sequenceTimer.Start();
            _countdownTimer.Start();
        }
        
        private void PerformClick(SequenceStep step)
        {
            // Perform the click based on type
            switch (step.ClickType)
            {
                case ClickType.LeftClick:
                    PerformLeftClick();
                    break;
                case ClickType.RightClick:
                    PerformRightClick();
                    break;
                case ClickType.MiddleClick:
                    PerformMiddleClick();
                    break;
                case ClickType.DoubleClick:
                    PerformDoubleClick();
                    break;
            }
        }
        
        // .NET 8 has Math.Clamp built-in, no need for custom implementation

        private void PerformLeftClick()
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        private void PerformRightClick()
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
            
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_RIGHTUP;
            
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        private void PerformMiddleClick()
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MIDDLEDOWN;
            
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_MIDDLEUP;
            
            SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        }

        private void PerformDoubleClick()
        {
            // Perform two left clicks with a small delay
            PerformLeftClick();
            Task.Delay(50).Wait(); // 50ms delay between clicks
            PerformLeftClick();
        }

        private void HandleClickValidationAndRetry(SequenceStep step)
        {
            try
            {
                // Check if click validation is enabled in settings
                if (!IsClickValidationEnabled())
                {
                    // Skip validation if disabled, just start the delay timer
                    StartStepDelayTimer(step);
                    return;
                }
                
                // Wait for the click indicator to appear (frames appear immediately)
                System.Threading.Thread.Sleep(50); // Reduced wait time
                
                // Get current cursor position
                GetCursorPos(out POINT currentPos);
                var clickPosition = new Point(currentPos.X, currentPos.Y);
                
                // Define a tiny area right at the click point to check for indicators
                var checkArea = new Rect(
                    clickPosition.X - 2, 
                    clickPosition.Y - 2, 
                    4, // Tiny 4x4 area
                    4
                );
                
                System.Diagnostics.Debug.WriteLine($"Click validation: Checking 4x4 area around ({clickPosition.X}, {clickPosition.Y}) for crosses");
                
                // Simple approach: Use direct pixel sampling instead of OpenCV
                // Sample a few pixels in the tiny area to check for yellow
                bool yellowDetected = false;
                
                try
                {
                    using (var bitmap = new System.Drawing.Bitmap(4, 4))
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen((int)checkArea.Left, (int)checkArea.Top, 0, 0, new System.Drawing.Size(4, 4));
                        
                        // Check center pixel and a few surrounding pixels
                        var centerPixel = bitmap.GetPixel(2, 2);
                        var topPixel = bitmap.GetPixel(2, 1);
                        var bottomPixel = bitmap.GetPixel(2, 3);
                        
                        // Check if any of these pixels are yellow (high R+G, low B)
                        var pixels = new[] { centerPixel, topPixel, bottomPixel };
                        foreach (var pixel in pixels)
                        {
                            if (pixel.R > 200 && pixel.G > 200 && pixel.B < 100)
                            {
                                yellowDetected = true;
                                System.Diagnostics.Debug.WriteLine($"Yellow pixel detected: R:{pixel.R} G:{pixel.G} B:{pixel.B}");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Pixel sampling error: {ex.Message}");
                }
                
                if (yellowDetected)
                {
                    System.Diagnostics.Debug.WriteLine($"Click validation: YELLOW cross detected - retrying click immediately");
                    
                    // Wait a bit before retry
                    System.Threading.Thread.Sleep(100);
                    
                    // Retry the click with a new position
                    RetryClickWithNewPositionSync(step, clickPosition);
                    
                    // Wait a bit for the retry click to register
                    System.Threading.Thread.Sleep(50);
                    
                    // After retry, start the delay timer
                    StartStepDelayTimer(step);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Click validation: No yellow cross detected - assuming success (red or no indicator)");
                    // No yellow cross = success, start the normal delay timer
                    StartStepDelayTimer(step);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Click validation error: {ex.Message}");
                // If validation fails, just continue with normal flow
                StartStepDelayTimer(step);
            }
        }

        private void RetryClickWithNewPositionSync(SequenceStep step, Point previousPosition)
        {
            try
            {
                Point newPosition;
                
                if (step.TargetMode == TargetMode.ColorClick)
                {
                    // For color clicks, find a new color match
                    var newColorPosition = ColorDetectionService.FindMatchingColors(
                        step.TargetColor, 
                        step.ColorTolerance, 
                        step.ColorSearchArea
                    );
                    
                    if (newColorPosition.HasValue)
                    {
                        newPosition = newColorPosition.Value;
                        System.Diagnostics.Debug.WriteLine($"Retry: Found new color position at ({newPosition.X}, {newPosition.Y})");
                    }
                    else
                    {
                        // Fallback to random position in search area
                        int x = _random.Next((int)step.ColorSearchArea.X, (int)(step.ColorSearchArea.X + step.ColorSearchArea.Width));
                        int y = _random.Next((int)step.ColorSearchArea.Y, (int)(step.ColorSearchArea.Y + step.ColorSearchArea.Height));
                        newPosition = new Point(x, y);
                        System.Diagnostics.Debug.WriteLine($"Retry: Using random position at ({newPosition.X}, {newPosition.Y})");
                    }
                }
                else if (step.UseRandomPosition)
                {
                    // For random position clicks, generate a new random position
                    int x = _random.Next((int)step.ClickArea.X, (int)(step.ClickArea.X + step.ClickArea.Width));
                    int y = _random.Next((int)step.ClickArea.Y, (int)(step.ClickArea.Y + step.ClickArea.Height));
                    newPosition = new Point(x, y);
                    System.Diagnostics.Debug.WriteLine($"Retry: Using new random position at ({newPosition.X}, {newPosition.Y})");
                }
                else
                {
                    // For fixed position, try a small offset
                    newPosition = new Point(
                        previousPosition.X + _random.Next(-10, 11),
                        previousPosition.Y + _random.Next(-10, 11)
                    );
                    System.Diagnostics.Debug.WriteLine($"Retry: Using offset position at ({newPosition.X}, {newPosition.Y})");
                }
                
                // Move to new position and click
                SetCursorPos((int)newPosition.X, (int)newPosition.Y);
                System.Threading.Thread.Sleep(100); // Small delay for movement
                
                // Perform the click
                PerformClick(step);
                
                System.Diagnostics.Debug.WriteLine($"Retry click completed at ({newPosition.X}, {newPosition.Y})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Retry click error: {ex.Message}");
            }
        }

        private bool IsClickValidationEnabled()
        {
            try
            {
                var config = System.Configuration.ConfigurationManager.AppSettings;
                return bool.TryParse(config["EnableClickValidation"], out bool enabled) && enabled;
            }
            catch
            {
                return false; // Default to disabled if there's an error
            }
        }

        private int GetValidationAreaSize()
        {
            try
            {
                var config = System.Configuration.ConfigurationManager.AppSettings;
                return int.TryParse(config["ValidationAreaSize"], out int size) ? size : 50;
            }
            catch
            {
                return 50; // Default to 50 pixels if there's an error
            }
        }
        
        /// <summary>
        /// Debug method to analyze what colors are actually in the validation area
        /// </summary>
        private void DebugColorsInArea(System.Windows.Rect area)
        {
            try
            {
                // Capture the area and analyze dominant colors
                using (var bitmap = new System.Drawing.Bitmap((int)area.Width, (int)area.Height))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen((int)area.Left, (int)area.Top, 0, 0, new System.Drawing.Size((int)area.Width, (int)area.Height));
                    
                    // Sample pixels in a grid pattern to detect cross patterns
                    var samplePoints = new[] { 
                        new System.Drawing.Point(5, 5),   // Top-left
                        new System.Drawing.Point(10, 10), // Center
                        new System.Drawing.Point(15, 15), // Bottom-right
                        new System.Drawing.Point(5, 15),  // Bottom-left
                        new System.Drawing.Point(15, 5)   // Top-right
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"Debug: Analyzing 20x20 area at ({area.Left}, {area.Top})");
                    
                    foreach (var point in samplePoints)
                    {
                        if (point.X < bitmap.Width && point.Y < bitmap.Height)
                        {
                            var pixel = bitmap.GetPixel(point.X, point.Y);
                            var brightness = (pixel.R + pixel.G + pixel.B) / 3;
                            var isBright = brightness > 100;
                            
                            System.Diagnostics.Debug.WriteLine($"Debug: Pixel at ({point.X}, {point.Y}) = R:{pixel.R} G:{pixel.G} B:{pixel.B} (Bright: {isBright})");
                            
                            // Check for potential cross indicators
                            if (pixel.R > 200 && pixel.G < 100 && pixel.B < 100)
                            {
                                System.Diagnostics.Debug.WriteLine($"  -> Potential RED cross indicator detected!");
                            }
                            else if (pixel.R > 200 && pixel.G > 200 && pixel.B < 100)
                            {
                                System.Diagnostics.Debug.WriteLine($"  -> Potential YELLOW cross indicator detected!");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug color analysis error: {ex.Message}");
            }
        }

        private void OnSequenceTimerTick(object? sender, EventArgs e)
        {
            _sequenceTimer.Stop();
            _countdownTimer.Stop();
            
            if (!_isRunning)
                return;

            // Move to next step
            _currentStepIndex++;
            
            if (_currentSequence != null && _currentStepIndex >= _currentSequence.Steps.Count)
            {
                CompleteLoop();
            }
            else
            {
                ExecuteCurrentStep();
            }
        }
        
        private void OnCountdownTimerTick(object? sender, EventArgs e)
        {
            if (_isRunning && _sequenceTimer.IsEnabled)
            {
                double msRemaining = (_nextActionTime - DateTime.Now).TotalMilliseconds;
                CountdownTick?.Invoke(this, new SequenceCountdownEventArgs
                {
                    MillisecondsRemaining = Math.Max(0, msRemaining),
                    NextActionTime = _nextActionTime
                });
            }
        }

        private void CompleteLoop()
        {
            _currentLoop++;
            
            // Check if we should continue looping
            bool shouldContinue = false;
            if (_currentSequence != null)
            {
                switch (_currentSequence.LoopMode)
                {
                    case LoopMode.Once:
                        shouldContinue = false;
                        break;
                    case LoopMode.Forever:
                        shouldContinue = true;
                        break;
                    case LoopMode.Count:
                        shouldContinue = _currentLoop < _totalLoops;
                        break;
                }
            }

            if (shouldContinue)
            {
                // Start next loop
                _currentStepIndex = 0;
                UpdateProgress();
                ExecuteCurrentStep();
            }
            else
            {
                // Sequence complete
                _isRunning = false;
                _sequenceTimer.Stop();
                _countdownTimer.Stop();
                
                // Hide the overlay
                HideOverlay();
                
                if (_currentSequence != null)
                {
                    SequenceCompleted?.Invoke(this, new SequenceCompletedEventArgs
                    {
                        Sequence = _currentSequence,
                        TotalLoops = _currentLoop,
                        TotalSteps = _currentSequence.Steps.Count * _currentLoop
                    });
                }
                
                _currentSequence = null!;
            }
        }

        private void UpdateProgress()
        {
            if (_currentSequence != null)
            {
                ProgressChanged?.Invoke(this, new SequenceProgressEventArgs
                {
                    CurrentStepIndex = _currentStepIndex,
                    TotalSteps = _currentSequence.Steps.Count,
                    CurrentLoop = _currentLoop,
                    TotalLoops = _totalLoops,
                    NextActionTime = _nextActionTime
                });
            }
        }
    }

    public class SequenceProgressEventArgs : EventArgs
    {
        public int CurrentStepIndex { get; set; }
        public int TotalSteps { get; set; }
        public int CurrentLoop { get; set; }
        public int TotalLoops { get; set; }
        public DateTime NextActionTime { get; set; }
    }

    public class SequenceCompletedEventArgs : EventArgs
    {
        public Sequence Sequence { get; set; } = null!;
        public int TotalLoops { get; set; }
        public int TotalSteps { get; set; }
    }

    public class SequenceStepEventArgs : EventArgs
    {
        public SequenceStep Step { get; set; } = null!;
        public int StepIndex { get; set; }
        public int LoopIndex { get; set; }
        public int ActualDelay { get; set; }
    }

    public class SequenceCountdownEventArgs : EventArgs
    {
        public double MillisecondsRemaining { get; set; }
        public DateTime NextActionTime { get; set; }
    }

    public class MovementStartedEventArgs : EventArgs
    {
        public int MovementDurationMs { get; set; }
        public Point TargetPosition { get; set; }
    }
}
