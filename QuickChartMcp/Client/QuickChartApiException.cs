namespace QuickChartMcp.Client;

/// <summary>
/// Thrown when the QuickChart API returns a non-success HTTP status or reports a chart
/// rendering error via the X-quickchart-error response header.
/// </summary>
public sealed class QuickChartApiException : Exception
{
    public int StatusCode { get; }

    public QuickChartApiException(int statusCode, string detail)
        : base($"QuickChart returned HTTP {statusCode}: {detail}")
    {
        StatusCode = statusCode;
    }
}
