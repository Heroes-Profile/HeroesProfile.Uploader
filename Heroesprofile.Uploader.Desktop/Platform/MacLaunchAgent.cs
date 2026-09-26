using System;
using System.Security;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    /// <summary>
    /// The text side of macOS start-on-login (see <see cref="MacPlatform.SetStartOnLogin"/>): kept apart
    /// from MacPlatform because it's plain string work that can be tested on any OS.
    /// </summary>
    internal static class MacLaunchAgent
    {
        /// <summary>The .app bundle <paramref name="exe"/> runs from (…/Name.app/Contents/MacOS/exe), or null.</summary>
        public static string AppBundleOf(string exe)
        {
            // macOS paths, handled as '/' strings so this behaves (and tests) the same on any OS.
            const string Inside = ".app/Contents/MacOS/";
            var at = exe?.LastIndexOf(Inside, StringComparison.OrdinalIgnoreCase) ?? -1;
            if (at < 0 || exe.IndexOf('/', at + Inside.Length) >= 0) {
                return null;
            }
            return exe.Substring(0, at + ".app".Length);
        }

        public static string Plist(string label, string[] programArguments)
        {
            var args = string.Concat(Array.ConvertAll(programArguments, a => $"\n    <string>{SecurityElement.Escape(a)}</string>"));
            return
$@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
  <key>Label</key>
  <string>{SecurityElement.Escape(label)}</string>
  <key>ProgramArguments</key>
  <array>{args}
  </array>
  <key>RunAtLoad</key>
  <true/>
  <key>ProcessType</key>
  <string>Interactive</string>
</dict>
</plist>
";
        }
    }
}
