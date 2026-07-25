using QuickChartMcp.Configuration;
using Xunit;

namespace QuickChartMcp.Tests;

public class PathPolicyTests
{
    private static PathPolicy Policy(params string[] patterns) =>
        new(new QuickChartOptions { AllowedOutputPatterns = new List<string>(patterns) });

    [Fact]
    public void EmptyPatterns_DenyAll()
    {
        var policy = Policy();
        Assert.False(policy.HasOutputPatterns);
        Assert.False(policy.IsOutputAllowed("C:\\anything\\at\\all"));
    }

    [Fact]
    public void Matching_Pattern_IsAllowed()
    {
        var policy = Policy(@"^C:\\charts(\\|$)");
        Assert.True(policy.IsOutputAllowed("C:\\charts"));
        Assert.True(policy.IsOutputAllowed("C:\\charts\\sub\\dir"));
    }

    [Fact]
    public void NonMatching_Pattern_IsDenied()
    {
        var policy = Policy(@"^C:\\charts(\\|$)");
        Assert.False(policy.IsOutputAllowed("C:\\somewhere\\else"));
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        var policy = Policy(@"^C:\\Charts(\\|$)");
        Assert.True(policy.IsOutputAllowed("c:\\charts\\x"));
    }
}
