using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// The macOS rank helper (hp-rank-helper, built from packaging/macos/rank-helper/main.swift and shipped next
    /// to the app's executable in the macOS Ranks builds): Apple's screen capture and text reading are Swift and
    /// Objective-C APIs, which a small native program uses far more safely than .NET interop could.
    /// </summary>
    internal static class MacHelper
    {
        public const string FileName = "hp-rank-helper";

        // Its exit codes.
        public const int Ok = 0, NoWindow = 2, NoPermission = 3, TooOld = 4;

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

        public static bool Exists => File.Exists(FilePath);

        public static Task<ExternalTool.Result> RunAsync(string command, byte[] input, TimeSpan timeout) =>
            ExternalTool.RunAsync(FilePath, new[] { command }, input, timeout);
    }

    /// <summary>
    /// Capture on macOS: the helper's ScreenCaptureKit screenshot of the game's window, once per interval
    /// (macOS 14 or later). The app needs Screen Recording permission (System Settings, Privacy &amp; Security);
    /// macOS asks the first time.
    /// </summary>
    internal sealed class MacCaptureBackend : ICaptureBackend
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static bool _permissionWarned;

        public string Unsupported =>
            !MacHelper.Exists ? "this build has no macOS capture helper"
            : !OperatingSystem.IsMacOSVersionAtLeast(14) ? "capturing the game's window needs macOS 14 or later"
            : null;

        public async Task PrepareAsync(bool showOutline)
        {
            try {
                var result = await MacHelper.RunAsync("permission", null, TimeSpan.FromMinutes(2));
                if (result.ExitCode != MacHelper.Ok) {
                    _log.Warn("Rank reading: the uploader isn't allowed to record the screen - allow it in System Settings > Privacy & Security > Screen Recording, then restart the uploader");
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Rank reading: couldn't check the Screen Recording permission");
            }
        }

        public IDisposable Start(TimeSpan interval, bool showOutline, Action<RawFrame> onFrame) =>
            new PollingCapture(interval, Grab, onFrame, ex => _log.Debug($"Rank reading: couldn't capture a frame: {ex.Message}"));

        private static RawFrame Grab()
        {
            var result = MacHelper.RunAsync("capture", null, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            switch (result.ExitCode) {
                case MacHelper.Ok:
                    return Png.DecodeRaw(result.Output);
                case MacHelper.NoWindow:
                    return null;
                case MacHelper.NoPermission:
                    if (!_permissionWarned) {
                        _permissionWarned = true;
                        _log.Warn("Rank reading: no Screen Recording permission - allow the uploader in System Settings > Privacy & Security > Screen Recording");
                    }
                    return null;
                default:
                    _log.Debug($"Rank reading: the capture helper failed: {result.Errors.Trim()}");
                    return null;
            }
        }
    }

    /// <summary>
    /// Text reading on macOS: Apple's Vision, through the helper. It works out the language itself and gives
    /// several alternative readings, all of which go to the matcher.
    /// </summary>
    internal sealed class MacTextReader : ITextReader
    {
        public string Language => "auto";

        public static IReadOnlyList<ITextReader> Installed() =>
            MacHelper.Exists ? new ITextReader[] { new MacTextReader() } : Array.Empty<ITextReader>();

        public async Task<IReadOnlyList<string>> ReadAsync(Pixels image)
        {
            var result = await MacHelper.RunAsync("ocr", Png.Encode(image), TimeSpan.FromSeconds(20));
            if (result.ExitCode != MacHelper.Ok) {
                return Array.Empty<string>();
            }
            return ExternalTool.Text(result.Output).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        }
    }
}
