using System;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Turns an HDR capture back into what the game drew. On an HDR display Windows composes the screen in
    /// scRGB - linear light, half floats, 1.0 = 80 nits - and shows SDR content (the game) with its white at
    /// the display's "SDR white" brightness. Asking Windows for an 8-bit capture there gave over-saturated
    /// frames with their highlights clipped to white, so frame colours couldn't be read (samples 2026-10-19).
    /// Capturing the scRGB itself and undoing that - SDR white back to 1.0, then the sRGB curve - gives the
    /// game's own colours.
    /// </summary>
    internal static class ScRgb
    {
        /// <summary>
        /// 32-bit BGRA rows from scRGB half-float RGBA rows (<paramref name="rowPitch"/> bytes apart, as
        /// Direct3D maps them), for a display that shows SDR white at <paramref name="sdrWhiteNits"/>.
        /// </summary>
        public static byte[] ToBgra(ReadOnlySpan<byte> halfRgba, int width, int height, int rowPitch, float sdrWhiteNits)
        {
            var table = Table(sdrWhiteNits);
            var bgra = new byte[width * height * 4];
            for (var y = 0; y < height; y++) {
                var row = halfRgba.Slice(y * rowPitch, width * 8);
                for (var x = 0; x < width; x++) {
                    var r = BitConverter.ToUInt16(row.Slice(x * 8));
                    var g = BitConverter.ToUInt16(row.Slice(x * 8 + 2));
                    var b = BitConverter.ToUInt16(row.Slice(x * 8 + 4));
                    var o = (y * width + x) * 4;
                    bgra[o] = table[b];
                    bgra[o + 1] = table[g];
                    bgra[o + 2] = table[r];
                    bgra[o + 3] = 255;
                }
            }
            return bgra;
        }

        /// <summary>One channel: a scRGB value to its 8-bit sRGB value on a display with that SDR white.</summary>
        public static byte ToSrgbByte(float scRgb, float sdrWhiteNits)
        {
            var linear = scRgb * WhiteNits / sdrWhiteNits;
            if (!(linear > 0)) {
                return 0;
            }
            if (linear >= 1) {
                return 255;
            }
            var encoded = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
            return (byte)Math.Clamp((int)MathF.Round(encoded * 255), 0, 255);
        }

        /// <summary>scRGB's 1.0, in nits: an SDR display's white.</summary>
        public const float WhiteNits = 80;

        private static readonly object _lock = new object();
        private static float _tableWhite;
        private static byte[] _table;

        /// <summary>Every half-float bit pattern's byte value, for one SDR white (built once, ~64 KB).</summary>
        private static byte[] Table(float sdrWhiteNits)
        {
            lock (_lock) {
                if (_table == null || _tableWhite != sdrWhiteNits) {
                    var table = new byte[65536];
                    for (var bits = 0; bits < 65536; bits++) {
                        table[bits] = ToSrgbByte((float)BitConverter.UInt16BitsToHalf((ushort)bits), sdrWhiteNits);
                    }
                    _table = table;
                    _tableWhite = sdrWhiteNits;
                }
                return _table;
            }
        }
    }
}
