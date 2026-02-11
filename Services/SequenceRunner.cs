using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PowerfulWizard.Models;
using System.Windows.Media;
using System.Collections.Generic;
using System;

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
        private Point _overshootPoint;
        private bool _useOvershoot;
        private bool _overshootCompleted;
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

            _ = ExecuteCurrentStepAsync();
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

        private async Task ExecuteCurrentStepAsync()
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
            await ExecuteClickAsync(step);

            // Update the overlay to highlight current step
            UpdateOverlayStep(_currentStepIndex);

            // Update progress
            UpdateProgress();
        }

        private async Task ExecuteClickAsync(SequenceStep step)
        {
            // Mouse position: click at current cursor position (no movement)
            if (step.TargetMode == TargetMode.MousePosition)
            {
                GetCursorPos(out POINT currentPos);
                PerformClick(step);
                await HandleClickValidationAndRetryAsync(step);
                return;
            }

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
                // Convert WPF/DIP coords to physical for SetCursorPos (scaled/multi-monitor)
                clickPosition = ScreenCoordinateHelper.DipToPhysical(clickPosition);
                // Always use smooth movement for color clicks
                StartSmoothMovement(clickPosition, step);
            }
            else if (step.TargetMode == TargetMode.ClickArea)
            {
                // Click Area: random position within the click area (stored in WPF DIPs; convert to physical)
                int x = _random.Next((int)step.ClickArea.X, (int)(step.ClickArea.X + step.ClickArea.Width));
                int y = _random.Next((int)step.ClickArea.Y, (int)(step.ClickArea.Y + step.ClickArea.Height));
                clickPosition = ScreenCoordinateHelper.DipToPhysical(new Point(x, y));
                StartSmoothMovement(clickPosition, step);
            }
            else
            {
                // Fallback: use current cursor position
                GetCursorPos(out POINT currentPos);
                clickPosition = new Point(currentPos.X, currentPos.Y);
                PerformClick(step);
                await HandleClickValidationAndRetryAsync(step);
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

            // Calculate movement distance
            double movementDistance = Math.Sqrt(Math.Pow(_targetPosition.X - _currentPosition.X, 2) + 
                                              Math.Pow(_targetPosition.Y - _currentPosition.Y, 2));
            
            // Check if we should use overshoot for longer paths (300px+)
            bool useOvershoot = movementDistance >= 300;
            
            if (useOvershoot)
            {
                // Calculate overshoot point (4-10% beyond target)
                double overshootDistance = movementDistance * _random.Next(4, 11) / 100.0;
                double overshootRatio = 1.0 + (overshootDistance / movementDistance);
                
                // Randomly choose overshoot direction (left/right or up/down)
                int direction = _random.Next(2) == 0 ? 1 : -1;
                
                // Calculate overshoot point
                double overshootX = _currentPosition.X + (_targetPosition.X - _currentPosition.X) * overshootRatio;
                double overshootY = _currentPosition.Y + (_targetPosition.Y - _currentPosition.Y) * overshootRatio;
                
                // Add some perpendicular offset for more natural overshoot
                double perpendicularOffset = _random.Next(15, 35); // 15-35 pixels
                if (_random.Next(2) == 0)
                {
                    overshootX += (_targetPosition.Y - _currentPosition.Y) * perpendicularOffset / movementDistance * direction;
                    overshootY -= (_targetPosition.X - _currentPosition.X) * perpendicularOffset / movementDistance * direction;
                }
                else
                {
                    overshootX -= (_targetPosition.Y - _currentPosition.Y) * perpendicularOffset / movementDistance * direction;
                    overshootY += (_targetPosition.X - _currentPosition.X) * perpendicularOffset / movementDistance * direction;
                }
                
                _overshootPoint = new Point(overshootX, overshootY);
                _useOvershoot = true;
                _overshootCompleted = false;
                
                System.Diagnostics.Debug.WriteLine($"Overshoot enabled for {movementDistance:F0}px path. Overshoot point: ({_overshootPoint.X:F0}, {_overshootPoint.Y:F0})");
            }
            else
            {
                _useOvershoot = false;
                _overshootPoint = targetPosition;
            }

            // Generate Bezier control point for natural movement
            double midX = (_currentPosition.X + _overshootPoint.X) / 2;
            double midY = (_currentPosition.Y + _overshootPoint.Y) / 2;
            
            // Add random offset - max 30% of the distance or 40px, whichever is smaller
            double maxOffset = Math.Min(movementDistance * 0.3, 40);
            
            double offsetX = (_random.NextDouble() - 0.5) * maxOffset * 2;
            double offsetY = (_random.NextDouble() - 0.5) * maxOffset * 2;
            
            // Keep control point within reasonable bounds
            _bezierControlPoint = new Point(
                Math.Clamp(midX + offsetX, 
                      Math.Min(_currentPosition.X, _overshootPoint.X) - 20,
                      Math.Max(_currentPosition.X, _overshootPoint.X) + 20),
                Math.Clamp(midY + offsetY,
                      Math.Min(_currentPosition.Y, _overshootPoint.Y) - 20, 
                      Math.Max(_currentPosition.Y, _overshootPoint.Y) + 20)
            );

            _currentStep = 0;
            _movementSteps = MOVEMENT_STEPS;
            
            // Calculate base movement duration with distance-based adjustment
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
            
            // Apply enhanced distance-based adjustment for more natural movement
            // Longer paths start faster and slow down more dramatically
            double distanceAdjustment = 1.0 + (movementDistance / 800.0) * DISTANCE_SPEED_FACTOR;
            
            // Additional adjustment: very short paths get even faster
            if (movementDistance < 100)
            {
                distanceAdjustment *= 1.5; // 50% faster for short movements
            }
            else if (movementDistance > 500)
            {
                distanceAdjustment *= 0.8; // 20% slower for long movements (more dramatic slow-down)
            }
            
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
        
        private async void OnMovementTimerTick(object? sender, EventArgs e)
        {
            if (_currentStep >= _movementSteps)
            {
                _movementTimer.Stop();
                
                // Check if we need to handle overshoot
                if (_useOvershoot && !_overshootCompleted)
                {
                    // Start overshoot return movement to actual target
                    StartOvershootReturn();
                    return;
                }
                
                // Add human-like mouse stutter/bump at click time (1-3 pixels)
                AddMouseStutter();
                
                // Movement complete, now perform the click
                if (_currentSequence != null && _currentStepIndex < _currentSequence.Steps.Count)
                {
                    var step = _currentSequence.Steps[_currentStepIndex];
                    PerformClick(step);
                    
                    // Validate click result and retry if needed
                    await HandleClickValidationAndRetryAsync(step);
                }
                return;
            }

            // Calculate t from 0.0 to 1.0 properly
            double t = (double)_currentStep / (_movementSteps - 1);
            
            // Apply easing function for more natural movement (fast start, slow finish)
            double easedT = ApplyEasingFunction(t);
            
            // Quadratic Bézier: B(t) = (1-t)^2 * P0 + 2*(1-t)*t * P1 + t^2 * P2
            double oneMinusT = 1 - easedT;
            double x = oneMinusT * oneMinusT * _currentPosition.X + 
                       2 * oneMinusT * easedT * _bezierControlPoint.X + 
                       easedT * easedT * _overshootPoint.X;
            double y = oneMinusT * oneMinusT * _currentPosition.Y + 
                       2 * oneMinusT * easedT * _bezierControlPoint.Y + 
                       easedT * easedT * _overshootPoint.Y;

            SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            
            _currentStep++;
            
            // Update timer interval for next step if we have more steps
            if (_currentStep < _movementSteps && _speedIntervals.Count > _currentStep)
            {
                _movementTimer.Interval = TimeSpan.FromMilliseconds(_speedIntervals[_currentStep]);
            }
        }
        
        private void AddMouseStutter()
        {
            try
            {
                // Get current cursor position
                GetCursorPos(out POINT currentPos);
                var currentPosition = new Point(currentPos.X, currentPos.Y);
                
                // Generate random stutter offset (1-3 pixels)
                int stutterX = _random.Next(-3, 4); // -3 to +3
                int stutterY = _random.Next(-3, 4); // -3 to +3
                
                // Apply stutter
                var stutterPosition = new Point(
                    currentPosition.X + stutterX,
                    currentPosition.Y + stutterY
                );
                
                // Move cursor to stutter position
                SetCursorPos((int)stutterPosition.X, (int)stutterPosition.Y);
                
                // Small delay to make stutter visible
                System.Threading.Thread.Sleep(10);
                
                // Return to original position
                SetCursorPos((int)currentPosition.X, (int)currentPosition.Y);
                
                System.Diagnostics.Debug.WriteLine($"Mouse stutter applied: ({stutterX}, {stutterY}) pixels");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mouse stutter error: {ex.Message}");
            }
        }
        
        private double ApplyEasingFunction(double t)
        {
            return 1.0 - Math.Pow(1.0 - t, 3); // Cubic ease-out
        }
        
        private void StartOvershootReturn()
        {
            try
            {
                // Set up return movement from overshoot point to actual target
                _currentPosition = _overshootPoint;
                _overshootPoint = _targetPosition;
                _overshootCompleted = true;
                
                // Generate new Bezier control point for return movement
                double midX = (_currentPosition.X + _overshootPoint.X) / 2;
                double midY = (_currentPosition.Y + _overshootPoint.Y) / 2;
                
                // Add smaller random offset for return movement
                double returnDistance = Math.Sqrt(Math.Pow(_overshootPoint.X - _currentPosition.X, 2) + 
                                                Math.Pow(_overshootPoint.Y - _currentPosition.Y, 2));
                double maxOffset = Math.Min(returnDistance * 0.2, 20); // Smaller offset for return
                
                double offsetX = (_random.NextDouble() - 0.5) * maxOffset * 2;
                double offsetY = (_random.NextDouble() - 0.5) * maxOffset * 2;
                
                _bezierControlPoint = new Point(
                    Math.Clamp(midX + offsetX, 
                          Math.Min(_currentPosition.X, _overshootPoint.X) - 15,
                          Math.Max(_currentPosition.X, _overshootPoint.X) + 15),
                    Math.Clamp(midY + offsetY,
                          Math.Min(_currentPosition.Y, _overshootPoint.Y) - 15, 
                          Math.Max(_currentPosition.Y, _overshootPoint.Y) + 15)
                );
                
                // Reset movement state for return
                _currentStep = 0;
                _movementSteps = MOVEMENT_STEPS;
                
                // Calculate return movement duration (faster than initial movement)
                int returnDuration = (int)(_movementDuration * 0.6); // 40% faster return
                returnDuration = Math.Max(MIN_MOVEMENT_DURATION_MS, Math.Min(MAX_MOVEMENT_DURATION_MS, returnDuration));
                
                // Create variable speed intervals for return movement
                _speedIntervals = GenerateVariableSpeedIntervals(returnDuration, _movementSteps);
                
                // Set timer interval and restart
                _movementTimer.Interval = TimeSpan.FromMilliseconds(_speedIntervals[0]);
                _movementTimer.Start();
                
                System.Diagnostics.Debug.WriteLine($"Overshoot return started to target: ({_targetPosition.X:F0}, {_targetPosition.Y:F0})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Overshoot return error: {ex.Message}");
                // Fallback: complete movement and continue
                _overshootCompleted = true;
                AddMouseStutter();
                if (_currentSequence != null && _currentStepIndex < _currentSequence.Steps.Count)
                {
                    // Since this is inside sync event handler but we need to call async methods...
                    // We will fire and forget here as this is a fallback path
                    var step = _currentSequence.Steps[_currentStepIndex];
                    PerformClick(step);
                    _ = HandleClickValidationAndRetryAsync(step);
                }
            }
        }
        
        private void StartStepDelayTimer(SequenceStep step)
        {
            // Notify step executed
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

        private async Task HandleClickValidationAndRetryAsync(SequenceStep step)
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
                
                // Wait for the click indicator to appear
                await Task.Delay(50); // Reduced wait time
                
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
                    await Task.Delay(100);
                    
                    // Retry the click with a new position
                    await RetryClickWithNewPositionAsync(step, clickPosition);
                    
                    // Wait a bit for the retry click to register
                    await Task.Delay(50);
                    
                    // After retry, start the delay timer
                    StartStepDelayTimer(step);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Click validation: No yellow cross detected - assuming success");
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

        private async Task RetryClickWithNewPositionAsync(SequenceStep step, Point previousPosition)
        {
            try
            {
                Point newPosition;
                
                if (step.TargetMode == TargetMode.MousePosition)
                {
                    // Do not move cursor at all - just retry the click in place (no SetCursorPos, no jitter)
                    PerformClick(step);
                    return;
                }
                else if (step.TargetMode == TargetMode.ColorClick)
                {
                    var newColorPosition = ColorDetectionService.FindMatchingColors(
                        step.TargetColor, 
                        step.ColorTolerance, 
                        step.ColorSearchArea
                    );
                    
                    if (newColorPosition.HasValue)
                    {
                        newPosition = newColorPosition.Value;
                    }
                    else
                    {
                        int x = _random.Next((int)step.ColorSearchArea.X, (int)(step.ColorSearchArea.X + step.ColorSearchArea.Width));
                        int y = _random.Next((int)step.ColorSearchArea.Y, (int)(step.ColorSearchArea.Y + step.ColorSearchArea.Height));
                        newPosition = new Point(x, y);
                    }
                    newPosition = ScreenCoordinateHelper.DipToPhysical(newPosition);
                }
                else if (step.TargetMode == TargetMode.ClickArea)
                {
                    int x = _random.Next((int)step.ClickArea.X, (int)(step.ClickArea.X + step.ClickArea.Width));
                    int y = _random.Next((int)step.ClickArea.Y, (int)(step.ClickArea.Y + step.ClickArea.Height));
                    newPosition = ScreenCoordinateHelper.DipToPhysical(new Point(x, y));
                }
                else
                {
                    newPosition = new Point(
                        previousPosition.X + _random.Next(-10, 11),
                        previousPosition.Y + _random.Next(-10, 11)
                    );
                }
                
                // Move to new position and click (MousePosition already returned above - never moves cursor)
                SetCursorPos((int)Math.Round(newPosition.X), (int)Math.Round(newPosition.Y));
                await Task.Delay(100);
                
                PerformClick(step);
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
                return false;
            }
        }

        private async void OnSequenceTimerTick(object? sender, EventArgs e)
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
                await ExecuteCurrentStepAsync();
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
                _ = ExecuteCurrentStepAsync();
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
