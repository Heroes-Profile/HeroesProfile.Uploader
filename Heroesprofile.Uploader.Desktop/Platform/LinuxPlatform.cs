using System;
using System.IO;
using System.Runtime.Versioning;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    [SupportedOSPlatform("linux")]
    internal sealed class LinuxPlatform : IPlatform
    {
        private static string XdgHome(string envVar, string fallbackLeaf)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallbackLeaf);
        }

        private readonly string _home;

        /// <param name="home">Folder for config, data and the socket instead of the usual places, or null (see Platforms.HomeOverrideVariable).</param>
        public LinuxPlatform(string home = null)
        {
            _home = home;
        }

        public string Name => "Linux";

        /// <summary>$XDG_CONFIG_HOME/heroesprofile, or ~/.config/heroesprofile</summary>
        public string ConfigDir => _home ?? Path.Combine(XdgHome("XDG_CONFIG_HOME", ".config"), "heroesprofile");

        /// <summary>$XDG_DATA_HOME/heroesprofile, or ~/.local/share/heroesprofile</summary>
        public string DataDir => _home ?? Path.Combine(XdgHome("XDG_DATA_HOME", Path.Combine(".local", "share")), "heroesprofile");

        // The game runs under Wine/Proton, so its folders are inside a prefix only the user knows.
        public string DefaultReplayFolder => null;
        public string DefaultBattleLobbyFolder => null;
        public bool ReplayPathIsWinePrefix => true;
        public bool UseSettledMonitor => true;

        public bool SupportsStartOnLogin => true;

        public void SetStartOnLogin(bool enabled) => DesktopIntegration.SetStartOnLogin(enabled);

        // $XDG_RUNTIME_DIR is per-user and private (0700); the data dir is the fallback when it isn't set.
        public string SingleInstanceSocketPath
        {
            get {
                if (_home != null) {
                    return Path.Combine(_home, "instance.sock");
                }
                var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return !string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir)
                    ? Path.Combine(runtimeDir, "heroesprofile-uploader.sock")
                    : Path.Combine(DataDir, "instance.sock");
            }
        }

        // config.json is 0600, the same trust boundary as an SSH key or .netrc.
        public string ProtectSecret(string secret) => secret ?? "";
        public string UnprotectSecret(string stored) => stored ?? "";
    }
}
