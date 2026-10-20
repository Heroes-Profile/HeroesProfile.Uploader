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
    /// app, from packaging/linux/build-tesseract.sh); the system's adds the languages it has beyond that.
    /// Without either no names are read, and cards are matched by elimination.
    /// </summary>
    internal sealed class TesseractTextReader : ITextReader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Data files that aren't languages.
        private static readonly string[] NotLanguages = { "osd", "equ" };

        private readonly string _tesseract;
        private readonly string[] _args;

        private TesseractTextReader(string tesseract, string[] dataArgs, string languages)
        {
            _tesseract = tesseract;
            Language = languages;
            // --psm 7: the image is a single line of text.
            _args = new[] { "stdin", "stdout" }.Concat(dataArgs).Concat(new[] { "-l", languages, "--psm", "7" }).ToArray();
        }

        private static string BundledDir => Path.Combine(AppContext.BaseDirectory, "tesseract");

        public string Language { get; }

        /// <summary>Where a reader's Tesseract comes from: the one shipped with the app, or the system's.</summary>
        internal enum Source
        {
            Bundled,
            System,
        }

        /// <summary>One reader to make: which Tesseract, and the languages it reads at once ("eng", "eng+kor").</summary>
        internal sealed record Choice(Source Source, string Languages);

        /// <summary>
        /// The readers to use, given the languages the bundled Tesseract and the system's have (either may be
        /// empty). The bundled one (English) always reads, so Latin names - nearly all of them - are read the
        /// same everywhere; the system's adds a reader when it has languages the bundle doesn't (Korean, Russian
        /// ...), with all of its languages at once, as Tesseract reads several scripts in one go. Without a
        /// bundled one, as before: the system's with everything, plus English alone, since mixing scripts makes
        /// it worse at plain Latin names.
        /// </summary>
        internal static IReadOnlyList<Choice> Choose(IReadOnlyList<string> bundled, IReadOnlyList<string> system)
        {
            var choices = new List<Choice>();
            if (bundled.Count > 0) {
                choices.Add(new Choice(Source.Bundled, string.Join("+", bundled)));
                if (system.Except(bundled).Any()) {
                    choices.Add(new Choice(Source.System, string.Join("+", system)));
                }
            } else if (system.Count > 0) {
                choices.Add(new Choice(Source.System, string.Join("+", system)));
                if (system.Count > 1 && system.Contains("eng")) {
                    choices.Add(new Choice(Source.System, "eng"));
                }
            }
            return choices;
        }

        /// <summary>
        /// The readers this machine has (see <see cref="Choose"/>): the bundled Tesseract (tesseract/ next to the
        /// app, Linux Ranks builds) and the system's. None without either.
        /// </summary>
        public static IReadOnlyList<ITextReader> Installed()
        {
            var bundled = Path.Combine(BundledDir, "tesseract");
            var bundledArgs = new[] { "--tessdata-dir", Path.Combine(BundledDir, "tessdata") };
            var hasBundled = File.Exists(bundled);
            var bundledLanguages = hasBundled ? Languages(bundled, bundledArgs) : new List<string>();
            if (hasBundled && bundledLanguages.Count == 0) {
                _log.Warn($"Rank reading: the bundled Tesseract ({bundled}) is there but couldn't run");
            }
            var system = ExternalTool.FindOnPath("tesseract");
            var systemLanguages = system != null ? Languages(system, Array.Empty<string>()) : new List<string>();

            var choices = Choose(bundledLanguages, systemLanguages);
            if (choices.Count == 0) {
                _log.Info(hasBundled
                    ? "Rank reading: no Tesseract runs here (the bundled one failed and none is installed), so names can't be read"
                    : "Rank reading: Tesseract isn't installed, so names can't be read (install tesseract-ocr)");
                return Array.Empty<ITextReader>();
            }
            _log.Info("Rank reading: reading names with " + string.Join(" and ", choices.Select(c =>
                $"{(c.Source == Source.Bundled ? "the bundled" : "the system's")} Tesseract ({c.Languages})")));
            return choices.Select(c => (ITextReader)(c.Source == Source.Bundled
                ? new TesseractTextReader(bundled, bundledArgs, c.Languages)
                : new TesseractTextReader(system, Array.Empty<string>(), c.Languages))).ToList();
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
            var result = await ExternalTool.RunAsync(_tesseract, _args, Png.Encode(image), TimeSpan.FromSeconds(20));
            if (result.ExitCode != 0) {
                _log.Debug($"Rank reading: tesseract failed: {result.Errors.Trim()}");
                return Array.Empty<string>();
            }
            var text = string.Join(" ", ExternalTool.Text(result.Output).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            return text.Length == 0 ? Array.Empty<string>() : new[] { text };
        }
    }
}
