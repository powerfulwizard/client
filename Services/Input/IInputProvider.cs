using System.Drawing;
using System.Windows;
using PowerfulWizard.Models;

namespace PowerfulWizard.Services.Input
{
    public interface IInputProvider
    {
        // Mouse
        System.Drawing.Point GetCursorPosition();
        void SetCursorPosition(int x, int y);
        void SendClick(ClickType clickType);
        
        // Screen
        Bitmap CaptureScreen(Rect area);
        Color GetPixelColor(int x, int y);
        
        // Utilities
        void Sleep(int milliseconds);
    }
}
