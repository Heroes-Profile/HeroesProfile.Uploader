using System.Runtime.CompilerServices;
using Heroesprofile.Uploader.Desktop.Platform;

namespace Heroesprofile.Uploader.Tests;

/// <summary>
/// Points the whole test run at a scratch home folder before anything reads <see cref="Platforms.Current"/>,
/// so no test can touch the developer's real config.json, upload history - or, on a Mac, the real
/// Keychain item holding their Twitch key.
/// </summary>
internal static class TestHome
{
    public static readonly string Folder = Path.Combine(Path.GetTempPath(), $"hp-uploader-tests-{Environment.ProcessId}");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(Platforms.HomeOverrideVariable, Folder);
    }
}
