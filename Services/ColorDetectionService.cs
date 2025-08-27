using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenCvSharp;
using System.Drawing;
using System.Drawing.Imaging;

namespace PowerfulWizard.Services
{
    public class ColorDetectionService
    {
        #region Win32 API Imports

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        #endregion

        #region Structures
        
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        #region OpenCV Color Detection

        /// <summary>
        /// OpenCV-based color detection - much more reliable than GetPixel
        /// </summary>
        public static System.Windows.Point? FindMatchingColors(System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            System.Diagnostics.Debug.WriteLine($"=== OPENCV COLOR DETECTION ===");
            System.Diagnostics.Debug.WriteLine($"Target: R={targetColor.R}, G={targetColor.G}, B={targetColor.B}, Tolerance={tolerance}");
            System.Diagnostics.Debug.WriteLine($"Area: {searchArea.Width}x{searchArea.Height} at ({searchArea.Left}, {searchArea.Top})");
            
            try
            {
                // Capture the screen area
                using (var bitmap = CaptureScreenArea(searchArea))
                {
                    if (bitmap == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to capture screen area");
                        return null;
                    }

                    System.Diagnostics.Debug.WriteLine($"Captured bitmap: {bitmap.Width}x{bitmap.Height}");
                    
                    // Convert to OpenCV Mat
                    using (var mat = BitmapToMat(bitmap))
                    {
                        // Convert BGR to RGB (OpenCV uses BGR by default)
                        using (var rgbMat = mat.CvtColor(ColorConversionCodes.BGR2RGB))
                        {
                            // Find matching colors using OpenCV
                            var matchingPoints = FindColorsWithOpenCV(rgbMat, targetColor, tolerance, searchArea);
                            
                            if (matchingPoints.Count > 0)
                            {
                                var random = new Random();
                                var selectedPoint = matchingPoints[random.Next(matchingPoints.Count)];
                                System.Diagnostics.Debug.WriteLine($"OpenCV found {matchingPoints.Count} matches, selected: ({selectedPoint.X}, {selectedPoint.Y})");
                                return selectedPoint;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("OpenCV found no matches");
                                return null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenCV color detection error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Captures a specific area of the screen
        /// </summary>
        private static Bitmap CaptureScreenArea(System.Windows.Rect area)
        {
            try
            {
                int width = (int)area.Width;
                int height = (int)area.Height;
                
                var bitmap = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen((int)area.Left, (int)area.Top, 0, 0, new System.Drawing.Size(width, height));
                }
                
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Screen capture error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts Bitmap to OpenCV Mat
        /// </summary>
        private static Mat BitmapToMat(Bitmap bitmap)
        {
                            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            
            try
            {
                var mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
                return mat.Clone(); // Clone to avoid memory issues
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>
        /// Uses OpenCV to find matching colors with multiple methods
        /// </summary>
        private static List<System.Windows.Point> FindColorsWithOpenCV(Mat image, System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            var matchingPoints = new List<System.Windows.Point>();
            
            try
            {
                // Method 1: Range-based color detection
                var lowerBound = new Scalar(
                    Math.Max(0, targetColor.R - tolerance),
                    Math.Max(0, targetColor.G - tolerance),
                    Math.Max(0, targetColor.B - tolerance)
                );
                
                var upperBound = new Scalar(
                    Math.Min(255, targetColor.R + tolerance),
                    Math.Min(255, targetColor.G + tolerance),
                    Math.Min(255, targetColor.B + tolerance)
                );
                
                using (var mask = image.InRange(lowerBound, upperBound))
                {
                    var contours = mask.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    
                    foreach (var contour in contours)
                    {
                        if (contour.Length > 0)
                        {
                            // Get center point of contour
                            var moments = Cv2.Moments(contour);
                            if (moments.M00 != 0)
                            {
                                int centerX = (int)(moments.M10 / moments.M00);
                                int centerY = (int)(moments.M01 / moments.M00);
                                
                                var screenPoint = new System.Windows.Point(
                                    searchArea.Left + centerX,
                                    searchArea.Top + centerY
                                );
                                
                                matchingPoints.Add(screenPoint);
                                
                                if (matchingPoints.Count <= 5)
                                {
                                    System.Diagnostics.Debug.WriteLine($"OpenCV contour match at ({screenPoint.X}, {screenPoint.Y})");
                                }
                            }
                        }
                    }
                }
                
                // Method 2: Template matching with color similarity
                if (matchingPoints.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Trying OpenCV template matching method");
                    matchingPoints.AddRange(FindColorsWithTemplateMatching(image, targetColor, tolerance, searchArea));
                }
                
                // Method 3: Flood fill approach
                if (matchingPoints.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Trying OpenCV flood fill method");
                    matchingPoints.AddRange(FindColorsWithFloodFill(image, targetColor, tolerance, searchArea));
                }
                
                System.Diagnostics.Debug.WriteLine($"OpenCV total matches: {matchingPoints.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenCV processing error: {ex.Message}");
            }
            
            return matchingPoints;
        }

        /// <summary>
        /// Template matching approach for color detection
        /// </summary>
        private static List<System.Windows.Point> FindColorsWithTemplateMatching(Mat image, System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            var matches = new List<System.Windows.Point>();
            
            try
            {
                // Create a small template of the target color
                int templateSize = 5;
                using (var template = new Mat(templateSize, templateSize, MatType.CV_8UC3, new Scalar(targetColor.B, targetColor.G, targetColor.R)))
                {
                    using (var result = new Mat())
                    {
                        Cv2.MatchTemplate(image, template, result, TemplateMatchModes.CCoeffNormed);
                        
                        double minVal, maxVal;
                        OpenCvSharp.Point minLoc, maxLoc;
                        Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);
                        
                        // If we have a good match
                        if (maxVal > 0.7)
                        {
                            var screenPoint = new System.Windows.Point(
                                searchArea.Left + maxLoc.X + templateSize / 2,
                                searchArea.Top + maxLoc.Y + templateSize / 2
                            );
                            matches.Add(screenPoint);
                            System.Diagnostics.Debug.WriteLine($"Template match at ({screenPoint.X}, {screenPoint.Y}) with confidence {maxVal:F3}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Template matching error: {ex.Message}");
            }
            
            return matches;
        }

        /// <summary>
        /// Flood fill approach for color detection
        /// </summary>
        private static List<System.Windows.Point> FindColorsWithFloodFill(Mat image, System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            var matches = new List<System.Windows.Point>();
            
            try
            {
                // Try flood fill from multiple starting points
                int step = Math.Max(10, Math.Min((int)searchArea.Width / 10, (int)searchArea.Height / 10));
                
                for (int x = step; x < image.Width; x += step)
                {
                    for (int y = step; y < image.Height; y += step)
                    {
                        var pixel = image.Get<Vec3b>(y, x);
                        var pixelColor = System.Windows.Media.Color.FromRgb(pixel.Item2, pixel.Item1, pixel.Item0);
                        
                        if (IsColorSimilar(pixelColor, targetColor, tolerance))
                        {
                            using (var mask = new Mat())
                            {
                                var seedPoint = new OpenCvSharp.Point(x, y);
                                var newVal = new Scalar(255, 255, 255);
                                var lowerDiff = new Scalar(tolerance, tolerance, tolerance);
                                var upperDiff = new Scalar(tolerance, tolerance, tolerance);
                                
                                int area = Cv2.FloodFill(image, mask, seedPoint, newVal, out var rect, lowerDiff, upperDiff, FloodFillFlags.FixedRange);
                                
                                if (area > 10) // Minimum area threshold
                                {
                                    var screenPoint = new System.Windows.Point(
                                        searchArea.Left + x,
                                        searchArea.Top + y
                                    );
                                    matches.Add(screenPoint);
                                    
                                    if (matches.Count <= 3)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Flood fill match at ({screenPoint.X}, {screenPoint.Y}) with area {area}");
                                    }
                                    
                                    if (matches.Count >= 10) break;
                                }
                            }
                        }
                    }
                    if (matches.Count >= 10) break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Flood fill error: {ex.Message}");
            }
            
            return matches;
        }

        /// <summary>
        /// Checks if two colors are similar within tolerance
        /// </summary>
        private static bool IsColorSimilar(System.Windows.Media.Color color1, System.Windows.Media.Color color2, int tolerance)
        {
            int rDiff = Math.Abs(color1.R - color2.R);
            int gDiff = Math.Abs(color1.G - color2.G);
            int bDiff = Math.Abs(color1.B - color2.B);
            
            return rDiff <= tolerance && gDiff <= tolerance && bDiff <= tolerance;
        }

        #endregion

        #region Legacy Methods (for compatibility)

        public static async Task<System.Windows.Point?> FindMatchingColorsAsync(System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            return await Task.Run(() => FindMatchingColors(targetColor, tolerance, searchArea));
        }

        public static System.Windows.Point? GetRandomMatchingPoint(System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            return FindMatchingColors(targetColor, tolerance, searchArea);
        }

        public static async Task<System.Windows.Point?> GetRandomMatchingPointAsync(System.Windows.Media.Color targetColor, int tolerance, System.Windows.Rect searchArea)
        {
            return await Task.Run(() => FindMatchingColors(targetColor, tolerance, searchArea));
        }

        public static System.Windows.Media.Color GetPixelAt(int x, int y)
        {
            IntPtr desktopWindow = GetDesktopWindow();
            IntPtr hdc = GetDC(desktopWindow);
            if (hdc == IntPtr.Zero) return Colors.Black;

            try
            {
                uint pixel = GetPixel(hdc, x, y);
                return System.Windows.Media.Color.FromRgb(
                    (byte)(pixel & 0xFF),
                    (byte)((pixel >> 8) & 0xFF),
                    (byte)((pixel >> 16) & 0xFF)
                );
            }
            finally
            {
                ReleaseDC(desktopWindow, hdc);
            }
        }

        public static System.Windows.Media.Color GetColorAtCurrentPosition()
        {
            if (GetCursorPosition(out var point))
            {
                return GetPixelAt(point.X, point.Y);
            }
            return Colors.Black;
        }

        public static bool GetCursorPosition(out POINT point)
        {
            return GetCursorPos(out point);
        }

        /// <summary>
        /// Debug method to sample colors in an area
        /// </summary>
        public static void DebugColorDetection(System.Windows.Media.Color targetColor, System.Windows.Rect searchArea, int tolerance = 10)
        {
            System.Diagnostics.Debug.WriteLine($"=== COLOR DETECTION DEBUG ===");
            System.Diagnostics.Debug.WriteLine($"Target Color: R={targetColor.R}, G={targetColor.G}, B={targetColor.B}");
            System.Diagnostics.Debug.WriteLine($"Search Area: {searchArea.Width}x{searchArea.Height} at ({searchArea.Left}, {searchArea.Top})");
            System.Diagnostics.Debug.WriteLine($"Tolerance: {tolerance}");
            
            try
            {
                using (var bitmap = CaptureScreenArea(searchArea))
                {
                    if (bitmap != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Debug: Captured {bitmap.Width}x{bitmap.Height} bitmap");
                        
                        // Sample a few pixels
                        int sampleCount = 0;
                        for (int y = 0; y < bitmap.Height && sampleCount < 20; y += 10)
                        {
                            for (int x = 0; x < bitmap.Width && sampleCount < 20; x += 10)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                var wpfColor = System.Windows.Media.Color.FromRgb(pixel.R, pixel.G, pixel.B);
                                
                                int rDiff = Math.Abs(wpfColor.R - targetColor.R);
                                int gDiff = Math.Abs(wpfColor.G - targetColor.G);
                                int bDiff = Math.Abs(wpfColor.B - targetColor.B);
                                double distance = Math.Sqrt(rDiff * rDiff + gDiff * gDiff + bDiff * bDiff);
                                
                                System.Diagnostics.Debug.WriteLine($"Debug Sample at ({x}, {y}): R={wpfColor.R}, G={wpfColor.G}, B={wpfColor.B} - Diffs: R={rDiff}, G={gDiff}, B={bDiff}, Distance={distance:F1}");
                                sampleCount++;
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Debug: Failed to capture bitmap");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug Error: {ex.Message}");
            }
            
            System.Diagnostics.Debug.WriteLine($"=== END DEBUG ===");
        }

        #endregion
    }
}
