namespace Heroesprofile.Uploader.Tests;

/// <summary>
/// Saving a config stores (or, when it's empty, deletes) the Twitch key - on macOS in one Keychain item
/// for the whole test run's scratch home. Test classes that save configs share this collection so they
/// run one after another instead of clobbering each other's key.
/// </summary>
internal static class StoredTwitchKey
{
    public const string Collection = "Stored Twitch key";
}
