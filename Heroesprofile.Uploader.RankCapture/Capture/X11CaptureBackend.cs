using NLog;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Capture on Linux. The game runs under Wine (or Proton, Lutris...), which gives it an X11 window - on a
    /// Wayland desktop too, through XWayland - so this finds that window by its title and reads its pixels
    /// with Xlib's XGetImage, once per interval. Only that window, only in memory. A window covered by another
    /// one reads what's on top where it's covered, unless the desktop composites; the loading screen is
    /// normally in front.
    /// </summary>
    internal sealed unsafe class X11CaptureBackend : ICaptureBackend
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // The game's window title, in every language.
        private const string GameTitle = "Heroes of the Storm";

        private readonly Lazy<string> _unsupported = new Lazy<string>(CheckSupport);

        public string Unsupported => _unsupported.Value;

        public Task PrepareAsync(bool showOutline) => Task.CompletedTask;

        public IDisposable Start(TimeSpan interval, bool showOutline, Action<RawFrame> onFrame)
        {
            var session = new Session();
            var polling = new PollingCapture(interval, session.Grab, onFrame,
                ex => _log.Debug($"Rank reading: couldn't capture a frame: {ex.Message}"));
            return new Both(polling, session);
        }

        private static string CheckSupport()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) {
                return "there's no X11 display (DISPLAY isn't set) - the game's window can't be read";
            }
            try {
                var display = X.XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) {
                    return "the X11 display can't be opened";
                }
                X.XCloseDisplay(display);
                return null;
            }
            catch (DllNotFoundException) {
                return "libX11 isn't installed";
            }
        }

        /// <summary>One game's capture: its own display connection, and the game's window once found.</summary>
        private sealed class Session : IDisposable
        {
            private readonly object _lock = new object();
            private IntPtr _display;
            private IntPtr _window;
            private bool _disposed;

            public RawFrame Grab()
            {
                lock (_lock) {
                    if (_disposed) {
                        return null;
                    }
                    if (_display == IntPtr.Zero) {
                        X.EnsureErrorHandler();
                        _display = X.XOpenDisplay(IntPtr.Zero);
                        if (_display == IntPtr.Zero) {
                            return null;
                        }
                    }
                    if (_window == IntPtr.Zero || !X.IsViewable(_display, _window)) {
                        _window = X.FindWindow(_display, GameTitle);
                        if (_window == IntPtr.Zero) {
                            return null;
                        }
                    }
                    return X.Capture(_display, _window);
                }
            }

            public void Dispose()
            {
                lock (_lock) {
                    _disposed = true;
                    if (_display != IntPtr.Zero) {
                        X.XCloseDisplay(_display);
                        _display = IntPtr.Zero;
                    }
                }
            }
        }

        private sealed class Both(IDisposable first, IDisposable second) : IDisposable
        {
            public void Dispose()
            {
                first.Dispose();
                second.Dispose();
            }
        }

        /// <summary>The few Xlib calls this needs.</summary>
        internal static class X
        {
            private const string Lib = "libX11.so.6";
            private const int ZPixmap = 2;
            private const int IsViewableState = 2;

            [StructLayout(LayoutKind.Sequential)]
            public struct XWindowAttributes
            {
                public int x, y, width, height, border_width, depth;
                public IntPtr visual, root;
                public int c_class, bit_gravity, win_gravity, backing_store;
                public UIntPtr backing_planes, backing_pixel;
                public int save_under;
                public IntPtr colormap;
                public int map_installed, map_state;
                public IntPtr all_event_masks, your_event_mask, do_not_propagate_mask;
                public int override_redirect;
                public IntPtr screen;
            }

            [DllImport(Lib)] public static extern IntPtr XOpenDisplay(IntPtr name);
            [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr display);
            [DllImport(Lib)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
            [DllImport(Lib)] private static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
            [DllImport(Lib)] private static extern int XFetchName(IntPtr display, IntPtr window, out IntPtr name);
            [DllImport(Lib)] private static extern int XFree(IntPtr data);
            [DllImport(Lib)] private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);
            [DllImport(Lib)] private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, UIntPtr planeMask, int format);
            [DllImport(Lib)] private static extern IntPtr XSetErrorHandler(IntPtr handler);
            [DllImport(Lib)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
            [DllImport(Lib)] private static extern bool XTranslateCoordinates(IntPtr display, IntPtr from, IntPtr to, int x, int y, out int toX, out int toY, out IntPtr child);
            [DllImport(Lib)]
            private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length,
                bool delete, IntPtr requestedType, out IntPtr actualType, out int actualFormat, out IntPtr itemCount, out IntPtr bytesAfter, out IntPtr data);

            private static int _handlerSet;

            /// <summary>
            /// Xlib's default error handler ends the process - a window that closes between finding it and
            /// reading it would take the uploader down. This one just ignores errors; the read then fails.
            /// </summary>
            public static void EnsureErrorHandler()
            {
                if (System.Threading.Interlocked.Exchange(ref _handlerSet, 1) == 0) {
                    XSetErrorHandler((IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)&IgnoreError);
                }
            }

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
            private static int IgnoreError(IntPtr display, IntPtr error) => 0;

            public static bool IsViewable(IntPtr display, IntPtr window) =>
                XGetWindowAttributes(display, window, out var attributes) != 0 && attributes.map_state == IsViewableState;

            /// <summary>The biggest viewable window titled <paramref name="title"/>, searching the whole tree; zero if none.</summary>
            public static IntPtr FindWindow(IntPtr display, string title)
            {
                var best = IntPtr.Zero;
                long bestArea = 0;
                var pending = new Stack<IntPtr>();
                pending.Push(XDefaultRootWindow(display));
                while (pending.Count > 0) {
                    var window = pending.Pop();
                    if (Title(display, window) == title && XGetWindowAttributes(display, window, out var a) != 0 && a.map_state == IsViewableState) {
                        long area = (long)a.width * a.height;
                        if (area > bestArea) {
                            best = window;
                            bestArea = area;
                        }
                    }
                    if (XQueryTree(display, window, out _, out _, out var children, out var count) != 0 && children != IntPtr.Zero) {
                        for (var i = 0; i < count; i++) {
                            pending.Push(Marshal.ReadIntPtr(children, i * IntPtr.Size));
                        }
                        XFree(children);
                    }
                }
                return best;
            }

            /// <summary>
            /// A window's title: _NET_WM_NAME (UTF-8, which Wine and most toolkits set) when it has one, else the
            /// older WM_NAME. Null when it has neither.
            /// </summary>
            public static string Title(IntPtr display, IntPtr window)
            {
                var netWmName = XInternAtom(display, "_NET_WM_NAME", true);
                var utf8 = XInternAtom(display, "UTF8_STRING", true);
                if (netWmName != IntPtr.Zero && utf8 != IntPtr.Zero
                    && XGetWindowProperty(display, window, netWmName, IntPtr.Zero, (IntPtr)1024, false, utf8,
                        out _, out var format, out var count, out _, out var data) == 0 && data != IntPtr.Zero) {
                    try {
                        if (format == 8 && count != IntPtr.Zero) {
                            return Marshal.PtrToStringUTF8(data, (int)count);
                        }
                    }
                    finally {
                        XFree(data);
                    }
                }
                if (XFetchName(display, window, out var name) != 0 && name != IntPtr.Zero) {
                    try {
                        return Marshal.PtrToStringUTF8(name);
                    }
                    finally {
                        XFree(name);
                    }
                }
                return null;
            }

            /// <summary>
            /// The window's pixels as BGRA, or null if it can't be read (gone, unmapped, off screen, an unusual
            /// pixel format). X only hands out the part of a window that's on the screen - asking for more is a
            /// BadMatch error - so only that part is read, placed where it belongs in a frame the window's full
            /// size (anything off screen stays black): the cards are found from the window's edges.
            /// </summary>
            public static RawFrame Capture(IntPtr display, IntPtr window)
            {
                if (XGetWindowAttributes(display, window, out var a) == 0 || a.map_state != IsViewableState || a.width <= 0 || a.height <= 0) {
                    return null;
                }
                var root = XDefaultRootWindow(display);
                if (XGetWindowAttributes(display, root, out var screen) == 0
                    || !XTranslateCoordinates(display, window, root, 0, 0, out var screenX, out var screenY, out _)) {
                    return null;
                }
                var x0 = Math.Max(0, -screenX);
                var y0 = Math.Max(0, -screenY);
                var x1 = Math.Min(a.width, screen.width - screenX);
                var y1 = Math.Min(a.height, screen.height - screenY);
                if (x1 <= x0 || y1 <= y0) {
                    return null;
                }

                var image = XGetImage(display, window, x0, y0, (uint)(x1 - x0), (uint)(y1 - y0), new UIntPtr(ulong.MaxValue), ZPixmap);
                if (image == IntPtr.Zero) {
                    return null;
                }
                try {
                    // struct XImage on 64-bit Linux: width 0, height 4, data 16, bytes_per_line 44,
                    // bits_per_pixel 48, red_mask 56, blue_mask 72.
                    var width = Marshal.ReadInt32(image, 0);
                    var height = Marshal.ReadInt32(image, 4);
                    var data = Marshal.ReadIntPtr(image, 16);
                    var imageStride = Marshal.ReadInt32(image, 44);
                    var bitsPerPixel = Marshal.ReadInt32(image, 48);
                    var redMask = (ulong)Marshal.ReadInt64(image, 56);
                    var blueMask = (ulong)Marshal.ReadInt64(image, 72);
                    if (bitsPerPixel != 32 || data == IntPtr.Zero) {
                        return null;
                    }

                    var stride = a.width * 4;
                    var bytes = new byte[stride * a.height];
                    for (var y = 0; y < height; y++) {
                        Marshal.Copy(data + y * imageStride, bytes, (y0 + y) * stride + x0 * 4, width * 4);
                    }
                    // The usual layout is BGRX (red 0xff0000). A display with red in the low byte needs swapping.
                    if (redMask == 0xff && blueMask == 0xff0000) {
                        for (var i = 0; i < bytes.Length; i += 4) {
                            (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
                        }
                    }
                    return new RawFrame(a.width, a.height, stride, bytes);
                }
                finally {
                    // XDestroyImage is a macro over the image's own destroy function (XImage.f.destroy_image, at 96).
                    var destroy = (delegate* unmanaged[Cdecl]<IntPtr, int>)Marshal.ReadIntPtr(image, 96);
                    destroy(image);
                }
            }
        }
    }
}
