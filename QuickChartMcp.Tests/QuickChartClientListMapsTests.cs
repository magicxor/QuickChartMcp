using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using QuickChartMcp.Client;
using Xunit;

namespace QuickChartMcp.Tests;

public class QuickChartClientListMapsTests
{
    /// <summary>Returns a canned response and records the request URI.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_respond(request));
        }
    }

    private static QuickChartClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://qc.test/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task ListsAllMaps()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """[{"name":"deu","source":"datamaps"},{"name":"world","source":"world-atlas"}]"""));
        var client = CreateClient(handler);

        var result = await client.ListMapsAsync(null, CancellationToken.None);

        Assert.Equal("http://qc.test/maps", handler.LastRequestUri!.ToString());
        var array = Assert.IsType<JsonArray>(result);
        Assert.Equal(2, array.Count);
        Assert.Equal("world", (string?)array[1]!["name"]);
    }

    [Fact]
    public async Task DescribesOneMapAndEscapesTheName()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{"name":"us-states","source":"us-atlas","features":[{"name":"California","id":"06"}]}"""));
        var client = CreateClient(handler);

        var result = await client.ListMapsAsync("us states&x=1", CancellationToken.None);

        // The name must arrive query-escaped so it cannot smuggle extra parameters.
        // (AbsoluteUri, not ToString(): the latter renders %20 back as a space.)
        Assert.Equal("http://qc.test/maps?name=us%20states%26x%3D1", handler.LastRequestUri!.AbsoluteUri);
        var obj = Assert.IsType<JsonObject>(result);
        Assert.Equal("California", (string?)obj["features"]![0]!["name"]);
    }

    [Fact]
    public async Task UnknownMapThrowsApiExceptionWithHeaderDetail()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Json(HttpStatusCode.BadRequest, """{"error":"Unknown map \"atlantis\"."}""");
            response.Headers.Add("X-quickchart-error", "Unknown map \"atlantis\". See GET /maps for available maps.");
            return response;
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<QuickChartApiException>(
            () => client.ListMapsAsync("atlantis", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("atlantis", ex.Message);
    }

    [Fact]
    public async Task ErrorWithoutHeaderFallsBackToJsonBody()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.InternalServerError, """{"error":"boom"}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<QuickChartApiException>(
            () => client.ListMapsAsync(null, CancellationToken.None));

        Assert.Equal(500, ex.StatusCode);
        Assert.Contains("boom", ex.Message);
    }
}
