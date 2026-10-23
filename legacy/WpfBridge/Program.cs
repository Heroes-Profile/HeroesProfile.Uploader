using Squirrel;
using System;
using System.Windows;

namespace Heroesprofile.Uploader.WpfBridge
{
    /// <summary>
    /// The WPF uploader's last update. It replaces 2.x in place through Squirrel, so it answers Squirrel's
    /// install and update events like the old app did (refreshing its shortcuts), then shows
    /// <see cref="NoticeWindow"/>. It never uploads.
    /// </summary>
    internal static class Program
    {
        // The page that picks the right uploader for the visitor's system.
        public const string DownloadPage = "https://www.heroesprofile.com/Upload/Uploader";

        [STAThread]
        public static void Main(string[] args)
        {
            try {
                using (var squirrel = new UpdateManager("not needed here")) {
                    SquirrelAwareApp.HandleEvents(
                        onInitialInstall: v => squirrel.CreateShortcutForThisExe(),
                        onAppUpdate: v => squirrel.CreateShortcutForThisExe(),
                        // Shortcuts only. The old app also deleted %APPDATA%\Heroesprofile here, its settings and
                        // upload history - which the new uploader imports, so it's left for that.
                        onAppUninstall: v => squirrel.RemoveShortcutForThisExe());
                }
            }
            catch (Exception) {
                // Not installed by Squirrel (run from a build folder): nothing to handle.
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new NoticeWindow());
        }
    }
}
