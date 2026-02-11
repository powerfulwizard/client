using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PowerfulWizard.Services
{
    public class MouseTrailService
    {
        private readonly Canvas _trailCanvas;
        private readonly List<TrailPoint> _trailPoints;
        private readonly DispatcherTimer _fadeTimer;
        private readonly Random _random = Random.Shared;
        
        // Settings
        private bool _isEnabled;
        private int _trailLength;
        private int _fadeSpeed;
        private Color _trailColor;
        private bool _isRainbow;
        private int _rainbowIndex;
        
        public MouseTrailService()
        {
            _trailCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            
            _trailPoints = new List<TrailPoint>();
            // Random.Shared is already initialized above
            
            _fadeTimer = new DispatcherTimer();
            _fadeTimer.Tick += OnFadeTimerTick;
            
            // Load default settings
            LoadSettings();
        }
        
        public Canvas TrailCanvas => _trailCanvas;
        
        // Temporary disable/enable for color selection
        private bool _temporarilyDisabled = false;
        
        public void TemporarilyDisable()
        {
            _temporarilyDisabled = true;
        }
        
        public void ReEnable()
        {
            _temporarilyDisabled = false;
        }
        
        public void LoadSettings()
        {
            try
            {
                var config = System.Configuration.ConfigurationManager.AppSettings;
                _isEnabled = bool.TryParse(config["EnableMouseTrails"], out bool enableTrails) && enableTrails;
                _trailLength = int.TryParse(config["TrailLength"], out int length) ? length : 20;
                _fadeSpeed = int.TryParse(config["TrailFadeSpeed"], out int speed) ? speed : 50;
                _isRainbow = bool.TryParse(config["RainbowTrail"], out bool rainbowTrail) && rainbowTrail;
                
                try
                {
                    var colorString = config["TrailColor"];
                    if (!string.IsNullOrEmpty(colorString))
                    {
                        _trailColor = (Color)ColorConverter.ConvertFromString(colorString);
                    }
                    else
                    {
                        _trailColor = Colors.Cyan;
                    }
                }
                catch
                {
                    _trailColor = Colors.Cyan;
                }
                
                // Update timer interval
                _fadeTimer.Interval = TimeSpan.FromMilliseconds(_fadeSpeed);
            }
            catch
            {
                // Use defaults if loading fails
                _isEnabled = false;
                _trailLength = 20;
                _fadeSpeed = 50;
                _trailColor = Colors.Cyan;
                _isRainbow = false;
            }
        }
        
        public void AddTrailPoint(Point position)
        {
            if (!_isEnabled || _temporarilyDisabled) return;
            
            // Skip if position is too close to the last point (reduces noise)
            if (_trailPoints.Count > 0)
            {
                var lastPoint = _trailPoints[_trailPoints.Count - 1];
                var distance = Math.Sqrt(Math.Pow(position.X - lastPoint.Position.X, 2) + 
                                       Math.Pow(position.Y - lastPoint.Position.Y, 2));
                if (distance < 0.5) return; // Skip if movement is less than 0.5 pixels
            }
            
            // Create new trail point
            var trailPoint = new TrailPoint
            {
                Position = position,
                Opacity = 1.0,
                Element = CreateTrailElement(position)
            };
            
            _trailPoints.Add(trailPoint);
            _trailCanvas.Children.Add(trailPoint.Element);
            
            // Create connecting line if we have a previous point
            if (_trailPoints.Count > 1)
            {
                var prevPoint = _trailPoints[_trailPoints.Count - 2];
                var line = CreateConnectingLine(prevPoint.Position, position);
                if (line != null)
                {
                    trailPoint.ConnectingLine = line;
                    _trailCanvas.Children.Add(line);
                }
            }
            
            // Remove oldest point if we exceed trail length
            if (_trailPoints.Count > _trailLength)
            {
                var oldestPoint = _trailPoints[0];
                _trailCanvas.Children.Remove(oldestPoint.Element);
                if (oldestPoint.ConnectingLine != null)
                {
                    _trailCanvas.Children.Remove(oldestPoint.ConnectingLine);
                }
                _trailPoints.RemoveAt(0);
            }
            
            // Start fade timer if not already running
            if (!_fadeTimer.IsEnabled)
            {
                _fadeTimer.Start();
            }
        }
        
        private UIElement CreateTrailElement(Point position)
        {
            var color = _isRainbow ? GetRainbowColor() : _trailColor;
            
            // Create a small circle
            var ellipse = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(color),
                Opacity = 1.0
            };
            
            // Position is window-relative (0-based); parent window covers virtual screen
            Canvas.SetLeft(ellipse, position.X - ellipse.Width / 2);
            Canvas.SetTop(ellipse, position.Y - ellipse.Height / 2);
            
            return ellipse;
        }
        
        private UIElement CreateConnectingLine(Point start, Point end)
        {
            var color = _isRainbow ? GetRainbowColor() : _trailColor;
            
            // Create a simple smooth line that follows the mouse movement
            var line = new System.Windows.Shapes.Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                Opacity = 1.0
            };
            
            return line;
        }
        
        private Color GetRainbowColor()
        {
            // Create rainbow effect by cycling through hues
            _rainbowIndex = (_rainbowIndex + 5) % 360; // Increment by 5 degrees each time
            
            // Convert HSV to RGB (Hue=_rainbowIndex, Saturation=1, Value=1)
            return HsvToRgb(_rainbowIndex, 1.0, 1.0);
        }
        
        private Color HsvToRgb(double h, double s, double v)
        {
            var hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            var f = h / 60 - Math.Floor(h / 60);

            var value = Convert.ToInt32(255 * v);
            var p = Convert.ToInt32(255 * v * (1 - s));
            var q = Convert.ToInt32(255 * v * (1 - f * s));
            var t = Convert.ToInt32(255 * v * (1 - (1 - f) * s));

            switch (hi)
            {
                case 0: return Color.FromRgb((byte)value, (byte)t, (byte)p);
                case 1: return Color.FromRgb((byte)q, (byte)value, (byte)p);
                case 2: return Color.FromRgb((byte)p, (byte)value, (byte)t);
                case 3: return Color.FromRgb((byte)p, (byte)q, (byte)value);
                case 4: return Color.FromRgb((byte)t, (byte)p, (byte)value);
                default: return Color.FromRgb((byte)value, (byte)p, (byte)q);
            }
        }
        
        private void OnFadeTimerTick(object? sender, EventArgs e)
        {
            bool hasVisiblePoints = false;
            
            for (int i = _trailPoints.Count - 1; i >= 0; i--)
            {
                var point = _trailPoints[i];
                point.Opacity -= 0.1; // Fade out gradually
                
                if (point.Opacity <= 0)
                {
                    // Remove completely faded points
                    _trailCanvas.Children.Remove(point.Element);
                    if (point.ConnectingLine != null)
                    {
                        _trailCanvas.Children.Remove(point.ConnectingLine);
                    }
                    _trailPoints.RemoveAt(i);
                }
                else
                {
                    // Update opacity for both element and connecting line
                    point.Element.Opacity = point.Opacity;
                    if (point.ConnectingLine != null)
                    {
                        point.ConnectingLine.Opacity = point.Opacity;
                    }
                    hasVisiblePoints = true;
                }
            }
            
            // Stop timer if no more visible points
            if (!hasVisiblePoints)
            {
                _fadeTimer.Stop();
            }
        }
        
        public void ClearTrail()
        {
            _trailPoints.Clear();
            _trailCanvas.Children.Clear();
            _fadeTimer.Stop();
        }
        
        public void CreateBurstEffect(Point position)
        {
            if (!_isEnabled) return;
            
            var color = _isRainbow ? GetRainbowColor() : _trailColor;
            
            // Create one simple fading circle
            var circle = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = new SolidColorBrush(color),
                Opacity = 0.8
            };
            
            Canvas.SetLeft(circle, position.X - 9);
            Canvas.SetTop(circle, position.Y - 9);
            _trailCanvas.Children.Add(circle);
            
            // Simple fade animation
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            
            timer.Tick += (s, e) =>
            {
                circle.Opacity -= 0.1;
                if (circle.Opacity <= 0)
                {
                    _trailCanvas.Children.Remove(circle);
                    timer.Stop();
                }
            };
            
            timer.Start();
        }
        
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled)
            {
                ClearTrail();
            }
        }
        
        private class TrailPoint
        {
            public Point Position { get; set; }
            public double Opacity { get; set; }
            public UIElement Element { get; set; } = null!;
            public UIElement? ConnectingLine { get; set; }
        }
    }
}
