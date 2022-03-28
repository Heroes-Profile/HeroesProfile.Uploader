using Microsoft.VisualBasic.ApplicationServices;
using Squirrel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Windows
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
#pragma warning disable 162
            if (!App.NoSquirrel) {
                // Note, in most of these scenarios, the app exits after this method completes!
                // ReSharper disable HeuristicUnreachableCode
                SquirrelAwareApp.HandleEvents(
                    onInitialInstall: v => App.DummyUpdateManager.CreateShortcutForThisExe(),
                    onAppUpdate: v => App.DummyUpdateManager.CreateShortcutForThisExe(),
                    onAppUninstall: v => {
                        App.DummyUpdateManager.RemoveShortcutForThisExe();
                        if (Directory.Exists(App.SettingsDir)) {
                            Directory.Delete(App.SettingsDir, true);
                        }
                    });
                // ReSharper restore HeuristicUnreachableCode
            }
#pragma warning restore 162

            if (!Directory.Exists(App.SettingsDir)) {
                Directory.CreateDirectory(App.SettingsDir);
            }


            SingleInstanceManager manager = new SingleInstanceManager();
            manager.Run(args);
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
