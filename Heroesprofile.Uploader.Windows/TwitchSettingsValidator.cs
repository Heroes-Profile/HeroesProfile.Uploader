using System;

using System.Net.Http;
using System.Net.Http.Headers;
using NLog;
using Heroesprofile.Uploader.Windows.Properties;

namespace Heroesprofile.Uploader.Windows
{
    public static class TwitchSettingsValidator
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private static readonly string validationUrl = @"twitch/validate/heroesprofile/token";

        public static bool Validate()
        {
            if (string.IsNullOrWhiteSpace(Settings.Default.HPAPIEmail) || string.IsNullOrWhiteSpace(Settings.Default.HPKey) || string.IsNullOrWhiteSpace(Settings.Default.HPAPIEmail)) {
                return false;
            }

            using (HttpClient client = new HttpClient() { BaseAddress = new Uri("https://api.heroesprofile.com/") }) {

                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = client.GetAsync($"{validationUrl}?email={Settings.Default.HPAPIEmail}&twitch_nickname={Settings.Default.TwitchNickname}&hp_twitch_key={Settings.Default.HPKey}").Result;

                if (response.IsSuccessStatusCode) {
                    if (int.TryParse(response.Content.ReadAsStringAsync().Result, out int value)) {
                        _log.Info("Heroes Profile Twitch Extension Validation Successfull");
                        Settings.Default.HPAPIUserID = value;
                        return true;
                    } else {
                        Settings.Default.HPAPIUserID = 0;
                    }
                } else {
                    _log.Warn("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                }
            }

            return false;
        }
    }
}
