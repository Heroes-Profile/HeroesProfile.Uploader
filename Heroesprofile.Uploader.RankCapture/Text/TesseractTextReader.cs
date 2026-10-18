using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Text reading on Linux: Tesseract, the open-source reader most distros package (tesseract-ocr, plus a
    /// tesseract-ocr-&lt;lang&gt; package per extra language). Run as a program per line read, so nothing has
    /// to be linked against it. Without it no names are read, and cards are matched by elimination.
    /// </summary>
    internal sealed class TesseractTextReader : ITextReader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Data files that aren't languages.
        private static readonly string[] NotLanguages = { "osd", "equ" };

        private readonly string _tesseract;

        private TesseractTextReader(string tesseract, string languages)
        {
            _tesseract = tesseract;
            Language = languages;
        }

        public string Language { get; }

        /// <summary>
        /// A reader for all the languages installed at once (Tesseract reads several scripts in one go), and
        /// one for English alone - mixing scripts makes it worse at plain Latin names. None without Tesseract.
        /// </summary>
        public static IReadOnlyList<ITextReader> Installed()
        {
            var tesseract = ExternalTool.FindOnPath("tesseract");
            if (tesseract == null) {
                _log.Info("Rank reading: Tesseract isn't installed, so names can't be read (install tesseract-ocr)");
                return Array.Empty<ITextReader>();
            }
            var result = ExternalTool.RunAsync(tesseract, new[] { "--list-langs" }, null, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            var languages = ExternalTool.Text(result.Output).Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Contains(' ') && !l.EndsWith(':') && !NotLanguages.Contains(l))
                .ToList();
            if (languages.Count == 0) {
                return Array.Empty<ITextReader>();
            }
            var readers = new List<ITextReader> { new TesseractTextReader(tesseract, string.Join("+", languages)) };
            if (languages.Count > 1 && languages.Contains("eng")) {
                readers.Add(new TesseractTextReader(tesseract, "eng"));
            }
            return readers;
        }

        public async Task<IReadOnlyList<string>> ReadAsync(Pixels image)
        {
            // --psm 7: the image is a single line of text.
            var result = await ExternalTool.RunAsync(_tesseract, new[] { "stdin", "stdout", "-l", Language, "--psm", "7" },
                Png.Encode(image), TimeSpan.FromSeconds(20));
            if (result.ExitCode != 0) {
                _log.Debug($"Rank reading: tesseract failed: {result.Errors.Trim()}");
                return Array.Empty<string>();
            }
            var text = string.Join(" ", ExternalTool.Text(result.Output).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            return text.Length == 0 ? Array.Empty<string>() : new[] { text };
        }
    }
}
