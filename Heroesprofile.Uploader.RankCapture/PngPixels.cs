using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>Decodes a PNG (a kept card strip) into <see cref="Pixels"/>, with Windows' own decoder.</summary>
    internal static class PngPixels
    {
        public static async Task<Pixels> DecodeAsync(byte[] png)
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyToBuffer(bytes.AsBuffer());
            return Pixels.FromBgra(bytes, bitmap.PixelWidth, bitmap.PixelHeight, bitmap.PixelWidth * 4);
        }
    }
}
