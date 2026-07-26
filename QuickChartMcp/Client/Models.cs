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

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 1280;

    public double DevicePixelRatio { get; init; } = 2.0;

    public string BackgroundColor { get; init; } = "transparent";

    public string Format { get; init; } = "png";
}
