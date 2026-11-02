using System;
using System.Runtime.InteropServices;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Where the game's own image sits in a capture of its window. Windows.Graphics.Capture captures a window
    /// as Windows draws it, so a game in a window comes with its title bar and borders: the loading screen's
    /// cards then sit lower, and smaller, than the reader expects from the capture's height, and every card
    /// read Unranked (75 of 3,191 games, 2026-10-23 to 11-02 - 1280x1032, 2560x1400, 1832x1033 windows and
    /// the like). Fullscreen and borderless-fullscreen games have nothing around them, so nothing changes.
    /// </summary>
    internal static class ClientArea
    {
        /// <summary>A rectangle in screen pixels, right and bottom exclusive (Win32's RECT).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
        {
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        /// <summary>
        /// The client area's place in a <paramref name="frameWidth"/> x <paramref name="frameHeight"/> capture
        /// of <paramref name="captured"/>, or null when the capture isn't that rectangle (its size doesn't
        /// match - a different DPI scale, say) or the client area doesn't fit inside it. When nothing is around
        /// the client area it's the whole frame.
        /// </summary>
        public static Rect? Within(int frameWidth, int frameHeight, Rect captured, Rect client)
        {
            if (captured.Width != frameWidth || captured.Height != frameHeight || client.Width <= 0 || client.Height <= 0) {
                return null;
            }
            var area = new Rect(client.Left - captured.Left, client.Top - captured.Top, client.Right - captured.Left, client.Bottom - captured.Top);
            if (area.Left < 0 || area.Top < 0 || area.Right > frameWidth || area.Bottom > frameHeight) {
                return null;
            }
            // A title bar and borders are a few percent of the window; anything bigger isn't them.
            if (area.Width < frameWidth * 0.8 || area.Height < frameHeight * 0.8) {
                return null;
            }
            return area;
        }

        /// <summary>
        /// The client area of <paramref name="window"/> in a capture of it, or null when it can't be told. The
        /// capture is the window's visible frame (DWM's extended frame bounds, without the invisible resize
        /// borders), or on some Windows versions the whole window rectangle - whichever matches the frame's size.
        /// </summary>
        public static Rect? Of(IntPtr window, int frameWidth, int frameHeight)
        {
            if (!GetClientRect(window, out var client)) {
                return null;
            }
            var origin = new Point();
            if (!ClientToScreen(window, ref origin)) {
                return null;
            }
            var onScreen = new Rect(origin.X, origin.Y, origin.X + client.Width, origin.Y + client.Height);
            if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out var frame, Marshal.SizeOf<Rect>()) == 0
                && Within(frameWidth, frameHeight, frame, onScreen) is Rect inFrame) {
                return inFrame;
            }
            return GetWindowRect(window, out var whole) ? Within(frameWidth, frameHeight, whole, onScreen) : null;
        }

        private const int ExtendedFrameBounds = 9; // DWMWA_EXTENDED_FRAME_BOUNDS

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X, Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    }
}
