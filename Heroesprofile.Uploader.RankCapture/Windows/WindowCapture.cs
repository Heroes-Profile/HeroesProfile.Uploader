using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
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
    /// On an HDR display (<see cref="DisplayColour"/>) it captures the screen's own scRGB image instead of
    /// asking Windows for an 8-bit one - which came out over-saturated and blown out - and turns it back into
    /// the game's colours itself (<see cref="ScRgb"/>).
    /// </summary>
    internal sealed class WindowCapture : IDisposable
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // The game's executable (64-bit, and the 32-bit client some older installs still run).
        private static readonly string[] GameProcesses = { "HeroesOfTheStorm_x64", "HeroesOfTheStorm" };

        public TimeSpan Interval { get; }

        /// <summary>The display's colour mode when capture started: HDR captures go through <see cref="ReadHdr"/>.</summary>
        public DisplayColour Colour { get; }

        private readonly Action<RawFrame> _onFrame;
        private readonly IDirect3DDevice _device;
        private readonly Direct3D.Device _d3d;
        private readonly object _hdrLock = new object();
        private IntPtr _staging;
        private int _stagingWidth, _stagingHeight;
        private bool _disposed;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _pool;
        private readonly GraphicsCaptureSession _session;
        private Windows.Graphics.SizeInt32 _size;
        private long _lastFrameTicks;
        private int _busy;

        private WindowCapture(IntPtr window, TimeSpan interval, bool showOutline, DisplayColour colour, Action<RawFrame> onFrame)
        {
            Interval = interval;
            Colour = colour;
            _onFrame = onFrame;
            _d3d = Direct3D.CreateDevice();
            _device = _d3d.WinRT;
            _item = CaptureItemForWindow(window);
            _size = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, PixelFormat, 2, _size);
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
        /// <paramref name="colour"/> is the window's display (<see cref="DisplayColour.ForWindow"/>): HDR
        /// captures the scRGB image. (Any display can be captured that way - the tests do, on SDR.)
        /// </summary>
        public static WindowCapture Start(IntPtr window, TimeSpan interval, bool showOutline, DisplayColour colour, Action<RawFrame> onFrame) =>
            new WindowCapture(window, interval, showOutline, colour, onFrame);

        private DirectXPixelFormat PixelFormat => Colour.Hdr ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized;

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
                _pool.Recreate(_device, PixelFormat, 2, _size);
                return;
            }

            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastFrameTicks), now) < Interval || Interlocked.Exchange(ref _busy, 1) == 1) {
                return;
            }
            try {
                Interlocked.Exchange(ref _lastFrameTicks, now);
                if (Colour.Hdr) {
                    var hdr = ReadHdr(frame);
                    if (hdr != null) {
                        _onFrame(hdr);
                    }
                    return;
                }
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyToBuffer(bytes.AsBuffer());
                _onFrame(new RawFrame(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.PixelWidth * 4, bytes));
            }
            catch (Exception ex) {
                _log.Debug($"Couldn't copy a captured frame: {ex.Message}");
            }
            finally {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>
        /// An HDR frame as the game drew it: copied off the GPU (SoftwareBitmap has no half-float format), then
        /// <see cref="ScRgb.ToBgra"/> with the display's SDR white.
        /// </summary>
        private unsafe RawFrame ReadHdr(Direct3D11CaptureFrame frame)
        {
            lock (_hdrLock) {
                if (_disposed) {
                    return null;
                }
                var texture = Direct3D.TextureOf(frame.Surface);
                try {
                    var desc = Direct3D.Describe(texture);
                    if (_staging == IntPtr.Zero || _stagingWidth != desc.Width || _stagingHeight != desc.Height) {
                        Direct3D.Release(ref _staging);
                        _staging = _d3d.CreateStaging(desc);
                        _stagingWidth = (int)desc.Width;
                        _stagingHeight = (int)desc.Height;
                    }
                    _d3d.CopyResource(_staging, texture);
                    var width = Math.Min(frame.ContentSize.Width, (int)desc.Width);
                    var height = Math.Min(frame.ContentSize.Height, (int)desc.Height);
                    var (data, rowPitch) = _d3d.Map(_staging);
                    try {
                        var pixels = new ReadOnlySpan<byte>((void*)data, rowPitch * (height - 1) + width * 8);
                        return new RawFrame(width, height, width * 4, ScRgb.ToBgra(pixels, width, height, rowPitch, Colour.SdrWhiteNits), Hdr: true, SdrWhiteNits: Colour.SdrWhiteNits);
                    }
                    finally {
                        _d3d.Unmap(_staging);
                    }
                }
                finally {
                    Marshal.Release(texture);
                }
            }
        }

        public void Dispose()
        {
            _pool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _pool.Dispose();
            lock (_hdrLock) {
                _disposed = true;
                Direct3D.Release(ref _staging);
                _d3d.Dispose();
            }
            _device.Dispose();
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
            private static readonly Guid DxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            private static readonly Guid Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

            [DllImport("d3d11.dll", ExactSpelling = true)]
            private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
                IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

            [DllImport("d3d11.dll", ExactSpelling = true)]
            private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

            /// <summary>D3D11_TEXTURE2D_DESC.</summary>
            [StructLayout(LayoutKind.Sequential)]
            public struct TextureDesc
            {
                public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
            }

            /// <summary>D3D11_MAPPED_SUBRESOURCE.</summary>
            [StructLayout(LayoutKind.Sequential)]
            private struct Mapped
            {
                public IntPtr Data;
                public uint RowPitch;
                public uint DepthPitch;
            }

            /// <summary>
            /// The Direct3D 11 device frames are captured with: its WinRT wrapper for the capture API, and its
            /// own device and immediate context for reading HDR frames back (kept until disposed). Called by
            /// vtable slot, as there's no Direct3D binding here: ID3D11Device CreateTexture2D 5;
            /// ID3D11DeviceContext Map 14, Unmap 15, CopyResource 47.
            /// </summary>
            public sealed class Device : IDisposable
            {
                public IDirect3DDevice WinRT { get; init; }
                public IntPtr D3D;
                public IntPtr Context;

                public unsafe IntPtr CreateStaging(TextureDesc desc)
                {
                    // Same size and format, readable by the CPU: D3D11_USAGE_STAGING, D3D11_CPU_ACCESS_READ.
                    desc.MipLevels = 1;
                    desc.ArraySize = 1;
                    desc.SampleCount = 1;
                    desc.SampleQuality = 0;
                    desc.Usage = 3;
                    desc.BindFlags = 0;
                    desc.CpuAccessFlags = 0x20000;
                    desc.MiscFlags = 0;
                    IntPtr staging;
                    var create = (delegate* unmanaged[Stdcall]<IntPtr, TextureDesc*, IntPtr, IntPtr*, int>)(*(IntPtr**)D3D)[5];
                    Marshal.ThrowExceptionForHR(create(D3D, &desc, IntPtr.Zero, &staging));
                    return staging;
                }

                public unsafe void CopyResource(IntPtr destination, IntPtr source)
                {
                    var copy = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)(*(IntPtr**)Context)[47];
                    copy(Context, destination, source);
                }

                public unsafe (IntPtr Data, int RowPitch) Map(IntPtr resource)
                {
                    Mapped mapped;
                    var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, Mapped*, int>)(*(IntPtr**)Context)[14];
                    // D3D11_MAP_READ.
                    Marshal.ThrowExceptionForHR(map(Context, resource, 0, 1, 0, &mapped));
                    return (mapped.Data, (int)mapped.RowPitch);
                }

                public unsafe void Unmap(IntPtr resource)
                {
                    var unmap = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)(*(IntPtr**)Context)[15];
                    unmap(Context, resource, 0);
                }

                public void Dispose()
                {
                    Release(ref Context);
                    Release(ref D3D);
                }
            }

            public static Device CreateDevice()
            {
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, HardwareDriver, IntPtr.Zero, BgraSupport,
                    IntPtr.Zero, 0, SdkVersion, out var device, out _, out var context));
                try {
                    var iid = DxgiDevice;
                    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out var dxgi));
                    try {
                        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                        try {
                            return new Device { WinRT = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable), D3D = device, Context = context };
                        }
                        finally {
                            Marshal.Release(inspectable);
                        }
                    }
                    finally {
                        Marshal.Release(dxgi);
                    }
                }
                catch {
                    Marshal.Release(context);
                    Marshal.Release(device);
                    throw;
                }
            }

            /// <summary>A captured frame's ID3D11Texture2D (released by the caller), through IDirect3DDxgiInterfaceAccess.</summary>
            public static unsafe IntPtr TextureOf(IDirect3DSurface surface)
            {
                var abi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
                try {
                    var accessIid = DxgiInterfaceAccess;
                    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in accessIid, out var access));
                    try {
                        var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)access)[3];
                        var textureIid = Texture2D;
                        IntPtr texture;
                        Marshal.ThrowExceptionForHR(getInterface(access, &textureIid, &texture));
                        return texture;
                    }
                    finally {
                        Marshal.Release(access);
                    }
                }
                finally {
                    Marshal.Release(abi);
                }
            }

            /// <summary>ID3D11Texture2D::GetDesc (vtable slot 10).</summary>
            public static unsafe TextureDesc Describe(IntPtr texture)
            {
                TextureDesc desc;
                var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, TextureDesc*, void>)(*(IntPtr**)texture)[10];
                getDesc(texture, &desc);
                return desc;
            }

            public static void Release(ref IntPtr pointer)
            {
                if (pointer != IntPtr.Zero) {
                    Marshal.Release(pointer);
                    pointer = IntPtr.Zero;
                }
            }
        }
    }
}
