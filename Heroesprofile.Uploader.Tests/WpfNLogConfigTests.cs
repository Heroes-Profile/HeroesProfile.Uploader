using NLog.Config;
using NLog.Targets;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class WpfNLogConfigTests
{
    [Fact]
    public void Wpf_NLog_config_loads_cleanly_with_the_current_NLog()
    {
        // The WPF app's NLog.config was written for NLog 4; make sure the NLog it now ships with
        // still accepts every attribute, rather than silently dropping the log file target.
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Wpf.NLog.config");
        using var factory = new NLog.LogFactory { ThrowConfigExceptions = true };
        var config = new XmlLoggingConfiguration(path, factory);

        var target = Assert.IsType<FileTarget>(config.FindTargetByName("logfile"));
        Assert.Equal(10_000_000, target.ArchiveAboveSize);
        Assert.Equal(3, target.MaxArchiveFiles);
        Assert.Single(config.LoggingRules);
    }
}
