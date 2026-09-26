using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// A semver-shaped version parsed out of free-form text: a GitHub release tag (`v2.8.0`,
    /// `linux-v2.8.0-test.2`), this build's own AssemblyInformationalVersion (`2.8.0-test.4+3da8633` -
    /// the SDK appends "+&lt;commit&gt;" automatically when built inside a git repo; that's metadata,
    /// not part of the version, so it's stripped), or a `--version` invocation's stdout.
    /// Comparison follows semver precedence: numeric prerelease identifiers compare numerically
    /// (2.8.0-test.3 &lt; 2.8.0-test.10), and a version with no prerelease outranks one that has any
    /// (2.8.0 &gt; 2.8.0-test.99) - otherwise a real release would look older than its own test builds.
    /// </summary>
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        // Deliberately not anchored - callers hand this whole tag names ("linux-v2.8.0-test.2") and
        // --version output lines, not just bare version strings.
        private static readonly Regex Pattern = new Regex(@"\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?", RegexOptions.Compiled);

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>Dot-separated prerelease identifiers ("test", "3" for "-test.3"); empty = a real release.</summary>
        public string[] PreRelease { get; }

        /// <summary>The matched version text itself (no surrounding tag noise), e.g. "2.8.0-test.3".</summary>
        public string Text { get; }

        private ReleaseVersion(int major, int minor, int patch, string[] preRelease, string text)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease;
            Text = text;
        }

        /// <summary>Finds and parses the first semver-shaped token anywhere in <paramref name="text"/>, or null if there isn't one.</summary>
        public static ReleaseVersion Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }

            var match = Pattern.Match(text);
            if (!match.Success) {
                return null;
            }

            var core = match.Value;
            var dash = core.IndexOf('-');
            var numbers = (dash >= 0 ? core.Substring(0, dash) : core).Split('.');
            var preRelease = dash >= 0 ? core.Substring(dash + 1).Split('.') : Array.Empty<string>();

            return new ReleaseVersion(int.Parse(numbers[0]), int.Parse(numbers[1]), int.Parse(numbers[2]), preRelease, core);
        }

        /// <summary>
        /// This build's own version: AssemblyInformationalVersion (what `-p:Version=` at publish time
        /// actually sets, prerelease suffix and all) with any "+commit" metadata the SDK appends stripped.
        /// Falls back to the plain AssemblyVersion if there's no informational version for some reason.
        /// </summary>
        public static ReleaseVersion Current()
        {
            var assembly = typeof(ReleaseVersion).Assembly;
            var informational = assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            var text = informational;
            if (!string.IsNullOrWhiteSpace(text)) {
                var plus = text.IndexOf('+');
                if (plus >= 0) {
                    text = text.Substring(0, plus);
                }
            }

            return Parse(text) ?? Parse(assembly.GetName().Version?.ToString()) ?? new ReleaseVersion(0, 0, 0, Array.Empty<string>(), "0.0.0");
        }

        public int CompareTo(ReleaseVersion other)
        {
            if (other is null) {
                return 1;
            }

            var c = Major.CompareTo(other.Major);
            if (c != 0) {
                return c;
            }
            c = Minor.CompareTo(other.Minor);
            if (c != 0) {
                return c;
            }
            c = Patch.CompareTo(other.Patch);
            if (c != 0) {
                return c;
            }

            var hasPre = PreRelease.Length > 0;
            var otherHasPre = other.PreRelease.Length > 0;
            if (hasPre != otherHasPre) {
                // No prerelease outranks any prerelease of the same core version.
                return hasPre ? -1 : 1;
            }
            if (!hasPre) {
                return 0;
            }

            var fieldCount = Math.Max(PreRelease.Length, other.PreRelease.Length);
            for (var i = 0; i < fieldCount; i++) {
                if (i >= PreRelease.Length) {
                    return -1; // fewer identifiers = lower precedence (semver rule)
                }
                if (i >= other.PreRelease.Length) {
                    return 1;
                }
                c = ComparePreReleaseIdentifier(PreRelease[i], other.PreRelease[i]);
                if (c != 0) {
                    return c;
                }
            }
            return 0;
        }

        private static int ComparePreReleaseIdentifier(string a, string b)
        {
            var aIsNumber = int.TryParse(a, out var aNum);
            var bIsNumber = int.TryParse(b, out var bNum);
            if (aIsNumber && bIsNumber) {
                return aNum.CompareTo(bNum);
            }
            if (aIsNumber != bIsNumber) {
                // Semver rule: numeric identifiers always have lower precedence than alphanumeric ones.
                return aIsNumber ? -1 : 1;
            }
            return string.CompareOrdinal(a, b);
        }

        public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a is object && a.CompareTo(b) > 0;
        public static bool operator <(ReleaseVersion a, ReleaseVersion b) => b is object && b.CompareTo(a) > 0;
        public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => !(a < b);
        public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => !(a > b);

        public override string ToString() => Text;
    }
}
