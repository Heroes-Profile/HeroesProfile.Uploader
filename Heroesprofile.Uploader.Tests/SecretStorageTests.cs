using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Platform;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>The Twitch key in config.json, on the real platform of whichever OS runs the tests.</summary>
[Collection(StoredTwitchKey.Collection)]
public class SecretStorageTests
{
    private const string Key = "twitch-uploader-key-1234";

    [Fact]
    public void The_Twitch_key_survives_a_save_and_load()
    {
        var json = new AppConfig { TwitchUploaderKey = Key }.ToJson();

        Assert.Equal(Key, AppConfig.FromJson(json).TwitchUploaderKey);
    }

    [Fact]
    public void On_Windows_the_Twitch_key_is_never_written_in_plain_text()
    {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        var json = new AppConfig { TwitchUploaderKey = Key }.ToJson();

        Assert.DoesNotContain(Key, json);
        Assert.Contains("dpapi:", json);
    }

    [Fact]
    public void A_plain_Twitch_key_from_a_Linux_config_still_loads()
    {
        Assert.Equal(Key, AppConfig.FromJson($$"""{ "TwitchUploaderKey": "{{Key}}" }""").TwitchUploaderKey);
    }

    [Fact]
    public void A_key_that_cannot_be_decrypted_comes_back_empty()
    {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        Assert.Equal("", Platforms.Current.UnprotectSecret("dpapi:bm90IGEgZHBhcGkgYmxvYg=="));
    }

    [Fact]
    public void The_WPF_apps_DPAPI_key_format_decrypts()
    {
        if (!OperatingSystem.IsWindows()) {
            return;
        }
        // What the WPF app stored: base64 of ProtectedData.Protect(UTF-8 key, no entropy, CurrentUser).
        var wpfStored = Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(Key), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

        Assert.Equal(Key, WindowsPlatform.UnprotectFromBase64(wpfStored));
    }

    [Fact]
    public void Delete_after_upload_is_saved_by_name()
    {
        var json = new AppConfig { DeleteAfterUpload = DeleteFiles.PTR | DeleteFiles.Ai }.ToJson();

        Assert.Contains("\"DeleteAfterUpload\": \"PTR, Ai\"", json);
        Assert.Equal(DeleteFiles.PTR | DeleteFiles.Ai, AppConfig.FromJson(json).DeleteAfterUpload);
    }
}
