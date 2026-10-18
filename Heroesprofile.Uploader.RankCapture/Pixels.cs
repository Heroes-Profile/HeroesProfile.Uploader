using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// An RGB image as floats (0-255), row by row: what <see cref="LoadingScreenReader"/> works on. Kept free
    /// of any Windows type so the reading itself can be tested on plain pixels.
    /// </summary>
    internal sealed class Pixels
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>Red, green, blue for each pixel, row by row.</summary>
        public float[] Rgb { get; }

        public Pixels(int width, int height) : this(width, height, new float[width * height * 3])
        {
        }

        public Pixels(int width, int height, float[] rgb)
        {
            if (rgb.Length != width * height * 3) {
                throw new ArgumentException("Wrong number of values for the size", nameof(rgb));
            }
            Width = width;
            Height = height;
            Rgb = rgb;
        }

        /// <summary>From 32-bit BGRA rows (Windows' SoftwareBitmap layout), ignoring alpha.</summary>
        public static Pixels FromBgra(byte[] bgra, int width, int height, int stride)
        {
            var pixels = new Pixels(width, height);
            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    var i = y * stride + x * 4;
                    var o = (y * width + x) * 3;
                    pixels.Rgb[o] = bgra[i + 2];
                    pixels.Rgb[o + 1] = bgra[i + 1];
                    pixels.Rgb[o + 2] = bgra[i];
                }
            }
            return pixels;
        }

        public float R(int x, int y) => Rgb[(y * Width + x) * 3];
        public float G(int x, int y) => Rgb[(y * Width + x) * 3 + 1];
        public float B(int x, int y) => Rgb[(y * Width + x) * 3 + 2];

        /// <summary>Brightness the way Pillow's "L" mode has it, which the templates were tuned with.</summary>
        public float Gray(int x, int y) => R(x, y) * 0.299f + G(x, y) * 0.587f + B(x, y) * 0.114f;

        /// <summary>The whole image as brightness, row by row.</summary>
        public float[] GrayValues()
        {
            var gray = new float[Width * Height];
            for (var y = 0; y < Height; y++) {
                for (var x = 0; x < Width; x++) {
                    gray[y * Width + x] = Gray(x, y);
                }
            }
            return gray;
        }

        /// <summary>
        /// Hue (degrees), saturation and value (both 0-255, as Pillow's HSV mode) of one pixel.
        /// </summary>
        public (float Hue, float Saturation, float Value) Hsv(int x, int y)
        {
            float r = R(x, y), g = G(x, y), b = B(x, y);
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            var saturation = max <= 0 ? 0 : delta / max * 255;
            float hue = 0;
            if (delta > 0) {
                if (max == r) {
                    hue = 60 * ((g - b) / delta);
                } else if (max == g) {
                    hue = 60 * ((b - r) / delta + 2);
                } else {
                    hue = 60 * ((r - g) / delta + 4);
                }
                if (hue < 0) {
                    hue += 360;
                }
            }
            return (hue, saturation, max);
        }

        /// <summary>The same image, mirrored left to right.</summary>
        public Pixels FlipHorizontal()
        {
            var flipped = new Pixels(Width, Height);
            for (var y = 0; y < Height; y++) {
                for (var x = 0; x < Width; x++) {
                    var from = (y * Width + x) * 3;
                    var to = (y * Width + (Width - 1 - x)) * 3;
                    flipped.Rgb[to] = Rgb[from];
                    flipped.Rgb[to + 1] = Rgb[from + 1];
                    flipped.Rgb[to + 2] = Rgb[from + 2];
                }
            }
            return flipped;
        }

        /// <summary>
        /// A <paramref name="width"/> x <paramref name="height"/> grid sampled out of this image: output pixel
        /// (i, j) covers the source rectangle at (<paramref name="x0"/> + i * <paramref name="unitX"/>,
        /// <paramref name="y0"/> + j * <paramref name="unitY"/>), <paramref name="unitX"/> x <paramref name="unitY"/>
        /// source pixels in size. Shrinking averages every source pixel the rectangle covers; enlarging
        /// interpolates between the nearest ones. Anything past the edge repeats the edge pixel.
        /// </summary>
        public Pixels Sample(double x0, double y0, double unitX, double unitY, int width, int height)
        {
            var columns = Weights(width, x0, unitX, Width);
            var rows = Weights(height, y0, unitY, Height);
            var rowLo = rows.SelectMany(r => r.Index).Min();
            var rowHi = rows.SelectMany(r => r.Index).Max();

            // Across first, for just the source rows the output needs; then down.
            var across = new float[(rowHi - rowLo + 1) * width * 3];
            for (var sy = rowLo; sy <= rowHi; sy++) {
                for (var i = 0; i < width; i++) {
                    var (index, weight) = columns[i];
                    float r = 0, g = 0, b = 0;
                    for (var k = 0; k < index.Length; k++) {
                        var s = (sy * Width + index[k]) * 3;
                        r += Rgb[s] * weight[k];
                        g += Rgb[s + 1] * weight[k];
                        b += Rgb[s + 2] * weight[k];
                    }
                    var o = ((sy - rowLo) * width + i) * 3;
                    across[o] = r;
                    across[o + 1] = g;
                    across[o + 2] = b;
                }
            }

            var result = new Pixels(width, height);
            for (var j = 0; j < height; j++) {
                var (index, weight) = rows[j];
                for (var i = 0; i < width; i++) {
                    float r = 0, g = 0, b = 0;
                    for (var k = 0; k < index.Length; k++) {
                        var s = ((index[k] - rowLo) * width + i) * 3;
                        r += across[s] * weight[k];
                        g += across[s + 1] * weight[k];
                        b += across[s + 2] * weight[k];
                    }
                    var o = (j * width + i) * 3;
                    result.Rgb[o] = r;
                    result.Rgb[o + 1] = g;
                    result.Rgb[o + 2] = b;
                }
            }
            return result;
        }

        /// <summary>Square-pixel <see cref="Sample(double, double, double, double, int, int)"/>.</summary>
        public Pixels Sample(double x0, double y0, double unit, int width, int height) => Sample(x0, y0, unit, unit, width, height);

        private static (int[] Index, float[] Weight)[] Weights(int count, double start, double unit, int size)
        {
            int Clamp(int i) => Math.Clamp(i, 0, size - 1);

            var result = new (int[], float[])[count];
            for (var i = 0; i < count; i++) {
                var a = start + i * unit;
                var b = a + unit;
                if (unit >= 1) {
                    var first = (int)Math.Floor(a);
                    var last = (int)Math.Ceiling(b) - 1;
                    var index = new List<int>();
                    var weight = new List<float>();
                    double total = 0;
                    for (var k = first; k <= last; k++) {
                        var cover = Math.Min(b, k + 1) - Math.Max(a, k);
                        if (cover <= 0) {
                            continue;
                        }
                        index.Add(Clamp(k));
                        weight.Add((float)cover);
                        total += cover;
                    }
                    result[i] = (index.ToArray(), weight.Select(w => (float)(w / total)).ToArray());
                } else {
                    var centre = (a + b) / 2 - 0.5;
                    var k = (int)Math.Floor(centre);
                    var f = (float)(centre - k);
                    result[i] = (new[] { Clamp(k), Clamp(k + 1) }, new[] { 1 - f, f });
                }
            }
            return result;
        }
    }
}
