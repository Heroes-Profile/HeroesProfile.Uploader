using System;
using System.Diagnostics;
using System.Linq;

namespace Heroesprofile.Uploader.Desktop.Migration
{
    /// <summary>
    /// The WPF uploader this app replaces. Both keep their upload history in the same file
    /// (%APPDATA%\Heroesprofile\replays_v8.xml), so running them side by side has each overwrite the
    /// other's record of what's been uploaded.
    /// </summary>
    internal static class LegacyApp
    {
        // The WPF app's executable name (its AssemblyName).
        private const string ProcessName = "Heroesprofile.Uploader";

        public const string RunningWarning =
            "The old Heroes Profile uploader is still running. Running both at once makes them overwrite " +
            "each other's upload history, so close the old one first (right-click its tray icon, or quit it " +
            "from the Task Manager).";

        /// <summary>True when the WPF app is running for any user on this machine. Always false off Windows.</summary>
        public static bool IsRunning()
        {
            if (!OperatingSystem.IsWindows()) {
                return false;
            }
            var processes = Process.GetProcessesByName(ProcessName);
            try {
                return processes.Any();
            }
            finally {
                foreach (var p in processes) {
                    p.Dispose();
                }
            }
        }
    }
}
