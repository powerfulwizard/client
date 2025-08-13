using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PowerfulWizard.Models
{
    public enum LoopMode
    {
        Once,
        Forever,
        Count
    }

    public enum ClickType
    {
        LeftClick,
        RightClick,
        MiddleClick,
        DoubleClick
    }

    public class SequenceStep : INotifyPropertyChanged
    {
        private ClickType _clickType;
        private int _delayMs;
        private int _deviationMs;
        private int _movementDurationMs;
        private bool _useRandomPosition;
        private Rect _clickArea;
        private string _description;

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

        public int MovementDurationMs
        {
            get => _movementDurationMs;
            set => SetProperty(ref _movementDurationMs, value);
        }

        public bool UseRandomPosition
        {
            get => _useRandomPosition;
            set => SetProperty(ref _useRandomPosition, value);
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

        public SequenceStep()
        {
            ClickType = ClickType.LeftClick;
            DelayMs = 1000;
            DeviationMs = 100;
            MovementDurationMs = 150; // Default movement duration
            UseRandomPosition = false;
            ClickArea = new Rect(0, 0, 100, 100);
            Description = "New Step";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class Sequence : INotifyPropertyChanged
    {
        private string _name;
        private ObservableCollection<SequenceStep> _steps;
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
