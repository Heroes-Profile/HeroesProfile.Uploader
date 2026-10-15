using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    /// <summary>
    /// Sends match page urls to a user configured webhook so external tools (e.g. Discord bots) can react to matches
    /// </summary>
    public static class WebhookNotifier
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private static readonly HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// Destination url. Empty or invalid urls disable notifications.
        /// </summary>
        public static string WebhookUrl { get; set; }

        /// <summary>
        /// Fire and forget a receiver agnostic json payload: "event" and "url" carry the structured data
        /// for custom receivers, while the same message is duplicated under "content" (Discord) and
        /// "text" (Slack) so those webhooks work directly. Receivers ignore fields they don't know.
        /// </summary>
        /// <param name="eventType">Event that produced the page, "prematch" or "postmatch"</param>
        /// <param name="matchUrl">Heroes Profile page url for the match</param>
        public static void Notify(string eventType, string matchUrl)
        {
            if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out Uri target) ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)) {
                return;
            }

            Task.Run(async () => {
                try {
                    var message = eventType == "prematch" ? $"Match started — prematch stats: {matchUrl}" : $"Match complete — results: {matchUrl}";
                    var payload = JsonConvert.SerializeObject(new Dictionary<string, string> {
                        { "event", eventType },
                        { "url", matchUrl },
                        { "content", message },
                        { "text", message },
                    });
                    _log.Debug($"Sending {eventType} webhook notification");
                    var response = await _client.PostAsync(target, new StringContent(payload, Encoding.UTF8, "application/json"));
                    if (!response.IsSuccessStatusCode) {
                        _log.Warn($"Webhook {eventType} notification returned {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex) {
                    _log.Warn(ex, $"Webhook {eventType} notification failed");
                }
            });
        }
    }
}
