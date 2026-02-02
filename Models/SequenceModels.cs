using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.IO;
using System.Windows.Media;

namespace PowerfulWizard.Models
{
    public enum ClickType
    {
        LeftClick,
        RightClick,
        MiddleClick,
        DoubleClick
    }

    public enum TargetMode
    {
        ClickArea,
        ColorClick,
        MousePosition
    }

    public enum LoopMode
    {
        Once,
        Forever,
        Count
    }

    public enum MovementSpeed
    {
        Fast,      // ~80-120ms
        Medium,    // ~120-200ms  
        Slow,      // ~200-300ms
        Custom     // User-defined
    }

    public class SequenceStep : INotifyPropertyChanged
    {
        private ClickType _clickType;
        private int _delayMs;
        private int _deviationMs;
        private MovementSpeed _movementSpeed;
        private int _customMovementDurationMs;
        private Rect _clickArea;
        private string _description = string.Empty;
        private TargetMode _targetMode;
        private Color _targetColor;
        private int _colorTolerance;
        private Rect _colorSearchArea;

        public ClickType ClickType
        {
            get => _clickType;
            set => SetProperty(ref _clickType, value);
        }

        public int DelayMs
        {
            get => _delayMs;
            set => SetProperty(ref _delayMs, value);
        }

        public int DeviationMs
        {
            get => _deviationMs;
            set => SetProperty(ref _deviationMs, value);
        }

        public MovementSpeed MovementSpeed
        {
            get => _movementSpeed;
            set => SetProperty(ref _movementSpeed, value);
        }

        public int CustomMovementDurationMs
        {
            get => _customMovementDurationMs;
            set => SetProperty(ref _customMovementDurationMs, value);
        }

        public Rect ClickArea
        {
            get => _clickArea;
            set => SetProperty(ref _clickArea, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public TargetMode TargetMode
        {
            get => _targetMode;
            set => SetProperty(ref _targetMode, value);
        }

        public Color TargetColor
        {
            get => _targetColor;
            set => SetProperty(ref _targetColor, value);
        }

        public int ColorTolerance
        {
            get => _colorTolerance;
            set => SetProperty(ref _colorTolerance, value);
        }

        public Rect ColorSearchArea
        {
            get => _colorSearchArea;
            set => SetProperty(ref _colorSearchArea, value);
        }

        public SequenceStep()
        {
            ClickType = ClickType.LeftClick;
            DelayMs = 1000;
            DeviationMs = 100;
            MovementSpeed = MovementSpeed.Medium;
            CustomMovementDurationMs = 150;
            ClickArea = new Rect(0, 0, 100, 100);
            Description = "New Step";
            TargetMode = TargetMode.ClickArea;
            TargetColor = Colors.Red;
            ColorTolerance = 30;
            ColorSearchArea = new Rect(0, 0, 100, 100);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class Sequence : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private ObservableCollection<SequenceStep> _steps = null!;
        private LoopMode _loopMode;
        private int _loopCount;
        private bool _isRunning;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public ObservableCollection<SequenceStep> Steps
        {
            get => _steps;
            set => SetProperty(ref _steps, value);
        }

        public LoopMode LoopMode
        {
            get => _loopMode;
            set => SetProperty(ref _loopMode, value);
        }

        public int LoopCount
        {
            get => _loopCount;
            set => SetProperty(ref _loopCount, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public Sequence()
        {
            Name = "New Sequence";
            Steps = new ObservableCollection<SequenceStep>();
            LoopMode = LoopMode.Once;
            LoopCount = 1;
            IsRunning = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        
        public void SaveToFile(string filePath)
        {
            var serializer = new XmlSerializer(typeof(Sequence));
            using var writer = new StreamWriter(filePath);
            serializer.Serialize(writer, this);
        }
        
        public static Sequence LoadFromFile(string filePath)
        {
            var serializer = new XmlSerializer(typeof(Sequence));
            using var reader = new StreamReader(filePath);
            var result = serializer.Deserialize(reader) as Sequence;
            if (result == null)
            {
                throw new InvalidOperationException("Failed to deserialize sequence from file.");
            }
            return result;
        }
    }
}
