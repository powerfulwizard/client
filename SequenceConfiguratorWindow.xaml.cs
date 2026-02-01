using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Globalization;
using Microsoft.Win32;
using PowerfulWizard.Models;
using System.Windows.Threading;
using System.Windows.Media;
using System;

namespace PowerfulWizard
{
    public partial class SequenceConfiguratorWindow : Window
    {
        private Sequence _currentSequence;
        
        public Sequence CurrentSequence => _currentSequence;
        
        public SequenceConfiguratorWindow(Sequence? existingSequence = null)
        {
            InitializeComponent();
            
            if (existingSequence != null)
            {
                _currentSequence = existingSequence;
            }
            else
            {
                _currentSequence = new Sequence();
                // Add a default step
                _currentSequence.Steps.Add(new SequenceStep
                {
                    Description = "Step 1",
                    DelayMs = 1000,
                    ClickType = ClickType.LeftClick,
                    UseRandomPosition = false,
                    MovementSpeed = MovementSpeed.Medium,
                    CustomMovementDurationMs = 150
                });
            }
            
            DataContext = _currentSequence;
            
            // Prevent any automatic scrolling behavior
            StepsItemsControl.Focusable = false;
            
            System.Diagnostics.Debug.WriteLine("SequenceConfiguratorWindow opened successfully");
        }
        
        private void OnAddStepClick(object sender, RoutedEventArgs e)
        {
            var newStep = new SequenceStep
            {
                Description = $"Step {_currentSequence.Steps.Count + 1}",
                DelayMs = 1000,
                ClickType = ClickType.LeftClick,
                UseRandomPosition = false,
                MovementSpeed = MovementSpeed.Medium,
                CustomMovementDurationMs = 150
            };
            
            _currentSequence.Steps.Add(newStep);
        }
        
        private void OnDeleteStepClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                _currentSequence.Steps.Remove(step);
            }
        }
        
        private void OnMoveStepUpClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                var index = _currentSequence.Steps.IndexOf(step);
                if (index > 0)
                {
                    _currentSequence.Steps.Move(index, index - 1);
                }
            }
        }
        
        private void OnMoveStepDownClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                var index = _currentSequence.Steps.IndexOf(step);
                if (index < _currentSequence.Steps.Count - 1)
                {
                    _currentSequence.Steps.Move(index, index + 1);
                }
            }
        }
        
        private void OnClearAllStepsClick(object sender, RoutedEventArgs e)
        {
            _currentSequence.Steps.Clear();
            _currentSequence.Name = "New Sequence";
            
            // Add a default step
            _currentSequence.Steps.Add(new SequenceStep
            {
                Description = "Step 1",
                DelayMs = 1000,
                ClickType = ClickType.LeftClick,
                UseRandomPosition = false,
                MovementSpeed = MovementSpeed.Medium,
                CustomMovementDurationMs = 150
            });
        }
        
        private void OnSetClickAreaClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                var clickAreaWindow = new ClickAreaWindow(this);
                if (clickAreaWindow.ShowDialog() == true)
                {
                    step.ClickArea = clickAreaWindow.SelectedArea;
                    step.UseRandomPosition = true;
                }
            }
        }

        private void OnSetColorClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                var colorPickerWindow = new ColorPickerWindow(GlobalMouseTrailWindow.CurrentMouseTrailService);
                
                // Set the current color and tolerance in the window
                colorPickerWindow.SelectedColor = step.TargetColor;
                colorPickerWindow.ColorTolerance = step.ColorTolerance;
                colorPickerWindow.UpdateColorValues();
                colorPickerWindow.UpdateColorPreview();
                colorPickerWindow.ToleranceSlider.Value = step.ColorTolerance;
                
                if (colorPickerWindow.ShowDialog() == true)
                {
                    step.TargetColor = colorPickerWindow.SelectedColor;
                    step.ColorTolerance = colorPickerWindow.ColorTolerance;
                }
            }
        }
        
        private void OnSetColorAreaClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                var clickAreaWindow = new ClickAreaWindow(this);
                if (clickAreaWindow.ShowDialog() == true)
                {
                    step.ColorSearchArea = clickAreaWindow.SelectedArea;
                }
            }
        }

        private void OnUseSequenceClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentSequence.Name))
            {
                MessageBox.Show("Please enter a sequence name before using the sequence.", 
                               "Sequence Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (_currentSequence.Steps.Count == 0)
            {
                MessageBox.Show("Please add at least one step before using the sequence.", 
                               "No Steps", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            DialogResult = true;
            Close();
        }
        
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void OnSaveSequenceClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Sequence Files (*.xml)|*.xml|All Files (*.*)|*.*",
                DefaultExt = "xml",
                FileName = _currentSequence.Name + ".xml"
            };
            
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _currentSequence.SaveToFile(saveDialog.FileName);
                    MessageBox.Show("Sequence saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving sequence: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void OnLoadSequenceClick(object sender, RoutedEventArgs e)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Sequence Files (*.xml)|*.xml|All Files (*.*)|*.*",
                DefaultExt = "xml"
            };
            
            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var loadedSequence = Sequence.LoadFromFile(openDialog.FileName);
                    _currentSequence.Name = loadedSequence.Name;
                    _currentSequence.LoopMode = loadedSequence.LoopMode;
                    _currentSequence.LoopCount = loadedSequence.LoopCount;
                    _currentSequence.Steps.Clear();
                    foreach (var step in loadedSequence.Steps)
                    {
                        _currentSequence.Steps.Add(step);
                    }
                    MessageBox.Show("Sequence loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading sequence: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            }
    }
    
    // Converters
    public class ClickTypeToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ClickType clickType)
            {
                return (int)clickType;
            }
            return 0;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (ClickType)index;
            }
            return ClickType.LeftClick;
        }
    }
    
    public class LoopModeToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LoopMode loopMode)
            {
                return (int)loopMode;
            }
            return 0;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (LoopMode)index;
            }
            return LoopMode.Once;
        }
    }
    
        public class IsLoopCountVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LoopMode loopMode)
            {
                return loopMode == LoopMode.Count ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class EnumToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enum enumValue)
            {
                return System.Convert.ToInt32(enumValue);
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index && targetType == typeof(MovementSpeed))
            {
                return (MovementSpeed)index;
            }
            return MovementSpeed.Medium;
        }
    }
    
    public class IsCustomSpeedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MovementSpeed movementSpeed)
            {
                return movementSpeed == MovementSpeed.Custom;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TargetModeToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TargetMode targetMode)
            {
                return (int)targetMode;
            }
            return 0;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                return (TargetMode)index;
            }
            return TargetMode.ClickArea;
        }
    }

    public class IsColorClickModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TargetMode targetMode)
            {
                return targetMode == TargetMode.ColorClick ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsClickAreaModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TargetMode targetMode)
            {
                return targetMode == TargetMode.ClickArea ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
