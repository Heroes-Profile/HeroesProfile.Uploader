using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Reads a line of text off an image (dark text on white - see <see cref="CardText"/>), offline, with what
    /// the OS has: Windows' own reader (Windows/WindowsTextReader), Tesseract on Linux
    /// (<see cref="TesseractTextReader"/>), Apple's Vision on macOS (<see cref="MacTextReader"/>).
    /// </summary>
    internal interface ITextReader
    {
        /// <summary>The language (or languages) it reads, e.g. "en-US", "eng+kor", "auto".</summary>
        string Language { get; }

        /// <summary>What it read: one or more alternative readings, none when it read nothing.</summary>
        Task<IReadOnlyList<string>> ReadAsync(Pixels image);
    }

    /// <summary>The text readers for the OS the app runs on.</summary>
    internal static class TextReaders
    {
        private static readonly Lazy<IReadOnlyList<ITextReader>> _all = new Lazy<IReadOnlyList<ITextReader>>(Create);

        /// <summary>
        /// Every reader there is here - one per installed language where the OS reads one script at a time
        /// (Windows), so a name in any of them can be read. Empty when the OS has none.
        /// </summary>
        public static IReadOnlyList<ITextReader> All => _all.Value;

        /// <summary>The reader for numbers: an English one when there is, else the first.</summary>
        public static ITextReader Digits =>
            All.FirstOrDefault(r => r.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? All.FirstOrDefault();

        private static IReadOnlyList<ITextReader> Create()
        {
            try {
#if WINDOWS_CAPTURE
                if (OperatingSystem.IsWindows()) {
                    return WindowsTextReader.Installed();
                }
#endif
                if (OperatingSystem.IsLinux()) {
                    return TesseractTextReader.Installed();
                }
                if (OperatingSystem.IsMacOS()) {
                    return MacTextReader.Installed();
                }
            }
            catch (Exception ex) {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Rank reading: no text reader available");
            }
            return Array.Empty<ITextReader>();
        }
    }
}
