using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace QuickChartMcp.Client;

/// <summary>
/// Typed HttpClient wrapper over the QuickChart POST /chart endpoint. The base address and
/// timeout are configured on the injected HttpClient (see Program.cs). Request JSON uses
/// camelCase property names, which is what QuickChart expects.
/// </summary>
/// <remarks>
/// Error contract of the target (modernized fork): invalid requests/configs return HTTP 400,
/// unexpected server failures return 500; in both cases the error message is rendered as an
/// image and echoed in the <c>X-quickchart-error</c> response header.
/// </remarks>
public sealed class QuickChartClient
{
    private const string ErrorHeader = "X-quickchart-error";
    private const string GeoCoverageHeader = "X-quickchart-geo-coverage";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public QuickChartClient(HttpClient http)
    {
        _http = http;
    }

    public readonly record struct ChartResult(byte[] Bytes, string? ContentType, GeoCoverage? GeoCoverage);

    /// <summary>
    /// Renders a chart and returns the raw response bytes (png/svg/pdf) together with the
    /// geo coverage the instance reported, if any (see <see cref="GeoCoverage"/>).
    /// </summary>
    public async Task<ChartResult> CreateChartAsync(ChartRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("chart", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new ChartResult(
            bytes, response.Content.Headers.ContentType?.MediaType, ReadGeoCoverage(response));
    }

    /// <summary>
    /// Reads the geo coverage header. The instance escapes non-ASCII names as JSON
    /// <c>\uXXXX</c>, since a header value cannot carry them, so this parses as-is.
    /// A malformed diagnostic is dropped: the chart itself rendered fine.
    /// </summary>
    private static GeoCoverage? ReadGeoCoverage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(GeoCoverageHeader, out var values))
            return null;

        GeoCoverage? coverage;
        try
        {
            coverage = JsonSerializer.Deserialize<GeoCoverage>(string.Concat(values), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return IsWellFormed(coverage) ? coverage : null;
    }

    /// <summary>
    /// Whether a parsed coverage report can be read without null checks. Parseable JSON is
    /// not enough: an explicit <c>null</c> in the payload overwrites a member's initializer,
    /// and a non-nullable annotation is not enforced at runtime — so a header saying
    /// <c>{"maps":null}</c> would otherwise hand a null list to the caller. Whatever
    /// instance the client is pointed at, a diagnostic must not break a rendered chart.
    /// </summary>
    private static bool IsWellFormed(GeoCoverage? coverage)
    {
        if (coverage is not { Maps: not null })
            return false;

        foreach (var map in coverage.Maps)
        {
            // The names go straight into the warning text, so a null among them would
            // print as a stray empty item - element nullability is not annotated at all.
            if (map is not { Map: not null, Missing: not null }
                || map.Missing.Any(static name => name is null))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lists the built-in geo maps (GET /maps), or — when <paramref name="mapName"/> is
    /// given — one map's matchable features (GET /maps?name=...). The endpoint returns
    /// plain JSON; unknown map names come back as HTTP 400 with the X-quickchart-error
    /// header, which <see cref="EnsureSuccessAsync"/> turns into a QuickChartApiException.
    /// </summary>
    public async Task<JsonNode> ListMapsAsync(string? mapName, CancellationToken ct)
    {
        var uri = string.IsNullOrWhiteSpace(mapName)
            ? "maps"
            : $"maps?name={Uri.EscapeDataString(mapName)}";

        using var response = await _http.GetAsync(uri, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: ct);
        return node ?? throw new QuickChartApiException(
            (int)response.StatusCode, "GET /maps returned an empty response body.");
    }

    /// <summary>
    /// Fails the call when the response has a non-success status OR carries the
    /// X-quickchart-error header. QuickChart renders error text into the image itself, so
    /// without the header check a broken chart could be saved as if it had succeeded.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var headerDetail = response.Headers.TryGetValues(ErrorHeader, out var values)
            ? string.Join("; ", values).Trim()
            : null;

        if (response.IsSuccessStatusCode && string.IsNullOrEmpty(headerDetail))
            return;

        var detail = string.IsNullOrEmpty(headerDetail)
            ? await ReadTextualBodyAsync(response, ct)
            : headerDetail;

        throw new QuickChartApiException((int)response.StatusCode, detail);
    }

    /// <summary>
    /// Reads the error body only when it is textual; a failed render usually returns the error
    /// message drawn as an image in the requested format, which is useless as an error text.
    /// </summary>
    private static async Task<string> ReadTextualBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var isTextual = mediaType is not null
            && (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase));

        if (!isTextual)
            return "(binary error response body)";

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return "(no response body)";

            return body.Length > 500 ? body[..500] + "…" : body;
        }
        catch
        {
            // Ignore body-read failures; the status code is still meaningful.
            return "(unreadable response body)";
        }
    }
}
