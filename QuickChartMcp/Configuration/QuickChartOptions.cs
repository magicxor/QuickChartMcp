namespace QuickChartMcp.Configuration;

/// <summary>
/// Configuration for the self-hosted QuickChart instance this MCP server renders charts
/// through. Bound from the "QuickChart" configuration section (appsettings.json or the
/// QuickChart__* environment variables).
/// </summary>
public sealed class QuickChartOptions
{
    public const string SectionName = "QuickChart";

    /// <summary>Base URL of the QuickChart instance, e.g. http://localhost:3400.</summary>
    public string BaseUrl { get; set; } = "http://localhost:3400";

    /// <summary>
    /// Optional QuickChart API key, sent as the "key" property of the request body (QuickChart
    /// uses no auth header). Leave empty for self-hosted instances, which require none. This is
    /// deliberately a server-side setting: the MCP tool does not expose it, so the AI agent
    /// never sees or controls it.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>HTTP timeout in seconds for QuickChart requests.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Regex patterns an output directory must match to be allowed for writing.
    /// A directory is allowed when it matches <em>any</em> pattern. An empty list means
    /// "deny all" — no chart may be written anywhere until at least one pattern is configured.
    /// Matched case-insensitively against the (validated, absolute) directory string.
    /// </summary>
    public List<string> AllowedOutputPatterns { get; set; } = new();
}
