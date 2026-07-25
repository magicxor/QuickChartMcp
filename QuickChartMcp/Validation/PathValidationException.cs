namespace QuickChartMcp.Validation;

/// <summary>
/// Represents an expected, user-facing rejection of an output path (not absolute,
/// contains a '.'/'..'/empty segment, invalid characters, or blocked by the configured
/// allow-list policy). The <see cref="Exception.Message"/> is written to be clear and
/// actionable for the calling AI agent and is surfaced as the tool's error text.
/// </summary>
public sealed class PathValidationException : Exception
{
    public PathValidationException(string message) : base(message)
    {
    }
}
