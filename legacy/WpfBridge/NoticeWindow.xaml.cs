using System.Diagnostics;
using System.Windows;

namespace Heroesprofile.Uploader.WpfBridge
{
    /// <summary>
    /// All the 2.9.1 update does: says this uploader is retired, and opens the download page, where the visitor
    /// picks the right uploader for their system. Nothing is downloaded or installed from here.
    /// </summary>
    public partial class NoticeWindow : Window
    {
        public NoticeWindow()
        {
            InitializeComponent();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            try {
                Process.Start(new ProcessStartInfo(Program.DownloadPage) { UseShellExecute = true });
            }
            catch {
                MessageBox.Show(this, "Couldn't open your browser. Go to " + Program.DownloadPage + " to get the new uploader.",
                    "Heroesprofile Uploader");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
