using System.Net;
using System.Net.Http.Headers;
using Heroesprofile.Uploader.Common;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>
/// The Heroes Profile API calls, against a fake server: what the uploader sends, and how it reads
/// successes, duplicates, "too many requests" and failures.
/// </summary>
public sealed class UploaderTests : IDisposable
{
    private const string Fingerprint = "d2a3cb4e-5f60-4f7a-8b9c-0d1e2f3a4b5c";

    private readonly string _replay;

    public UploaderTests()
    {
        _replay = Path.Combine(Directory.CreateTempSubdirectory("hp-uploader-").FullName, "2026-09-20 21.14.03 Cursed Hollow.StormReplay");
        File.WriteAllBytes(_replay, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_replay)!, recursive: true);

    /// <summary>Answers each request with the next response from a script, and records the requests.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public FakeServer(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public List<(HttpMethod Method, Uri Uri, string? FileField, string? FileName, byte[]? FileBytes, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? field = null, fileName = null, body = null;
            byte[]? bytes = null;
            if (request.Content is MultipartFormDataContent form) {
                var part = Assert.Single(form);
                field = part.Headers.ContentDisposition?.Name?.Trim('"');
                fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                bytes = await part.ReadAsByteArrayAsync(cancellationToken);
            } else if (request.Content != null) {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            Requests.Add((request.Method, request.RequestUri!, field, fileName, bytes, body));
            return _responses.Dequeue()(request);
        }
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(json) };

    private static Func<HttpRequestMessage, HttpResponseMessage> Status(HttpStatusCode status) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent("") };

    private static readonly Func<HttpRequestMessage, HttpResponseMessage> NoConnection =
        _ => throw new HttpRequestException("No connection could be made", null, HttpStatusCode.ServiceUnavailable);

    private static string UploadResult(string status) => $$"""{ "fingerprint": "{{Fingerprint}}", "replayID": 0, "status": "{{status}}" }""";

    private static Common.Uploader UploaderFor(FakeServer server) => new(new HttpClient(server), TimeSpan.Zero);

    private ReplayFile Replay() => new(_replay) { Fingerprint = Fingerprint };

    [Fact]
    public async Task Uploads_the_replay_as_a_multipart_file_and_reads_back_its_status()
    {
        var server = new FakeServer(Json("""{ "exists": false }"""), Json(UploadResult("Success")));
        var file = Replay();

        await UploaderFor(server).Upload(null!, file, PostMatchPage: false);

        Assert.Equal(UploadStatus.Success, file.UploadStatus);
        var check = server.Requests[0];
        Assert.Equal(HttpMethod.Get, check.Method);
        Assert.EndsWith($"/replays/fingerprints/{Fingerprint}", check.Uri.AbsolutePath);

        var upload = server.Requests[1];
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.EndsWith("/upload/heroesprofile/desktop", upload.Uri.AbsolutePath);
        Assert.Contains($"fingerprint={Fingerprint}", upload.Uri.Query);
        Assert.Contains("version=", upload.Uri.Query);
        Assert.Equal("file", upload.FileField);
        Assert.Equal(Path.GetFileName(_replay), upload.FileName);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, upload.FileBytes);
    }

    [Fact]
    public async Task The_postmatch_check_runs_in_the_background_and_keeps_asking_while_the_site_parses()
    {
        // Never "true" - that would open a real browser.
        var parsing = Enumerable.Repeat(Json("false"), 50);
        var server = new FakeServer(new[] {
            Json("""{ "exists": false }"""),
            Json($$"""{ "fingerprint": "{{Fingerprint}}", "replayID": 123, "status": "Success" }"""),
        }.Concat(parsing).ToArray());
        var uploader = UploaderFor(server);
        uploader.PostMatchWaitTime = TimeSpan.FromMilliseconds(600);
        uploader.PostMatchPollInterval = TimeSpan.FromMilliseconds(50);
        var file = Replay();

        var upload = System.Diagnostics.Stopwatch.StartNew();
        await uploader.Upload(null!, file, PostMatchPage: true);
        upload.Stop();

        // The upload queue isn't held up while the site parses the replay...
        Assert.Equal(UploadStatus.Success, file.UploadStatus);
        Assert.True(upload.Elapsed < uploader.PostMatchWaitTime, $"Upload waited {upload.ElapsedMilliseconds}ms for the postmatch check");

        // ...but the check carries on after it.
        await Task.Delay(uploader.PostMatchWaitTime + TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var checks = server.Requests.ToArray().Where(r => r.Uri.AbsolutePath.EndsWith("/replays/parsed")).ToList();
        Assert.True(checks.Count > 3, $"only {checks.Count} parsed checks");
        Assert.All(checks, r => Assert.Contains("replayID=123", r.Uri.Query));
    }

    [Fact]
    public async Task A_replay_already_on_Heroes_Profile_is_a_duplicate_and_is_not_uploaded()
    {
        var server = new FakeServer(Json("""{ "exists": true }"""));
        var file = Replay();

        await UploaderFor(server).Upload(null!, file, PostMatchPage: false);

        Assert.Equal(UploadStatus.Duplicate, file.UploadStatus);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Too_many_requests_waits_and_tries_again()
    {
        var server = new FakeServer(
            Status(HttpStatusCode.TooManyRequests), Json("""{ "exists": false }"""),
            Status(HttpStatusCode.TooManyRequests), Json(UploadResult("Success")));
        var file = Replay();

        await UploaderFor(server).Upload(null!, file, PostMatchPage: false);

        Assert.Equal(UploadStatus.Success, file.UploadStatus);
        Assert.Equal(4, server.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_server_error_is_an_upload_error(HttpStatusCode status)
    {
        var server = new FakeServer(Json("""{ "exists": false }"""), Status(status));
        var file = Replay();

        await UploaderFor(server).Upload(null!, file, PostMatchPage: false);

        Assert.Equal(UploadStatus.UploadError, file.UploadStatus);
    }

    [Fact]
    public async Task No_connection_is_an_upload_error_rather_than_a_crash()
    {
        var server = new FakeServer(NoConnection, NoConnection);
        var file = Replay();

        await UploaderFor(server).Upload(null!, file, PostMatchPage: false);

        Assert.Equal(UploadStatus.UploadError, file.UploadStatus);
    }

    [Fact]
    public async Task A_status_the_uploader_does_not_know_is_an_upload_error()
    {
        var server = new FakeServer(Json("""{ "exists": false }"""), Json(UploadResult("SomethingNew")));
        var file = Replay();

        await UploaderFor(server).Upload(null!, file, PostMatchPage: false);

        Assert.Equal(UploadStatus.UploadError, file.UploadStatus);
    }

    [Fact]
    public async Task The_batch_check_marks_the_replays_Heroes_Profile_already_has()
    {
        var server = new FakeServer(Json("""{ "exists": ["fp-2"] }"""));
        var replays = new[] {
            new ReplayFile("a.StormReplay") { Fingerprint = "fp-1" },
            new ReplayFile("b.StormReplay") { Fingerprint = "fp-2" },
        };

        await UploaderFor(server).CheckDuplicate(replays);

        Assert.Equal(UploadStatus.None, replays[0].UploadStatus);
        Assert.Equal(UploadStatus.Duplicate, replays[1].UploadStatus);
        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("fp-1\nfp-2", request.Body);
    }
}
