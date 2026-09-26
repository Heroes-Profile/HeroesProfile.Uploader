using Avalonia;

namespace Heroesprofile.Uploader.Linux.Gui
{
    /// <summary>Avalonia bootstrapping - kept separate from Program.cs so the CLI paths never pull in Avalonia at all.</summary>
    public static class Gui
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }

        /// <summary>Runs the GUI to completion (blocks until the window/app exits) and returns its exit code.</summary>
        internal static int Run(bool minimized, SingleInstance instance)
        {
            App.StartMinimized = minimized;
            App.Instance = instance;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(minimized ? new[] { "--minimized" } : System.Array.Empty<string>());
        }
    }
}
