using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Windows' own text recognition (Windows.Media.Ocr): one reader per language Windows has one installed for,
    /// each reading only its own script.
    /// </summary>
    internal sealed class WindowsTextReader : ITextReader
    {
        private readonly OcrEngine _engine;

        private WindowsTextReader(OcrEngine engine)
        {
            _engine = engine;
        }

        public string Language => _engine.RecognizerLanguage.LanguageTag;

        public static IReadOnlyList<ITextReader> Installed()
        {
            var readers = OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).Where(e => e != null).ToList();
            if (readers.Count == 0 && OcrEngine.TryCreateFromUserProfileLanguages() is OcrEngine profile) {
                readers.Add(profile);
            }
            return readers.Select(e => (ITextReader)new WindowsTextReader(e)).ToList();
        }

        public async Task<IReadOnlyList<string>> ReadAsync(Pixels image)
        {
            var bytes = new byte[image.Width * image.Height * 4];
            for (var i = 0; i < image.Width * image.Height; i++) {
                bytes[i * 4] = (byte)Math.Clamp(image.Rgb[i * 3 + 2], 0, 255);
                bytes[i * 4 + 1] = (byte)Math.Clamp(image.Rgb[i * 3 + 1], 0, 255);
                bytes[i * 4 + 2] = (byte)Math.Clamp(image.Rgb[i * 3], 0, 255);
                bytes[i * 4 + 3] = 255;
            }
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Ignore);
            var result = await _engine.RecognizeAsync(bitmap);
            var text = string.Join(" ", result.Lines.Select(l => l.Text));
            return text.Length == 0 ? Array.Empty<string>() : new[] { text };
        }
    }
}
