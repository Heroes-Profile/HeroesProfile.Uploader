using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Text reading on Linux: Tesseract, the open-source reader most distros package (tesseract-ocr, plus a
    /// tesseract-ocr-&lt;lang&gt; package per extra language). Run as a program per line read, so nothing has
    /// to be linked against it. The Linux Ranks build ships its own, English only (in tesseract/ next to the
    /// app, from packaging/linux/build-tesseract.sh); the system's is used instead when it has more languages.
    /// Without either no names are read, and cards are matched by elimination.
    /// </summary>
    internal sealed class TesseractTextReader : ITextReader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Data files that aren't languages.
        private static readonly string[] NotLanguages = { "osd", "equ" };

        private readonly string _tesseract;
        private readonly string[] _dataArgs;

        private TesseractTextReader(string tesseract, string[] dataArgs, string languages)
        {
            _tesseract = tesseract;
            _dataArgs = dataArgs;
            Language = languages;
        }

        private static string BundledDir => Path.Combine(AppContext.BaseDirectory, "tesseract");

        public string Language { get; }

        /// <summary>
        /// A reader for all the languages installed at once (Tesseract reads several scripts in one go), and
        /// one for English alone - mixing scripts makes it worse at plain Latin names. None without Tesseract.
        /// </summary>
        public static IReadOnlyList<ITextReader> Installed()
        {
            var bundled = Path.Combine(BundledDir, "tesseract");
            var bundledArgs = new[] { "--tessdata-dir", Path.Combine(BundledDir, "tessdata") };
            var bundledLanguages = File.Exists(bundled) ? Languages(bundled, bundledArgs) : new List<string>();
            var system = ExternalTool.FindOnPath("tesseract");
            var systemLanguages = system != null ? Languages(system, Array.Empty<string>()) : new List<string>();

            string tesseract;
            string[] dataArgs;
            List<string> languages;
            if (systemLanguages.Count > bundledLanguages.Count) {
                (tesseract, dataArgs, languages) = (system, Array.Empty<string>(), systemLanguages);
            }
            else if (bundledLanguages.Count > 0) {
                (tesseract, dataArgs, languages) = (bundled, bundledArgs, bundledLanguages);
            }
            else {
                _log.Info("Rank reading: Tesseract isn't installed, so names can't be read (install tesseract-ocr)");
                return Array.Empty<ITextReader>();
            }
            _log.Info($"Rank reading: reading names with {(tesseract == bundled ? "the bundled" : "the system's")} Tesseract ({string.Join("+", languages)})");

            var readers = new List<ITextReader> { new TesseractTextReader(tesseract, dataArgs, string.Join("+", languages)) };
            if (languages.Count > 1 && languages.Contains("eng")) {
                readers.Add(new TesseractTextReader(tesseract, dataArgs, "eng"));
            }
            return readers;
        }

        /// <summary>The languages <paramref name="tesseract"/> can read; none if it doesn't run.</summary>
        private static List<string> Languages(string tesseract, string[] dataArgs)
        {
            try {
                var result = ExternalTool.RunAsync(tesseract, dataArgs.Append("--list-langs").ToArray(), null, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                return ExternalTool.Text(result.Output).Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.Contains(' ') && !l.EndsWith(':') && !NotLanguages.Contains(l))
                    .ToList();
            }
            catch (Exception ex) {
                _log.Warn($"Rank reading: couldn't run {tesseract}: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<IReadOnlyList<string>> ReadAsync(Pixels image)
        {
            // --psm 7: the image is a single line of text.
            var result = await ExternalTool.RunAsync(_tesseract, new[] { "stdin", "stdout" }.Concat(_dataArgs).Concat(new[] { "-l", Language, "--psm", "7" }).ToArray(),
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
