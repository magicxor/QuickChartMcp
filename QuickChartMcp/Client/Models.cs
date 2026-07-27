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
