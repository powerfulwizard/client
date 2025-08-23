using System.Windows;

namespace PowerfulWizard.Models
{
    public enum RecordedActionType
    {
        MouseMove,
        LeftClick,
        RightClick,
        MiddleClick,
        DoubleClick,
        LeftDrag,
        RightDrag,
        MiddleDrag
    }

    public class RecordedAction
    {
        public RecordedActionType ActionType { get; set; }
        public Point Position { get; set; }
        public long Timestamp { get; set; } // Milliseconds since recording start
        public int Duration { get; set; } // Duration in milliseconds for this action
    }

    public class MouseRecording
    {
        public string Name { get; set; } = "New Recording";
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public List<RecordedAction> Actions { get; set; } = new List<RecordedAction>();
        public int TotalDuration { get; set; } // Total duration in milliseconds
        
        public void AddAction(RecordedAction action)
        {
            Actions.Add(action);
            TotalDuration = (int)(Actions.Last().Timestamp + action.Duration);
        }
        
        public void Clear()
        {
            Actions.Clear();
            TotalDuration = 0;
        }
    }
}
