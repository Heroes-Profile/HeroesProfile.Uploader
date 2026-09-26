using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Heroesprofile.Uploader.Linux.Gui.ViewModels;
using System.Linq;

namespace Heroesprofile.Uploader.Linux.Gui.Views
{
    /// <summary>
    /// Prefix/theme/webhook/Twitch/log-level dialog. Mirrors the Windows SettingsWindow: Save
    /// commits, Cancel discards - neither is blocked by ShowDialog, so <see cref="Result"/> is what
    /// MainWindow checks afterwards, same shape as the Windows app's DialogResult use.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public bool Result { get; private set; }

        private SettingsWindowViewModel ViewModel => (SettingsWindowViewModel)DataContext;

        public SettingsWindow()
        {
            InitializeComponent();
        }

        private async void BrowsePrefix_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Select the Wine/Proton prefix (or the Heroes of the Storm \"Accounts\" folder)",
                AllowMultiple = false,
            });
            var folder = folders.FirstOrDefault();
            if (folder?.TryGetLocalPath() is string path) {
                ViewModel.PrefixPath = path;
            }
        }

        private void Cancel_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        private void Save_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Same rule as the Windows app's Window_Closing: don't let a broken webhook url get saved.
            if (!ViewModel.IsWebhookValid) {
                ViewModel.WebhookStatusText = "Invalid url. Must start with http:// or https://";
                return;
            }
            Result = true;
            Close();
        }
    }
}
