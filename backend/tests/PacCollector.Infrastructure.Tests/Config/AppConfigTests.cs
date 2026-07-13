using FluentAssertions;
using PacCollector.Infrastructure.Config;

namespace PacCollector.Infrastructure.Tests.Config;

public class AppConfigTests
{
    [Fact]
    public void DefaultConfig_IsValid()
    {
        new AppConfig().Validate().Should().BeEmpty();
    }

    [Fact]
    public void RejectsZeroPrintPort()
    {
        var c = new AppConfig();
        c.General.PrintPort = 0;
        c.Validate().Should().Contain(e => e.Contains("print_port"));
    }

    [Fact]
    public void RejectsUnsafeDbDir()
    {
        var c = new AppConfig();
        c.General.DbDir = "/etc";
        c.Validate().Should().Contain(e => e.Contains("peligrosa"));
    }

    [Fact]
    public void RejectsZeroRecentKeep()
    {
        var c = new AppConfig();
        c.General.RecentKeep = 0;
        c.Validate().Should().Contain(e => e.Contains("recent_keep"));
    }

    [Fact]
    public void RejectsEmptyDelimiter()
    {
        var c = new AppConfig();
        c.General.Delimiter = "";
        c.Validate().Should().Contain(e => e.Contains("delimiter"));
    }
}
