using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using QuickChartMcp.Client;
using Xunit;

namespace QuickChartMcp.Tests;

public class QuickChartClientCreateChartTests
{
    /// <summary>Captures the request body and returns a stub image.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        /// <summary>Value of the geo coverage header the stub response carries, if any.</summary>
        public string? GeoCoverageHeader { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            if (GeoCoverageHeader is not null)
                response.Headers.TryAddWithoutValidation("X-quickchart-geo-coverage", GeoCoverageHeader);

            return response;
        }
    }

    private static QuickChartClient ClientFor(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://qc.test/") });

    private static async Task<JsonObject> PostAsync(ChartRequest request)
    {
        var handler = new CapturingHandler();

        await ClientFor(handler).CreateChartAsync(request, CancellationToken.None);

        return Assert.IsType<JsonObject>(JsonNode.Parse(handler.Body!));
    }

    private static async Task<GeoCoverage?> CoverageFromAsync(string? header)
    {
        var handler = new CapturingHandler { GeoCoverageHeader = header };

        var result = await ClientFor(handler).CreateChartAsync(Request(), CancellationToken.None);

        return result.GeoCoverage;
    }

    private static ChartRequest Request() => new()
    {
        Chart = JsonNode.Parse("""{"type":"bar"}""")!,
    };

    [Fact]
    public async Task OmitsDimensionsTheCallerLeftOpen()
    {
        var body = await PostAsync(Request());

        // The instance reads an absent dimension as "derive it from the chart";
        // sending an explicit null or a guessed default would defeat that.
        Assert.False(body.ContainsKey("width"));
        Assert.False(body.ContainsKey("height"));
        Assert.Equal("png", (string?)body["format"]);
    }

    [Fact]
    public async Task SendsDimensionsTheCallerGave()
    {
        var body = await PostAsync(Request() with { Width = 900, Height = 600 });

        Assert.Equal(900, (int?)body["width"]);
        Assert.Equal(600, (int?)body["height"]);
    }

    [Fact]
    public async Task SendsOneDimensionWithoutTheOther()
    {
        var body = await PostAsync(Request() with { Width = 900 });

        Assert.Equal(900, (int?)body["width"]);
        Assert.False(body.ContainsKey("height"));
    }

    [Fact]
    public async Task ReadsTheGeoCoverageTheInstanceReported()
    {
        // Non-ASCII names arrive as JSON \u escapes: a header value cannot carry them.
        var coverage = await CoverageFromAsync(
            """{"maps":[{"map":"blr","framed":7,"covered":2,"missing":["Gomel","Минск"],"more":3}]}""");

        var map = Assert.Single(coverage!.Maps);
        Assert.Equal("blr", map.Map);
        Assert.Equal(7, map.Framed);
        Assert.Equal(2, map.Covered);
        Assert.Equal(["Gomel", "Минск"], map.Missing);
        Assert.Equal(3, map.More);
    }

    [Fact]
    public async Task ReportsNoCoverageWhenTheInstanceSentNone()
    {
        Assert.Null(await CoverageFromAsync(null));
    }

    [Fact]
    public async Task IgnoresAGeoCoverageHeaderItCannotParse()
    {
        // The chart itself rendered; a broken diagnostic must not fail the call.
        Assert.Null(await CoverageFromAsync("{not json"));
    }

    [Theory]
    // Parseable JSON that would leave a non-nullable member null. Whatever a
    // caller has pointed this client at, a diagnostic header must not hand a
    // null list to the code that phrases the warning.
    [InlineData("""{"maps":null}""")]
    [InlineData("""{"maps":[{"map":"blr","framed":7,"covered":2,"missing":null}]}""")]
    [InlineData("""{"maps":[{"map":"blr","framed":7,"covered":2,"missing":["Gomel",null]}]}""")]
    [InlineData("""{"maps":[{"map":null,"framed":7,"covered":2,"missing":["Gomel"]}]}""")]
    [InlineData("""{"maps":[null]}""")]
    [InlineData("null")]
    public async Task IgnoresAGeoCoverageHeaderWithNullsWhereListsBelong(string header)
    {
        Assert.Null(await CoverageFromAsync(header));
    }
}
