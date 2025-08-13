using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using PowerfulWizard.Models;
using System.Globalization;

namespace PowerfulWizard
{
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

    public partial class SequenceConfiguratorWindow : Window
    {
        private Sequence _currentSequence;

        public Sequence CurrentSequence => _currentSequence;

        public SequenceConfiguratorWindow(Sequence existingSequence = null)
        {
            InitializeComponent();
            
            // Debug: Show what we received
            string debugInfo = existingSequence == null ? "null" : $"'{existingSequence.Name}' with {existingSequence.Steps.Count} steps";
            System.Diagnostics.Debug.WriteLine($"SequenceConfiguratorWindow received: {debugInfo}");
            
            if (existingSequence != null && existingSequence.Steps.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"  First step: {existingSequence.Steps[0].Description}");
            }
            
            if (existingSequence != null)
            {
                _currentSequence = existingSequence;
                System.Diagnostics.Debug.WriteLine($"Using existing sequence: {_currentSequence.Steps.Count} steps");
            }
            else
            {
                _currentSequence = new Sequence();
                // Add a default step only for new sequences
                _currentSequence.Steps.Add(new SequenceStep
                {
                    Description = "Step 1",
                    DelayMs = 1000,
                    DeviationMs = 100,
                    MovementDurationMs = 150,
                    ClickType = ClickType.LeftClick,
                    UseRandomPosition = false,
                    ClickArea = new Rect(0, 0, 100, 100)
                });
                System.Diagnostics.Debug.WriteLine("Created new sequence with default step");
            }
            
            DataContext = _currentSequence;
            
            // Set up loop mode change handler
            LoopModeComboBox.SelectionChanged += OnLoopModeChanged;
            
            // Initialize loop mode based on existing sequence or default
            if (existingSequence != null)
            {
                LoopModeComboBox.SelectedIndex = (int)existingSequence.LoopMode;
                OnLoopModeChanged(null, null); // Update UI state
            }
            else
            {
                LoopModeComboBox.SelectedIndex = 0;
            }
        }

        private void OnLoopModeChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update the sequence's LoopMode property
            if (LoopModeComboBox.SelectedIndex >= 0)
            {
                _currentSequence.LoopMode = (LoopMode)LoopModeComboBox.SelectedIndex;
            }
            
            // Show/hide loop count grid
            if (LoopModeComboBox.SelectedIndex == 2) // Loop Count
            {
                LoopCountGrid.Visibility = Visibility.Visible;
            }
            else
            {
                LoopCountGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow numbers
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void OnAddStepClick(object sender, RoutedEventArgs e)
        {
            var newStep = new SequenceStep
            {
                Description = $"Step {_currentSequence.Steps.Count + 1}",
                DelayMs = 1000,
                DeviationMs = 100,
                MovementDurationMs = 150,
                ClickType = ClickType.LeftClick,
                UseRandomPosition = false,
                ClickArea = new Rect(0, 0, 100, 100)
            };
            
            _currentSequence.Steps.Add(newStep);
        }

        private void OnClearAllStepsClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to clear the entire sequence?\nThis will reset the name, loop settings, and all steps.", 
                                       "Clear Sequence", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                // Reset the sequence to defaults
                _currentSequence.Steps.Clear();
                _currentSequence.Name = "New Sequence";
                _currentSequence.LoopMode = LoopMode.Once;
                _currentSequence.LoopCount = 1;
                
                // Update the UI controls
                LoopModeComboBox.SelectedIndex = 0; // "Run Once"
                OnLoopModeChanged(null, null); // Update the loop count visibility
                
                // Add a default step
                _currentSequence.Steps.Add(new SequenceStep
                {
                    Description = "Step 1",
                    DelayMs = 1000,
                    DeviationMs = 100,
                    MovementDurationMs = 150,
                    ClickType = ClickType.LeftClick,
                    UseRandomPosition = false,
                    ClickArea = new Rect(0, 0, 100, 100)
                });
            }
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

        private void OnSetClickAreaClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SequenceStep step)
            {
                var clickAreaWindow = new ClickAreaWindow(this);
                if (clickAreaWindow.ShowDialog() == true)
                {
                    step.ClickArea = clickAreaWindow.SelectedArea;
                    step.UseRandomPosition = true; // Enable random position when area is set
                }
            }
        }

        private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock dragHandle)
            {
                var step = dragHandle.DataContext as SequenceStep;
                if (step != null)
                {
                    DragDrop.DoDragDrop(StepsListView, step, DragDropEffects.Move);
                }
            }
        }

        private void OnStepDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(SequenceStep)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnStepDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(SequenceStep)))
            {
                var draggedStep = e.Data.GetData(typeof(SequenceStep)) as SequenceStep;
                var targetListView = sender as ListView;
                
                if (draggedStep != null && targetListView != null)
                {
                    var dropPosition = e.GetPosition(targetListView);
                    var targetItem = GetItemAtPosition(targetListView, dropPosition);
                    
                    if (targetItem != null)
                    {
                        var targetStep = targetItem.DataContext as SequenceStep;
                        if (targetStep != null && targetStep != draggedStep)
                        {
                            var oldIndex = _currentSequence.Steps.IndexOf(draggedStep);
                            var newIndex = _currentSequence.Steps.IndexOf(targetStep);
                            
                            _currentSequence.Steps.Move(oldIndex, newIndex);
                        }
                    }
                }
            }
            e.Handled = true;
        }

        private ListViewItem GetItemAtPosition(ListView listView, Point position)
        {
            var result = VisualTreeHelper.HitTest(listView, position);
            if (result != null)
            {
                var element = result.VisualHit;
                while (element != null && !(element is ListViewItem))
                {
                    element = VisualTreeHelper.GetParent(element);
                }
                return element as ListViewItem;
            }
            return null;
        }

        private void OnTestSequenceClick(object sender, RoutedEventArgs e)
        {
            if (_currentSequence.Steps.Count == 0)
            {
                MessageBox.Show("Please add at least one step to test the sequence.", 
                               "No Steps", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var testWindow = new SequenceTestWindow(_currentSequence);
            testWindow.ShowDialog();
        }

        private void OnSaveSequenceClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentSequence.Name))
            {
                MessageBox.Show("Please enter a sequence name before saving.", 
                               "Sequence Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentSequence.Steps.Count == 0)
            {
                MessageBox.Show("Please add at least one step before saving.", 
                               "No Steps", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Sequence Files (*.xml)|*.xml|All Files (*.*)|*.*",
                DefaultExt = "xml",
                FileName = _currentSequence.Name.Replace(" ", "_") + ".xml"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    SaveSequenceToFile(_currentSequence, saveDialog.FileName);
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
                    var loadedSequence = LoadSequenceFromFile(openDialog.FileName);
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

        private void SaveSequenceToFile(Sequence sequence, string filePath)
        {
            var serializer = new XmlSerializer(typeof(Sequence));
            using (var writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, sequence);
            }
        }

        private Sequence LoadSequenceFromFile(string filePath)
        {
            var serializer = new XmlSerializer(typeof(Sequence));
            using (var reader = new StreamReader(filePath))
            {
                return (Sequence)serializer.Deserialize(reader);
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
        



    }
}
