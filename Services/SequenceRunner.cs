using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PowerfulWizard.Models;
using System.Windows.Media;
using PowerfulWizard;

namespace PowerfulWizard.Services
{
    public class SequenceRunner
    {
        private DispatcherTimer _sequenceTimer;
        private DispatcherTimer _movementTimer;
        private DispatcherTimer _countdownTimer;
        private Random _random = new Random();
        private Sequence _currentSequence;
        private int _currentStepIndex = 0;
        private int _currentLoop = 0;
        private int _totalLoops = 0;
        private bool _isRunning = false;
        private DateTime _nextActionTime;
        private SequenceVisualOverlayWindow _overlayWindow;
        private int _pendingStepDelay;
        
        // Movement-related fields
        private Point _currentPosition;
        private Point _targetPosition;
        private Point _bezierControlPoint;
        private int _movementSteps;
        private int _currentStep;
        private int _movementDuration;
        private const int MIN_MOVEMENT_DURATION_MS = 100;
        private const int MAX_MOVEMENT_DURATION_MS = 250;
        private const int MOVEMENT_STEPS = 10;

        public event EventHandler<SequenceProgressEventArgs> ProgressChanged;
        public event EventHandler<SequenceCompletedEventArgs> SequenceCompleted;
        public event EventHandler<SequenceStepEventArgs> StepExecuted;
        public event EventHandler<SequenceCountdownEventArgs> CountdownTick;
        public event EventHandler<MovementStartedEventArgs> MovementStarted;

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
            _currentSequence = null;
            
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
                _overlayWindow = new SequenceVisualOverlayWindow(_currentSequence);
                _overlayWindow.Show();
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
                    _overlayWindow = null;
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
            if (!_isRunning || _currentStepIndex >= _currentSequence.Steps.Count)
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
            if (step.UseRandomPosition)
            {
                // Generate random position within the click area
                int x = _random.Next((int)step.ClickArea.X, (int)(step.ClickArea.X + step.ClickArea.Width));
                int y = _random.Next((int)step.ClickArea.Y, (int)(step.ClickArea.Y + step.ClickArea.Height));
                clickPosition = new Point(x, y);
            }
            else
            {
                // Use current cursor position
                GetCursorPos(out POINT currentPos);
                clickPosition = new Point(currentPos.X, currentPos.Y);
            }

            // Start smooth movement if using random position
            if (step.UseRandomPosition)
            {
                StartSmoothMovement(clickPosition, step);
            }
            else
            {
                // Perform the click immediately at current position
                PerformClick(step);
                
                // Start delay timer after immediate click
                StartStepDelayTimer(step);
            }
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
                Clamp(midX + offsetX, 
                      Math.Min(_currentPosition.X, _targetPosition.X) - 20,
                      Math.Max(_currentPosition.X, _targetPosition.X) + 20),
                Clamp(midY + offsetY,
                      Math.Min(_currentPosition.Y, _targetPosition.Y) - 20, 
                      Math.Max(_currentPosition.Y, _targetPosition.Y) + 20)
            );

            _currentStep = 0;
            _movementSteps = MOVEMENT_STEPS;
            // Use custom movement duration from the step, or random if not specified
            if (step.MovementDurationMs > 0)
            {
                _movementDuration = step.MovementDurationMs;
            }
            else
            {
                _movementDuration = _random.Next(MIN_MOVEMENT_DURATION_MS, MAX_MOVEMENT_DURATION_MS + 1);
            }
            
            // Set timer interval for smooth movement
            _movementTimer.Interval = TimeSpan.FromMilliseconds((double)_movementDuration / (_movementSteps - 1));
            _movementTimer.Start();
            
            // Notify about movement start
            MovementStarted?.Invoke(this, new MovementStartedEventArgs
            {
                MovementDurationMs = _movementDuration,
                TargetPosition = _targetPosition
            });
        }
        
        private void OnMovementTimerTick(object sender, EventArgs e)
        {
            if (_currentStep >= _movementSteps)
            {
                _movementTimer.Stop();
                // Movement complete, now perform the click
                if (_currentSequence != null && _currentStepIndex < _currentSequence.Steps.Count)
                {
                    var step = _currentSequence.Steps[_currentStepIndex];
                    PerformClick(step);
                    
                    // NOW start the delay timer after click is performed
                    StartStepDelayTimer(step);
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
        
        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void PerformLeftClick()
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void PerformRightClick()
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
            
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_RIGHTUP;
            
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void PerformMiddleClick()
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MIDDLEDOWN;
            
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = MOUSEEVENTF_MIDDLEUP;
            
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void PerformDoubleClick()
        {
            // Perform two left clicks with a small delay
            PerformLeftClick();
            Task.Delay(50).Wait(); // 50ms delay between clicks
            PerformLeftClick();
        }

        private void OnSequenceTimerTick(object sender, EventArgs e)
        {
            _sequenceTimer.Stop();
            _countdownTimer.Stop();
            
            if (!_isRunning)
                return;

            // Move to next step
            _currentStepIndex++;
            
            if (_currentStepIndex >= _currentSequence.Steps.Count)
            {
                CompleteLoop();
            }
            else
            {
                ExecuteCurrentStep();
            }
        }
        
        private void OnCountdownTimerTick(object sender, EventArgs e)
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
                
                SequenceCompleted?.Invoke(this, new SequenceCompletedEventArgs
                {
                    Sequence = _currentSequence,
                    TotalLoops = _currentLoop,
                    TotalSteps = _currentSequence.Steps.Count * _currentLoop
                });
                
                _currentSequence = null;
            }
        }

        private void UpdateProgress()
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
        public Sequence Sequence { get; set; }
        public int TotalLoops { get; set; }
        public int TotalSteps { get; set; }
    }

    public class SequenceStepEventArgs : EventArgs
    {
        public SequenceStep Step { get; set; }
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
