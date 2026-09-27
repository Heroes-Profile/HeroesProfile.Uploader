using System.IO;
using System.Linq;

namespace Heroesprofile.Uploader.Desktop.Platform
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
        /// pre-match page and Twitch extension read. Null if there isn't one. <paramref name="path"/> is
        /// normally the prefix, but may be a folder inside it - typically the "Accounts" folder, which is
        /// also accepted as the replay path: the prefix is then the folder above its drive_c.
        /// </summary>
        public static string FindTemp(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
                return null;
            }

            return FindUnderUsers(path, "AppData", "Local", "Temp")
                ?? (ContainingPrefix(path) is string prefix ? FindUnderUsers(prefix, "AppData", "Local", "Temp") : null);
        }

        /// <summary>The Wine prefix <paramref name="path"/> is inside (the parent of its drive_c), or null.</summary>
        private static string ContainingPrefix(string path)
        {
            for (var dir = new DirectoryInfo(path); dir?.Parent != null; dir = dir.Parent) {
                if (dir.Name == "drive_c") {
                    return dir.Parent.FullName;
                }
            }
            return null;
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
