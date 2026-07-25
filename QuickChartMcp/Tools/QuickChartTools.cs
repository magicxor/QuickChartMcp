using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using QuickChartMcp.Client;
using QuickChartMcp.IO;

namespace QuickChartMcp.Tools;

/// <summary>
/// MCP tool that renders charts via a configured self-hosted QuickChart instance. The
/// arguments mirror the QuickChart POST /chart endpoint; the rendered binary is written to
/// the caller-supplied <c>outputDirectory</c> and only a small summary (path, size,
/// metadata) is returned inline.
/// </summary>
[McpServerToolType]
internal sealed class QuickChartTools
{
    /// <summary>
    /// Allowed values of the <c>format</c> argument and their file extensions. QuickChart's
    /// "base64" format is deliberately excluded: the result is saved as a file, so an inline
    /// base64 representation has no use here.
    /// </summary>
    private static readonly Dictionary<string, string> FormatExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = ".png",
        ["svg"] = ".svg",
        ["webp"] = ".webp",
        ["jpg"] = ".jpg",
        ["jpeg"] = ".jpg",
        ["pdf"] = ".pdf",
    };

    private readonly QuickChartClient _client;
    private readonly ArtifactWriter _writer;

    public QuickChartTools(QuickChartClient client, ArtifactWriter writer)
    {
        _client = client;
        _writer = writer;
    }

    [McpServerTool(Name = "create_chart")]
    [Description("Render a chart via a self-hosted QuickChart instance from a Chart.js configuration and save the result to the given output directory. Returns the file path and metadata; the binary is NOT returned inline.")]
    public async Task<object> CreateChart(
        [Description("Chart.js configuration as a string. Plain JSON is forwarded as an object; JavaScript object syntax (e.g. with callback functions or unquoted keys) is forwarded as a string for QuickChart to evaluate. REQUIRED.")] string chart,
        [Description("Directory where the chart file will be written. Must be an absolute path. Created if it does not exist. REQUIRED.")] string outputDirectory,
        [Description("Chart width in pixels (default 500).")] int width = 500,
        [Description("Chart height in pixels (default 300).")] int height = 300,
        [Description("Device pixel ratio; output dimensions are multiplied by this (default 2.0; use 1.0 for exact width/height).")] double devicePixelRatio = 2.0,
        [Description("Canvas background color: a color name, hex, rgb() or hsl() value (default 'transparent').")] string backgroundColor = "transparent",
        [Description("Output format: 'png' (default), 'svg', 'webp', 'jpg' or 'pdf'.")] string format = "png",
        [Description("Chart.js version to render with, e.g. '2', '3' or '4'. Omitted = the QuickChart instance's default.")] string? version = null,
        [Description("Optional output file name. If omitted, a name is derived from the chart title, falling back to 'chart'. Any path components are rejected. Existing files are never overwritten; a numeric suffix is appended on collision.")] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _writer.EnsureOutputDirectoryAllowed(outputDirectory);

            if (string.IsNullOrWhiteSpace(chart))
            {
                return new
                {
                    success = false,
                    error = "The 'chart' argument is required and must be a non-empty Chart.js configuration.",
                };
            }

            if (!FormatExtensions.TryGetValue(format, out var extension))
            {
                throw new ArgumentException(
                    $"format '{format}' is not supported; it must be one of: png, svg, webp, jpg, pdf.",
                    nameof(format));
            }

            var normalizedFormat = extension[1..];
            var chartNode = ParseChart(chart);
            var request = new ChartRequest
            {
                Chart = chartNode ?? (JsonNode)JsonValue.Create(chart),
                Width = width,
                Height = height,
                DevicePixelRatio = devicePixelRatio,
                BackgroundColor = backgroundColor,
                Format = normalizedFormat,
                Version = version,
            };

            var result = await _client.CreateChartAsync(request, cancellationToken);
            var written = await _writer.WriteBytesAsync(
                outputDirectory, fileName, DeriveBaseName(chartNode), extension, result.Bytes, cancellationToken);

            return new
            {
                success = true,
                filePath = written.Path,
                bytes = written.Bytes,
                format = normalizedFormat,
                width,
                height,
                devicePixelRatio,
                contentType = result.ContentType,
            };
        }
        catch (Exception ex)
        {
            return Error(ex);
        }
    }

    /// <summary>
    /// Parses the chart argument as JSON when possible. Returns the parsed object, or null when
    /// the string is not valid JSON or its root is not an object — in that case the raw string
    /// is sent instead and the QuickChart instance evaluates it as JavaScript (the documented
    /// way to use configs containing functions or unquoted keys).
    /// </summary>
    private static JsonObject? ParseChart(string chart)
    {
        try
        {
            return JsonNode.Parse(chart) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Derives a default file base name from the chart title: options.plugins.title.text
    /// (Chart.js v3/v4) or options.title.text (v2); the text may be a string or an array of
    /// strings. Falls back to "chart" when no title can be extracted, e.g. when the config was
    /// sent as a raw JavaScript string. Unsafe characters are handled by the ArtifactWriter.
    /// </summary>
    private static string DeriveBaseName(JsonObject? chart)
    {
        string? title;
        try
        {
            title = ExtractTitleText(chart?["options"]?["plugins"]?["title"]?["text"])
                    ?? ExtractTitleText(chart?["options"]?["title"]?["text"]);
        }
        catch (InvalidOperationException)
        {
            // A config node of an unexpected shape (e.g. "options" is a scalar) is not an
            // error — there is just no title to derive a name from.
            title = null;
        }

        if (string.IsNullOrWhiteSpace(title))
            return "chart";

        title = title.Trim();
        return title.Length <= 60 ? title : title[..60].Trim();
    }

    private static string? ExtractTitleText(JsonNode? text)
    {
        if (text is JsonValue value && value.TryGetValue<string>(out var single))
            return single;

        if (text is JsonArray array)
        {
            var parts = array
                .Select(static item => item is JsonValue v && v.TryGetValue<string>(out var part) ? part : null)
                .Where(static part => !string.IsNullOrWhiteSpace(part));
            var joined = string.Join(' ', parts);
            return joined.Length == 0 ? null : joined;
        }

        return null;
    }

    private static object Error(Exception ex) => ex switch
    {
        QuickChartApiException api => new { success = false, error = api.Message, statusCode = api.StatusCode },
        _ => new { success = false, error = ex.Message },
    };
}
