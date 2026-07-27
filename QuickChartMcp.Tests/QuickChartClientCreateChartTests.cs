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
            return response;
        }
    }

    private static async Task<JsonObject> PostAsync(ChartRequest request)
    {
        var handler = new CapturingHandler();
        var client = new QuickChartClient(new HttpClient(handler) { BaseAddress = new Uri("http://qc.test/") });

        await client.CreateChartAsync(request, CancellationToken.None);

        return Assert.IsType<JsonObject>(JsonNode.Parse(handler.Body!));
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
}
