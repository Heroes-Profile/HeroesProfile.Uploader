using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Heroesprofile.Uploader.Desktop.Gui.Views
{
    /// <summary>
    /// The main window's Theme 2 layout. Everything it shows comes from the shared
    /// MainWindowViewModel; what needs the window itself (Settings, restart) goes through <see cref="MainWindow"/>.
    /// </summary>
    public partial class Theme2MainView : UserControl
    {
        public Theme2MainView()
        {
            InitializeComponent();
        }

        private MainWindow Window => TopLevel.GetTopLevel(this) as MainWindow;

        private void Logo_PointerReleased(object sender, PointerReleasedEventArgs e) => Window?.OpenWebsite();

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window is MainWindow window) {
                await window.OpenSettingsAsync();
            }
        }

        private void RestartNow_Click(object sender, RoutedEventArgs e) => Window?.RestartNow();
    }
}
