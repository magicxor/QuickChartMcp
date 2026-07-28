using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickChartMcp.Client;
using QuickChartMcp.Configuration;
using QuickChartMcp.IO;
using QuickChartMcp.Tools;
using Xunit;

namespace QuickChartMcp.Tests;

/// <summary>
/// What create_chart makes of the shape its 'chart' argument arrives in, and what it says
/// when the instance cannot parse it.
/// </summary>
/// <remarks>
/// Both matter more than they look. The argument is typed loosely because a shape the MCP
/// SDK cannot bind fails before the tool runs and reaches the caller as a line with no
/// detail in it; and a config that is not JSON is evaluated by the instance as
/// <c>new Function("return " + config)</c>, so a truncated one reports the <c>)</c> that
/// closes the wrapper - a character the caller never wrote.
/// </remarks>
public sealed class CreateChartArgumentTests : IDisposable
{
    private const string SyntaxError = "Invalid input\nSyntaxError: Unexpected token ')'";

    private readonly string _outputDirectory =
        Path.Combine(Path.GetTempPath(), "quickchartmcp-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
            Directory.Delete(_outputDirectory, recursive: true);
    }

    /// <summary>Captures the request body and answers with a stub image, or the given error.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        /// <summary>When set, every request is answered as the instance answers a rejected config.</summary>
        public string? Error { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(Error is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            if (Error is not null)
                response.Headers.TryAddWithoutValidation("X-quickchart-error", Error);

            return response;
        }
    }

    /// <summary>The 'chart' argument as it arrives over the wire, from JSON written as the caller would.</summary>
    private static JsonElement Argument(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement StringArgument(string config) =>
        Argument(JsonSerializer.Serialize(config));

    /// <summary>Calls create_chart against the stub and returns its answer as the caller sees it.</summary>
    private async Task<JsonObject> CallAsync(JsonElement chart, StubHandler handler)
    {
        var client = new QuickChartClient(new HttpClient(handler) { BaseAddress = new Uri("http://qc.test/") });
        var policy = new PathPolicy(new QuickChartOptions { AllowedOutputPatterns = [".*"] });
        var tools = new QuickChartTools(client, new ArtifactWriter(policy));

        var result = await tools.CreateChart(chart, _outputDirectory, cancellationToken: CancellationToken.None);

        return (JsonObject)JsonNode.Parse(JsonSerializer.Serialize(result))!;
    }

    private async Task<JsonObject> CallAsync(JsonElement chart) => await CallAsync(chart, new StubHandler());

    /// <summary>The config as the instance received it, out of the captured request body.</summary>
    private static JsonNode SentChart(StubHandler handler) =>
        JsonNode.Parse(handler.Body!)!["chart"]!;

    [Fact]
    public async Task PlainJsonStringReachesTheInstanceAsAnObject()
    {
        var handler = new StubHandler();

        var answer = await CallAsync(StringArgument("""{"type":"bar"}"""), handler);

        Assert.True((bool)answer["success"]!);
        Assert.IsType<JsonObject>(SentChart(handler));
    }

    [Fact]
    public async Task AConfigThatIsNotJsonStillReachesTheInstanceVerbatimToEvaluate()
    {
        const string javascript = "{ type: 'bar', options: { plugins: { datalabels: { formatter: function (v) { return v; } } } } }";
        var handler = new StubHandler();

        var answer = await CallAsync(StringArgument(javascript), handler);

        Assert.True((bool)answer["success"]!);
        Assert.Equal(javascript, SentChart(handler).GetValue<string>());
    }

    /// <summary>
    /// The shape a caller reaches for once a config of its own has been rejected. Bound to a
    /// string parameter it would fail in the SDK's argument binding, outside this tool's reach.
    /// </summary>
    [Fact]
    public async Task AnObjectIsAcceptedInPlaceOfTheString()
    {
        var handler = new StubHandler();

        var answer = await CallAsync(Argument("""{"type":"bar","options":{"plugins":{"title":{"text":"Ports"}}}}"""), handler);

        Assert.True((bool)answer["success"]!);
        Assert.IsType<JsonObject>(SentChart(handler));
        // The title is read off the parsed config either way, so the file is named from it.
        Assert.Contains("Ports", (string)answer["filePath"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATruncatedConfigIsAnsweredWithTheJsonReadersOwnWords()
    {
        const string truncated = """{"type":"bar","data":{"labels":["a"]}""";
        var handler = new StubHandler { Error = SyntaxError };

        var answer = await CallAsync(StringArgument(truncated), handler);

        Assert.False((bool)answer["success"]!);
        Assert.Equal(400, (int)answer["statusCode"]!);

        var hint = (string)answer["hint"]!;
        // Whatever this runtime's reader says about the same input is what the hint must
        // carry; asserting its wording here would pin the test to a message System.Text.Json
        // does not treat as API and has reworded before.
        Assert.Contains(MessageOfReading(truncated), hint, StringComparison.Ordinal);
        Assert.Contains("resend the config whole", hint, StringComparison.Ordinal);
        // The point of the hint: the reported character is not the missing one.
        Assert.Contains("not the missing character", hint, StringComparison.Ordinal);
    }

    /// <summary>What this runtime's JSON reader says about <paramref name="config"/>.</summary>
    private static string MessageOfReading(string config)
    {
        // ThrowsAny, not Throws: the reader raises the internal JsonReaderException, and it is
        // as a JsonException that the tool catches it.
        var thrown = Assert.ThrowsAny<JsonException>(() => JsonNode.Parse(config));

        // A test that silently stopped exercising the truncation branch would still pass on
        // an empty needle, so make the premise itself an assertion.
        Assert.NotEmpty(thrown.Message);
        return thrown.Message;
    }

    /// <summary>
    /// A config that parsed as JSON was never evaluated as JavaScript, so a syntax error in
    /// it is the instance's own business - a JSON diagnostic would be a red herring.
    /// </summary>
    [Fact]
    public async Task A400ForAConfigThatDidParseKeepsTheGenericHint()
    {
        var handler = new StubHandler { Error = SyntaxError };

        var answer = await CallAsync(StringArgument("""{"type":"bar","options":{"plugins":{"datalabels":{"formatter":"function (v) { return v; )"}}}}"""), handler);

        Assert.False((bool)answer["success"]!);
        Assert.DoesNotContain("did not parse as JSON", (string)answer["hint"]!, StringComparison.Ordinal);
    }

    /// <summary>A 400 that is not a parse failure is not the truncation story either.</summary>
    [Fact]
    public async Task A400ThatNamesNoSyntaxErrorKeepsTheGenericHint()
    {
        var handler = new StubHandler { Error = "Invalid input\nUnsupported chart type 'pyramid'" };

        var answer = await CallAsync(StringArgument("{ type: 'pyramid' }"), handler);

        Assert.False((bool)answer["success"]!);
        Assert.DoesNotContain("did not parse as JSON", (string)answer["hint"]!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Named by type, not by value: ValueKind spells a boolean as "True", and "got a bare
    /// true" reads as the value that was sent rather than as what was wrong with it.
    /// </summary>
    [Theory]
    [InlineData("42", "number")]
    [InlineData("true", "boolean")]
    [InlineData("false", "boolean")]
    [InlineData("""["bar"]""", "array")]
    public async Task AValueThatIsNoConfigAtAllIsNamedForWhatItIs(string json, string kind)
    {
        var answer = await CallAsync(Argument(json));

        Assert.False((bool)answer["success"]!);

        var error = (string)answer["error"]!;
        Assert.Contains("must be a Chart.js configuration", error, StringComparison.Ordinal);
        Assert.Contains($"got a bare {kind}.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"   \"")]
    [InlineData("null")]
    public async Task AnAbsentConfigIsAnsweredAsMissing(string json)
    {
        var answer = await CallAsync(Argument(json));

        Assert.False((bool)answer["success"]!);
        Assert.Contains("is required", (string)answer["error"]!, StringComparison.Ordinal);
    }
}
