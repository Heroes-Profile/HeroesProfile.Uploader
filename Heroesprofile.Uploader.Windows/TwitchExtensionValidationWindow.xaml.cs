using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Heroesprofile.Uploader.Windows.Properties;

namespace Heroesprofile.Uploader.Windows
{

    /// <summary>
    /// Interaction logic for TwitchExtensionValidationWindow.xaml
    /// </summary>
    public partial class TwitchExtensionValidationWindow : Window
    {
        public string email { get; set; }
        public string twitch_nickname { get; set; }
        public string hp_key { get; set; }

        public TwitchExtensionValidationWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            this.twitch_nickname = Settings.Default.TwitchNickname;
            this.hp_key = Settings.Default.HPKey;
            this.email = Settings.Default.HPAPIEmail;
        }

        private void validate_button_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.HPAPIEmail = email;
            Settings.Default.TwitchNickname = twitch_nickname;
            Settings.Default.HPKey = hp_key;

            // Always reset validation and disable the extension
            Settings.Default.HPTwitchValidated = false;
            Settings.Default.HPTwitchExtension = false;

            Settings.Default.HPTwitchValidated = TwitchSettingsValidator.Validate();

            validation_successful_label.Visibility = Settings.Default.HPTwitchValidated ? Visibility.Visible : Visibility.Hidden;

            validate_button.Content = Settings.Default.HPTwitchValidated ? "Validate" : "Validate (Try again)";
            validate_button.Visibility = Settings.Default.HPTwitchValidated ? Visibility.Hidden : Visibility.Visible;

            if (Settings.Default.HPTwitchValidated) {
                Settings.Default.Save();
            }
        }
    }
}
