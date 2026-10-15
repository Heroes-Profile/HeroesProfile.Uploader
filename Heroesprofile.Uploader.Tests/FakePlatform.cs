using Heroesprofile.Uploader.Desktop.Platform;

namespace Heroesprofile.Uploader.Tests;

/// <summary>An <see cref="IPlatform"/> with a real OS's replay-folder behaviour, but folders chosen by the test.</summary>
internal sealed class FakePlatform : IPlatform
{
    public static FakePlatform WindowsLike(string? defaultReplayFolder) => new() { Name = "Windows", DefaultReplayFolder = defaultReplayFolder };

    public static FakePlatform LinuxLike() => new() { Name = "Linux", ReplayPathIsWinePrefix = true, UseSettledMonitor = true };

    public string Name { get; init; } = "Test";
    public string ConfigDir => "config";
    public string DataDir => "data";
    public string? DefaultReplayFolder { get; init; }
    public string? DefaultBattleLobbyFolder { get; init; }
    public bool ReplayPathIsWinePrefix { get; init; }
    public bool UseSettledMonitor { get; init; }
    public bool SupportsStartOnLogin => false;
    public void SetStartOnLogin(bool enabled) { }
    public string SingleInstanceSocketPath => "instance.sock";
    public string ProtectSecret(string secret) => secret;
    public string UnprotectSecret(string stored) => stored;
}
