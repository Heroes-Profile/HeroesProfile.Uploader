using System;
using System.Runtime.InteropServices;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Whether the monitor a window is on shows HDR (Windows' "advanced colour": HDR, or Auto Color Management
    /// on Windows 11), and how bright it shows ordinary SDR white. Asked through the display configuration API,
    /// which needs no window or UI thread of our own.
    /// </summary>
    internal readonly record struct DisplayColour(bool Hdr, float SdrWhiteNits)
    {
        /// <summary>An ordinary SDR display: white at scRGB 1.0, which is 80 nits.</summary>
        public static readonly DisplayColour Sdr = new DisplayColour(false, ScRgbWhiteNits);

        /// <summary>scRGB's 1.0 is 80 nits.</summary>
        public const float ScRgbWhiteNits = ScRgb.WhiteNits;

        public override string ToString() => Hdr ? $"HDR, SDR white {SdrWhiteNits:0} nits" : "SDR";

        /// <summary>The colour mode of <paramref name="window"/>'s monitor; <see cref="Sdr"/> when it can't be told.</summary>
        public static DisplayColour ForWindow(IntPtr window) => TryForWindow(window, out var colour) ? colour : Sdr;

        /// <summary>
        /// The colour mode of <paramref name="window"/>'s monitor; false when its display couldn't be found in
        /// the display configuration (then <paramref name="colour"/> is <see cref="Sdr"/>).
        /// </summary>
        public static bool TryForWindow(IntPtr window, out DisplayColour colour)
        {
            colour = Sdr;
            try {
                var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info)) {
                    return false;
                }

                if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) {
                    return false;
                }
                var paths = new PathInfo[pathCount];
                var modes = new ModeInfo[modeCount];
                if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) {
                    return false;
                }

                for (var i = 0; i < pathCount; i++) {
                    // The path whose source is this monitor (\\.\DISPLAY1 ...).
                    var source = new SourceName { Header = Header(TypeGetSourceName, Marshal.SizeOf<SourceName>(), paths[i].SourceAdapter, paths[i].SourceId) };
                    if (DisplayConfigGetDeviceInfo(ref source) != 0 || !string.Equals(source.GdiDeviceName, info.Device, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    var advanced = new AdvancedColorInfo { Header = Header(TypeGetAdvancedColorInfo, Marshal.SizeOf<AdvancedColorInfo>(), paths[i].TargetAdapter, paths[i].TargetId) };
                    if (DisplayConfigGetDeviceInfo(ref advanced) != 0) {
                        return false;
                    }
                    if ((advanced.Value & AdvancedColorEnabled) == 0) {
                        return true;
                    }
                    var white = new SdrWhiteLevel { Header = Header(TypeGetSdrWhiteLevel, Marshal.SizeOf<SdrWhiteLevel>(), paths[i].TargetAdapter, paths[i].TargetId) };
                    // SDRWhiteLevel is in thousandths of scRGB's 80 nits: 1000 means 80 nits.
                    var nits = DisplayConfigGetDeviceInfo(ref white) == 0 && white.Level > 0 ? white.Level / 1000f * ScRgbWhiteNits : ScRgbWhiteNits;
                    colour = new DisplayColour(true, nits);
                    return true;
                }
            }
            catch (Exception) {
            }
            return false;
        }

        // --- Interop ---

        private const uint MonitorDefaultToNearest = 2;
        private const uint QdcOnlyActivePaths = 2;
        private const uint TypeGetSourceName = 1;
        private const uint TypeGetAdvancedColorInfo = 9;
        private const uint TypeGetSdrWhiteLevel = 11;
        private const uint AdvancedColorEnabled = 0x2;

        private static DeviceInfoHeader Header(uint type, int size, long adapter, uint id) =>
            new DeviceInfoHeader { Type = type, Size = (uint)size, Adapter = adapter, Id = id };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public int MonitorLeft, MonitorTop, MonitorRight, MonitorBottom;
            public int WorkLeft, WorkTop, WorkRight, WorkBottom;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        // DISPLAYCONFIG_PATH_INFO, 72 bytes: the source (adapter LUID, id, mode index, flags) and target
        // (adapter LUID, id, mode index, output technology, rotation, scaling, refresh rate, scan line
        // ordering, available, flags) - only the adapters and ids are used. A LUID is two 32-bit halves, so
        // nothing is aligned past 4 bytes.
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PathInfo
        {
            public long SourceAdapter;
            public uint SourceId;
            public uint SourceModeIndex;
            public uint SourceFlags;
            public long TargetAdapter;
            public uint TargetId;
            public uint TargetModeIndex;
            public uint OutputTechnology;
            public uint Rotation;
            public uint Scaling;
            public uint RefreshNumerator;
            public uint RefreshDenominator;
            public uint ScanLineOrdering;
            public int TargetAvailable;
            public uint TargetFlags;
            public uint Flags;
        }

        // DISPLAYCONFIG_MODE_INFO, 64 bytes; not looked at.
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct ModeInfo
        {
            public uint InfoType;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct DeviceInfoHeader
        {
            public uint Type;
            public uint Size;
            public long Adapter;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SourceName
        {
            public DeviceInfoHeader Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string GdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdvancedColorInfo
        {
            public DeviceInfoHeader Header;
            public uint Value;
            public uint ColorEncoding;
            public uint BitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SdrWhiteLevel
        {
            public DeviceInfoHeader Header;
            public uint Level;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out int pathCount, out int modeCount);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref int pathCount, [Out] PathInfo[] paths, ref int modeCount, [Out] ModeInfo[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref SdrWhiteLevel request);
    }
}
