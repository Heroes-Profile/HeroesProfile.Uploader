using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Captures the Heroes of the Storm window with Windows' own screen capture (Windows.Graphics.Capture, the
    /// API OBS, Discord and the Game Bar use). It only ever looks at that one window, never reads or touches
    /// the game's process, and hands out a frame every <see cref="Interval"/> - the rest are dropped straight
    /// away. Frames stay in memory.
    /// </summary>
    internal sealed class WindowCapture : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // The game's executable (64-bit, and the 32-bit client some older installs still run).
        private static readonly string[] GameProcesses = { "HeroesOfTheStorm_x64", "HeroesOfTheStorm" };

        public TimeSpan Interval { get; }

        private readonly Action<SoftwareBitmap> _onFrame;
        private readonly IDirect3DDevice _device;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _pool;
        private readonly GraphicsCaptureSession _session;
        private Windows.Graphics.SizeInt32 _size;
        private long _lastFrameTicks;
        private int _busy;

        private WindowCapture(IntPtr window, TimeSpan interval, bool showOutline, Action<SoftwareBitmap> onFrame)
        {
            Interval = interval;
            _onFrame = onFrame;
            _device = Direct3D.CreateDevice();
            _item = CaptureItemForWindow(window);
            _size = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            _pool.FrameArrived += OnFrameArrived;
            _session = _pool.CreateCaptureSession(_item);
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsCursorCaptureEnabled))) {
                _session.IsCursorCaptureEnabled = false;
            }
            // Windows draws a yellow outline around anything being captured. Windows 11 lets an app turn it
            // off once it has borderless access (EnsureBorderlessAccessAsync); Windows 10 always shows it.
            if (!showOutline && _borderlessAllowed && CanHideOutline) {
                _session.IsBorderRequired = false;
            }
            _session.StartCapture();
        }

        /// <summary>Whether this Windows can do it at all (Windows 10 1903 and later).</summary>
        public static bool IsSupported => GraphicsCaptureSession.IsSupported();

        /// <summary>The game's main window, or IntPtr.Zero when it isn't running (or has no window yet).</summary>
        public static IntPtr FindGameWindow()
        {
            foreach (var process in GameProcesses.SelectMany(Process.GetProcessesByName)) {
                using (process) {
                    if (process.MainWindowHandle != IntPtr.Zero) {
                        return process.MainWindowHandle;
                    }
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Starts capturing <paramref name="window"/>, calling <paramref name="onFrame"/> (on a worker thread)
        /// at most every <paramref name="interval"/>. <paramref name="showOutline"/> false hides Windows'
        /// capture outline where it can (Windows 11, after <see cref="EnsureBorderlessAccessAsync"/>).
        /// </summary>
        public static WindowCapture Start(IntPtr window, TimeSpan interval, bool showOutline, Action<SoftwareBitmap> onFrame) =>
            new WindowCapture(window, interval, showOutline, onFrame);

        /// <summary>Whether this Windows can capture without the yellow outline (Windows 11 and later).</summary>
        public static bool CanHideOutline =>
            Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired");

        private static bool _borderlessAllowed;

        /// <summary>
        /// Asks Windows (once per run) to allow capture without the outline. A desktop app like this gets it
        /// without the user being asked; false where Windows can't (Windows 10) or says no.
        /// </summary>
        public static async Task<bool> EnsureBorderlessAccessAsync()
        {
            if (_borderlessAllowed || !CanHideOutline) {
                return _borderlessAllowed;
            }
            try {
                var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
                _borderlessAllowed = status == Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed;
                if (!_borderlessAllowed) {
                    _log.Info($"Rank reading: Windows won't allow capture without its outline ({status})");
                }
            }
            catch (Exception ex) {
                _log.Debug($"Rank reading: couldn't ask for capture without the outline: {ex.Message}");
            }
            return _borderlessAllowed;
        }

        private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) {
                return;
            }

            // The window was resized: the pool has to match or frames come out cropped.
            if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height) {
                _size = frame.ContentSize;
                _pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                return;
            }

            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastFrameTicks), now) < Interval || Interlocked.Exchange(ref _busy, 1) == 1) {
                return;
            }
            try {
                Interlocked.Exchange(ref _lastFrameTicks, now);
                var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                _onFrame(bitmap);
            }
            catch (Exception ex) {
                _log.Debug($"Couldn't copy a captured frame: {ex.Message}");
            }
            finally {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        public void Dispose()
        {
            _pool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _pool.Dispose();
            _device.Dispose();
        }

        /// <summary>
        /// Encodes the part of <paramref name="bitmap"/> inside <paramref name="bounds"/> as a PNG, at full
        /// resolution.
        /// </summary>
        public static async Task<byte[]> EncodePngAsync(SoftwareBitmap bitmap, BitmapBounds bounds)
        {
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(bitmap);
            encoder.BitmapTransform.Bounds = bounds;
            await encoder.FlushAsync();

            using var output = new MemoryStream();
            stream.Seek(0);
            await stream.AsStreamForRead().CopyToAsync(output);
            return output.ToArray();
        }

        // --- Interop: a capture item for a window, and a Direct3D device to capture with ---

        // IGraphicsCaptureItemInterop, and the IGraphicsCaptureItem it hands back. The interop interface is
        // plain COM, which CsWinRT's AsInterface<T> can't cast to, so its CreateForWindow (vtable slot 3,
        // after IUnknown's three) is called through its function pointer.
        private static readonly Guid CaptureItemInteropIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid CaptureItemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        private static unsafe GraphicsCaptureItem CaptureItemForWindow(IntPtr window)
        {
            var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interopIid = CaptureItemInteropIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in interopIid, out var interop));
            try {
                var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)interop)[3];
                var itemIid = CaptureItemIid;
                IntPtr item;
                Marshal.ThrowExceptionForHR(createForWindow(interop, window, &itemIid, &item));
                try {
                    return GraphicsCaptureItem.FromAbi(item);
                }
                finally {
                    Marshal.Release(item);
                }
            }
            finally {
                Marshal.Release(interop);
            }
        }

        private static class Direct3D
        {
            private const int HardwareDriver = 1;         // D3D_DRIVER_TYPE_HARDWARE
            private const uint BgraSupport = 0x20;        // D3D11_CREATE_DEVICE_BGRA_SUPPORT
            private const uint SdkVersion = 7;            // D3D11_SDK_VERSION
            private static readonly Guid DxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

            [DllImport("d3d11.dll", ExactSpelling = true)]
            private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
                IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

            [DllImport("d3d11.dll", ExactSpelling = true)]
            private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

            public static IDirect3DDevice CreateDevice()
            {
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, HardwareDriver, IntPtr.Zero, BgraSupport,
                    IntPtr.Zero, 0, SdkVersion, out var device, out _, out var context));
                try {
                    var iid = DxgiDevice;
                    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out var dxgi));
                    try {
                        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                        try {
                            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                        }
                        finally {
                            Marshal.Release(inspectable);
                        }
                    }
                    finally {
                        Marshal.Release(dxgi);
                    }
                }
                finally {
                    Marshal.Release(context);
                    Marshal.Release(device);
                }
            }
        }
    }
}
