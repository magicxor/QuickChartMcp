using System.Text.Json.Nodes;

namespace QuickChartMcp.Client;

/// <summary>
/// Body of a QuickChart POST /chart request. Serialized with camelCase property names
/// (which the QuickChart API expects); null properties are omitted.
/// </summary>
public sealed record ChartRequest
{
    /// <summary>
    /// Chart.js configuration: a <see cref="JsonObject"/> when the caller supplied valid JSON,
    /// or a plain string (<see cref="JsonValue"/>) when the config uses JavaScript syntax
    /// (functions, unquoted keys) that the QuickChart instance evaluates server-side.
    /// </summary>
    public required JsonNode Chart { get; init; }

    public int Width { get; init; } = 500;

    public int Height { get; init; } = 300;

    public double DevicePixelRatio { get; init; } = 2.0;

    public string BackgroundColor { get; init; } = "transparent";

    public string Format { get; init; } = "png";

    /// <summary>Chart.js version ("2", "3" or "4"); null lets the QuickChart instance decide.</summary>
    public string? Version { get; init; }

    /// <summary>QuickChart API key; null for self-hosted instances that require none.</summary>
    public string? Key { get; init; }
}
