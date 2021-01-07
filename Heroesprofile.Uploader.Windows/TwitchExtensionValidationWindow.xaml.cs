using System;
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

using System.Net.Http;
using System.Net.Http.Headers;
using NLog;

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

        private static readonly string validationUrl = @"twitch/validate/heroesprofile/token";
        HttpClient client = new HttpClient();
        public int user_id;
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public TwitchExtensionValidationWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            defaultConfigurationSetup();

        }

        private void defaultConfigurationSetup()
        {
            client.BaseAddress = new Uri("https://api.heroesprofile.com/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private void validate_button_Click(object sender, RoutedEventArgs e)
        {
            validate_button.IsEnabled = false;
            string urlParameters = $"{validationUrl}?email={email}&twitch_nickname={twitch_nickname}&hp_twitch_key={hp_key}";

            // List data response.
            HttpResponseMessage response = client.GetAsync(urlParameters).Result;
            if (response.IsSuccessStatusCode) {
                if (Int32.TryParse(response.Content.ReadAsStringAsync().Result, out int value)) {
                    user_id = value;
                } else {
                    user_id = 0;
                }
                _log.Info("Heroes Profile Twitch Extension Validation Successfull");
            } else {
                _log.Warn("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }

            if (user_id != 0) {
                Properties.Settings.Default.HPAPIEmail = email;
                Properties.Settings.Default.TwitchNickname = twitch_nickname;
                Properties.Settings.Default.HPKey = hp_key;
                Properties.Settings.Default.HPAPIUserID = user_id;
                Properties.Settings.Default.HPTwitchExtension = true;
                Properties.Settings.Default.Save();

                validate_button.IsEnabled = false;
                validate_button.Visibility = Visibility.Hidden;
                validation_successful_label.Foreground = Brushes.Green;
                validation_successful_label.Visibility = Visibility.Visible;
            } else {
                email_textbox.Text = "Validation Failed";
                hp_key_textbox.Text = "Validation Failed";
                twitch_nickname_textbox.Text = "Validation Failed";
                validate_button.Content = "Try validation again";

                Properties.Settings.Default.HPAPIEmail = "";
                Properties.Settings.Default.TwitchNickname = "";
                Properties.Settings.Default.HPKey = "";
                Properties.Settings.Default.HPAPIUserID = 0;
                Properties.Settings.Default.HPTwitchExtension = false;
                validate_button.IsEnabled = true;
            }
        }
    }
}
