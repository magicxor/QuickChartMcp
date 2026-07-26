using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using QuickChartMcp.Client;
using QuickChartMcp.IO;

namespace QuickChartMcp.Tools;

/// <summary>
/// MCP tool that renders charts via a configured self-hosted QuickChart instance (the
/// modernized Chart.js-4-only fork). The arguments mirror the QuickChart POST /chart
/// endpoint; the rendered binary is written to the caller-supplied <c>outputDirectory</c>
/// and only a small summary (path, size, metadata) is returned inline.
/// </summary>
[McpServerToolType]
internal sealed class QuickChartTools
{
    private const string ToolDescription =
        "Render a chart via a self-hosted QuickChart instance (Chart.js 4) and save the result to the given " +
        "output directory. Returns the file path and metadata; the binary is NOT returned inline. " +
        "Supported chart types - standard Chart.js 4: bar, line, pie, doughnut, radar, polarArea, scatter, bubble; " +
        "QuickChart custom: sparkline, progressBar, donut (alias of doughnut); " +
        "box plots: boxplot, horizontalBoxplot, violin, horizontalViolin; " +
        "error bars: barWithErrorBars, lineWithErrorBars, scatterWithErrorBars, polarAreaWithErrorBars; " +
        "funnel: funnel; geo: choropleth, bubbleMap; graphs/trees: graph, forceDirectedGraph, dendrogram, tree; " +
        "parallel coordinates: pcp, logarithmicPcp; set diagrams: venn, euler; word clouds: wordCloud. " +
        "Also available: the 'hierarchical' category axis scale, the annotation and datalabels plugins " +
        "(options.plugins.annotation / options.plugins.datalabels), and time scales with moment.js format strings. " +
        "GEO CHARTS: the instance bundles no map files, so GeoJSON must be inlined in the config. " +
        "choropleth dataset: { outline: <GeoJSON Feature or Feature[]>, data: [{ feature: <GeoJSON Feature>, value: <number> }] }. " +
        "bubbleMap dataset: { outline: <GeoJSON Feature or Feature[]>, showOutline: true, data: [{ longitude, latitude, value }] }. " +
        "Both need options.scales: { projection: { axis: 'x', projection: 'equalEarth' }, color: { axis: 'x' } } " +
        "(use a 'size' scale instead of 'color' for bubbleMap).";

    private const string ChartArgDescription =
        "Chart.js 4 configuration as a string. Plain JSON is forwarded as an object; JavaScript object syntax " +
        "(e.g. with callback functions or unquoted keys) is forwarded as a string for QuickChart to evaluate. " +
        "MUST use Chart.js 4 syntax: options.scales.x / options.scales.y objects, options.plugins.title / " +
        "options.plugins.legend. Chart.js 2 syntax (scales.xAxes/yAxes arrays, top-level title/legend, " +
        "type 'horizontalBar') is NOT translated and will misrender or be rejected; use type 'bar' with " +
        "options.indexAxis: 'y' for horizontal bars. Invalid configs are rejected with HTTP 400. REQUIRED.";

    /// <summary>
    /// Allowed values of the <c>format</c> argument and their file extensions. The modernized
    /// QuickChart fork renders png, svg and pdf only (its POST /chart silently falls back to
    /// png for unknown formats, hence this client-side validation). QuickChart's "base64"
    /// format is deliberately excluded: the result is saved as a file, so an inline base64
    /// representation has no use here.
    /// </summary>
    private static readonly Dictionary<string, string> FormatExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = ".png",
        ["svg"] = ".svg",
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
    [Description(ToolDescription)]
    public async Task<object> CreateChart(
        [Description(ChartArgDescription)] string chart,
        [Description("Directory where the chart file will be written. Must be an absolute path. Created if it does not exist. REQUIRED.")] string outputDirectory,
        [Description("Chart width in pixels (default 500).")] int width = 500,
        [Description("Chart height in pixels (default 300).")] int height = 300,
        [Description("Device pixel ratio; output dimensions are multiplied by this (default 2.0; use 1.0 for exact width/height).")] double devicePixelRatio = 2.0,
        [Description("Canvas background color: a color name, hex, rgb() or hsl() value (default 'transparent').")] string backgroundColor = "transparent",
        [Description("Output format: 'png' (default), 'svg' or 'pdf'.")] string format = "png",
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
                    $"format '{format}' is not supported; it must be one of: png, svg, pdf.",
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
    /// (Chart.js 3/4) with a legacy options.title.text fallback; the text may be a string or
    /// an array of strings. Falls back to "chart" when no title can be extracted, e.g. when
    /// the config was sent as a raw JavaScript string. Unsafe characters are handled by the
    /// ArtifactWriter.
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
        // 400 = the QuickChart instance rejected the request as invalid input (bad or
        // non-Chart.js-4 config, unknown chart type, out-of-range size). The config needs
        // fixing; retrying unchanged will not help.
        QuickChartApiException { StatusCode: 400 } api => new
        {
            success = false,
            error = api.Message,
            statusCode = api.StatusCode,
            hint = "QuickChart rejected the chart configuration. Fix the config (Chart.js 4 syntax) and retry.",
        },
        QuickChartApiException api => new { success = false, error = api.Message, statusCode = api.StatusCode },
        _ => new { success = false, error = ex.Message },
    };
}
