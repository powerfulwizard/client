using System.Linq;
using System.Windows;
using System.Windows.Threading;
using PowerfulWizard.Models;

namespace PowerfulWizard
{
    public partial class SequenceTestWindow : Window
    {
        private Sequence _sequence;
        private DispatcherTimer _testTimer;
        private readonly Random _random = Random.Shared;
        
        private int _currentStepIndex = 0;
        private int _currentLoop = 0;
        private int _totalLoops = 0;
        private bool _isTestRunning = false;
        private DateTime _nextActionTime;

        public SequenceTestWindow(Sequence sequence)
        {
            InitializeComponent();
            _sequence = sequence;
            
            SequenceNameText.Text = $"Testing Sequence: {_sequence.Name}";
            
            _testTimer = new DispatcherTimer();
            _testTimer.Tick += OnTestTimerTick;
            
            UpdateDisplay();
        }

        private void OnStartTestClick(object sender, RoutedEventArgs e)
        {
            if (_sequence.Steps.Count == 0)
            {
                MessageBox.Show("No steps to test!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartTest();
        }

        private void OnStopTestClick(object sender, RoutedEventArgs e)
        {
            StopTest();
        }

        private void StartTest()
        {
            _isTestRunning = true;
            _currentStepIndex = 0;
            _currentLoop = 0;
            
            // Calculate total loops
            switch (_sequence.LoopMode)
            {
                case LoopMode.Once:
                    _totalLoops = 1;
                    break;
                case LoopMode.Forever:
                    _totalLoops = -1; // Infinite
                    break;
                case LoopMode.Count:
                    _totalLoops = _sequence.LoopCount;
                    break;
            }

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            
            // Start with first step
            ExecuteCurrentStep();
        }

        private void StopTest()
        {
            _isTestRunning = false;
            _testTimer.Stop();
            
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            
            StatusText.Text = "Test stopped";
            StatusText.Foreground = System.Windows.Media.Brushes.Orange;
        }

        private void ExecuteCurrentStep()
        {
            if (!_isTestRunning || _currentStepIndex >= _sequence.Steps.Count)
            {
                CompleteLoop();
                return;
            }

            var step = _sequence.Steps[_currentStepIndex];
            
            // Calculate delay with deviation
            int actualDelay = step.DelayMs;
            if (step.DeviationMs > 0)
            {
                actualDelay += _random.Next(-step.DeviationMs, step.DeviationMs + 1);
                actualDelay = Math.Max(0, actualDelay);
            }

            // Update display
            UpdateCurrentStepDisplay(step, actualDelay);
            
            // Schedule next step
            _nextActionTime = DateTime.Now.AddMilliseconds(actualDelay);
            _testTimer.Interval = TimeSpan.FromMilliseconds(actualDelay);
            _testTimer.Start();
        }

        private void OnTestTimerTick(object? sender, EventArgs e)
        {
            _testTimer.Stop();
            
            if (!_isTestRunning)
                return;

            // Move to next step
            _currentStepIndex++;
            
            if (_currentStepIndex >= _sequence.Steps.Count)
            {
                CompleteLoop();
            }
            else
            {
                ExecuteCurrentStep();
            }
        }

        private void CompleteLoop()
        {
            _currentLoop++;
            
            // Check if we should continue looping
            bool shouldContinue = false;
            switch (_sequence.LoopMode)
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
                UpdateDisplay();
                ExecuteCurrentStep();
            }
            else
            {
                // Test complete
                _isTestRunning = false;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                
                StatusText.Text = "Test completed successfully!";
                StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                
                UpdateDisplay();
            }
        }

        private void UpdateCurrentStepDisplay(SequenceStep step, int actualDelay)
        {
            CurrentStepText.Text = step.Description;
            CurrentStepDetails.Text = $"{step.ClickType} - Delay: {actualDelay}ms (±{step.DeviationMs}ms)";
            
            if (step.UseRandomPosition)
            {
                CurrentStepDetails.Text += $" - Random position in area: {step.ClickArea.X:F0},{step.ClickArea.Y:F0} {step.ClickArea.Width:F0}x{step.ClickArea.Height:F0}";
            }
            else
            {
                CurrentStepDetails.Text += " - Current cursor position";
            }

            // Update next action
            if (_currentStepIndex < _sequence.Steps.Count - 1)
            {
                var nextStep = _sequence.Steps[_currentStepIndex + 1];
                NextActionText.Text = $"{nextStep.Description} ({nextStep.ClickType})";
            }
            else
            {
                NextActionText.Text = "Complete loop";
            }

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            // Update progress bar
            if (_sequence.Steps.Count > 0)
            {
                double progress = (double)_currentStepIndex / _sequence.Steps.Count;
                ProgressBar.Value = progress * 100;
                ProgressText.Text = $"{_currentStepIndex}/{_sequence.Steps.Count}";
            }

            // Update loop progress
            if (_totalLoops > 0)
            {
                LoopProgressText.Text = $"Loop {_currentLoop + 1} of {_totalLoops}";
            }
            else if (_totalLoops == -1)
            {
                LoopProgressText.Text = $"Loop {_currentLoop + 1} (infinite)";
            }
            else
            {
                LoopProgressText.Text = "Single run";
            }

            // Update status
            if (_isTestRunning)
            {
                var timeUntilNext = _nextActionTime - DateTime.Now;
                if (timeUntilNext.TotalMilliseconds > 0)
                {
                    StatusText.Text = $"Next action in {timeUntilNext.TotalMilliseconds:F0}ms";
                    StatusText.Foreground = System.Windows.Media.Brushes.LightBlue;
                }
                else
                {
                    StatusText.Text = "Executing...";
                    StatusText.Foreground = System.Windows.Media.Brushes.Yellow;
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            StopTest();
            base.OnClosing(e);
        }
    }
}
