using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using QuickChartMcp.Configuration;

namespace QuickChartMcp.Client;

/// <summary>
/// Typed HttpClient wrapper over the QuickChart POST /chart endpoint. The base address and
/// timeout are configured on the injected HttpClient (see Program.cs). Request JSON uses
/// camelCase property names, which is what QuickChart expects.
/// </summary>
public sealed class QuickChartClient
{
    private const string ErrorHeader = "X-quickchart-error";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly QuickChartOptions _options;

    public QuickChartClient(HttpClient http, IOptions<QuickChartOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public readonly record struct ChartResult(byte[] Bytes, string? ContentType);

    /// <summary>Renders a chart and returns the raw response bytes (png/svg/webp/jpg/pdf).</summary>
    public async Task<ChartResult> CreateChartAsync(ChartRequest request, CancellationToken ct)
    {
        // The API key travels in the request body ("key"; QuickChart uses no auth header). It is
        // a server-side setting applied here so the calling agent never sees or controls it.
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request = request with { Key = _options.ApiKey };

        using var response = await _http.PostAsJsonAsync("chart", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new ChartResult(bytes, response.Content.Headers.ContentType?.MediaType);
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
