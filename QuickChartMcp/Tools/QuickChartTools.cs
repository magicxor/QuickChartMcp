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
        "CANVAS SIZE: width and height are optional. Omit both - the usual case - and the instance sizes the " +
        "canvas from the chart itself, longest side 1280; give one and the other follows it; give both only when " +
        "a specific size is required. " +
        "Supported chart types - standard Chart.js 4: bar, line, pie, doughnut, radar, polarArea, scatter, bubble; " +
        "QuickChart custom: sparkline, progressBar, donut (alias of doughnut); " +
        "box plots: boxplot, horizontalBoxplot, violin, horizontalViolin; " +
        "error bars: barWithErrorBars, lineWithErrorBars, scatterWithErrorBars, polarAreaWithErrorBars; " +
        "funnel: funnel; geo: choropleth, bubbleMap; graphs/trees: graph, forceDirectedGraph, dendrogram, tree; " +
        "parallel coordinates: pcp, logarithmicPcp; set diagrams: venn, euler; word clouds: wordCloud. " +
        "Also available: the 'hierarchical' category axis scale, the annotation and datalabels plugins " +
        "(options.plugins.annotation / options.plugins.datalabels), and time scales with moment.js format strings. " +
        "DATA LABELS: options.plugins.datalabels is on by default for pie/doughnut and off elsewhere, so any " +
        "datalabels option (e.g. display: true) turns it on. Its default label text already handles object data - " +
        "an { x, y } point shows the value-axis coordinate, an { x, y, r } bubble shows r, a choropleth row shows " +
        "the feature name above the value, and a bubbleMap row shows its value - so a custom formatter is only " +
        "needed to " +
        "change that text, never to make it readable. A formatter returning an array of strings renders one line " +
        "per element. " +
        "GEO CHARTS: the instance bundles map data - reference maps by name, do NOT inline GeoJSON for standard maps. " +
        "Map names: 'world', 'world-50m', 'world-land', 'us', 'us-states', 'us-counties', and ISO 3166-1 alpha-3 " +
        "country codes ('deu', 'fra', 'jpn', ...) for a single country with its first-level subdivisions. " +
        "choropleth dataset: { map: '<map name>', data: [{ feature: '<feature name or id>', value: <number> }] } - " +
        "feature strings are matched case-insensitively by properties.name or id " +
        "(e.g. { feature: 'Germany' } on map 'world', { feature: 'California' } on 'us-states'). " +
        "A choropleth data row may also carry a 'label' - the region's name as it should appear in data labels: " +
        "built-in maps name their features in English only, so this is how regions get labelled in another " +
        "language ({ feature: 'Minsk', label: 'Минская', value: 1471 }). " +
        "bubbleMap dataset: { outline: '<map name>', data: [{ longitude, latitude, value }] }. " +
        "When a named map is used, the color/size scales, showOutline and a hidden legend are defaulted " +
        "automatically, and the projection is aimed at the map - including single countries such as Russia or " +
        "New Zealand - so do NOT name a projection yourself unless you have a reason to. " +
        "To show only part of a map, set options.scales.projection.fit to [west, south, east, north] in degrees " +
        "(west may exceed east for a region past the antimeridian), { map, features: [...] }, { map } or GeoJSON: " +
        "the view is framed on that region, the projection is aimed at it, and everything outside is clipped. " +
        "To aim a projection by hand, options.scales.projection.projection also accepts an object - " +
        "{ type: 'conicEqualArea', rotate: [-100, 0], center: [0, 65], parallels: [50, 70] }, plus clipAngle, " +
        "clipExtent, precision, angle, reflectX, reflectY - and pixel-space nudging is available via the scale's " +
        "projectionScale, projectionOffset and padding options. " +
        "Use the list_maps tool to discover available maps, each map's matchable features, and the projection " +
        "spec the server would aim at it. " +
        "Inline GeoJSON Features still work anywhere a named reference does - use them only for custom shapes. " +
        "JS-string configs can also call getMap('<map name>'), which returns { features, topology }.";

    private const string ListMapsDescription =
        "List the built-in geo maps available on the QuickChart instance for choropleth/bubbleMap charts " +
        "(see create_chart). Without mapName: returns every available map as { name, source } - sources are " +
        "world-atlas, us-atlas, and datamaps (per-country ISO 3166-1 alpha-3 maps; a few codes are non-standard, " +
        "e.g. 'kos' for Kosovo). With mapName: returns { name, source, bbox, centroid, projection, " +
        "features: [{ name, id }] } - the feature names/ids that choropleth data rows can reference, matched " +
        "case-insensitively by name first, then id; the map's extent as [west, south, east, north] degrees " +
        "(west > east when the map crosses the antimeridian); and the projection spec the server aims at this map, " +
        "which can be copied into options.scales.projection.projection and adjusted. " +
        "Call this before create_chart when unsure of a country map code or of exact feature names/ids " +
        "(subdivision names are in local spelling, some are null and only matchable by id, e.g. 'DE.BE'), " +
        "or when picking the coordinates/features for an options.scales.projection.fit region. " +
        "Note: 'us-counties' has ~3200 features, so prefer listing smaller maps. Returns JSON inline; writes no files.";

    private const string ChartArgDescription =
        "Chart.js 4 configuration as a string. Plain JSON is forwarded as an object; JavaScript object syntax " +
        "(e.g. with callback functions or unquoted keys) is forwarded as a string for QuickChart to evaluate. " +
        "Options that take a function - datalabels formatter/display, scales ticks.callback, tooltip callbacks, " +
        "scriptable colors - work either way: write them unquoted in a JavaScript config, or, in plain JSON, as " +
        "quoted sources (\"formatter\": \"function(v) { return v.y; }\"), which the QuickChart instance " +
        "compiles on arrival - this tool forwards the config either way. A quoted source that does not parse " +
        "comes back as HTTP 400 naming the option. " +
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
        [Description("Chart width in pixels. Optional - see CANVAS SIZE.")] int? width = null,
        [Description("Chart height in pixels. Optional - see CANVAS SIZE.")] int? height = null,
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

    [McpServerTool(Name = "list_maps")]
    [Description(ListMapsDescription)]
    public async Task<object> ListMaps(
        [Description("Optional map name (e.g. 'world', 'us-states', 'deu'). When omitted, all available maps are listed. When provided, that map's matchable features (name/id pairs) are returned.")] string? mapName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.ListMapsAsync(mapName, cancellationToken);
            return string.IsNullOrWhiteSpace(mapName)
                ? new { success = true, maps = result }
                : new { success = true, map = result };
        }
        catch (QuickChartApiException api) when (api.StatusCode == 400)
        {
            // For this tool a 400 means the map name itself is unknown - the generic
            // "fix the chart config" hint of Error() would mislead.
            return new
            {
                success = false,
                error = api.Message,
                statusCode = api.StatusCode,
                hint = "Unknown map name. Call list_maps without arguments to see all available maps.",
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
            hint = "QuickChart rejected the request (HTTP 400). Fix the chart config/request (Chart.js 4 syntax, supported chart types, sizes/body limits) and retry.",
        },
        QuickChartApiException api => new { success = false, error = api.Message, statusCode = api.StatusCode },
        _ => new { success = false, error = ex.Message },
    };
}
