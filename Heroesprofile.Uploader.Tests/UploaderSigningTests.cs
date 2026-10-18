using Heroesprofile.Uploader.Common;
using System.Net.Http;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>
/// The uploader's request signatures must be exactly what Heroes Profile works out (UploaderSignature on the
/// site: key = HMAC-SHA256(master, version), signature = HMAC-SHA256(key, "{time}\n{payload}"), hex). The
/// expected values were computed independently, in PHP as the site does and in Python.
/// </summary>
public class UploaderSigningTests
{
    [Fact]
    public void Keys_and_signatures_match_the_sites()
    {
        var key = UploaderSigning.DeriveKey("test-master", "3.1.4");
        Assert.Equal("bbbfca0caefda68a0b69a526e7a237c541652823e719b3e6195c5302036e1039", Convert.ToHexString(key).ToLowerInvariant());
        Assert.Equal("b666304f4157c9e3af6edd2b3609324d90fa541fb416219568ebf893519ec932", UploaderSigning.Signature(key, 1700000000, "{\"replayID\":1}"));
    }

    [Fact]
    public void Every_version_has_its_own_key()
    {
        Assert.NotEqual(UploaderSigning.DeriveKey("test-master", "3.1.4"), UploaderSigning.DeriveKey("test-master", "3.1.5"));
    }

    [Fact]
    public void A_build_without_a_key_signs_nothing()
    {
        // Test and CI builds carry no key.
        Assert.False(UploaderSigning.IsAvailable);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/");
        UploaderSigning.Sign(request.Headers, "payload");
        Assert.Empty(request.Headers);
    }

    [Fact]
    public void A_file_is_signed_over_its_sha256()
    {
        var path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, "abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", UploaderSigning.FileHash(path));
        }
        finally {
            File.Delete(path);
        }
    }
}
