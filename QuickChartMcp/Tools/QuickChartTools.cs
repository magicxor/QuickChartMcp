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
        "DATA LABELS: options.plugins.datalabels is on by default for the types that draw no axis to read a value " +
        "off - pie, doughnut, funnel - and off elsewhere, so any datalabels option (e.g. display: true) turns it on. " +
        "Its default label text already handles object data - an { x, y } point shows the value-axis coordinate, an " +
        "{ x, y, r } bubble shows r, a funnel stage shows its name above its value, a choropleth row shows the " +
        "feature name above the value, and a bubbleMap row shows its value. To say something else at a point, " +
        "give its data row a 'label': a string, or an array of strings for one line each " +
        "({ x: 'Mar', y: 21, label: ['21%', 'record'] }). A funnel prints the values as given: pass the numbers " +
        "to show, not fractions. Set display: 'auto' to have labels that would overlap hidden instead. " +
        "GEO CHARTS: the instance bundles map data - reference maps by name, do NOT inline GeoJSON for standard maps. " +
        "Map names: 'world', 'world-50m', 'world-land', 'us', 'us-states', 'us-counties', and ISO 3166-1 alpha-3 " +
        "country codes ('deu', 'fra', 'jpn', ...) for a single country with its first-level subdivisions. " +
        "choropleth dataset: { map: '<map name>', data: [{ feature: '<feature name or id>', value: <number> }] } - " +
        "feature strings are matched case-insensitively by properties.name or id " +
        "(e.g. { feature: 'Germany' } on map 'world', { feature: 'California' } on 'us-states'). " +
        "A choropleth data row may also carry a 'label' - the region's name as it should appear in data labels: " +
        "built-in maps name their features in English only, so this is how regions get labelled in another " +
        "language ({ feature: 'Minsk', label: 'Минская', value: 1471 }); an array of strings gives one line each, " +
        "with the value under the last. " +
        "bubbleMap dataset: { outline: '<map name>', data: [{ longitude, latitude, value }] }. " +
        "When a named map is used, the color/size scales, showOutline and a hidden legend are defaulted " +
        "automatically, and the projection is aimed at the map - including single countries such as Russia or " +
        "New Zealand - so do NOT name a projection yourself unless you have a reason to. " +
        "To show only part of a map, set options.scales.projection.fit to [west, south, east, north] in degrees " +
        "(west may exceed east for a region past the antimeridian), { map, features: [...] }, { map } or GeoJSON: " +
        "the view is framed on that region, the projection is aimed at it, and everything outside is clipped. " +
        "A feature covers a country's whole territory, so framing one that has distant parts reaches them too - " +
        "France's world feature includes French Guiana in South America. Add mainland: true to a fit object " +
        "({ map, features: [...], mainland: true }) to frame only the main body of that geometry. " +
        "COVERAGE: a choropleth paints the features it has data rows for and leaves the rest as the grey backdrop, " +
        "so this tool returns a 'warnings' entry listing the features in view that got no row (up to 20 of them, " +
        "then a count). Fill them in, or state in your answer that their data is unknown - never invent values to " +
        "silence it. " +
        "To aim a projection by hand, options.scales.projection.projection also accepts an object - " +
        "{ type: 'conicEqualArea', rotate: [-100, 0], center: [0, 65], parallels: [50, 70] }, plus clipAngle, " +
        "clipExtent, precision, angle, reflectX, reflectY - and pixel-space nudging is available via the scale's " +
        "projectionScale, projectionOffset and padding options. " +
        "Use the list_maps tool to discover available maps, each map's matchable features, and the projection " +
        "spec the server would aim at it. " +
        "Inline GeoJSON Features still work anywhere a named reference does - use them only for custom shapes.";

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
        "Chart.js 4 configuration, as a JSON string (a JSON object is accepted in its place). " +
        "The config is data: the instance executes no JavaScript, so options that take a function - datalabels " +
        "formatter/display, scales ticks.callback, tooltip callbacks, scriptable colors - cannot be used, and a " +
        "function source quoted into one (\"formatter\": \"function(v) { return v.y; }\") is rejected with " +
        "HTTP 400 naming the option. Use plain values instead: a data row's 'label' for the text at that point " +
        "(an array of strings for several lines), ticks.format with Intl.NumberFormat options for axis numbers " +
        "({ style: 'percent' }, { style: 'currency', currency: 'USD' }, { notation: 'compact' }, " +
        "{ useGrouping: false }), an array of colors for per-point colors, datalabels display: 'auto' to hide " +
        "labels that would overlap. " +
        "A string that is not valid JSON is rejected by this tool with the JSON reader's message; a config cut " +
        "short (a closing brace or bracket missing at the end) is the usual cause - resend it whole. " +
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
        [Description(ChartArgDescription)] JsonElement chart,
        [Description("Directory where the chart file will be written. Must be an absolute path. Created if it does not exist. REQUIRED.")] string outputDirectory,
        [Description("Chart width in logical pixels, before devicePixelRatio. Optional - see CANVAS SIZE.")] int? width = null,
        [Description("Chart height in logical pixels, before devicePixelRatio. Optional - see CANVAS SIZE.")] int? height = null,
        [Description("Device pixel ratio; output dimensions are multiplied by this (default 2.0; use 1.0 for exact width/height).")] double devicePixelRatio = 2.0,
        [Description("Canvas background color: a color name, hex, rgb() or hsl() value (default 'transparent').")] string backgroundColor = "transparent",
        [Description("Output format: 'png' (default), 'svg' or 'pdf'.")] string format = "png",
        [Description("Optional output file name. If omitted, a name is derived from the chart title, falling back to 'chart'. Any path components are rejected. Existing files are never overwritten; a numeric suffix is appended on collision.")] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _writer.EnsureOutputDirectoryAllowed(outputDirectory);

            var argument = ReadChartArgument(chart);
            if (argument.Node is null)
            {
                return new { success = false, error = argument.Rejection };
            }

            if (!FormatExtensions.TryGetValue(format, out var extension))
            {
                throw new ArgumentException(
                    $"format '{format}' is not supported; it must be one of: png, svg, pdf.",
                    nameof(format));
            }

            var normalizedFormat = extension[1..];
            var chartNode = argument.Node;
            var request = new ChartRequest
            {
                Chart = chartNode,
                Width = width,
                Height = height,
                DevicePixelRatio = devicePixelRatio,
                BackgroundColor = backgroundColor,
                Format = normalizedFormat,
            };

            var result = await _client.CreateChartAsync(request, cancellationToken);
            var written = await _writer.WriteBytesAsync(
                outputDirectory, fileName, DeriveBaseName(chartNode), extension, result.Bytes, cancellationToken);

            var summary = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["filePath"] = written.Path,
                ["bytes"] = written.Bytes,
                ["format"] = normalizedFormat,
                ["width"] = width,
                ["height"] = height,
                ["devicePixelRatio"] = devicePixelRatio,
                ["contentType"] = result.ContentType,
            };

            // Only when there is something to say: the chart rendered either way, and an
            // always-present empty list would read as noise on every single call.
            var warnings = DescribeGeoCoverage(result.GeoCoverage);
            if (warnings.Count > 0)
                summary["warnings"] = warnings;

            return summary;
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
    /// Turns the instance's geo coverage report into warnings the caller can act on: a
    /// choropleth paints only the features it has data rows for, and the ones it skips are
    /// drawn as the grey backdrop, which looks exactly like an unfinished map.
    /// </summary>
    /// <remarks>
    /// Reports arrive over a wire the caller configured, so this phrases whatever it is given
    /// rather than trusting the numbers to line up: an entry that reports nothing missing is
    /// not a warning, and a region the instance could not name is counted rather than printed
    /// as a blank item. What it must never do is drop a report it can still act on - the
    /// counts and the other names are the whole point of the header.
    /// </remarks>
    internal static List<string> DescribeGeoCoverage(GeoCoverage? coverage)
    {
        if (coverage is null)
            return [];

        var warnings = new List<string>();
        foreach (var map in coverage.Maps)
        {
            // One list, so an unnamed remainder reads as "and 3 more" rather than
            // ", and 3 more". Features the instance has no name or id for come through
            // in More; a blank name is the same thing said differently, so it joins them.
            var named = map.Missing.Where(static name => !string.IsNullOrWhiteSpace(name)).ToList();
            var unnamed = map.More + (map.Missing.Count - named.Count);
            if (unnamed > 0)
                named.Add($"and {unnamed} more");

            // Anything at all to report: a region to name, one to count, or a shortfall in
            // the counts. Reading the counts alone would drop a named region that arrived
            // with a shortfall of zero, and a named region is the actionable part.
            var uncovered = Math.Max(0, map.Framed - map.Covered);
            if (named.Count == 0 && uncovered == 0)
                continue;

            var listed = named.Count > 0 ? $": {string.Join(", ", named)}" : string.Empty;
            warnings.Add(
                $"Map '{map.Map}': only {map.Covered} of the {map.Framed} features in view have a data row. "
                + $"Without one a feature is drawn as the grey backdrop, indistinguishable from a region with no data{listed}. "
                + "If that is not intended, add data rows for them (list_maps lists every feature of the map: "
                + "by name, or by id where the map data gives it no name). "
                + "If the data genuinely does not exist, keep them grey and say so in your answer - do not invent values.");
        }

        return warnings;
    }

    /// <summary>
    /// The chart argument as the request body needs it, or why it cannot be used.
    /// </summary>
    private sealed record ChartArgument
    {
        /// <summary>The config as an object: the caller sent one, or its JSON parsed to one.</summary>
        public JsonObject? Node { get; init; }

        /// <summary>
        /// Set when the argument cannot be used; the tool answers with it and stops. Null
        /// exactly when <see cref="Node"/> is set.
        /// </summary>
        public string? Rejection { get; init; }
    }

    /// <summary>
    /// Reads the chart argument, which is documented as a string but is deliberately typed
    /// loosely. A string is parsed as JSON here, so a config that is not JSON is answered with
    /// the JSON reader's own words - which, for a config cut short, name the place it ran out.
    /// </summary>
    /// <remarks>
    /// A caller whose config was just rejected tends to reach for the object form next. Against
    /// a <c>string</c> parameter that fails inside the MCP SDK's argument binding — before this
    /// class is reached, and therefore outside its error handling — and the SDK renders any such
    /// failure as a bare "An error occurred invoking 'create_chart'." with no detail in it,
    /// leaving the caller with strictly less to go on than the config error it was trying to fix.
    /// Accepting both shapes keeps every answer one the caller can act on.
    /// </remarks>
    private static ChartArgument ReadChartArgument(JsonElement chart)
    {
        const string missing =
            "The 'chart' argument is required and must be a non-empty Chart.js configuration.";

        switch (chart.ValueKind)
        {
            case JsonValueKind.String:
                var source = chart.GetString();
                if (string.IsNullOrWhiteSpace(source))
                    return new ChartArgument { Rejection = missing };

                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(source);
                }
                catch (JsonException e)
                {
                    return new ChartArgument
                    {
                        Rejection = $"The 'chart' argument is not valid JSON: {e.Message} The config is data "
                            + "only, so JavaScript object syntax (unquoted keys, functions) is not accepted. "
                            + "A config cut short - a closing brace or bracket missing at the very end - is "
                            + "the usual cause; resend it whole.",
                    };
                }

                return parsed is JsonObject config
                    ? new ChartArgument { Node = config }
                    : new ChartArgument
                    {
                        Rejection = $"The 'chart' argument must be a JSON object; got a JSON {Describe(parsed)}.",
                    };

            case JsonValueKind.Object:
                return new ChartArgument { Node = JsonNode.Parse(chart.GetRawText()) as JsonObject };

            case JsonValueKind.Undefined or JsonValueKind.Null:
                return new ChartArgument { Rejection = missing };

            default:
                return new ChartArgument
                {
                    Rejection = "The 'chart' argument must be a Chart.js configuration - a JSON string or a "
                        + $"JSON object; got a bare {Describe(chart.ValueKind)}.",
                };
        }
    }

    /// <summary>What a parsed JSON root is, for an error message.</summary>
    private static string Describe(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray => "array",
        _ => Describe(node.GetValueKind()),
    };

    /// <summary>
    /// Names the type, not the value: ValueKind spells a boolean as "True"/"False", and "got a
    /// bare true" reads as the value that was sent rather than as what was wrong with it.
    /// </summary>
    private static string Describe(JsonValueKind kind) => kind is JsonValueKind.True or JsonValueKind.False
        ? "boolean"
        : kind.ToString().ToLowerInvariant();

    /// <summary>
    /// Derives a default file base name from the chart title: options.plugins.title.text
    /// (Chart.js 3/4) with a legacy options.title.text fallback; the text may be a string or
    /// an array of strings. Falls back to "chart" when no title can be extracted. Unsafe
    /// characters are handled by the ArtifactWriter.
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

    private const string Hint400 =
        "QuickChart rejected the request (HTTP 400). Fix the chart config/request (Chart.js 4 syntax, supported chart types, sizes/body limits) and retry.";

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
            hint = Hint400,
        },
        QuickChartApiException api => new { success = false, error = api.Message, statusCode = api.StatusCode },
        _ => new { success = false, error = ex.Message },
    };
}
