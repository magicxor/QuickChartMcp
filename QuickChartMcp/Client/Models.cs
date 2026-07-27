using System.Text.Json.Nodes;

namespace QuickChartMcp.Client;

/// <summary>
/// Body of a QuickChart POST /chart request. Serialized with camelCase property names
/// (which the QuickChart API expects); null properties are omitted.
/// </summary>
/// <remarks>
/// Targets the modernized self-hosted QuickChart fork (Chart.js 4 only): the legacy
/// <c>version</c> parameter is deprecated/ignored there and the fork has no API-key concept,
/// so neither is part of this model.
/// </remarks>
public sealed record ChartRequest
{
    /// <summary>
    /// Chart.js 4 configuration: a <see cref="JsonObject"/> when the caller supplied valid
    /// JSON, or a plain string (<see cref="JsonValue"/>) when the config uses JavaScript
    /// syntax (functions, unquoted keys) that the QuickChart instance evaluates server-side.
    /// </summary>
    public required JsonNode Chart { get; init; }

    /// <summary>
    /// Canvas width in logical pixels, or null to let the instance derive it from the chart
    /// (a map is sized to the map's own proportions, other types to the ratio their type is
    /// read at). Null is omitted from the request body, which is what the instance reads as
    /// "not specified".
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Canvas height in logical pixels, or null to derive it — see <see cref="Width"/>.
    /// </summary>
    public int? Height { get; init; }

    public double DevicePixelRatio { get; init; } = 2.0;

    public string BackgroundColor { get; init; } = "transparent";

    public string Format { get; init; } = "png";
}

/// <summary>
/// Payload of the <c>X-quickchart-geo-coverage</c> response header: the features of each
/// built-in map that a choropleth left without a data row. The instance omits the header
/// entirely when every feature in view has one, so its presence is the signal.
/// </summary>
public sealed record GeoCoverage
{
    public IReadOnlyList<GeoMapCoverage> Maps { get; init; } = [];
}

/// <summary>Coverage of one map, pooled across the datasets that draw it.</summary>
public sealed record GeoMapCoverage
{
    /// <summary>Built-in map name, e.g. <c>world</c> or <c>rus</c>.</summary>
    public string Map { get; init; } = string.Empty;

    /// <summary>Features of the map the view shows — a projection fit narrows this.</summary>
    public int Framed { get; init; }

    /// <summary>How many of the framed features have a data row.</summary>
    public int Covered { get; init; }

    /// <summary>Names of framed features without a data row (at most 20).</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>How many uncovered features are not named in <see cref="Missing"/>.</summary>
    public int More { get; init; }
}
