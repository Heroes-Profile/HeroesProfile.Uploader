using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Heroesprofile.Uploader.Common
{
    /// <summary>
    /// Signs requests so Heroes Profile can tell a released build sent them. The release workflow gives each
    /// build its own key - HMAC-SHA256(a master secret it shares with the site, the build's version) - which
    /// the build carries as assembly metadata (UploaderSigningKey, UploaderSigningVersion; see the Desktop
    /// .csproj). A request is signed over "{unix time}\n{payload}" and sends the version, time and signature as
    /// headers; the site recomputes the key from the version and checks. The key itself never travels.
    /// Builds without a key (from source, CI) sign nothing, and the site treats them as unsigned.
    /// A deterrent, not a lock: the key sits in every installed copy.
    /// </summary>
    public static class UploaderSigning
    {
        private static readonly Lazy<(string Version, byte[] Key)?> _key = new Lazy<(string, byte[])?>(Load);

        /// <summary>Whether this build has a key to sign with.</summary>
        public static bool IsAvailable => _key.Value != null;

        /// <summary>
        /// Adds the signature headers for <paramref name="payload"/> - the request's body, or for a replay upload
        /// the file's SHA-256 (<see cref="FileHash"/>) - when this build can sign; does nothing when it can't.
        /// </summary>
        public static void Sign(HttpRequestHeaders headers, string payload)
        {
            if (_key.Value is not var (version, key)) {
                return;
            }
            var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            headers.Add("X-HP-Uploader-Version", version);
            headers.Add("X-HP-Timestamp", time.ToString());
            headers.Add("X-HP-Signature", Signature(key, time, payload));
        }

        /// <summary>A file's SHA-256 as lowercase hex: what a replay upload is signed over.</summary>
        public static string FileHash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>The signature of <paramref name="payload"/> sent at <paramref name="unixTime"/>, as lowercase hex.</summary>
        public static string Signature(byte[] key, long unixTime, string payload)
        {
            using var hmac = new HMACSHA256(key);
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{unixTime}\n{payload}"))).ToLowerInvariant();
        }

        /// <summary>A version's key from the master secret - what the release workflow works out for each build.</summary>
        public static byte[] DeriveKey(string masterSecret, string version)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(masterSecret));
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(version));
        }

        private static (string Version, byte[] Key)? Load()
        {
            try {
                var metadata = (Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>() ?? Enumerable.Empty<AssemblyMetadataAttribute>())
                    .ToDictionary(a => a.Key, a => a.Value);
                if (metadata.TryGetValue("UploaderSigningKey", out var hex) && metadata.TryGetValue("UploaderSigningVersion", out var version)
                    && !string.IsNullOrEmpty(hex) && !string.IsNullOrEmpty(version)) {
                    return (version, Convert.FromHexString(hex));
                }
            }
            catch (Exception) {
                // A malformed key is no key: requests go unsigned.
            }
            return null;
        }
    }
}
