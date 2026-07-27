using QuickChartMcp.Client;
using QuickChartMcp.Tools;
using Xunit;

namespace QuickChartMcp.Tests;

/// <summary>
/// The warning text create_chart hands the agent for a choropleth that left map features
/// without data rows. The numbers come off a wire the caller configured, so the phrasing
/// has to hold up for reports that do not line up as the instance's own always do.
/// </summary>
public class GeoCoverageWarningTests
{
    private static GeoCoverage Coverage(params GeoMapCoverage[] maps) => new() { Maps = maps };

    private static string Single(GeoCoverage coverage) =>
        Assert.Single(QuickChartTools.DescribeGeoCoverage(coverage));

    [Fact]
    public void NamesTheMapTheCountsAndTheRegions()
    {
        var warning = Single(Coverage(new GeoMapCoverage
        {
            Map = "blr",
            Framed = 7,
            Covered = 2,
            Missing = ["Gomel", "Mogilev"],
        }));

        Assert.Contains("Map 'blr': only 2 of the 7 features in view have a data row.", warning);
        Assert.Contains("no data: Gomel, Mogilev.", warning);
        // Some subdivisions are matchable by id only, so the text must not promise a name.
        Assert.Contains("by name, or by id where the map data gives it no name", warning);
        // The agent must not read a nudge to fill the gaps as a licence to make them up.
        Assert.Contains("do not invent values", warning);
    }

    [Fact]
    public void CountsTheRegionsItWasNotGivenNamesFor()
    {
        var warning = Single(Coverage(new GeoMapCoverage
        {
            Map = "world",
            Framed = 177,
            Covered = 1,
            Missing = ["Germany"],
            More = 175,
        }));

        Assert.Contains("no data: Germany, and 175 more.", warning);
    }

    [Fact]
    public void ReadsWithoutALeadingCommaWhenNothingCouldBeNamed()
    {
        // A feature with neither a name nor an id is only counted (world-land has one),
        // so a report can carry a remainder and no names at all.
        var warning = Single(Coverage(new GeoMapCoverage
        {
            Map = "world-land",
            Framed = 1,
            Covered = 0,
            Missing = [],
            More = 1,
        }));

        Assert.Contains("no data: and 1 more.", warning);
        Assert.DoesNotContain(": ,", warning);
    }

    [Fact]
    public void CountsABlankNameInsteadOfListingIt()
    {
        var warning = Single(Coverage(new GeoMapCoverage
        {
            Map = "blr",
            Framed = 7,
            Covered = 4,
            Missing = ["Gomel", "  ", ""],
        }));

        // Still three regions unaccounted for, but only one of them can be named.
        Assert.Contains("no data: Gomel, and 2 more.", warning);
    }

    [Fact]
    public void SaysNothingAboutAnEntryThatReportsNothingMissing()
    {
        // The instance never sends one; a report is only ever a report.
        Assert.Empty(QuickChartTools.DescribeGeoCoverage(Coverage(
            new GeoMapCoverage { Map = string.Empty, Framed = 0, Covered = 0, Missing = [] })));
        // Counts that overshoot say nothing either, rather than a negative shortfall.
        Assert.Empty(QuickChartTools.DescribeGeoCoverage(Coverage(
            new GeoMapCoverage { Map = "blr", Framed = 7, Covered = 9, Missing = [] })));
    }

    [Fact]
    public void WarnsAboutANamedRegionEvenWhenTheCountsSayNothingIsMissing()
    {
        // The named region is the actionable half of the report, so counts that do not
        // line up with it must not be what decides whether it is mentioned at all.
        var warning = Single(Coverage(new GeoMapCoverage
        {
            Map = "blr",
            Framed = 0,
            Covered = 0,
            Missing = ["Gomel"],
        }));

        Assert.Contains("no data: Gomel.", warning);
    }

    [Fact]
    public void WarnsAboutACountedRegionEvenWhenTheCountsSayNothingIsMissing()
    {
        var warning = Single(Coverage(new GeoMapCoverage
        {
            Map = "blr",
            Framed = 0,
            Covered = 0,
            Missing = [],
            More = 2,
        }));

        Assert.Contains("no data: and 2 more.", warning);
    }

    [Fact]
    public void KeepsWarningAboutTheMapsThatDoReportSomething()
    {
        var warnings = QuickChartTools.DescribeGeoCoverage(Coverage(
            new GeoMapCoverage { Map = "us-states", Framed = 51, Covered = 51, Missing = [] },
            new GeoMapCoverage { Map = "blr", Framed = 7, Covered = 6, Missing = ["Gomel"] }));

        var warning = Assert.Single(warnings);
        Assert.Contains("Map 'blr'", warning);
    }

    [Fact]
    public void SaysNothingWithoutAReport()
    {
        Assert.Empty(QuickChartTools.DescribeGeoCoverage(null));
        Assert.Empty(QuickChartTools.DescribeGeoCoverage(Coverage()));
    }
}
