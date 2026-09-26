using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Desktop.Updates
{
    /// <summary>
    /// "Is there a newer release?" and nothing more, for copies Velopack can't update: the headless
    /// `run` service (so journalctl shows it), the Linux tarball, and dev builds. Looks at the same
    /// GitHub releases Velopack does, and leaves the updating to the user.
    /// </summary>
    internal sealed class ReleaseChecker
    {
        public sealed class Release
        {
            public ReleaseVersion Version { get; init; }
            public string Url { get; init; }
        }

        private readonly HttpClient _http;

        public ReleaseChecker(HttpClient http = null)
        {
            _http = http ?? new HttpClient();
            if (_http.DefaultRequestHeaders.UserAgent.Count == 0) {
                // GitHub's API rejects requests without one.
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("heroesprofile-uploader");
            }
        }

        /// <summary>The newest release in <paramref name="repository"/> ("owner/repo") newer than this build, or null. Throws on network errors.</summary>
        public async Task<Release> FindNewerAsync(string repository, bool includePreReleases)
        {
            var json = await _http.GetStringAsync($"https://api.github.com/repos/{repository}/releases?per_page=30");
            return Newest(JArray.Parse(json), includePreReleases, ReleaseVersion.Current());
        }

        /// <summary>The newest non-draft release in a GitHub releases API response that's newer than <paramref name="current"/>.</summary>
        internal static Release Newest(JArray releases, bool includePreReleases, ReleaseVersion current)
        {
            Release best = null;
            foreach (var release in releases) {
                if ((bool?)release["draft"] == true || (!includePreReleases && (bool?)release["prerelease"] == true)) {
                    continue;
                }
                var version = ReleaseVersion.Parse((string)release["tag_name"]);
                if (version != null && version > current && (best == null || version > best.Version)) {
                    best = new Release { Version = version, Url = (string)release["html_url"] };
                }
            }
            return best;
        }
    }
}
