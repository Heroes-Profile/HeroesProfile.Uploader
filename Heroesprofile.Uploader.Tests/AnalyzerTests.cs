using Heroes.ReplayParser;
using Heroesprofile.Uploader.Common;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class AnalyzerTests
{
    [Theory]
    [InlineData(DataParser.ReplayParseResult.ComputerPlayerFound, UploadStatus.AiDetected)]
    [InlineData(DataParser.ReplayParseResult.TryMeMode, UploadStatus.AiDetected)]
    [InlineData(DataParser.ReplayParseResult.Incomplete, UploadStatus.Incomplete)]
    [InlineData(DataParser.ReplayParseResult.PTRRegion, UploadStatus.PtrRegion)]
    [InlineData(DataParser.ReplayParseResult.PreAlphaWipe, UploadStatus.TooOld)]
    public void Known_parse_results_map_to_a_status(DataParser.ReplayParseResult parseResult, UploadStatus expected)
    {
        Assert.Equal(expected, new Analyzer().GetPreStatus(null, parseResult));
    }

    [Theory]
    [InlineData(DataParser.ReplayParseResult.UnexpectedResult)]
    [InlineData(DataParser.ReplayParseResult.Exception)]
    [InlineData(DataParser.ReplayParseResult.FileNotFound)]
    [InlineData(DataParser.ReplayParseResult.FileSizeTooLarge)]
    public void Other_parse_failures_get_no_pre_status(DataParser.ReplayParseResult parseResult)
    {
        // Analyze() then marks these UploadError itself, rather than leaving them In progress.
        Assert.Null(new Analyzer().GetPreStatus(null, parseResult));
    }
}
