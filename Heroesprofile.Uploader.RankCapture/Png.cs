using SkiaSharp;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>PNG in and out with SkiaSharp (which the app already ships, through Avalonia), on every OS.</summary>
    internal static class Png
    {
        /// <summary>The part of <paramref name="frame"/> from column <paramref name="x"/>, <paramref name="width"/> wide and full height, as a PNG.</summary>
        public static byte[] EncodeStrip(RawFrame frame, int x, int width)
        {
            var info = new SKImageInfo(width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            var rows = new byte[width * 4 * frame.Height];
            for (var y = 0; y < frame.Height; y++) {
                Buffer.BlockCopy(frame.Bgra, y * frame.Stride + x * 4, rows, y * width * 4, width * 4);
            }
            var handle = GCHandle.Alloc(rows, GCHandleType.Pinned);
            try {
                using var image = SKImage.FromPixelCopy(info, handle.AddrOfPinnedObject(), width * 4);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
            finally {
                handle.Free();
            }
        }

        /// <summary>A PNG as 32-bit BGRA rows.</summary>
        public static RawFrame DecodeRaw(byte[] png)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(png)) ?? throw new FormatException("Not an image Skia can read");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var bytes = new byte[info.Width * info.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try {
                var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) {
                    throw new FormatException($"Couldn't decode the image: {result}");
                }
            }
            finally {
                handle.Free();
            }
            return new RawFrame(info.Width, info.Height, info.Width * 4, bytes);
        }

        /// <summary>A PNG (a kept card strip) as <see cref="Pixels"/>.</summary>
        public static Pixels Decode(byte[] png)
        {
            var raw = DecodeRaw(png);
            return Pixels.FromBgra(raw.Bgra, raw.Width, raw.Height, raw.Stride);
        }

        /// <summary><paramref name="image"/> (as the reader works on it) as a PNG - what the macOS and Linux text readers are handed.</summary>
        public static byte[] Encode(Pixels image)
        {
            var bgra = new byte[image.Width * image.Height * 4];
            for (var i = 0; i < image.Width * image.Height; i++) {
                bgra[i * 4] = (byte)Math.Clamp(image.Rgb[i * 3 + 2], 0, 255);
                bgra[i * 4 + 1] = (byte)Math.Clamp(image.Rgb[i * 3 + 1], 0, 255);
                bgra[i * 4 + 2] = (byte)Math.Clamp(image.Rgb[i * 3], 0, 255);
                bgra[i * 4 + 3] = 255;
            }
            return EncodeStrip(new RawFrame(image.Width, image.Height, image.Width * 4, bgra), 0, image.Width);
        }

        /// <summary>For the callers that decode in the background, as Windows' decoder used to.</summary>
        public static Task<Pixels> DecodeAsync(byte[] png) => Task.FromResult(Decode(png));
    }
}
