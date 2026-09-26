using System.IO;
using System.Linq;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// Finds the folders HotS writes to inside a Wine/Proton prefix. Accepts what a user might
    /// reasonably point us at: a prefix root (containing drive_c/users/*/...), a Steam
    /// compatdata/&lt;appid&gt; folder (Proton keeps the actual prefix under its pfx/ subfolder), or,
    /// for the Accounts lookup, the Accounts folder itself.
    /// </summary>
    internal static class WinePrefix
    {
        /// <summary>drive_c/users/*/Documents/Heroes of the Storm/Accounts, or null if there isn't one</summary>
        public static string FindAccounts(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
                return null;
            }

            if (Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) == "Accounts") {
                return path;
            }

            return FindUnderUsers(path, "Documents", "Heroes of the Storm", "Accounts");
        }

        /// <summary>
        /// drive_c/users/*/AppData/Local/Temp, where the game writes the .battlelobby file the
        /// pre-match page and Twitch extension read. Null if there isn't one.
        /// </summary>
        public static string FindTemp(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
                return null;
            }

            return FindUnderUsers(path, "AppData", "Local", "Temp");
        }

        private static string FindUnderUsers(string path, params string[] relativeSegments)
        {
            // Proton keeps the actual Wine prefix under pfx/ inside a Steam compatdata/<appid> folder
            return FindUnderPrefixUsers(Path.Combine(path, "pfx"), relativeSegments)
                ?? FindUnderPrefixUsers(path, relativeSegments);
        }

        private static string FindUnderPrefixUsers(string prefixRoot, string[] relativeSegments)
        {
            var usersDir = Path.Combine(prefixRoot, "drive_c", "users");
            if (!Directory.Exists(usersDir)) {
                return null;
            }

            return Directory.EnumerateDirectories(usersDir)
                .Select(userDir => Path.Combine(new[] { userDir }.Concat(relativeSegments).ToArray()))
                .FirstOrDefault(Directory.Exists);
        }
    }
}
