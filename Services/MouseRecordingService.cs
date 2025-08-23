using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PowerfulWizard.Models;

namespace PowerfulWizard.Services
{
    public class MouseRecordingService
    {
        private readonly DispatcherTimer _recordingTimer;
        private readonly List<RecordedAction> _currentRecording;
        private bool _isRecording;
        private bool _isPlaying;
        private DateTime _recordingStartTime;
        private DateTime _playbackStartTime;
        private int _currentActionIndex;
        private MouseRecording? _completedRecording;
        private MouseRecording? _currentRecordingSession;
        private DispatcherTimer _playbackTimer;
        
        // Drag detection
        private bool _isLeftButtonDown;
        private bool _isRightButtonDown;
        private bool _isMiddleButtonDown;
        private Point _lastDragPosition;
        
        public event EventHandler<RecordedAction>? ActionRecorded;
        public event EventHandler? RecordingStarted;
        public event EventHandler? RecordingStopped;
        public event EventHandler? PlaybackStarted;
        public event EventHandler? PlaybackStopped;
        public event EventHandler<int>? PlaybackProgressChanged;
        
        public bool IsRecording => _isRecording;
        public bool IsPlaying => _isPlaying;
        public MouseRecording? CurrentRecording 
        { 
            get 
            {
                var result = _completedRecording;
                Console.WriteLine($"CurrentRecording getter called. Completed: {(result != null ? $"{result.Actions.Count} actions" : "null")}, Session: {(_currentRecordingSession != null ? $"{_currentRecordingSession.Actions.Count} actions" : "null")}, _isRecording={_isRecording}, _isPlaying={_isPlaying}");
                Console.WriteLine($"Returning: {(result != null ? $"{result.Actions.Count} actions" : "null")}");
                return result;
            }
        }
        
        // Temporary pause for UI interactions
        private bool _isPaused;
        
        // Debug counter
        private int _tickCount;
        
        public MouseRecordingService()
        {
            _recordingTimer = new DispatcherTimer();
            _recordingTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS for smooth recording
            _recordingTimer.Tick += OnRecordingTimerTick;
            
            _playbackTimer = new DispatcherTimer();
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(16);
            _playbackTimer.Tick += OnPlaybackTimerTick;
            
            _currentRecording = new List<RecordedAction>();
        }
        
        public void StartRecording()
        {
            Console.WriteLine($"StartRecording called. Current state: _isRecording={_isRecording}, _isPlaying={_isPlaying}");
            
            if (_isRecording || _isPlaying) 
            {
                Console.WriteLine("Cannot start recording - already recording or playing");
                return;
            }
            
            Console.WriteLine("Starting new recording...");
            
            // Clear the current recording list but preserve the session until we save it
            _currentRecording.Clear();
            
            // Create a completely new recording session
            _currentRecordingSession = new MouseRecording();
            
            // Reset all state for new recording
            _isRecording = true;
            _isPaused = false; // Reset paused state for new recording
            _recordingStartTime = DateTime.Now;
            _tickCount = 0;
            
            // Initialize position tracking
            if (GetCursorPos(out POINT point))
            {
                _lastDragPosition = new Point(point.X, point.Y);
            }
            
            // Reset button states for new recording
            _isLeftButtonDown = false;
            _isRightButtonDown = false;
            _isMiddleButtonDown = false;
            
            Console.WriteLine($"Recording started - Fresh recording, Button states reset. Session actions: {_currentRecordingSession.Actions.Count}");
            Console.WriteLine($"State after start: _isRecording={_isRecording}, _isPlaying={_isPlaying}, _isPaused={_isPaused}");
            
            _recordingTimer.Start();
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }
        
        public void StopRecording()
        {
            Console.WriteLine($"StopRecording called. Current state: _isRecording={_isRecording}, _isPlaying={_isPlaying}");
            
            if (!_isRecording) 
            {
                Console.WriteLine("Cannot stop recording - not currently recording");
                return;
            }
            
            Console.WriteLine("Stopping recording...");
            _isRecording = false;
            _recordingTimer.Stop();
            
            Console.WriteLine($"Stopping recording. Current actions: {_currentRecording.Count}");
            
            // Save the current recording as the completed recording
            if (_currentRecording.Count > 0)
            {
                // Create a new completed recording with all the actions
                _completedRecording = new MouseRecording();
                
                // Copy all actions from the current recording to the completed recording
                foreach (var action in _currentRecording)
                {
                    _completedRecording.Actions.Add(action);
                }
                
                // Calculate total duration
                var lastAction = _currentRecording[_currentRecording.Count - 1];
                _completedRecording.TotalDuration = (int)(lastAction.Timestamp + lastAction.Duration);
                
                Console.WriteLine($"Recording completed: {_currentRecording.Count} actions, Total duration: {_completedRecording.TotalDuration}ms");
                Console.WriteLine($"Completed recording now has: {_completedRecording.Actions.Count} actions");
            }
            else
            {
                Console.WriteLine("No actions to save - clearing completed recording");
                _completedRecording = null;
            }
            
            // Reset button states when stopping
            _isLeftButtonDown = false;
            _isRightButtonDown = false;
            _isMiddleButtonDown = false;
            
            Console.WriteLine("Recording stopped - Button states reset");
            
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }
        
        public void StartPlayback(MouseRecording recording, double speedMultiplier = 1.0)
        {
            Console.WriteLine($"StartPlayback called with recording: {(recording != null ? $"{recording.Actions.Count} actions" : "null")}");
            Console.WriteLine($"Current state: _isRecording={_isRecording}, _isPlaying={_isPlaying}");
            
            if (_isRecording || _isPlaying || recording.Actions.Count == 0) 
            {
                Console.WriteLine($"Cannot start playback: _isRecording={_isRecording}, _isPlaying={_isPlaying}, actions={recording?.Actions.Count ?? 0}");
                return;
            }
            
            Console.WriteLine("Starting playback...");
            _isPlaying = true;
            _currentActionIndex = 0;
            _playbackStartTime = DateTime.Now;
            
            // Set the recording session for playback
            _currentRecordingSession = recording;
            Console.WriteLine($"Set playback session: {_currentRecordingSession.Actions.Count} actions");
            
            // Adjust timer interval based on speed multiplier
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(16 / speedMultiplier);
            _playbackTimer.Start();
            
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }
        
        public void StopPlayback()
        {
            if (!_isPlaying) return;
            
            _isPlaying = false;
            _playbackTimer.Stop();
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
        
        public void RecordMouseMove(Point position)
        {
            if (!_isRecording || _isPaused) return;
            
            var timestamp = (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;
            var action = new RecordedAction
            {
                ActionType = RecordedActionType.MouseMove,
                Position = position,
                Timestamp = timestamp,
                Duration = 0
            };
            
            _currentRecording.Add(action);
            Console.WriteLine($"Mouse move recorded at {position}, Total actions: {_currentRecording.Count}");
            ActionRecorded?.Invoke(this, action);
        }
        
        public void RecordClick(RecordedActionType clickType, Point position)
        {
            if (!_isRecording || _isPaused) return;
            
            var timestamp = (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;
            var action = new RecordedAction
            {
                ActionType = clickType,
                Position = position,
                Timestamp = timestamp,
                Duration = 50 // Default click duration
            };
            
            _currentRecording.Add(action);
            ActionRecorded?.Invoke(this, action);
        }
        
        public void RecordClickAtCurrentPosition(RecordedActionType clickType)
        {
            if (!_isRecording) return;
            
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                RecordClick(clickType, position);
            }
        }
        
        public void RecordButtonDown(RecordedActionType buttonType)
        {
            if (!_isRecording || _isPaused) return;
            
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                _lastDragPosition = position;
                
                // Record the click action immediately
                switch (buttonType)
                {
                    case RecordedActionType.LeftClick:
                        _isLeftButtonDown = true;
                        RecordClick(RecordedActionType.LeftClick, position);
                        Console.WriteLine($"LEFT BUTTON DOWN at {position} - Button state set to TRUE");
                        break;
                    case RecordedActionType.RightClick:
                        _isRightButtonDown = true;
                        RecordClick(RecordedActionType.RightClick, position);
                        Console.WriteLine($"RIGHT BUTTON DOWN at {position} - Button state set to TRUE");
                        break;
                    case RecordedActionType.MiddleClick:
                        _isMiddleButtonDown = true;
                        RecordClick(RecordedActionType.MiddleClick, position);
                        Console.WriteLine($"MIDDLE BUTTON DOWN at {position} - Button state set to TRUE");
                        break;
                }
            }
        }
        
        public void RecordButtonUp(RecordedActionType buttonType)
        {
            if (!_isRecording) return;
            
            switch (buttonType)
            {
                case RecordedActionType.LeftClick:
                    _isLeftButtonDown = false;
                    Console.WriteLine("LEFT BUTTON UP");
                    break;
                    case RecordedActionType.RightClick:
                    _isRightButtonDown = false;
                    Console.WriteLine("RIGHT BUTTON UP");
                    break;
                case RecordedActionType.MiddleClick:
                    _isMiddleButtonDown = false;
                    Console.WriteLine("MIDDLE BUTTON UP");
                    break;
            }
        }
        
        public void PauseRecording()
        {
            _isPaused = true;
        }
        
        public void ResumeRecording()
        {
            _isPaused = false;
        }
        
        public void ClearRecording()
        {
            _currentRecording.Clear();
            _currentRecordingSession = null;
            // Don't clear _completedRecording - that's what the UI needs for playback
            Console.WriteLine("Current recording cleared, completed recording preserved");
        }
        
        private void RecordDrag(RecordedActionType dragType, Point position)
        {
            if (!_isRecording || _isPaused) return;
            
            var timestamp = (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;
            var action = new RecordedAction
            {
                ActionType = dragType,
                Position = position,
                Timestamp = timestamp,
                Duration = 0
            };
            
            _currentRecording.Add(action);
            ActionRecorded?.Invoke(this, action);
            
            // Debug: Log drag actions
            Console.WriteLine($"DRAG RECORDED: {dragType} at {position}, Total actions: {_currentRecording.Count}");
        }
        
        private void OnRecordingTimerTick(object? sender, EventArgs e)
        {
            if (!_isRecording) return;
            
            // Get current mouse position
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                
                // Debug: Log button states every 100 ticks (about every 1.6 seconds)
                if (_tickCount % 100 == 0)
                {
                    Console.WriteLine($"TIMER TICK: Left={_isLeftButtonDown}, Right={_isRightButtonDown}, Middle={_isMiddleButtonDown}, Pos={position}");
                }
                _tickCount++;
                
                // Check if we're dragging (button held down while moving)
                if (_isLeftButtonDown)
                {
                    var distance = Math.Sqrt(Math.Pow(position.X - _lastDragPosition.X, 2) + 
                                           Math.Pow(position.Y - _lastDragPosition.Y, 2));
                    if (distance > 2.0) // More reasonable drag detection threshold
                    {
                        RecordDrag(RecordedActionType.LeftDrag, position);
                        _lastDragPosition = position;
                        Console.WriteLine($"DRAG TICK: Left drag at {position}, distance: {distance:F2}");
                    }
                }
                else if (_isRightButtonDown)
                {
                    var distance = Math.Sqrt(Math.Pow(position.X - _lastDragPosition.X, 2) + 
                                           Math.Pow(position.Y - _lastDragPosition.Y, 2));
                    if (distance > 2.0)
                    {
                        RecordDrag(RecordedActionType.RightDrag, position);
                        _lastDragPosition = position;
                        Console.WriteLine($"DRAG TICK: Right drag at {position}, distance: {distance:F2}");
                    }
                }
                else if (_isMiddleButtonDown)
                {
                    var distance = Math.Sqrt(Math.Pow(position.X - _lastDragPosition.X, 2) + 
                                           Math.Pow(position.Y - _lastDragPosition.Y, 2));
                    if (distance > 2.0)
                    {
                        RecordDrag(RecordedActionType.MiddleDrag, position);
                        _lastDragPosition = position;
                        Console.WriteLine($"DRAG TICK: Middle drag at {position}, distance: {distance:F2}");
                    }
                }
                else
                {
                    // Normal mouse movement - only record if moved significantly
                    var distance = Math.Sqrt(Math.Pow(position.X - _lastDragPosition.X, 2) + 
                                           Math.Pow(position.Y - _lastDragPosition.Y, 2));
                    if (distance > 2) // Reduced threshold for smoother movement
                    {
                        RecordMouseMove(position);
                        _lastDragPosition = position;
                    }
                }
            }
        }
        

        
        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            if (!_isPlaying || _currentRecordingSession == null) 
            {
                Console.WriteLine($"Playback timer tick skipped: _isPlaying={_isPlaying}, _currentRecordingSession={(_currentRecordingSession != null ? "not null" : "null")}");
                return;
            }
            
            var currentTime = (long)(DateTime.Now - _playbackStartTime).TotalMilliseconds;
            var actions = _currentRecordingSession.Actions;
            
            Console.WriteLine($"Playback timer tick: currentTime={currentTime}ms, actionIndex={_currentActionIndex}/{actions.Count}");
            
            // Execute actions that should happen at this time
            while (_currentActionIndex < actions.Count && 
                   actions[_currentActionIndex].Timestamp <= currentTime)
            {
                var action = actions[_currentActionIndex];
                Console.WriteLine($"Executing action {_currentActionIndex}: {action.ActionType} at {action.Position}");
                ExecuteAction(action);
                _currentActionIndex++;
                
                PlaybackProgressChanged?.Invoke(this, _currentActionIndex);
            }
            
            // Check if playback is complete
            if (_currentActionIndex >= actions.Count)
            {
                Console.WriteLine("Playback complete - stopping");
                StopPlayback();
            }
        }
        
        private void ExecuteAction(RecordedAction action)
        {
            Console.WriteLine($"ExecuteAction: {action.ActionType} at {action.Position}");
            
            switch (action.ActionType)
            {
                case RecordedActionType.MouseMove:
                    Console.WriteLine($"Moving cursor to {action.Position}");
                    SetCursorPos((int)action.Position.X, (int)action.Position.Y);
                    break;
                case RecordedActionType.LeftClick:
                    Console.WriteLine($"Left click at {action.Position}");
                    SetCursorPos((int)action.Position.X, (int)action.Position.Y);
                    SimulateClick(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP);
                    break;
                case RecordedActionType.RightClick:
                    Console.WriteLine($"Right click at {action.Position}");
                    SetCursorPos((int)action.Position.X, (int)action.Position.Y);
                    SimulateClick(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP);
                    break;
                case RecordedActionType.MiddleClick:
                    Console.WriteLine($"Middle click at {action.Position}");
                    SetCursorPos((int)action.Position.X, (int)action.Position.Y);
                    SimulateClick(MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_MIDDLEUP);
                    break;
                case RecordedActionType.DoubleClick:
                    Console.WriteLine($"Double click at {action.Position}");
                    SetCursorPos((int)action.Position.X, (int)action.Position.Y);
                    SimulateClick(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP);
                    Thread.Sleep(50);
                    SimulateClick(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP);
                    break;
                case RecordedActionType.LeftDrag:
                case RecordedActionType.RightDrag:
                case RecordedActionType.MiddleDrag:
                    Console.WriteLine($"Drag at {action.Position}");
                    SetCursorPos((int)action.Position.X, (int)action.Position.Y);
                    break;
            }
        }
        
        private void SimulateClick(uint flags)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            };
            
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }
        
        // P/Invoke declarations
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        
        private const int INPUT_MOUSE = 0;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
        
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
        

    }
}
