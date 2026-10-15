using Avalonia.Controls;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Desktop.Gui.Views
{
    /// <summary>A small modal message with one or more buttons - what WPF's MessageBox.Show did.</summary>
    public partial class MessageDialog : Window
    {
        public MessageDialog()
        {
            InitializeComponent();
        }

        /// <summary>Shows <paramref name="message"/> over <paramref name="owner"/> and returns the label of the button pressed (null if closed).</summary>
        public static async Task<string> ShowAsync(Window owner, string title, string message, params string[] buttons)
        {
            var dialog = new MessageDialog { Title = title };
            dialog.MessageText.Text = message;
            string pressed = null;
            foreach (var label in buttons.Length > 0 ? buttons : new[] { "OK" }) {
                var button = new Button { Content = label, MinWidth = 80 };
                button.Click += (_, __) => {
                    pressed = label;
                    dialog.Close();
                };
                dialog.ButtonPanel.Children.Add(button);
            }
            await dialog.ShowDialog(owner);
            return pressed;
        }
    }
}
