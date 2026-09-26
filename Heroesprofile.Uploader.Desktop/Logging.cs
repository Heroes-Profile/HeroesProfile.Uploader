using NLog;
using NLog.Config;
using NLog.Targets;
using System.IO;

namespace Heroesprofile.Uploader.Desktop
{
    /// <summary>
    /// Console + rolling file logging, configured in code so a single published binary needs no
    /// NLog.config sitting next to it. Mirrors the layout and archive settings the Windows app's
    /// NLog.config uses, just aimed at the XDG data dir instead of ApplicationData.
    /// </summary>
    internal static class Logging
    {
        /// <summary>Where the log file lives - shared with the GUI's "Show log" (xdg-open) command.</summary>
        public static string LogFilePath => Path.Combine(AppConfig.DataDir, "logs", "log.txt");

        /// <param name="fileLevel">
        /// Minimum level written to the log file - the GUI's Settings "Log level" field. The console
        /// target always stays at Info+, matching the CLI's original behaviour (systemd captures it
        /// via journald regardless of this setting).
        /// </param>
        public static void Configure(LogLevel fileLevel = null)
        {
            var config = new LoggingConfiguration();

            var console = new ConsoleTarget("console") {
                Layout = "${uppercase:${level}}: ${message} ${exception:format=tostring}",
            };
            config.AddRule(LogLevel.Info, LogLevel.Fatal, console);

            var logDir = Path.Combine(AppConfig.DataDir, "logs");
            Directory.CreateDirectory(logDir);

            // ArchiveFileName keeps NLog's "move the active file aside, then recreate it" archiving, so
            // the current log is always log.txt (what "Show log" opens). Without it NLog 6 rolls on to
            // new numbered files instead.
            var file = new FileTarget("logfile") {
                FileName = Path.Combine(logDir, "log.txt"),
                ArchiveFileName = Path.Combine(logDir, "log.{#}.txt"),
                ArchiveAboveSize = 10_000_000,
                MaxArchiveFiles = 3,
                Layout = "[${longdate}] ${uppercase:${level}}: ${message} ${exception:format=tostring}",
            };
            config.AddRule(fileLevel ?? LogLevel.Debug, LogLevel.Fatal, file);

            LogManager.Configuration = config;
        }

        /// <summary>Parses a config "logLevel" string (as saved by the settings dialog), defaulting to Info.</summary>
        public static LogLevel ParseLevel(string name)
        {
            return LogLevel.FromString(string.IsNullOrWhiteSpace(name) ? "Info" : name);
        }
    }
}
