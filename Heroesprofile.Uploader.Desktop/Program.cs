using Heroesprofile.Uploader.Desktop.Gui;
using Heroesprofile.Uploader.Desktop.Platform;
using NLog;
using System;
using System.Linq;
using System.Threading.Tasks;
using Velopack;

namespace Heroesprofile.Uploader.Desktop
{
    internal static class Program
    {
        // No async Main: Avalonia's classic desktop lifetime is itself a blocking call, so the GUI
        // path and the CLI paths (which just block on .GetAwaiter().GetResult()) both fit a plain
        // synchronous entry point - and Avalonia wants to own the startup thread directly.
        private static int Main(string[] args)
        {
            // The first non-flag argument is the subcommand; no args (or just "--minimized") means "launch the GUI".
            var command = FindCommand(args);

            // Velopack first, before anything else reads the arguments: it handles its install/update/
            // uninstall hooks here (and exits for those), and applies an update an earlier run downloaded,
            // restarting into the new version. Not for the headless `run`: under systemd that restart
            // would be killed along with the old process, so the service just keeps its version.
            var restartedAfterUpdate = false;
            var velopack = VelopackApp.Build()
                .SetAutoApplyOnStartup(command != "run")
                .OnRestarted(_ => restartedAfterUpdate = true);
            if (OperatingSystem.IsWindows()) {
                // Only Windows installs have an uninstaller to hook into.
                velopack.OnBeforeUninstallFastCallback(_ => BeforeUninstall());
            }
            velopack.Run();

            // Log anything that escapes, like the WPF app's SetExceptionHandlers - the log is often
            // all a bug report has to go on.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogManager.GetLogger("Unhandled").Fatal(e.ExceptionObject as Exception, "Unhandled exception");
            TaskScheduler.UnobservedTaskException += (_, e) =>
                LogManager.GetLogger("Unhandled").Error(e.Exception, "Unobserved task exception");

            if (args.Contains("--help") || args.Contains("-h")) {
                PrintHelp();
                return 0;
            }

            if (args.Contains("--version")) {
                PrintVersion();
                return 0;
            }

            var replayPathOverride = GetOptionValue(args, "--prefix");

            try {
                switch (command) {
                    case null: {
                        ConfigureLoggingForCommand();
                        // Just restarted by an update: the old instance may still be on its way out, so
                        // wait for it rather than handing over to it.
                        using var instance = SingleInstance.TryAcquire(waitForPrevious: restartedAfterUpdate);
                        if (instance == null) {
                            Console.WriteLine("Heroes Profile Uploader is already running - showing its window.");
                            return 0;
                        }
                        if (OperatingSystem.IsLinux()) {
                            DesktopIntegration.RefreshInstalledCopyIfStale();
                        }
                        return Gui.Gui.Run(minimized: args.Contains("--minimized"), instance);
                    }

                    case "run":
                        ConfigureLoggingForCommand();
                        if (OperatingSystem.IsLinux()) {
                            DesktopIntegration.RefreshInstalledCopyIfStale();
                        }
                        return RunCommand.Execute(replayPathOverride).GetAwaiter().GetResult();

                    case "scan":
                        if (!args.Contains("--dry-run")) {
                            Console.Error.WriteLine("scan currently only supports --dry-run.\n");
                            PrintHelp();
                            return 1;
                        }
                        ConfigureLoggingForCommand();
                        return ScanCommand.Execute(replayPathOverride).GetAwaiter().GetResult();

                    case "install":
                    case "uninstall":
                        if (!OperatingSystem.IsLinux()) {
                            Console.Error.WriteLine($"`{command}` is only needed on Linux.");
                            return 1;
                        }
                        return command == "install" ? InstallCommand.Install() : InstallCommand.Uninstall();

                    default:
                        Console.Error.WriteLine($"Unknown command '{command}'.\n");
                        PrintHelp();
                        return 1;
                }
            }
            catch (ConfigError ex) {
                // A setup problem the user can fix - the message says how, a stack trace would not help.
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Fatal error: {ex}");
                return 1;
            }
        }

        /// <summary>
        /// Velopack's uninstall hook (Windows): remove the Run key, so nothing starts the removed app at
        /// login. Settings and upload history are kept - the WPF app deleted them on uninstall, but this
        /// hook runs without a window to ask, and a reinstall shouldn't have to re-upload everything.
        /// </summary>
        private static void BeforeUninstall()
        {
            try {
                Platforms.Current.SetStartOnLogin(false);
            }
            catch (Exception ex) {
                LogManager.GetCurrentClassLogger().Warn(ex, "Uninstall cleanup failed");
            }
        }

        /// <summary>
        /// Sets up NLog at the configured log level before a command (or the GUI) starts, so its
        /// own "no prefix configured"-style messages are logged too. Falls back to Info if the
        /// config can't be read yet - the command re-loads it right after and reports the real error.
        /// </summary>
        private static void ConfigureLoggingForCommand()
        {
            LogLevel level;
            try {
                level = Logging.ParseLevel(AppConfig.Load().LogLevel);
            }
            catch (ConfigError) {
                level = LogLevel.Info;
            }
            Logging.Configure(level);
        }

        private static string GetOptionValue(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        // Options that consume the following argument as their value - anything here must be skipped
        // over (both the flag and its value) when scanning for the subcommand, or e.g.
        // "--prefix /p run" would misread "/p" as the command instead of "run".
        private static readonly string[] OptionsWithValues = { "--prefix" };

        private static string FindCommand(string[] args)
        {
            for (var i = 0; i < args.Length; i++) {
                if (!args[i].StartsWith("-", StringComparison.Ordinal)) {
                    return args[i];
                }
                if (Array.IndexOf(OptionsWithValues, args[i]) >= 0) {
                    i++; // skip this option's value too, not just the option itself
                }
            }
            return null;
        }

        private static void PrintVersion()
        {
            // What Uploader.cs actually sends as ?version= - it reads Assembly.GetExecutingAssembly()
            // from inside Common.dll, so this is Common's AssemblyVersion, not this exe's.
            var commonVersion = typeof(Heroesprofile.Uploader.Common.ReplayLocation).Assembly.GetName().Version;
            // This build's own full version, prerelease suffix and all (e.g. "2.8.0-test.91" rather
            // than a bare "2.8.0") - the plain AssemblyVersion can't tell two test builds of the same
            // release apart (it drops the suffix), which both a human comparing `--version` output and
            // DesktopIntegration.RefreshInstalledCopyIfStale (which parses this exact line) need to.
            var appVersion = ReleaseVersion.Current();
            Console.WriteLine($"heroesprofile-uploader ({Platforms.Current.Name}) {appVersion} - Heroesprofile.Uploader.Common {commonVersion}");
        }

        private static void PrintHelp()
        {
            var linux = OperatingSystem.IsLinux();
            var replayPathHelp = linux
                ? "Wine/Proton prefix root, a Steam compatdata/<appid> folder, or the HotS\n" +
                  "                     \"Accounts\" folder directly."
                : "The Heroes of the Storm \"Accounts\" folder. Defaults to the game's own\n" +
                  "                     location.";
            var installHelp = linux
                ? @"
  heroesprofile-uploader install
      Copy this binary to ~/.local/bin and add it to the app menu (and, if
      ""Start on login"" is on in config.json, to XDG autostart).

  heroesprofile-uploader uninstall
      Remove what `install` added. Doesn't touch your config or replay data.
"
                : "";

            Console.WriteLine(
$@"heroesprofile-uploader - Heroes Profile replay uploader ({Platforms.Current.Name})

Usage:
  heroesprofile-uploader
      Launch the GUI. If it can't find your replays, it asks you to browse to them.

  heroesprofile-uploader --minimized
      Launch the GUI already minimized to the tray (used by the start-on-login entry).

  heroesprofile-uploader run [--prefix <path>]
      Headless: watch the replay folder and upload new replays as they appear.
      Runs until stopped (Ctrl+C, SIGTERM, or `systemctl --user stop`).

  heroesprofile-uploader scan --dry-run [--prefix <path>]
      Analyze every replay under the replay folder and report what would be
      uploaded, without uploading or writing anything.
{installHelp}
  heroesprofile-uploader --version
      Print the Heroesprofile.Uploader.Common version sent to the Heroes Profile API.

  heroesprofile-uploader --help
      Show this message.

Options:
  --prefix <path>   {replayPathHelp}
                     Overrides ""ReplayPath"" in the config file.

Config file ({AppConfig.ConfigPath}):
  {{ ""ReplayPath"": ""/path/to/replays"", ""PreMatchPage"": false, ""PostMatchPage"": false, ""WebhookUrl"": """" }}

Replay storage and logs are kept under {AppConfig.DataDir}.
Set {Platforms.HomeOverrideVariable} to a folder to keep all of them there instead (a portable
install, or a test run that mustn't touch your real settings).");
        }
    }
}
