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
                return result;
            }
        }
        
        // Temporary pause for UI interactions
        private bool _isPaused;
        
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
            if (_isRecording || _isPlaying) 
            {
                return;
            }
            
            // Clear the current recording list but preserve the session until we save it
            _currentRecording.Clear();
            
            // Create a completely new recording session
            _currentRecordingSession = new MouseRecording();
            
            // Reset all state for new recording
            _isRecording = true;
            _isPaused = false; // Reset paused state for new recording
            _recordingStartTime = DateTime.Now;
            
            // Initialize position tracking
            if (GetCursorPos(out POINT point))
            {
                _lastDragPosition = new Point(point.X, point.Y);
            }
            
            _recordingTimer.Start();
            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }
        
        public void StopRecording()
        {
            if (!_isRecording) 
            {
                return;
            }
            
            _isRecording = false;
            _recordingTimer.Stop();
            
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
            }
            else
            {
                _completedRecording = null;
            }
            
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }
        
        public void StartPlayback(MouseRecording recording, double speedMultiplier = 1.0)
        {
            if (_isRecording || _isPlaying || recording.Actions.Count == 0) 
            {
                return;
            }
            
            _isPlaying = true;
            _currentActionIndex = 0;
            _playbackStartTime = DateTime.Now;
            
            // Set the recording session for playback
            _currentRecordingSession = recording;
            
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
            ActionRecorded?.Invoke(this, action);
        }
        
        public void RecordAction(RecordedActionType actionType, Point position)
        {
            if (!_isRecording || _isPaused) return;
            
            var timestamp = (long)(DateTime.Now - _recordingStartTime).TotalMilliseconds;
            var action = new RecordedAction
            {
                ActionType = actionType,
                Position = position,
                Timestamp = timestamp,
                Duration = 0
            };
            
            _currentRecording.Add(action);
            ActionRecorded?.Invoke(this, action);
        }
        
        // Kept for backward compatibility if needed, but forwards to RecordAction
        public void RecordClick(RecordedActionType clickType, Point position)
        {
            RecordAction(clickType, position);
        }
        
        public void RecordClickAtCurrentPosition(RecordedActionType clickType)
        {
            if (!_isRecording) return;
            
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                RecordAction(clickType, position);
            }
        }
        
        public void RecordButtonDown(RecordedActionType buttonType)
        {
            if (!_isRecording || _isPaused) return;
            
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                _lastDragPosition = position;
                
                // Record the Down action
                switch (buttonType)
                {
                    case RecordedActionType.LeftClick:
                        RecordAction(RecordedActionType.LeftDown, position);
                        break;
                    case RecordedActionType.RightClick:
                        RecordAction(RecordedActionType.RightDown, position);
                        break;
                    case RecordedActionType.MiddleClick:
                        RecordAction(RecordedActionType.MiddleDown, position);
                        break;
                }
            }
        }
        
        public void RecordButtonUp(RecordedActionType buttonType)
        {
            if (!_isRecording) return;
            
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                
                switch (buttonType)
                {
                    case RecordedActionType.LeftClick:
                        RecordAction(RecordedActionType.LeftUp, position);
                        break;
                    case RecordedActionType.RightClick:
                        RecordAction(RecordedActionType.RightUp, position);
                        break;
                    case RecordedActionType.MiddleClick:
                        RecordAction(RecordedActionType.MiddleUp, position);
                        break;
                }
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
        }
        
        private void OnRecordingTimerTick(object? sender, EventArgs e)
        {
            if (!_isRecording) return;
            
            // Get current mouse position
            if (GetCursorPos(out POINT point))
            {
                var position = new Point(point.X, point.Y);
                
                // Check for significant movement
                var distance = Math.Sqrt(Math.Pow(position.X - _lastDragPosition.X, 2) + 
                                       Math.Pow(position.Y - _lastDragPosition.Y, 2));
                                       
                if (distance > 2)
                {
                    // We just record moves. The Down/Up state determines if it's a drag.
                    // This simplifies logic and is more robust.
                    RecordMouseMove(position);
                    _lastDragPosition = position;
                }
            }
        }
        
        private void OnPlaybackTimerTick(object? sender, EventArgs e)
        {
            if (!_isPlaying || _currentRecordingSession == null) return;
            
            var currentTime = (long)(DateTime.Now - _playbackStartTime).TotalMilliseconds;
            var actions = _currentRecordingSession.Actions;
            
            // Execute actions that should happen at this time
            while (_currentActionIndex < actions.Count && 
                   actions[_currentActionIndex].Timestamp <= currentTime)
            {
                var action = actions[_currentActionIndex];
                ExecuteAction(action);
                _currentActionIndex++;
                
                PlaybackProgressChanged?.Invoke(this, _currentActionIndex);
            }
            
            // Check if playback is complete
            if (_currentActionIndex >= actions.Count)
            {
                StopPlayback();
            }
        }
        
        private void ExecuteAction(RecordedAction action)
        {
            // Always use SendInput for movement to maintain input stream integrity
            // Convert coordinates to absolute range (0-65535)
            MoveMouse(action.Position);
            
            switch (action.ActionType)
            {
                case RecordedActionType.MouseMove:
                case RecordedActionType.LeftDrag:   // Backward compatibility
                case RecordedActionType.RightDrag:  // Backward compatibility
                case RecordedActionType.MiddleDrag: // Backward compatibility
                    // Already moved mouse above
                    break;
                    
                case RecordedActionType.LeftDown:
                    SimulateClick(MOUSEEVENTF_LEFTDOWN);
                    break;
                case RecordedActionType.LeftUp:
                    SimulateClick(MOUSEEVENTF_LEFTUP);
                    break;
                    
                case RecordedActionType.RightDown:
                    SimulateClick(MOUSEEVENTF_RIGHTDOWN);
                    break;
                case RecordedActionType.RightUp:
                    SimulateClick(MOUSEEVENTF_RIGHTUP);
                    break;
                    
                case RecordedActionType.MiddleDown:
                    SimulateClick(MOUSEEVENTF_MIDDLEDOWN);
                    break;
                case RecordedActionType.MiddleUp:
                    SimulateClick(MOUSEEVENTF_MIDDLEUP);
                    break;
                    
                // Legacy full-click support
                case RecordedActionType.LeftClick:
                    SimulateClick(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP);
                    break;
                case RecordedActionType.RightClick:
                    SimulateClick(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP);
                    break;
                case RecordedActionType.MiddleClick:
                    SimulateClick(MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_MIDDLEUP);
                    break;
                case RecordedActionType.DoubleClick:
                    SimulateClick(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP);
                    Thread.Sleep(50);
                    SimulateClick(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP);
                    break;
            }
        }
        
        private void MoveMouse(Point position)
        {
            // Use SetCursorPos with virtual screen coordinates directly. GetCursorPos/recorded positions
            // are in virtual screen space; normalizing with SM_CXSCREEN/SM_CYSCREEN (primary only) for
            // SendInput(MOUSEEVENTF_ABSOLUTE) would map secondary monitors incorrectly on multi-monitor.
            SetCursorPos((int)Math.Round(position.X), (int)Math.Round(position.Y));
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
