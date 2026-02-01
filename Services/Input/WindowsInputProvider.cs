using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using PowerfulWizard.Models;

namespace PowerfulWizard.Services.Input
{
    public class WindowsInputProvider : IInputProvider
    {
        #region Win32 Imports

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

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

        private const int INPUT_MOUSE = 0;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const int MOUSEEVENTF_MIDDLEUP = 0x0040;

        #endregion

        public System.Drawing.Point GetCursorPosition()
        {
            GetCursorPos(out POINT p);
            return new System.Drawing.Point(p.X, p.Y);
        }

        public void SetCursorPosition(int x, int y)
        {
            SetCursorPos(x, y);
        }

        public void SendClick(ClickType clickType)
        {
            switch (clickType)
            {
                case ClickType.LeftClick:
                    SendMouseInput(MOUSEEVENTF_LEFTDOWN);
                    SendMouseInput(MOUSEEVENTF_LEFTUP);
                    break;
                case ClickType.RightClick:
                    SendMouseInput(MOUSEEVENTF_RIGHTDOWN);
                    SendMouseInput(MOUSEEVENTF_RIGHTUP);
                    break;
                case ClickType.MiddleClick:
                    SendMouseInput(MOUSEEVENTF_MIDDLEDOWN);
                    SendMouseInput(MOUSEEVENTF_MIDDLEUP);
                    break;
                case ClickType.DoubleClick:
                    SendMouseInput(MOUSEEVENTF_LEFTDOWN);
                    SendMouseInput(MOUSEEVENTF_LEFTUP);
                    Thread.Sleep(50);
                    SendMouseInput(MOUSEEVENTF_LEFTDOWN);
                    SendMouseInput(MOUSEEVENTF_LEFTUP);
                    break;
            }
        }

        private void SendMouseInput(uint flags)
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = flags;
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }

        public Bitmap CaptureScreen(Rect area)
        {
            try
            {
                int width = (int)area.Width;
                int height = (int)area.Height;
                
                if (width <= 0 || height <= 0) return null!;

                var bitmap = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen((int)area.Left, (int)area.Top, 0, 0, new System.Drawing.Size(width, height));
                }
                
                return bitmap;
            }
            catch
            {
                return null!;
            }
        }

        public Color GetPixelColor(int x, int y)
        {
            IntPtr desktopWindow = GetDesktopWindow();
            IntPtr hdc = GetDC(desktopWindow);
            try
            {
                uint pixel = GetPixel(hdc, x, y);
                return Color.FromArgb(
                    (int)(pixel & 0xFF),
                    (int)((pixel >> 8) & 0xFF),
                    (int)((pixel >> 16) & 0xFF)
                );
            }
            finally
            {
                ReleaseDC(desktopWindow, hdc);
            }
        }

        public void Sleep(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }
    }
}
