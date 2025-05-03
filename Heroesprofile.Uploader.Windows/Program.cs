using Microsoft.VisualBasic.ApplicationServices;
using NLog.Config;
using NLog.Targets;
using NLog;
using Squirrel;
using System.IO;
using System;
using System.Windows.Forms;

namespace Heroesprofile.Uploader.Windows
{
    internal static class Program
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        [STAThread]
        public static void Main(string[] args)
        {
            try {
                _log.Debug("Main started");
                // Squirrel + directory setup...
                if (!App.NoSquirrel) {
                    SquirrelAwareApp.HandleEvents(
                        onInitialInstall: v => App.DummyUpdateManager.CreateShortcutForThisExe(),
                        onAppUpdate: v => App.DummyUpdateManager.CreateShortcutForThisExe(),
                        onAppUninstall: v => {
                            App.DummyUpdateManager.RemoveShortcutForThisExe();
                            if (Directory.Exists(App.SettingsDir)) {
                                Directory.Delete(App.SettingsDir, true);
                            }
                        });
                }

                if (!Directory.Exists(App.SettingsDir)) {
                    Directory.CreateDirectory(App.SettingsDir);
                }

                SingleInstanceManager manager = new SingleInstanceManager();
                manager.Run(args);
            }
            catch (Exception ex) {
                _log.Error(ex, "An error occurred in Program.Main");

                // Show a MessageBox to notify the user to review the log
                MessageBox.Show("An error occurred. Please review the log for more details.",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }
    }

    public class SingleInstanceManager : WindowsFormsApplicationBase
    {
        private App _application;

        public SingleInstanceManager()
        {
            IsSingleInstance = true;
            ShutdownStyle = ShutdownMode.AfterAllFormsClose;
        }

        protected override bool OnStartup(StartupEventArgs eventArgs)
        {
            _application = new App();
            _application.InitializeComponent();
            _application.Run();
            return false;
        }

        protected override void OnStartupNextInstance(StartupNextInstanceEventArgs eventArgs)
        {
            base.OnStartupNextInstance(eventArgs);
            _application.Activate();
        }
    }
}
